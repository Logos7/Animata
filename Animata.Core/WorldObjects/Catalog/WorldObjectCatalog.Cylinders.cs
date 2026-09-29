using System.Numerics;
using Animata.Core.Actuators;
using Animata.Core.Brains;
using Animata.Core.Brains.Modules;
using Animata.Core.Brains.Neural;
using Animata.Core.Sensors;

namespace Animata.Core.WorldObjects;

// Walce: sieć 3-6-2 z ręcznymi wagami i gotowe walce.
public static partial class WorldObjectCatalog
{
    /// <summary>Sieć walca z losowymi wagami: te same wejścia i wyjścia co <see cref="CreateCylinderNeuralModule()"/>, podane warstwy ukryte.</summary>
    public static NeuralNetworkModule CreateCylinderNeuralModule(params int[] aHidden)
    {
        var module = new NeuralNetworkModule(new NeuralNetwork([3, .. aHidden, 2])) { Name = "Neural" };
        module.Ports.AddRange(TargetSensor.SteeringPorts);
        module.Inputs.Add(new NeuralInput("Found * DirectionY"));
        module.Inputs.Add(new NeuralInput("Found * Gap"));
        module.Inputs.Add(new NeuralInput("Found * DirectionX"));
        module.Outputs.Add(new NeuralOutput(DiskDriveActuator.TurnPort));
        module.Outputs.Add(new NeuralOutput(DiskDriveActuator.StepPort));
        return module;
    }

    /// <summary>
    /// Sieć 3-6-2 z ręcznie ustawionymi wagami startowymi (punkt wyjścia do ewolucji):
    /// Turn ≈ tanh(2·tanh(3·DirectionY)), Step ≈ tanh(2·tanh(2·Gap) + 2·tanh(3·DirectionX) − 2).
    /// Step spada do zera przy styku z celem i gdy cel jest z boku lub z tyłu.
    /// </summary>
    public static NeuralNetworkModule CreateCylinderNeuralModule()
    {
        var module = CreateCylinderNeuralModule(6);
        var weights = module.Network.Weights;
        var biases = module.Network.Biases;
        for (var layer = 0; layer < weights.Length; layer++)
            for (var neuron = 0; neuron < weights[layer].Length; neuron++)
            {
                Array.Fill(weights[layer][neuron], 0);
                biases[layer][neuron] = 0;
            }

        weights[0][0][0] = 3;   // h0 ← kierunek w bok
        weights[0][1][1] = 2;   // h1 ← szczelina
        weights[0][2][2] = 3;   // h2 ← kierunek do przodu
        weights[1][0][0] = 2;   // Turn ← h0
        weights[1][1][1] = 2;   // Step ← h1
        weights[1][1][2] = 2;   // Step ← h2
        biases[1][1] = -2;      // Step wymaga jednocześnie szczeliny i celu z przodu
        return module;
    }

    /// <summary>Stwór z okiem namierzającym cel i napędem różnicowym, sterowany podanym modułem.</summary>
    public static CylinderCreature CreateSeeker(Vector3 aPosition, Vector3 aColor, Guid? aTargetId, BrainModule aController)
    {
        var brain = new Brain();
        var creature = new CylinderCreature(brain) { Color = aColor };
        creature.Place(aPosition, Quaternion.Identity);
        var eye = new TargetSensor { Slot = "Eye", TargetId = aTargetId };
        var wheels = new DiskDriveActuator { Slot = "Wheels" };
        creature.Body.Sensors.Add(eye);
        creature.Body.Actuators.Add(wheels);
        BuildBrain(brain, [("Eye", eye)], aController, wheels);
        return creature;
    }

    public static CylinderCreature CreateControllerSeeker(Vector3 aPosition, Guid? aTargetId) =>
        CreateSeeker(aPosition, RandomColor(), aTargetId, new ApproachTargetModule { Name = "Approach" });

    public static CylinderCreature CreateNeuralSeeker(Vector3 aPosition, Guid? aTargetId) =>
        CreateSeeker(aPosition, RandomColor(), aTargetId, CreateCylinderNeuralModule());

    /// <summary>
    /// Walec z siecią gotową do nauki: ręczne wagi zapisane w mózgu jako snapshot „ręczne wagi”, sieć wylosowana.
    /// </summary>
    public static CylinderCreature CreateLearningSeeker(Vector3 aPosition, Guid? aTargetId)
    {
        var creature = CreateNeuralSeeker(aPosition, aTargetId);
        var network = creature.Brain!.Graph.Modules.OfType<NeuralNetworkModule>().Single();
        creature.Brain.Capture("ręczne wagi", network);
        network.Network.Randomize();
        return creature;
    }
}
