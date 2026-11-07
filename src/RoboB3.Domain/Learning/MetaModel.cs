using RoboB3.Domain.Common;

namespace RoboB3.Domain.Learning;

public enum MetaModelStatus
{
    /// <summary>Em uso pelo robô e pelos backtests com filtro.</summary>
    Active = 1,

    /// <summary>Já foi usado e foi substituído ou deixou de ajudar.</summary>
    Retired = 2,

    /// <summary>Treinado, mas não passou nos critérios de promoção.</summary>
    Rejected = 3,
}

/// <summary>Modelo de filtro de sinais de uma estratégia, com a evidência que justificou (ou não) o seu uso.</summary>
public sealed class MetaModel
{
    private MetaModel()
    {
        StrategyKey = string.Empty;
        Decision = string.Empty;
        FeatureImportance = new Dictionary<string, double>();
    }

    public MetaModel(
        string strategyKey,
        DateTimeOffset createdAt,
        DateOnly trainingFrom,
        DateOnly splitDate,
        DateOnly validationTo,
        int trainingExamples,
        double threshold,
        ValidationReport validation,
        EconomicTest economics,
        IReadOnlyDictionary<string, double> featureImportance,
        byte[]? modelBytes)
    {
        ArgumentNullException.ThrowIfNull(validation);
        ArgumentNullException.ThrowIfNull(economics);
        Id = Guid.NewGuid();
        StrategyKey = Guard.NotBlank(strategyKey, nameof(strategyKey));
        CreatedAt = createdAt;
        TrainingFrom = trainingFrom;
        SplitDate = splitDate;
        ValidationTo = validationTo;
        TrainingExamples = trainingExamples;
        ValidationExamples = validation.Examples;
        Threshold = threshold;
        Auc = validation.Auc;
        BaseWinRatePercent = validation.BaseWinRatePercent;
        FilteredWinRatePercent = validation.FilteredWinRatePercent;
        BaseAverageReturnPercent = validation.BaseAverageReturnPercent;
        FilteredAverageReturnPercent = validation.FilteredAverageReturnPercent;
        KeptPercent = validation.KeptPercent;
        UnfilteredSharpe = economics.UnfilteredSharpe;
        FilteredSharpe = economics.CandidateSharpe;
        ChampionSharpe = economics.ChampionSharpe;
        FeatureImportance = featureImportance ?? throw new ArgumentNullException(nameof(featureImportance));
        ModelBytes = modelBytes;
        Status = MetaModelStatus.Rejected;
        Decision = "Em avaliação.";
    }

    public Guid Id { get; private set; }

    public string StrategyKey { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public MetaModelStatus Status { get; private set; }

    /// <summary>Motivo da promoção, rejeição ou aposentadoria, em linguagem simples.</summary>
    public string Decision { get; private set; }

    public DateOnly TrainingFrom { get; private set; }

    /// <summary>Operações antes desta data treinam; a partir dela, validam.</summary>
    public DateOnly SplitDate { get; private set; }

    public DateOnly ValidationTo { get; private set; }

    public int TrainingExamples { get; private set; }

    public int ValidationExamples { get; private set; }

    /// <summary>Probabilidade mínima de acerto para o sinal passar.</summary>
    public double Threshold { get; private set; }

    public double Auc { get; private set; }

    public decimal BaseWinRatePercent { get; private set; }

    public decimal FilteredWinRatePercent { get; private set; }

    public decimal BaseAverageReturnPercent { get; private set; }

    public decimal FilteredAverageReturnPercent { get; private set; }

    public decimal KeptPercent { get; private set; }

    public decimal UnfilteredSharpe { get; private set; }

    public decimal FilteredSharpe { get; private set; }

    public decimal? ChampionSharpe { get; private set; }

    /// <summary>Quanto a AUC cai ao embaralhar cada variável: o que o modelo mais usa para decidir.</summary>
    public IReadOnlyDictionary<string, double> FeatureImportance { get; private set; }

    public byte[]? ModelBytes { get; private set; }

    public DateTimeOffset? ActivatedAt { get; private set; }

    public DateTimeOffset? RetiredAt { get; private set; }

    /// <summary>Análise em texto escrita pelo Claude (opcional).</summary>
    public string? Analysis { get; private set; }

    public void Activate(string reason, DateTimeOffset now)
    {
        if (ModelBytes is null)
        {
            throw new DomainException("Modelo sem conteúdo treinado não pode ser ativado.");
        }

        Status = MetaModelStatus.Active;
        Decision = Guard.NotBlank(reason, nameof(reason));
        ActivatedAt = now;
    }

    public void Reject(string reason) => Decision = Guard.NotBlank(reason, nameof(reason));

    public void Retire(string reason, DateTimeOffset now)
    {
        if (Status != MetaModelStatus.Active)
        {
            throw new DomainException("Só um modelo ativo pode ser aposentado.");
        }

        Status = MetaModelStatus.Retired;
        Decision = $"{Decision} | Aposentado: {Guard.NotBlank(reason, nameof(reason))}";
        RetiredAt = now;
    }

    public void SetAnalysis(string analysis) => Analysis = Guard.NotBlank(analysis, nameof(analysis));
}
