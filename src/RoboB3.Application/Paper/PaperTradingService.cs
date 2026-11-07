using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RoboB3.Application.Abstractions;
using RoboB3.Application.Backtesting;
using RoboB3.Application.Learning;
using RoboB3.Domain.Analysis;
using RoboB3.Domain.Audit;
using RoboB3.Domain.Backtesting;
using RoboB3.Domain.Common;
using RoboB3.Domain.Learning;
using RoboB3.Domain.MarketData;
using RoboB3.Domain.Paper;
using RoboB3.Domain.Risk;
using RoboB3.Domain.Strategies;
using RoboB3.Domain.Trading;

namespace RoboB3.Application.Paper;

public sealed record CycleReport(bool FeedConnected, int Opened, int Closed, IReadOnlyList<string> Messages);

/// <summary>
/// Um ciclo da operação simulada, com as mesmas regras do backtest:
/// marca as posições a mercado, executa stops e o encerramento de day trade, e avalia cada alocação
/// apenas quando fecha uma barra nova. Entradas passam pela IA (se ativada) e pelo <see cref="RiskManager"/>;
/// saídas nunca são bloqueadas. Nada é enviado à corretora.
/// </summary>
public sealed partial class PaperTradingService(
    IMarketDataFeed feed,
    IPaperRepository paper,
    IStrategyAssignmentRepository assignments,
    IAssetRepository assets,
    ITradingStateRepository tradingState,
    IRiskSettingsRepository riskSettings,
    ITradeJournalRepository journal,
    LearningService learning,
    IAuditLog audit,
    IUnitOfWork unitOfWork,
    IOptions<PaperTradingOptions> paperOptions,
    IOptions<BacktestOptions> backtestOptions,
    IOptions<TradingCapitalOptions> capitalOptions,
    TimeProvider clock,
    ILogger<PaperTradingService> logger)
{
    public async Task<CycleReport> RunCycleAsync(CancellationToken cancellationToken)
    {
        var status = await feed.GetStatusAsync(cancellationToken);
        if (!status.Connected)
        {
            return new CycleReport(false, 0, 0, [$"MetaTrader 5 desconectado: {status.Message}"]);
        }

        var cycle = await StartCycleAsync(cancellationToken);
        await MarkToMarketAsync(cycle, cancellationToken);
        await ApplyStopsAsync(cycle, cancellationToken);
        ApplyDayTradeClose(cycle);

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

        await unitOfWork.SaveChangesAsync(cancellationToken);
        foreach (var message in cycle.Messages)
        {
            LogMessage(message);
        }

        return new CycleReport(true, cycle.Opened, cycle.Closed, cycle.Messages);
    }

    /// <summary>Encerramento manual a mercado. Sempre permitido: reduzir risco nunca é bloqueado.</summary>
    public async Task ClosePositionAsync(Guid positionId, string actor, CancellationToken cancellationToken)
    {
        var cycle = await StartCycleAsync(cancellationToken);
        var position = cycle.Positions.FirstOrDefault(p => p.Id == positionId)
            ?? throw new DomainException("Posição não encontrada.");
        var quote = await feed.GetQuoteAsync(position.ContractSymbol, cancellationToken);
        Close(cycle, position, quote.PriceFor(isBuy: position.Direction == PositionDirection.Short), "manual");
        audit.Record(new AuditEntry("paper.posicao-encerrada", actor, $"{position.ContractSymbol} {position.Quantity}", cycle.Now));
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Zera a conta simulada. Exige o robô travado e nenhuma posição aberta.</summary>
    public async Task ResetAsync(string actor, CancellationToken cancellationToken)
    {
        if (!(await tradingState.GetAsync(cancellationToken)).IsHalted)
        {
            throw new DomainException("Trave o robô antes de reiniciar a conta simulada.");
        }

        if ((await paper.ListPositionsAsync(cancellationToken)).Count > 0)
        {
            throw new DomainException("Encerre as posições abertas antes de reiniciar a conta simulada.");
        }

        await paper.ResetAsync(cancellationToken);
        audit.Record(new AuditEntry("paper.conta-reiniciada", actor, null, clock.GetUtcNow()));
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    private async Task<Cycle> StartCycleAsync(CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var account = await paper.GetAccountAsync(cancellationToken);
        if (account is null)
        {
            account = PaperAccount.Open(capitalOptions.Value.InitialCapital, now);
            paper.AddAccount(account);
        }

        return new Cycle(
            now,
            account,
            await paper.ListPositionsAsync(cancellationToken),
            await assignments.ListAsync(cancellationToken),
            (await assets.ListAsync(onlyActive: false, cancellationToken)).ToDictionary(a => a.Symbol),
            await tradingState.GetAsync(cancellationToken),
            (await riskSettings.GetAsync(cancellationToken)).Limits);
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
                Close(cycle, position, quote.PriceFor(isBuy: position.Direction == PositionDirection.Short), "stop");
            }
        }
    }

    private void ApplyDayTradeClose(Cycle cycle)
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
                Close(cycle, position, quote.PriceFor(isBuy: position.Direction == PositionDirection.Short), "fim do pregão");
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
        if (candles.Count < 2)
        {
            return;
        }

        // A última barra ainda está em formação: decide-se só com barras fechadas, como no backtest.
        var closed = candles.OrderBy(c => c.OpenTime).SkipLast(1).ToList();
        if (!assignment.MarkProcessed(closed[^1].OpenTime))
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

        if (position is not null)
        {
            position.CountBar();
            if (target == position.Direction)
            {
                if (signal.StopPrice is { } stop)
                {
                    position.TightenStop(stop);
                }

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
            return;
        }

        if (position is not null)
        {
            Close(cycle, position, quote.PriceFor(isBuy: position.Direction == PositionDirection.Short), "sinal");
        }

        if (target != PositionDirection.Flat)
        {
            await TryOpenAsync(cycle, assignment, asset, instrument, contract, quote, history, signal, target, cancellationToken);
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
        CancellationToken cancellationToken)
    {
        var options = paperOptions.Value;
        var localTime = B3Calendar.LocalTime(cycle.Now);
        var blocked = cycle.State.IsHalted ? $"robô travado ({cycle.State.HaltReason})"
            : RiskManager.FindGlobalBreach(cycle.Account.Snapshot(cycle.Positions), cycle.Limits) is { } breach ? breach
            : localTime < options.NoEntriesBefore || localTime > options.NoEntriesAfter ? "fora do horário de entradas"
            : assignment.FlattenAtDayEnd && localTime >= options.DayTradeFlattenAt ? "day trade após o horário de encerramento"
            : null;
        if (blocked is not null)
        {
            cycle.Messages.Add($"{contract}: entrada bloqueada, {blocked}.");
            return;
        }

        var features = FeatureExtractor.TryExtract(history, target);
        double? probability = null;
        if (assignment.UseMetaModel && features is not null
            && await learning.GetActiveFilterAsync(assignment.StrategyKey, cancellationToken) is { } active)
        {
            var assessment = active.Filter.Assess(features);
            probability = assessment.Probability;
            if (!assessment.Allowed)
            {
                cycle.Messages.Add($"{contract}: a IA vetou a entrada (chance estimada {assessment.Probability:P0}).");
                return;
            }
        }

        var isBuy = target == PositionDirection.Long;
        var price = quote.PriceFor(isBuy);
        decimal? stopDistance = signal.StopPrice is { } stop ? Math.Abs(history.Current.Close - stop) : null;
        var equity = cycle.Account.Equity(cycle.Positions);
        var otherContracts = cycle.Positions.Where(p => p.IsFuture).Sum(p => p.Quantity);
        var quantity = PositionSizer.Size(price, stopDistance, equity, options.RiskPerTradePercent, instrument, cycle.Limits, otherContracts);
        if (quantity <= 0)
        {
            cycle.Messages.Add($"{contract}: tamanho calculado menor que um lote.");
            return;
        }

        var order = new OrderRequest(asset.Symbol, asset.AssetClass, isBuy ? OrderSide.Buy : OrderSide.Sell, quantity, price * instrument.Multiplier);
        var decision = RiskManager.Evaluate(order, cycle.Account.Snapshot(cycle.Positions), cycle.Limits, cycle.State);
        if (!decision.IsApproved)
        {
            cycle.Messages.Add($"{contract}: entrada rejeitada pelo risco: {string.Join(" ", decision.Violations)}");
            return;
        }

        var (position, fill) = cycle.Account.OpenPosition(assignment.Id, instrument, contract, assignment.StrategyKey, target, quantity,
            price, stopDistance, backtestOptions.Value.Costs(), features, probability, cycle.Now);
        paper.AddPosition(position);
        paper.AddFill(fill);
        cycle.Positions.Add(position);
        cycle.Opened++;
        cycle.Messages.Add($"{contract}: {(isBuy ? "compra" : "venda")} de {quantity} a {fill.Price}.");
    }

    private void Close(Cycle cycle, PaperPosition position, decimal price, string reason)
    {
        var (fill, entry) = cycle.Account.ClosePosition(position, price, backtestOptions.Value.Costs(), reason, cycle.Now);
        paper.AddFill(fill);
        paper.RemovePosition(position);
        journal.Add(entry);
        cycle.Positions.Remove(position);
        cycle.Closed++;
        cycle.Messages.Add($"{position.ContractSymbol}: encerrada ({reason}), resultado {fill.RealizedProfit:C}.");
    }

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

    [LoggerMessage(Level = LogLevel.Information, Message = "Simulação: {Message}")]
    private partial void LogMessage(string message);

    private sealed class Cycle(
        DateTimeOffset now,
        PaperAccount account,
        List<PaperPosition> positions,
        IReadOnlyList<StrategyAssignment> assignments,
        Dictionary<string, Asset> assets,
        TradingState state,
        RiskLimits limits)
    {
        public DateTimeOffset Now { get; } = now;

        public PaperAccount Account { get; } = account;

        public List<PaperPosition> Positions { get; } = positions;

        public IReadOnlyList<StrategyAssignment> Assignments { get; } = assignments;

        public Dictionary<string, Asset> Assets { get; } = assets;

        public TradingState State { get; } = state;

        public RiskLimits Limits { get; } = limits;

        public Dictionary<string, Quote> Quotes { get; } = [];

        public List<string> Messages { get; } = [];

        public int Opened { get; set; }

        public int Closed { get; set; }
    }
}
