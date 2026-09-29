using System.Numerics;
using System.Runtime.CompilerServices;
using Animata.Core.Actuators;
using Animata.Core.Bodies;
using Animata.Core.Brains;
using Animata.Core.Brains.Modules;
using Animata.Core.Brains.Neural;
using Animata.Core.Sensors;

namespace Animata.Core.WorldObjects;

// Węże: plan ciała, CPG, sieć z zegarem i czuciem terenu.
public static partial class WorldObjectCatalog
{
    /// <summary>
    /// Zakres skrętu i pochylenia stawu węża (rad) i moment serwa (N·m). ±1.2 rad w obu osiach — tyle trzeba, żeby
    /// owinąć się ciasno wokół pnia r 0.25 m (odstęp segmentów 0.36 m); 8 N·m — żeby zwój ścisnął pień i utrzymał ciężar.
    /// </summary>
    public const float SnakeMaxYaw = 1.2f;
    public const float SnakeMaxPitch = 1.2f;
    public const float SnakeJointStrength = 8;

    public const int MinSnakeSegments = 2;
    public const int MaxSnakeSegments = 24;
    public const int DefaultSnakeSegments = 8;

    /// <summary>Promień segmentu węża i długość walca kapsuły (bez półkul).</summary>
    public const float SnakeRadius = 0.07f;
    public const float SnakeSegmentLength = 0.2f;

    /// <summary>Odstęp środków kolejnych segmentów: kapsuła + mała szczelina na staw.</summary>
    public const float SnakeSpacing = SnakeSegmentLength + 2 * SnakeRadius + 0.02f;

    /// <summary>Tarcie łusek węża: małe zwykłe (sunie do przodu), duże w bok, średnie do tyłu (Coulomb, patrz PartPlan).</summary>
    public const float SnakeFriction = 0.1f;
    public const float SnakeLateralFriction = 1.5f;
    public const float SnakeBackwardFriction = 0.5f;

    public static bool IsValidSnakeLength(int aSegments) => aSegments is >= MinSnakeSegments and <= MaxSnakeSegments;

    /// <summary>Rzuca <see cref="ArgumentOutOfRangeException"/>, gdy wąż miałby za mało albo za dużo segmentów.</summary>
    public static void CheckSnakeLength(int aSegments, [CallerArgumentExpression(nameof(aSegments))] string? aName = null)
    {
        if (!IsValidSnakeLength(aSegments))
            throw new ArgumentOutOfRangeException(aName, aSegments, $"Wąż ma od {MinSnakeSegments} do {MaxSnakeSegments} segmentów.");
    }

    /// <summary>
    /// Plan węża: Seg0 (głowa, korzeń) … Seg{n−1} wzdłuż −X, leżące na ziemi; staw J{k} między Seg{k} a Seg{k+1}
    /// w połowie odstępu. Stawy kulowe: skręt i pochylenie ±1.2 rad (±69°), moment 8 N·m. Każdy segment ma łuski:
    /// małe tarcie zwykłe <see cref="SnakeFriction"/>, duże w bok <see cref="SnakeLateralFriction"/> i średnie do tyłu
    /// <see cref="SnakeBackwardFriction"/> (współczynniki Coulomba, patrz <see cref="PartPlan"/>).
    /// </summary>
    public static BodyPlan SnakePlan(int aSegments)
    {
        CheckSnakeLength(aSegments);
        var height = SnakeRadius + 0.005f;
        var builder = new BodyPlanBuilder();
        for (var segment = 0; segment < aSegments; segment++)
            builder.Part(new PartPlan($"Seg{segment}", PartShape.Capsule, new Vector3(SnakeRadius, SnakeSegmentLength, 0), 0.3f,
                new Vector3(-segment * SnakeSpacing, 0, height), Quaternion.Identity, SnakeFriction, SnakeLateralFriction, SnakeBackwardFriction));
        for (var joint = 0; joint < aSegments - 1; joint++)
            builder.Joint($"J{joint}", $"Seg{joint}", $"Seg{joint + 1}", new Vector3(-(joint + 0.5f) * SnakeSpacing, 0, height),
                aMaxYaw: SnakeMaxYaw, aMaxPitch: SnakeMaxPitch, aStrength: SnakeJointStrength);
        return builder.Build();
    }

    /// <summary>
    /// CPG wspinacza: toczenie zwoju — skręt i pochylenie każdego stawu to ten sam wektor zgięcia obracający się w czasie
    /// (Yaw = A sin φ, Pitch = A sin(φ + π/2)), o długości zgięcia helisy owiniętej wokół pnia r 0.25 m. Zwój toczy się po korze
    /// i wkręca w górę (~0.35 m/s przy 1.2 Hz; na pniu 2–3.5 m dochodzi do kuli na szczycie w 5 z 6 prób). Bez skrętu do celu — na pniu cel jest „nad głową”.
    /// </summary>
    public static CpgModule CreateClimbingCpg(int aSegments = DefaultSnakeSegments)
    {
        var (yaw, pitch) = SnakeWrap.BendFor(Training.SeekRigs.ClimbTrunkRadius + SnakeRadius - SnakeWrap.Squeeze);
        var bend = MathF.Sqrt(yaw * yaw + pitch * pitch) / SnakeMaxYaw;
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
    public static CpgModule CreateCpg(int aSegments = DefaultSnakeSegments) => new(aSegments - 1) { Name = "CPG" };

    /// <summary>
    /// Wąż: oko (slot „Eye”) na głowie, czucie stawów („Joints”), zegar rytmu („Clock”), czucie terenu („Feel”), kręgosłup („Spine”),
    /// mózg sensory → controller → spine.
    /// Controller musi mieć wyjścia Yaw{i}/Pitch{i} dla wszystkich n−1 stawów (np. <see cref="CreateCpg"/>).
    /// </summary>
    public static SnakeCreature CreateSnake(Vector3 aPosition, float aYaw, Vector3 aColor, Guid? aTargetId, BrainModule aController,
        int aSegments = DefaultSnakeSegments)
    {
        var brain = new Brain();
        var snake = new SnakeCreature(aSegments, brain) { Color = aColor };
        snake.Equip();
        Aim(snake, aTargetId);
        BuildBrain(brain, aController);
        snake.Place(aPosition, Quaternion.CreateFromAxisAngle(Vector3.UnitZ, aYaw));
        return snake;
    }

    /// <summary>
    /// Wąż gotowy do nauki: ręczne parametry CPG zapisane jako snapshot „ręczne parametry”, a CPG wylosowane —
    /// uczy się pełzać od zera (jak fioletowy walec w demo).
    /// </summary>
    public static SnakeCreature CreateLearningSnake(Vector3 aPosition, float aYaw, Guid? aTargetId, int aSegments = DefaultSnakeSegments)
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
    public static NeuralNetworkModule CreateSnakeNeuralModule(int aSegments = DefaultSnakeSegments, params int[] aHidden)
    {
        CheckSnakeLength(aSegments);
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
    public static SnakeCreature CreateNeuralSnake(Vector3 aPosition, float aYaw, Guid? aTargetId, int aSegments = DefaultSnakeSegments) =>
        CreateSnake(aPosition, aYaw, RandomColor(), aTargetId, CreateSnakeNeuralModule(aSegments), aSegments);

}
