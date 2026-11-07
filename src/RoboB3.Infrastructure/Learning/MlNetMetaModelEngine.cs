using Microsoft.ML;
using Microsoft.ML.Data;
using Microsoft.ML.Trainers.FastTree;
using RoboB3.Application.Abstractions;
using RoboB3.Domain.Learning;

namespace RoboB3.Infrastructure.Learning;

/// <summary>
/// Classificador de árvores com boosting (FastTree, ML.NET). Pequeno e regularizado de propósito:
/// com poucas centenas de operações, modelos grandes só decoram o passado.
/// </summary>
internal sealed class MlNetMetaModelEngine : IMetaModelEngine
{
    private const int Seed = 42;

    public byte[] Train(IReadOnlyList<LabeledExample> examples)
    {
        ArgumentNullException.ThrowIfNull(examples);
        var ml = new MLContext(Seed);
        var data = ml.Data.LoadFromEnumerable(examples.Select(e => new ModelInput
        {
            Features = e.Features.Values.ToArray(),
            Label = e.IsWin,
            Weight = e.Weight,
        }));

        var trainer = ml.BinaryClassification.Trainers.FastTree(new FastTreeBinaryTrainer.Options
        {
            NumberOfLeaves = 8,
            NumberOfTrees = 60,
            MinimumExampleCountPerLeaf = 10,
            LearningRate = 0.05,
            FeatureFraction = 0.8,
            ExampleWeightColumnName = nameof(ModelInput.Weight),
        });

        var model = trainer.Fit(data);
        using var stream = new MemoryStream();
        ml.Model.Save(model, data.Schema, stream);
        return stream.ToArray();
    }

    public IProbabilityModel Load(byte[] modelBytes)
    {
        ArgumentNullException.ThrowIfNull(modelBytes);
        var ml = new MLContext(Seed);
        using var stream = new MemoryStream(modelBytes);
        var model = ml.Model.Load(stream, out _);
        return new Predictor(ml.Model.CreatePredictionEngine<ModelInput, ModelOutput>(model));
    }

    /// <summary>PredictionEngine não é thread-safe: o acesso é serializado.</summary>
    private sealed class Predictor(PredictionEngine<ModelInput, ModelOutput> engine) : IProbabilityModel
    {
        private readonly Lock _lock = new();

        public double Predict(FeatureVector features)
        {
            ArgumentNullException.ThrowIfNull(features);
            lock (_lock)
            {
                return engine.Predict(new ModelInput { Features = features.Values.ToArray() }).Probability;
            }
        }
    }

    private sealed class ModelInput
    {
        [VectorType(FeatureVector.Count)]
        public float[] Features { get; set; } = [];

        public bool Label { get; set; }

        public float Weight { get; set; } = 1f;
    }

    private sealed class ModelOutput
    {
        public float Probability { get; set; }
    }
}
