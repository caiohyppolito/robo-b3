using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RoboB3.Application.Abstractions;
using RoboB3.Application.Backtesting;
using RoboB3.Application.Learning;
using RoboB3.Domain.Analysis;
using RoboB3.Domain.Audit;
using RoboB3.Domain.Backtesting;
using RoboB3.Domain.Common;
using RoboB3.Domain.Execution;
using RoboB3.Domain.Learning;
using RoboB3.Domain.MarketData;
using RoboB3.Domain.Paper;
using RoboB3.Domain.Risk;
using RoboB3.Domain.Strategies;
using RoboB3.Domain.Trading;

namespace RoboB3.Application.Paper;

public sealed record CycleReport(bool FeedConnected, int Opened, int Closed, IReadOnlyList<string> Messages);

/// <summary>
/// Um ciclo de operação, com as mesmas regras do backtest e no modo de execução vigente
/// (simulado, MT5 demo ou MT5 real):
/// confere a conta e concilia as posições com a corretora, marca a mercado, executa stops e o encerramento
/// de day trade, e avalia cada alocação só quando fecha uma barra nova. Entradas passam pela IA (se ativada)
/// e pelo <see cref="RiskManager"/>; saídas nunca são bloqueadas. Qualquer divergência trava o robô.
/// </summary>
public sealed partial class PaperTradingService(
    IMarketDataFeed feed,
    IPaperRepository paper,
    IStrategyAssignmentRepository assignments,
    IAssetRepository assets,
    ITradingStateRepository tradingState,
    IRiskSettingsRepository riskSettings,
    ITradeJournalRepository journal,
    IExecutionSettingsRepository executionSettings,
    IOrderExecutorFactory executors,
    LearningService learning,
    IAuditLog audit,
    IUnitOfWork unitOfWork,
    IOptions<PaperTradingOptions> paperOptions,
    IOptions<BacktestOptions> backtestOptions,
    IOptions<TradingCapitalOptions> capitalOptions,
    TimeProvider clock,
    ILogger<PaperTradingService> logger)
{
    public const string Actor = "robo";

    public async Task<CycleReport> RunCycleAsync(CancellationToken cancellationToken)
    {
        var status = await feed.GetStatusAsync(cancellationToken);
        var mode = (await executionSettings.GetAsync(cancellationToken)).Mode;
        if (!status.Connected)
        {
            return new CycleReport(false, 0, 0, [$"MetaTrader 5 desconectado: {status.Message}"]);
        }

        var cycle = await StartCycleAsync(mode, cancellationToken);
        if (ExecutionPolicy.FindAccountMismatch(mode, status.Connected, status.IsDemo) is { } mismatch)
        {
            Halt(cycle, mismatch);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return new CycleReport(true, 0, 0, cycle.Messages);
        }

        await ReconcileAsync(cycle, cancellationToken);
        await MarkToMarketAsync(cycle, cancellationToken);
        await ApplyStopsAsync(cycle, cancellationToken);
        await ApplyDayTradeCloseAsync(cycle, cancellationToken);

        foreach (var assignment in cycle.Assignments.Where(a => a.IsEnabled))
        {
            try
            {
                await EvaluateAssignmentAsync(cycle, assignment, cancellationToken);
            }
            catch (Exception ex) when (ex is DomainException or HttpRequestException or InvalidOperationException)
            {
                cycle.Messages.Add($"{assignment.Symbol}/{assignment.StrategyKey}: {ex.Message}");
            }
        }

        foreach (var decision in cycle.Decisions)
        {
            paper.AddDecision(decision);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        foreach (var message in cycle.Messages)
        {
            LogMessage(mode.Describe(), message);
        }

        return new CycleReport(true, cycle.Opened, cycle.Closed, cycle.Messages);
    }

    /// <summary>Encerramento manual a mercado. Sempre permitido: reduzir risco nunca é bloqueado.</summary>
    public async Task ClosePositionAsync(Guid positionId, string actor, CancellationToken cancellationToken)
    {
        var mode = (await executionSettings.GetAsync(cancellationToken)).Mode;
        var cycle = await StartCycleAsync(mode, cancellationToken);
        var position = cycle.Positions.FirstOrDefault(p => p.Id == positionId)
            ?? throw new DomainException("Posição não encontrada.");
        var quote = await feed.GetQuoteAsync(position.ContractSymbol, cancellationToken);
        if (!await CloseAsync(cycle, position, quote, "manual", cancellationToken))
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
            throw new DomainException(cycle.Messages.LastOrDefault() ?? "A corretora não executou o encerramento.");
        }

        audit.Record(new AuditEntry("operacao.posicao-encerrada", actor, $"{position.ContractSymbol} {position.Quantity} ({mode.Describe()})", cycle.Now));
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Zera o livro do modo vigente. Exige o robô travado e nenhuma posição aberta.</summary>
    public async Task ResetAsync(string actor, CancellationToken cancellationToken)
    {
        var mode = (await executionSettings.GetAsync(cancellationToken)).Mode;
        if (!(await tradingState.GetAsync(cancellationToken)).IsHalted)
        {
            throw new DomainException("Trave o robô antes de reiniciar a conta.");
        }

        if ((await paper.ListPositionsAsync(mode, cancellationToken)).Count > 0)
        {
            throw new DomainException("Encerre as posições abertas antes de reiniciar a conta.");
        }

        await paper.ResetAsync(mode, cancellationToken);
        audit.Record(new AuditEntry("operacao.conta-reiniciada", actor, mode.Describe(), clock.GetUtcNow()));
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    private async Task<Cycle> StartCycleAsync(ExecutionMode mode, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var executor = executors.For(mode);
        var account = await paper.GetAccountAsync(mode, cancellationToken);
        if (account is null)
        {
            // No MT5, o livro começa com o patrimônio real da conta; na simulação, com o capital configurado.
            var capital = await executor.GetEquityAsync(cancellationToken) ?? capitalOptions.Value.InitialCapital;
            account = PaperAccount.Open(capital, now, mode);
            paper.AddAccount(account);
        }

        return new Cycle(
            now,
            mode,
            executor,
            account,
            await paper.ListPositionsAsync(mode, cancellationToken),
            await assignments.ListAsync(cancellationToken),
            (await assets.ListAsync(onlyActive: false, cancellationToken)).ToDictionary(a => a.Symbol),
            await tradingState.GetAsync(cancellationToken),
            (await riskSettings.GetAsync(cancellationToken)).Limits);
    }

    /// <summary>
    /// Compara o livro do robô com a corretora. Posição encerrada lá (ex.: stop na corretora) é encerrada aqui
    /// ao preço real; posição do robô na corretora sem registro interno, ou com volume diferente, trava o robô.
    /// </summary>
    private async Task ReconcileAsync(Cycle cycle, CancellationToken cancellationToken)
    {
        var broker = await cycle.Executor.GetPositionsAsync(cancellationToken);
        if (broker is null)
        {
            return;
        }

        var byTicket = broker.ToDictionary(b => b.Ticket);
        foreach (var position in cycle.Positions.ToList())
        {
            if (position.BrokerTicket is not { } ticket)
            {
                Halt(cycle, $"Posição {position.ContractSymbol} sem ticket da corretora.");
                continue;
            }

            if (!byTicket.TryGetValue(ticket, out var match))
            {
                var price = await cycle.Executor.GetClosingPriceAsync(ticket, cancellationToken) ?? position.MarkPrice;
                RecordClose(cycle, position, price, "encerrada na corretora");
                continue;
            }

            if (match.Volume != position.Quantity)
            {
                Halt(cycle, $"Volume divergente em {position.ContractSymbol}: robô {position.Quantity}, corretora {match.Volume}.");
            }
        }

        var known = cycle.Positions.Select(p => p.BrokerTicket).ToHashSet();
        foreach (var unknown in broker.Where(b => b.IsRobot && !known.Contains(b.Ticket)))
        {
            Halt(cycle, $"Posição do robô na corretora sem registro interno: {unknown.Symbol} {unknown.Volume} (ticket {unknown.Ticket}).");
        }
    }

    private async Task MarkToMarketAsync(Cycle cycle, CancellationToken cancellationToken)
    {
        foreach (var position in cycle.Positions)
        {
            if (await FreshQuoteAsync(cycle, position.ContractSymbol, cancellationToken) is { } quote)
            {
                position.UpdateMark(quote.Last, quote.Time);
            }
        }

        cycle.Account.Refresh(cycle.Account.Equity(cycle.Positions), B3Calendar.TradingDate(cycle.Now));
    }

    private async Task ApplyStopsAsync(Cycle cycle, CancellationToken cancellationToken)
    {
        foreach (var position in cycle.Positions.ToList())
        {
            if (await FreshQuoteAsync(cycle, position.ContractSymbol, cancellationToken) is { } quote
                && position.IsStopHit(quote.Bid, quote.Ask))
            {
                await CloseAsync(cycle, position, quote, "stop", cancellationToken);
            }
        }
    }

    private async Task ApplyDayTradeCloseAsync(Cycle cycle, CancellationToken cancellationToken)
    {
        if (B3Calendar.LocalTime(cycle.Now) < paperOptions.Value.DayTradeFlattenAt)
        {
            return;
        }

        var dayTrade = cycle.Assignments.Where(a => a.FlattenAtDayEnd).Select(a => a.Id).ToHashSet();
        foreach (var position in cycle.Positions.Where(p => p.AssignmentId is { } id && dayTrade.Contains(id)).ToList())
        {
            if (cycle.Quotes.TryGetValue(position.ContractSymbol, out var quote))
            {
                await CloseAsync(cycle, position, quote, "fim do pregão", cancellationToken);
            }
        }
    }

    private async Task EvaluateAssignmentAsync(Cycle cycle, StrategyAssignment assignment, CancellationToken cancellationToken)
    {
        if (!cycle.Assets.TryGetValue(assignment.Symbol, out var asset))
        {
            cycle.Messages.Add($"{assignment.Symbol}: ativo não cadastrado.");
            return;
        }

        var options = paperOptions.Value;
        var instrument = InstrumentSpec.For(asset);
        var contract = await feed.ResolveSymbolAsync(asset, cancellationToken);
        var candles = await feed.GetBarsAsync(asset, contract, assignment.Timeframe, options.BarsToLoad, cancellationToken);

        // Decide-se só com barras fechadas, como no backtest: a barra só conta depois que o horário de fim dela passou.
        // Com o mercado fechado, a última barra do pregão já está completa e entra normalmente.
        var barLength = TimeSpan.FromMinutes((int)assignment.Timeframe);
        var closed = candles.Where(c => c.OpenTime + barLength <= cycle.Now).OrderBy(c => c.OpenTime).ToList();
        if (closed.Count == 0 || !assignment.MarkProcessed(closed[^1].OpenTime))
        {
            return;
        }

        var strategy = assignment.CreateStrategy();
        var bars = Bar.FromCandles(closed);
        if (bars.Count < strategy.WarmupBars)
        {
            cycle.Messages.Add($"{assignment.Symbol}: histórico insuficiente ({bars.Count} de {strategy.WarmupBars} barras).");
            return;
        }

        var history = new History(bars, bars.Count - 1);
        var position = cycle.Positions.FirstOrDefault(p => p.AssignmentId == assignment.Id);
        var context = position is null ? PositionContext.None : new PositionContext(position.Direction, position.EntryPrice, position.BarsHeld);
        var signal = strategy.Evaluate(history, context);
        var target = signal.Target == PositionDirection.Short && !instrument.AllowShort ? PositionDirection.Flat : signal.Target;
        cycle.Messages.Add($"{assignment.Symbol} → {contract}: barra {DescribeBar(closed[^1], assignment.Timeframe)} avaliada, sinal: {Describe(target)}.");
        var decision = new TradingDecision(cycle.Now, cycle.Mode, assignment.Id, assignment.Symbol, contract, assignment.StrategyKey, closed[^1].OpenTime, target);
        cycle.Decisions.Add(decision);

        if (position is not null)
        {
            position.CountBar();
            if (target == position.Direction)
            {
                // O stop vai arredondado para o tick: fora do tick a corretora recusa a alteração e o livro
                // do robô passaria a acreditar numa proteção mais apertada do que a registrada lá.
                if (signal.StopPrice is { } candidate)
                {
                    var stop = instrument.RoundToTick(candidate);
                    if (position.TightenStop(stop)
                        && position.BrokerTicket is { } ticket
                        && !await cycle.Executor.UpdateStopAsync(position.ContractSymbol, ticket, stop, cancellationToken))
                    {
                        cycle.Messages.Add($"{position.ContractSymbol}: a corretora não aceitou o novo stop {stop}; o robô segue vigiando o stop.");
                    }
                }

                decision.Conclude(DecisionOutcome.Held, $"Mantém a posição {Describe(position.Direction)} de {position.Quantity}; stop {position.Stop?.ToString("N2") ?? "—"}.");
                return;
            }
        }
        else if (target == PositionDirection.Flat)
        {
            return;
        }

        var quote = await FreshQuoteAsync(cycle, contract, cancellationToken);
        if (quote is null)
        {
            cycle.Messages.Add($"{contract}: sem cotação recente (mercado fechado?).");
            decision.Conclude(DecisionOutcome.Blocked, "Sem cotação recente (mercado fechado?).");
            return;
        }

        if (position is not null)
        {
            if (!await CloseAsync(cycle, position, quote, "sinal", cancellationToken))
            {
                decision.Conclude(DecisionOutcome.Failed, $"A corretora não executou o encerramento da posição {Describe(position.Direction)}.");
                return;
            }

            decision.Conclude(DecisionOutcome.Exited, $"Encerrou a posição {Describe(position.Direction)}: o sinal mudou para {Describe(target)}.");
        }

        if (target != PositionDirection.Flat)
        {
            await TryOpenAsync(cycle, assignment, asset, instrument, contract, quote, history, signal, target, decision, cancellationToken);
        }
    }

    private async Task TryOpenAsync(
        Cycle cycle,
        StrategyAssignment assignment,
        Asset asset,
        InstrumentSpec instrument,
        string contract,
        Quote quote,
        History history,
        StrategySignal signal,
        PositionDirection target,
        TradingDecision decision,
        CancellationToken cancellationToken)
    {
        var options = paperOptions.Value;
        var localTime = B3Calendar.LocalTime(cycle.Now);
        var blocked = cycle.State.IsHalted ? $"robô travado ({cycle.State.HaltReason})"
            : RiskManager.FindGlobalBreach(cycle.Account.Snapshot(cycle.Positions), cycle.Limits) is { } breach ? breach
            : localTime < options.NoEntriesBefore || localTime > options.NoEntriesAfter ? "fora do horário de entradas"
            : assignment.FlattenAtDayEnd && localTime >= options.DayTradeFlattenAt ? "day trade após o horário de encerramento"
            : cycle.Mode.SendsToBroker() && cycle.Positions.Any(p => p.ContractSymbol == contract)
                ? "já existe posição do robô neste contrato (a corretora soma posições do mesmo ativo)"
            : null;
        if (blocked is not null)
        {
            cycle.Messages.Add($"{contract}: entrada bloqueada, {blocked}.");
            decision.Conclude(DecisionOutcome.Blocked, $"Entrada bloqueada: {blocked}.");
            return;
        }

        var features = FeatureExtractor.TryExtract(history, target);
        double? probability = null;
        if (assignment.UseMetaModel && features is not null
            && await learning.GetActiveFilterAsync(assignment.StrategyKey, cancellationToken) is { } active)
        {
            var assessment = active.Filter.Assess(features);
            probability = assessment.Probability;
            decision.SetProbability(assessment.Probability);
            if (!assessment.Allowed)
            {
                cycle.Messages.Add($"{contract}: a IA vetou a entrada (chance estimada {assessment.Probability:P0}).");
                decision.Conclude(DecisionOutcome.Vetoed, $"A IA vetou: chance estimada de acerto {assessment.Probability:P0}, abaixo do limiar do modelo.");
                return;
            }
        }

        var isBuy = target == PositionDirection.Long;
        var price = quote.PriceFor(isBuy);
        decimal? stopDistance = signal.StopPrice is { } signalStop ? Math.Abs(history.Current.Close - signalStop) : null;
        var equity = cycle.Account.Equity(cycle.Positions);
        var otherContracts = cycle.Positions.Where(p => p.IsFuture).Sum(p => p.Quantity);
        var sizing = PositionSizer.Explain(price, stopDistance, equity, options.RiskPerTradePercent, instrument, cycle.Limits, otherContracts);
        var quantity = sizing.Quantity;
        decision.SetSizing(sizing.Explanation);
        if (quantity <= 0)
        {
            cycle.Messages.Add($"{contract}: tamanho calculado menor que um lote.");
            decision.Conclude(DecisionOutcome.Blocked, "Tamanho calculado menor que um lote.");
            return;
        }

        var side = isBuy ? OrderSide.Buy : OrderSide.Sell;
        var order = new OrderRequest(asset.Symbol, asset.AssetClass, side, quantity, price * instrument.Multiplier);
        var riskDecision = RiskManager.Evaluate(order, cycle.Account.Snapshot(cycle.Positions), cycle.Limits, cycle.State);
        if (!riskDecision.IsApproved)
        {
            cycle.Messages.Add($"{contract}: entrada rejeitada pelo risco: {string.Join(" ", riskDecision.Violations)}");
            decision.Conclude(DecisionOutcome.Rejected, $"Recusada pelo gestor de risco: {string.Join(" ", riskDecision.Violations)}");
            return;
        }

        decimal? stop = stopDistance is > 0 ? instrument.RoundToTick(price - ((int)target * stopDistance.Value)) : null;
        var instruction = new OrderInstruction(contract, side, quantity, price, stop, null, NewClientId("e"), $"entrada {assignment.StrategyKey}");
        var result = await cycle.Executor.SendAsync(instruction, cancellationToken);
        AuditOrder(cycle, instruction, result);
        if (!result.Filled || result.Quantity <= 0)
        {
            cycle.Messages.Add($"{contract}: ordem de entrada não executada: {result.Message}");
            decision.Conclude(DecisionOutcome.Failed, $"Ordem de entrada não executada: {result.Message}");
            return;
        }

        var (position, fill) = cycle.Account.OpenPosition(assignment.Id, instrument, contract, assignment.StrategyKey, target, result.Quantity,
            result.Price, stop, backtestOptions.Value.Costs(), features, probability, cycle.Now, result.BrokerTicket);
        paper.AddPosition(position);
        paper.AddFill(fill);
        cycle.Positions.Add(position);
        cycle.Opened++;
        cycle.Messages.Add($"{contract}: {(isBuy ? "compra" : "venda")} de {result.Quantity} a {fill.Price}.");
        decision.SetExecution(result.Quantity, fill.Price, stop);
        decision.Conclude(DecisionOutcome.Entered, $"{(isBuy ? "Comprou" : "Vendeu")} {result.Quantity} a {fill.Price:N2}" + (stop is { } s ? $", stop {s:N2}." : ", sem stop."));
    }

    /// <returns>Falso se a corretora não executou: a posição continua aberta e o robô é travado.</returns>
    private async Task<bool> CloseAsync(Cycle cycle, PaperPosition position, Quote quote, string reason, CancellationToken cancellationToken)
    {
        var side = position.Direction == PositionDirection.Long ? OrderSide.Sell : OrderSide.Buy;
        var instruction = new OrderInstruction(position.ContractSymbol, side, position.Quantity, quote.PriceFor(side == OrderSide.Buy),
            null, position.BrokerTicket, NewClientId("s"), reason);
        var result = await cycle.Executor.SendAsync(instruction, cancellationToken);
        AuditOrder(cycle, instruction, result);
        if (!result.Filled)
        {
            Halt(cycle, $"Falha ao encerrar {position.ContractSymbol} ({reason}): {result.Message} A posição continua aberta.");
            return false;
        }

        RecordClose(cycle, position, result.Price, reason);
        return true;
    }

    private void RecordClose(Cycle cycle, PaperPosition position, decimal price, string reason)
    {
        var (fill, entry) = cycle.Account.ClosePosition(position, price, backtestOptions.Value.Costs(), reason, cycle.Now);
        paper.AddFill(fill);
        paper.RemovePosition(position);
        journal.Add(entry);
        cycle.Positions.Remove(position);
        cycle.Closed++;
        cycle.Messages.Add($"{position.ContractSymbol}: encerrada ({reason}), resultado {fill.RealizedProfit:C}.");
    }

    /// <summary>Trava o robô (bloqueia entradas; saídas continuam). Destravar é sempre humano.</summary>
    private void Halt(Cycle cycle, string reason)
    {
        cycle.Messages.Add($"ROBÔ TRAVADO: {reason}");
        if (!cycle.State.IsHalted)
        {
            cycle.State.Halt(reason, Actor, cycle.Now);
            audit.Record(new AuditEntry("kill-switch.halt", Actor, reason, cycle.Now));
        }
    }

    private void AuditOrder(Cycle cycle, OrderInstruction instruction, OrderResult result)
    {
        if (!cycle.Mode.SendsToBroker())
        {
            return;
        }

        audit.Record(new AuditEntry(result.Filled ? "ordem.executada" : "ordem.recusada", Actor,
            $"[{cycle.Mode.Describe()}] {instruction.Side} {instruction.Quantity} {instruction.Symbol} ({instruction.Reason}) " +
            $"stop={instruction.StopLoss} id={instruction.ClientOrderId}: {result.Message} preço={result.Price} ticket={result.BrokerTicket}",
            cycle.Now));
    }

    private static string NewClientId(string kind) => $"{kind}-{Guid.NewGuid():N}";

    private async Task<Quote?> FreshQuoteAsync(Cycle cycle, string symbol, CancellationToken cancellationToken)
    {
        if (!cycle.Quotes.TryGetValue(symbol, out var quote))
        {
            try
            {
                quote = await feed.GetQuoteAsync(symbol, cancellationToken);
                cycle.Quotes[symbol] = quote;
            }
            catch (HttpRequestException ex)
            {
                cycle.Messages.Add($"{symbol}: cotação indisponível ({ex.Message}).");
                return null;
            }
        }

        return quote.IsFresh(cycle.Now, TimeSpan.FromSeconds(paperOptions.Value.MaxQuoteAgeSeconds)) ? quote : null;
    }

    private static string DescribeBar(Candle candle, Timeframe timeframe) =>
        timeframe == Timeframe.D1
            ? $"de {DateOnly.FromDateTime(candle.OpenTime.UtcDateTime):dd/MM}"
            : $"das {B3Calendar.ToLocal(candle.OpenTime):dd/MM HH:mm}";

    private static string Describe(PositionDirection direction) => direction switch
    {
        PositionDirection.Long => "comprado",
        PositionDirection.Short => "vendido",
        _ => "fora do mercado",
    };

    [LoggerMessage(Level = LogLevel.Information, Message = "Operação ({Mode}): {Message}")]
    private partial void LogMessage(string mode, string message);

    private sealed class Cycle(
        DateTimeOffset now,
        ExecutionMode mode,
        IOrderExecutor executor,
        PaperAccount account,
        List<PaperPosition> positions,
        IReadOnlyList<StrategyAssignment> assignments,
        Dictionary<string, Asset> assets,
        TradingState state,
        RiskLimits limits)
    {
        public DateTimeOffset Now { get; } = now;

        public ExecutionMode Mode { get; } = mode;

        public IOrderExecutor Executor { get; } = executor;

        public PaperAccount Account { get; } = account;

        public List<PaperPosition> Positions { get; } = positions;

        public IReadOnlyList<StrategyAssignment> Assignments { get; } = assignments;

        public Dictionary<string, Asset> Assets { get; } = assets;

        public TradingState State { get; } = state;

        public RiskLimits Limits { get; } = limits;

        public Dictionary<string, Quote> Quotes { get; } = [];

        public List<string> Messages { get; } = [];

        public List<TradingDecision> Decisions { get; } = [];

        public int Opened { get; set; }

        public int Closed { get; set; }
    }
}
