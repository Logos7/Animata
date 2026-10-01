using Animata.Core.Brains;
using Animata.Core.Brains.Modules;
using Animata.Core.Brains.Neural;
using Animata.Core.WorldObjects;

namespace Animata.Tests;

public class NeuralTests
{
    [Theory]
    [InlineData("1 + 2 * 3", 7f)]
    [InlineData("(1 + 2) * 3", 9f)]
    [InlineData("-a / 4 + b", 0.5f)]
    [InlineData("2 * -(a - b)", -2f)]
    public void Expressions_FollowPrecedence(string aExpression, float aExpected)
    {
        var compiled = SensorExpression.Compile(aExpression);
        Assert.Equal(aExpected, compiled.Evaluate(new Dictionary<string, float> { ["a"] = 2, ["b"] = 1 }));
    }

    [Theory]
    [InlineData("1 +")]
    [InlineData("(a")]
    [InlineData("a b")]
    [InlineData("1..2")]
    public void Expressions_RejectBadSyntax(string aExpression) =>
        Assert.Throws<FormatException>(() => SensorExpression.Compile(aExpression));

    /// <summary>Porty ciał mają polskie litery (dotyk „Tułów”, czucie mięśni „DłBiodrowyL”) — wyrażenie musi je przyjąć.</summary>
    [Fact]
    public void Expressions_AcceptPortNamesWithPolishLetters()
    {
        var compiled = SensorExpression.Compile("Tułów + 2 * DłPośladkowyŚrL");
        Assert.Equal(2, compiled.Variables.Count);
        Assert.Contains("Tułów", compiled.Variables);
        Assert.Equal(7f, compiled.Evaluate(new Dictionary<string, float> { ["Tułów"] = 1, ["DłPośladkowyŚrL"] = 3 }));
    }

    /// <summary>Ogólna sieć (wszystkie porty zmysłów na wejściu) daje się zbudować dla humanoida mięśniowego.</summary>
    [Fact]
    public void GeneralNetwork_TakesEverySensePort_OfTheMuscleHumanoid()
    {
        var humanoid = (Creature)MuscleHumanoid.Design.Type.Create();
        var network = CreatureDesign.GeneralNetwork(humanoid);
        Assert.Contains("Tułów", network.InputPorts);
        Assert.Contains(Animata.Core.Sensors.MuscleSensor.ForcePort("PłaszczkowatyL"), network.InputPorts);
        WorldObjectCatalog.BuildBrain(humanoid.Brain!, network);
    }

    [Fact]
    public void Parameters_RoundTrip_InDocumentedOrder()
    {
        var network = new NeuralNetwork(2, 3, 1);
        var parameters = Enumerable.Range(0, network.ParameterCount).Select(aIndex => (float)aIndex).ToArray();
        network.SetParameters(parameters);

        Assert.Equal(parameters, network.GetParameters());
        Assert.Equal(0f, network.Weights[0][0][0]);
        Assert.Equal(2f, network.Biases[0][0]);         // warstwa → neuron → (wagi, potem bias)
        Assert.Equal(13, network.ParameterCount);
    }

    [Fact]
    public void Create_UsesShapeAndBindings_ButNotTemplateWeights()
    {
        var template = WorldObjectCatalog.CreateCarNeuralModule();
        var state = (NeuralNetworkState)template.CaptureState();
        var parameters = Enumerable.Repeat(0.5f, state.ParameterCount).ToArray();

        var module = (NeuralNetworkModule)state.CreateTrainable(parameters, "Neural");

        Assert.Equal(parameters, module.Network.GetParameters());
        Assert.Equal(template.Ports, module.Ports);
        Assert.Equal(template.Inputs.Select(aInput => aInput.Expression), module.Inputs.Select(aInput => aInput.Expression));
        Assert.Equal(template.OutputPorts, module.OutputPorts);
        module.Validate();
    }

    [Fact]
    public void Evolution_ImprovesAndIsDeterministic()
    {
        static float Fitness(float[] aGenes, int aGeneration) => -aGenes.Sum(aGene => (aGene - 1) * (aGene - 1));

        float Run()
        {
            var evolution = new Animata.Core.Training.Evolution(new float[5], new Animata.Core.Training.EvolutionOptions { Seed = 9 });
            for (var generation = 0; generation < 40; generation++)
                evolution.Step(Fitness);
            return evolution.BestFitness;
        }

        var first = Run();
        Assert.True(first > -0.5f, $"fitness {first}");  // start: -5
        Assert.Equal(first, Run());
    }
}
