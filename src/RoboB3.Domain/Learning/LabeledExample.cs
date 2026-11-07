namespace RoboB3.Domain.Learning;

/// <summary>Operação passada com o contexto da entrada e o resultado (acertou = lucro líquido positivo).</summary>
public sealed record LabeledExample(
    string Symbol,
    DateOnly EntryDate,
    DateOnly ExitDate,
    FeatureVector Features,
    bool IsWin,
    decimal ReturnPercent,
    float Weight = 1f);
