using System.Numerics;
using System.Runtime.CompilerServices;
using Animata.Core.Actuators;
using Animata.Core.Bodies;
using Animata.Core.Brains;
using Animata.Core.Brains.Modules;
using Animata.Core.Brains.Neural;
using Animata.Core.Sensors;

namespace Animata.Core.WorldObjects;

// Węże: CPG i sieć z zegarem i czuciem terenu (ciało — projekt Snake).
public static partial class WorldObjectCatalog
{
    /// <summary>
    /// CPG wspinacza: toczenie zwoju — skręt i pochylenie każdego stawu to ten sam wektor zgięcia obracający się w czasie
    /// (Yaw = A sin φ, Pitch = A sin(φ + π/2)), o długości zgięcia helisy owiniętej wokół pnia r 0.25 m. Zwój toczy się po korze
    /// i wkręca w górę (~0.35 m/s przy 1.2 Hz; na pniu 2–3.5 m dochodzi do kuli na szczycie w 5 z 6 prób). Bez skrętu do celu — na pniu cel jest „nad głową”.
    /// </summary>
    public static CpgModule CreateClimbingCpg(int aSegments = Snake.DefaultSegments)
    {
        var (yaw, pitch) = SnakeWrap.BendFor(Training.SeekRigs.ClimbTrunkRadius + Snake.SegmentRadius - SnakeWrap.Squeeze);
        var bend = MathF.Sqrt(yaw * yaw + pitch * pitch) / Snake.MaxYaw;
        return new CpgModule(aSegments - 1)
        {
            Name = "CPG",
            Amplitude = bend,
            PitchAmplitude = bend,
            PitchPhase = MathF.PI / 2,
            PhaseLag = 0,
            Frequency = 1.2f,
            TurnGain = 0,
            Grip = true
        };
    }

    /// <summary>CPG węża o ręcznie dobranych parametrach (fala od głowy do ogona, skręt do celu).</summary>
    public static CpgModule CreateCpg(int aSegments = Snake.DefaultSegments) => new(aSegments - 1) { Name = "CPG" };

    /// <summary>
    /// Wąż: oko (slot „Eye”) na głowie, czucie stawów („Joints”), zegar rytmu („Clock”), czucie terenu („Feel”), kręgosłup („Spine”),
    /// mózg sensory → controller → spine.
    /// Controller musi mieć wyjścia Yaw{i}/Pitch{i} dla wszystkich n−1 stawów (np. <see cref="CreateCpg"/>).
    /// </summary>
    public static Creature CreateSnake(Vector3 aPosition, float aYaw, Vector3 aColor, Guid? aTargetId, BrainModule aController,
        int aSegments = Snake.DefaultSegments)
    {
        Snake.CheckLength(aSegments);
        return new Spawn(Snake.Design.Type)
        {
            Settings = Spawn.Configure<Creature>(aSnake =>
            {
                Snake.SetSegments(aSnake, aSegments);
                aSnake.Color = aColor;
            }),
            Slots = Spawn.Aim(aTargetId),
            Brain = Spawn.Controller(aController),
            Pose = Spawn.At(aPosition, aYaw)
        }.Build<Creature>();
    }

    /// <summary>
    /// Wąż gotowy do nauki: ręczne parametry CPG zapisane jako snapshot „ręczne parametry”, a CPG wylosowane —
    /// uczy się pełzać od zera (jak fioletowy walec w demo).
    /// </summary>
    public static Creature CreateLearningSnake(Vector3 aPosition, float aYaw, Guid? aTargetId, int aSegments = Snake.DefaultSegments)
    {
        var cpg = CreateCpg(aSegments);
        var snake = CreateSnake(aPosition, aYaw, RandomColor(), aTargetId, cpg, aSegments);
        snake.Brain!.Capture("ręczne parametry", cpg);
        cpg.Randomize();
        return snake;
    }

    /// <summary>
    /// Sieć węża: wejścia Sin, Cos (zegar rytmu), kierunek do celu (bok, przód, góra), szczelina/4 i czucie terenu
    /// (próg przed głową, pochylenie głowy — <see cref="FeelSensor"/>); wyjścia Yaw{k}, Pitch{k}
    /// na przemian (Yaw0, Pitch0, Yaw1, …), więc dołożony staw dopisuje wyjścia na końcu. Wagi losowe.
    /// </summary>
    public static NeuralNetworkModule CreateSnakeNeuralModule(int aSegments = Snake.DefaultSegments, params int[] aHidden)
    {
        Snake.CheckLength(aSegments);
        string[] inputs =
        [
            ClockSensor.SinPort, ClockSensor.CosPort,
            "Found * DirectionY", "Found * DirectionX", "Found * DirectionZ", "Found * Gap / 4",
            FeelSensor.AheadPort, FeelSensor.HeadPitchPort
        ];
        var outputs = JointNetworkOutputs(aSegments - 1);
        var hidden = aHidden.Length > 0 ? aHidden : SnakeHiddenLayers;
        var module = new NeuralNetworkModule(new NeuralNetwork([inputs.Length, .. hidden, outputs.Length])) { Name = "Neural" };
        module.Ports.AddRange([ClockSensor.SinPort, ClockSensor.CosPort, .. TargetSensor.SteeringPorts, TargetSensor.DirectionZPort,
            FeelSensor.AheadPort, FeelSensor.HeadPitchPort]);
        module.Inputs.AddRange(inputs.Select(aExpression => new NeuralInput(aExpression)));
        module.Outputs.AddRange(outputs.Select(aPort => new NeuralOutput(aPort)));
        // Czucie terenu startuje z zerowymi wagami: sieć zaczyna tak, jakby go nie było, a ewolucja dokłada je, gdy pomaga
        // (z losowymi wagami od początku zaszumiało start — w pomiarze uczyła się wolniej).
        for (var input = inputs.Length - 2; input < inputs.Length; input++)
            foreach (var neuron in module.Network.Weights[0])
                neuron[input] = 0;
        return module;
    }

    /// <summary>Domyślne warstwy ukryte sieci węża.</summary>
    public static readonly int[] SnakeHiddenLayers = [12, 12];

    /// <summary>Wąż z własną siecią neuronową (losowe wagi) — uczy się pełzać bez gotowego CPG, z zegarem rytmu.</summary>
    public static Creature CreateNeuralSnake(Vector3 aPosition, float aYaw, Guid? aTargetId, int aSegments = Snake.DefaultSegments) =>
        CreateSnake(aPosition, aYaw, RandomColor(), aTargetId, CreateSnakeNeuralModule(aSegments), aSegments);

}
