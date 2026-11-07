using RoboB3.Domain.Analysis;
using RoboB3.Domain.Learning;
using RoboB3.Domain.MarketData;
using RoboB3.Domain.Risk;
using RoboB3.Domain.Strategies;
using RoboB3.Domain.Taxes;
using RoboB3.Domain.Trading;

namespace RoboB3.Domain.Backtesting;

/// <summary>
/// Simulação barra a barra com as mesmas regras do robô real:
/// sinal no fechamento, execução na abertura seguinte, stop com gap, custos, IR mensal
/// e o <see cref="RiskManager"/> aprovando cada entrada.
/// </summary>
public sealed class BacktestEngine
{
    private readonly IReadOnlyList<Bar> _bars;
    private readonly IStrategy _strategy;
    private readonly InstrumentSpec _instrument;
    private readonly BacktestSettings _settings;
    private readonly StrategyParameters _parameters;
    private readonly TradingState _runningState;
    private readonly ISignalFilter? _filter;
    private readonly bool _captureFeatures;

    private readonly List<BacktestTrade> _trades = [];
    private readonly List<EquityPoint> _equity = [];
    private readonly List<BacktestEvent> _events = [];
    private readonly List<RealizedTrade> _monthTrades = [];
    private readonly BrazilianTaxCalculator _taxes = new();

    private decimal _cash;
    private decimal _costsPaid;
    private decimal _taxesPaid;
    private decimal _interestEarned;
    private decimal _peakEquity;
    private int _barsInPosition;
    private int _signalsFiltered;
    private Position? _position;
    private PendingOrder? _pending;
    private bool _killSwitch;
    private int _currentMonth;

    private BacktestEngine(
        IReadOnlyList<Bar> bars,
        IStrategy strategy,
        StrategyParameters parameters,
        InstrumentSpec instrument,
        BacktestSettings settings,
        ISignalFilter? filter,
        bool captureFeatures)
    {
        _filter = filter;
        _captureFeatures = captureFeatures || filter is not null;
        _bars = bars;
        _strategy = strategy;
        _parameters = parameters;
        _instrument = instrument;
        _settings = settings;
        _cash = settings.InitialCapital;
        _peakEquity = settings.InitialCapital;
        _runningState = TradingState.CreateHalted(DateTimeOffset.UnixEpoch);
        _runningState.Resume("backtest", DateTimeOffset.UnixEpoch);
    }

    /// <param name="startIndex">
    /// Primeira barra em que se pode operar. As anteriores servem só de aquecimento dos indicadores
    /// (útil para testar fora da amostra sem perder o histórico).
    /// </param>
    /// <param name="filter">Filtro da IA: pode vetar entradas, nunca saídas.</param>
    /// <param name="captureFeatures">Registra o contexto de mercado de cada entrada (para treinar a IA).</param>
    public static BacktestResult Run(
        IReadOnlyList<Bar> bars,
        IStrategy strategy,
        StrategyParameters parameters,
        InstrumentSpec instrument,
        BacktestSettings settings,
        int startIndex = 0,
        ISignalFilter? filter = null,
        bool captureFeatures = false)
    {
        ArgumentNullException.ThrowIfNull(bars);
        ArgumentNullException.ThrowIfNull(strategy);
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentNullException.ThrowIfNull(instrument);
        ArgumentNullException.ThrowIfNull(settings);

        var first = Math.Max(startIndex, strategy.WarmupBars - 1);
        if (first >= bars.Count - 1)
        {
            throw new Common.DomainException(
                $"Histórico insuficiente para {strategy.Key}: {bars.Count} barras, necessárias pelo menos {first + 2}.");
        }

        return new BacktestEngine(bars, strategy, parameters, instrument, settings, filter, captureFeatures).Execute(first);
    }

    private BacktestResult Execute(int first)
    {
        var startClose = _bars[first].Close;
        var riskFreeDaily = Math.Pow(1d + ((double)_settings.RiskFreeRatePercent / 100d), 1d / PerformanceMetrics.TradingDaysPerYear);
        var dailyInterest = (decimal)riskFreeDaily - 1m;
        var dayStartEquity = _settings.InitialCapital;
        var lastEquity = _settings.InitialCapital;
        var tradingDay = 0;
        var totalBars = 0;
        _currentMonth = MonthKey(_bars[first].Date);

        for (var i = first; i < _bars.Count; i++)
        {
            var bar = _bars[i];
            var isNewDay = i > first && bar.Date != _bars[i - 1].Date;

            if (isNewDay)
            {
                // Juros, CDI e limite de perda diária andam por pregão, não por barra (vale para barras intradiárias).
                tradingDay++;
                dayStartEquity = lastEquity;
                EarnInterest(dailyInterest);
                PayTaxesOnMonthChange(bar.Date);
            }

            if (i > first)
            {
                ExecutePending(bar);
                CheckStop(bar);
            }

            var isLastBar = i == _bars.Count - 1;
            var isLastBarOfDay = isLastBar || _bars[i + 1].Date != bar.Date;
            if (_settings.FlattenAtDayEnd && isLastBarOfDay && _position is not null && !isLastBar)
            {
                ClosePosition(bar.Close, bar, "fim do pregão");
            }

            totalBars++;
            if (_position is not null)
            {
                _barsInPosition++;
                _position.Track(bar);
            }

            var equity = MarkToMarket(bar.Close);
            lastEquity = equity;
            _peakEquity = Math.Max(_peakEquity, equity);
            RecordEquity(new EquityPoint(
                bar.Date,
                Math.Round(equity, 2),
                Math.Round(_settings.InitialCapital * bar.Close / startClose, 2),
                Math.Round(_settings.InitialCapital * (decimal)Math.Pow(riskFreeDaily, tradingDay), 2)));

            if (isLastBar)
            {
                break;
            }

            var entriesBlocked = _killSwitch | CheckRiskLimits(bar, equity, dayStartEquity);

            // Em day trade, a última barra do pregão não gera entrada para o dia seguinte.
            if (_settings.FlattenAtDayEnd && isLastBarOfDay)
            {
                _pending = null;
                continue;
            }

            EvaluateStrategy(i, allowNewRisk: !entriesBlocked);
        }

        var lastBar = _bars[^1];
        if (_position is not null)
        {
            ClosePosition(lastBar.Close, lastBar, "fim do período");
        }

        PayTaxes(_taxes.CloseMonth(_monthTrades) + _taxes.Flush(), lastBar.Date);
        _monthTrades.Clear();
        _equity[^1] = _equity[^1] with { Equity = Math.Round(_cash, 2) };

        var metrics = PerformanceMetrics.Calculate(
            _equity, _trades, _barsInPosition, totalBars, _costsPaid, _taxesPaid, _interestEarned, _settings.RiskFreeRatePercent)
            with { SignalsFiltered = _signalsFiltered };
        return new BacktestResult(_instrument.Symbol, _strategy.Key, _parameters, _trades, _equity, _events, metrics);
    }

    /// <summary>Um ponto por pregão: em barras intradiárias, o último ponto do dia substitui os anteriores.</summary>
    private void RecordEquity(EquityPoint point)
    {
        if (_equity.Count > 0 && _equity[^1].Date == point.Date)
        {
            _equity[^1] = point;
        }
        else
        {
            _equity.Add(point);
        }
    }

    /// <summary>
    /// Mesmo comportamento do robô real: estourar um limite bloqueia novas entradas,
    /// mas saídas (redução de risco) continuam permitidas.
    /// </summary>
    /// <returns>Verdadeiro se novas entradas estão bloqueadas nesta barra.</returns>
    private bool CheckRiskLimits(Bar bar, decimal equity, decimal previousEquity)
    {
        if (_killSwitch)
        {
            return true;
        }

        var snapshot = new PortfolioSnapshot(Math.Max(0, equity), previousEquity, _peakEquity, []);
        var breach = RiskManager.FindGlobalBreach(snapshot, _settings.Limits);
        if (breach is null)
        {
            return false;
        }

        if (snapshot.DrawdownPercent >= _settings.Limits.MaxDrawdownPercent)
        {
            // O robô real não se destrava sozinho: no backtest, nenhuma entrada nova até o fim.
            _killSwitch = true;
            _events.Add(new BacktestEvent(bar.Date, $"Kill switch: {breach} Novas entradas bloqueadas até o fim; saídas continuam permitidas."));
        }
        else
        {
            _events.Add(new BacktestEvent(bar.Date, $"{breach} Novas entradas bloqueadas no próximo pregão."));
        }

        return true;
    }

    private void EarnInterest(decimal dailyRate)
    {
        if (!_settings.EarnRiskFreeOnCash || dailyRate <= 0)
        {
            return;
        }

        // Em ações e ETFs o valor comprado sai do caixa; em futuros a margem segue rendendo.
        var invested = _position is not null && !_instrument.IsFuture
            ? _position.EntryPrice * _position.Quantity * _instrument.Multiplier
            : 0m;
        var idle = _cash - invested;
        if (idle <= 0)
        {
            return;
        }

        var interest = idle * dailyRate;
        _cash += interest;
        _interestEarned += interest;
    }

    private void EvaluateStrategy(int index, bool allowNewRisk)
    {
        var history = new History(_bars, index);
        var context = _position is null
            ? PositionContext.None
            : new PositionContext(_position.Direction, _position.EntryPrice, _position.BarsHeld);

        var signal = _strategy.Evaluate(history, context);
        var target = signal.Target == PositionDirection.Short && !_instrument.AllowShort
            ? PositionDirection.Flat
            : signal.Target;

        var addsRisk = target != PositionDirection.Flat && target != _position?.Direction;
        if (addsRisk && !allowNewRisk)
        {
            // Bloqueado: só é possível sair (uma virada de mão vira apenas o encerramento).
            target = PositionDirection.Flat;
            addsRisk = false;
        }

        FeatureVector? features = null;
        double? probability = null;
        if (addsRisk && _captureFeatures)
        {
            features = FeatureExtractor.TryExtract(history, target);
            if (_filter is not null && features is not null)
            {
                var assessment = _filter.Assess(features);
                probability = assessment.Probability;
                if (!assessment.Allowed)
                {
                    // A IA vetou a entrada; se havia posição contrária, ela ainda é encerrada.
                    _signalsFiltered++;
                    target = PositionDirection.Flat;
                    features = null;
                    probability = null;
                }
            }
        }

        if (_position is not null)
        {
            _position.BarsHeld++;
            if (target == _position.Direction)
            {
                // Stop móvel: só pode apertar, nunca afrouxar.
                if (signal.StopPrice is { } stop && _position.IsTighter(stop))
                {
                    _position.Stop = stop;
                }

                _pending = null;
                return;
            }
        }
        else if (target == PositionDirection.Flat)
        {
            _pending = null;
            return;
        }

        var close = history.Current.Close;
        _pending = new PendingOrder(target, signal.StopPrice is { } s ? Math.Abs(close - s) : null, features, probability,
            signal.TargetPrice is { } t ? Math.Abs(t - close) : null);
    }

    private void ExecutePending(Bar bar)
    {
        if (_pending is null)
        {
            return;
        }

        var order = _pending;
        _pending = null;

        if (_position is not null && _position.Direction != order.Target)
        {
            ClosePosition(bar.Open, bar, "sinal");
        }

        if (order.Target != PositionDirection.Flat && _position is null)
        {
            OpenPosition(order, bar);
        }
    }

    private void OpenPosition(PendingOrder order, Bar bar)
    {
        var isBuy = order.Target == PositionDirection.Long;
        var price = _settings.Costs.Fill(bar.Open, isBuy);
        var equity = _cash;
        var quantity = PositionSizer.Size(price, order.StopDistance, equity, _settings.RiskPerTradePercent, _instrument, _settings.Limits);
        if (quantity <= 0)
        {
            _events.Add(new BacktestEvent(bar.Date, "Entrada ignorada: tamanho calculado menor que um lote."));
            return;
        }

        var side = isBuy ? OrderSide.Buy : OrderSide.Sell;
        var request = new OrderRequest(_instrument.Symbol, _instrument.AssetClass, side, quantity, price * _instrument.Multiplier);
        var snapshot = new PortfolioSnapshot(equity, equity, _peakEquity, []);
        var decision = RiskManager.Evaluate(request, snapshot, _settings.Limits, _runningState);
        if (!decision.IsApproved)
        {
            _events.Add(new BacktestEvent(bar.Date, $"Entrada rejeitada pelo risco: {string.Join(" ", decision.Violations)}"));
            return;
        }

        var cost = _settings.Costs.OrderCost(price * quantity * _instrument.Multiplier, quantity, _instrument.IsFuture);
        _cash -= cost;
        _costsPaid += cost;

        decimal? stop = order.StopDistance is { } distance ? price - ((int)order.Target * distance) : null;
        _position = new Position(order.Target, bar.Date, price, quantity, stop, cost)
        {
            TargetPrice = order.TargetDistance is { } td ? price + ((int)order.Target * td) : null,
            EntryTime = bar.Time,
            Features = order.Features,
            Probability = order.Probability,
        };
    }

    /// <summary>
    /// Stop e alvo dentro da barra. Sem dados intrabarra não dá para saber o que veio primeiro: se os dois
    /// cabem na mesma barra, vale o STOP (premissa conservadora) e a operação fica marcada como ambígua.
    /// Gaps executam na abertura; o alvo nunca executa melhor que o próprio preço, salvo em gap a favor.
    /// </summary>
    private void CheckStop(Bar bar)
    {
        if (_position is null)
        {
            return;
        }

        var isLong = _position.Direction == PositionDirection.Long;
        var stopHit = _position.Stop is { } stop && (isLong ? bar.Low <= stop : bar.High >= stop);
        var targetHit = _position.TargetPrice is { } target && (isLong ? bar.High >= target : bar.Low <= target);

        if (stopHit)
        {
            var stopPrice = _position.Stop!.Value;
            _position.Ambiguous = targetHit;
            ClosePosition(isLong ? Math.Min(bar.Open, stopPrice) : Math.Max(bar.Open, stopPrice), bar, targetHit ? "stop (barra ambígua)" : "stop");
        }
        else if (targetHit)
        {
            var targetPrice = _position.TargetPrice!.Value;
            ClosePosition(isLong ? Math.Max(bar.Open, targetPrice) : Math.Min(bar.Open, targetPrice), bar, "alvo");
        }
    }

    private void ClosePosition(decimal rawPrice, Bar bar, string reason)
    {
        var date = bar.Date;
        var position = _position!;
        _position = null;

        var isBuy = position.Direction == PositionDirection.Short;
        var price = _settings.Costs.Fill(rawPrice, isBuy);
        var direction = (int)position.Direction;
        var gross = (price - position.EntryPrice) * position.Quantity * _instrument.Multiplier * direction;
        var exitCost = _settings.Costs.OrderCost(price * position.Quantity * _instrument.Multiplier, position.Quantity, _instrument.IsFuture);
        _cash += gross - exitCost;
        _costsPaid += exitCost;

        var totalCosts = position.EntryCost + exitCost;
        var net = gross - totalCosts;
        var invested = position.EntryPrice * position.Quantity * _instrument.Multiplier;
        _trades.Add(new BacktestTrade(
            position.Direction,
            position.EntryDate,
            Math.Round(position.EntryPrice, 4),
            date,
            Math.Round(price, 4),
            position.Quantity,
            Math.Round(gross, 2),
            Math.Round(totalCosts, 2),
            Math.Round(net, 2),
            invested == 0 ? 0 : Math.Round(net / invested * 100m, 2),
            reason,
            position.Features?.Values,
            position.Probability,
            position.EntryTime,
            bar.Time,
            Math.Round(position.MaxAdverse / position.EntryPrice * 100m, 3),
            Math.Round(position.MaxFavorable / position.EntryPrice * 100m, 3),
            position.Ambiguous));

        var saleAmount = (position.Direction == PositionDirection.Long ? price : position.EntryPrice) * position.Quantity * _instrument.Multiplier;
        _monthTrades.Add(new RealizedTrade(date, Categorize(position.EntryDate, date), net, saleAmount));
    }

    private TaxCategory Categorize(DateOnly entry, DateOnly exit) =>
        entry == exit ? TaxCategory.DayTrade
        : _instrument.AssetClass == AssetClass.Stock ? TaxCategory.StockSwing
        : TaxCategory.OtherSwing;

    private void PayTaxesOnMonthChange(DateOnly date)
    {
        var month = MonthKey(date);
        if (month == _currentMonth)
        {
            return;
        }

        _currentMonth = month;
        PayTaxes(_taxes.CloseMonth(_monthTrades), date);
        _monthTrades.Clear();
    }

    private void PayTaxes(decimal tax, DateOnly date)
    {
        if (!_settings.ApplyTaxes || tax <= 0)
        {
            return;
        }

        _cash -= tax;
        _taxesPaid += tax;
        _events.Add(new BacktestEvent(date, $"DARF de IR pago: R$ {tax:N2}."));
    }

    private decimal MarkToMarket(decimal price) =>
        _position is null
            ? _cash
            : _cash + ((price - _position.EntryPrice) * _position.Quantity * _instrument.Multiplier * (int)_position.Direction);

    private static int MonthKey(DateOnly date) => (date.Year * 12) + date.Month;

    private sealed record PendingOrder(PositionDirection Target, decimal? StopDistance, FeatureVector? Features, double? Probability, decimal? TargetDistance = null);

    private sealed class Position(
        PositionDirection direction,
        DateOnly entryDate,
        decimal entryPrice,
        int quantity,
        decimal? stop,
        decimal entryCost)
    {
        public PositionDirection Direction { get; } = direction;

        public DateOnly EntryDate { get; } = entryDate;

        public decimal EntryPrice { get; } = entryPrice;

        public int Quantity { get; } = quantity;

        public decimal? Stop { get; set; } = stop;

        public decimal EntryCost { get; } = entryCost;

        public int BarsHeld { get; set; }

        public DateTimeOffset? EntryTime { get; init; }

        public decimal? TargetPrice { get; init; }

        public bool Ambiguous { get; set; }

        public decimal MaxFavorable { get; private set; }

        public decimal MaxAdverse { get; private set; }

        /// <summary>Excursões pela máxima e mínima da barra (MFE/MAE), no sentido da posição.</summary>
        public void Track(Bar bar)
        {
            var favorable = Direction == PositionDirection.Long ? bar.High - EntryPrice : EntryPrice - bar.Low;
            var adverse = Direction == PositionDirection.Long ? EntryPrice - bar.Low : bar.High - EntryPrice;
            MaxFavorable = Math.Max(MaxFavorable, favorable);
            MaxAdverse = Math.Max(MaxAdverse, adverse);
        }

        public FeatureVector? Features { get; init; }

        public double? Probability { get; init; }

        public bool IsTighter(decimal candidate) =>
            Stop is null
            || (Direction == PositionDirection.Long && candidate > Stop)
            || (Direction == PositionDirection.Short && candidate < Stop);
    }
}
