namespace RoboB3.Application.Backtesting;

/// <summary>Parâmetros comuns de simulação.</summary>
public sealed record SimulationSettings(
    decimal InitialCapital = 100_000m,
    decimal RiskPerTradePercent = 1m,
    decimal MaxPositionPercent = 100m,
    decimal? RiskFreeRatePercent = null,
    bool ApplyTaxes = true);

public sealed record BacktestRequest(
    string Symbol,
    string StrategyKey,
    IReadOnlyDictionary<string, decimal>? Parameters,
    DateOnly? From,
    DateOnly? To,
    SimulationSettings Settings,
    bool UseMetaModel = false,
    Domain.MarketData.Timeframe Timeframe = Domain.MarketData.Timeframe.D1,
    bool FlattenAtDayEnd = false);

public sealed record ComparisonRequest(
    IReadOnlyList<string>? Symbols,
    DateOnly? From,
    DateOnly? To,
    SimulationSettings Settings,
    decimal InSamplePercent = 70m,
    Domain.MarketData.Timeframe Timeframe = Domain.MarketData.Timeframe.D1,
    bool FlattenAtDayEnd = false,
    int Folds = 1);

/// <summary>Uma janela fora da amostra do walk-forward: parâmetros escolhidos com tudo o que veio antes dela.</summary>
public sealed record FoldResult(
    DateOnly From,
    DateOnly To,
    IReadOnlyDictionary<string, decimal> Parameters,
    decimal Sharpe,
    decimal ReturnPercent,
    decimal BenchmarkReturnPercent,
    decimal RiskFreeReturnPercent,
    int Trades);

/// <summary>Resultado de uma estratégia num ativo: parâmetros escolhidos dentro da amostra, avaliados fora dela.</summary>
public sealed record ComparisonRow(
    string Symbol,
    string StrategyKey,
    IReadOnlyDictionary<string, decimal> Parameters,
    decimal InSampleSharpe,
    decimal InSampleReturnPercent,
    decimal OutOfSampleSharpe,
    decimal OutOfSampleReturnPercent,
    decimal OutOfSampleMaxDrawdownPercent,
    int OutOfSampleTrades,
    decimal OutOfSampleBenchmarkReturnPercent,
    decimal OutOfSampleRiskFreeReturnPercent,
    int Folds = 1,
    int FoldsBeatingRiskFree = 0,
    int FoldsBeatingBenchmark = 0,
    decimal ExpectancyPerTrade = 0,
    decimal ExpectancyMargin95 = 0,
    IReadOnlyList<FoldResult>? FoldResults = null);

public sealed record StrategyRanking(
    string StrategyKey,
    string StrategyName,
    int Assets,
    decimal AverageOutOfSampleSharpe,
    decimal AverageOutOfSampleReturnPercent,
    decimal WorstOutOfSampleDrawdownPercent,
    decimal BeatsBuyAndHoldPercent,
    decimal BeatsRiskFreePercent,
    int AssetsWithTrades = 0,
    decimal FoldsBeatingRiskFreePercent = 0);

public sealed record ComparisonResult(
    DateOnly InSampleFrom,
    DateOnly OutOfSampleFrom,
    DateOnly OutOfSampleTo,
    IReadOnlyList<StrategyRanking> Ranking,
    IReadOnlyList<ComparisonRow> Rows,
    IReadOnlyList<string> Skipped,
    int TrialsEvaluated = 0,
    Guid? ExperimentId = null);
