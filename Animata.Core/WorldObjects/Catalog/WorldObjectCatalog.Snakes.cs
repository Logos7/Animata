using System.Numerics;
using Animata.Core.Actuators;
using Animata.Core.Bodies;
using Animata.Core.Brains;
using Animata.Core.Brains.Modules;
using Animata.Core.Brains.Neural;
using Animata.Core.Entities;
using Animata.Core.Sensors;
using Animata.Core.Worlds;

namespace Animata.Core.WorldObjects;

// Węże: plan ciała, CPG, sieć z zegarem i czuciem terenu.
public static partial class WorldObjectCatalog
{
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

    /// <summary>
    /// Plan węża: Seg0 (głowa, korzeń) … Seg{n−1} wzdłuż −X, leżące na ziemi; staw J{k} między Seg{k} a Seg{k+1}
    /// w połowie odstępu. Stawy kulowe: skręt ±52°, pochylenie ±34°, moment 4 N·m. Każdy segment ma łuski
    /// (tarcie w bok 0.8 na krok 1/30 s, do przodu 0.02, do tyłu 0.3).
    /// </summary>
    public static BodyPlan SnakePlan(int aSegments)
    {
        if (!IsValidSnakeLength(aSegments))
            throw new ArgumentOutOfRangeException(nameof(aSegments), aSegments,
                $"Wąż ma od {MinSnakeSegments} do {MaxSnakeSegments} segmentów.");
        var height = SnakeRadius + 0.005f;
        var builder = new BodyPlanBuilder();
        for (var segment = 0; segment < aSegments; segment++)
            builder.Part(new PartPlan($"Seg{segment}", PartShape.Capsule, new Vector3(SnakeRadius, SnakeSegmentLength, 0), 0.3f,
                new Vector3(-segment * SnakeSpacing, 0, height), Quaternion.Identity, SnakeFriction, SnakeLateralFriction, SnakeBackwardFriction));
        for (var joint = 0; joint < aSegments - 1; joint++)
            builder.Joint($"J{joint}", $"Seg{joint}", $"Seg{joint + 1}", new Vector3(-(joint + 0.5f) * SnakeSpacing, 0, height),
                aMaxYaw: 0.9f, aMaxPitch: 0.6f, aStrength: 4);
        return builder.Build();
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
        var eye = new TargetSensor { Slot = "Eye", TargetId = aTargetId };
        var joints = new JointSensor(aSegments - 1) { Slot = "Joints" };
        var clock = new ClockSensor { Slot = "Clock" };
        var feel = new FeelSensor { Slot = "Feel" };
        var spine = new SpineActuator(aSegments - 1) { Slot = "Spine" };
        snake.Body.Sensors.Add(eye);
        snake.Body.Sensors.Add(joints);
        snake.Body.Sensors.Add(clock);
        snake.Body.Sensors.Add(feel);
        snake.Body.Actuators.Add(spine);
        BuildBrain(brain, [("Eye", eye), ("Joints", joints), ("Clock", clock), ("Feel", feel)], aController, spine);
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
        var snake = CreateSnake(aPosition, aYaw, NeuralColor, aTargetId, cpg, aSegments);
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
        if (!IsValidSnakeLength(aSegments))
            throw new ArgumentOutOfRangeException(nameof(aSegments), aSegments,
                $"Wąż ma od {MinSnakeSegments} do {MaxSnakeSegments} segmentów.");
        string[] inputs =
        [
            ClockSensor.SinPort, ClockSensor.CosPort,
            "Found * DirectionY", "Found * DirectionX", "Found * DirectionZ", "Found * Gap / 4",
            FeelSensor.AheadPort, FeelSensor.HeadPitchPort
        ];
        var outputs = SnakeNetworkOutputs(aSegments - 1);
        var hidden = aHidden.Length > 0 ? aHidden : SnakeHiddenLayers;
        var module = new NeuralNetworkModule(new NeuralNetwork([inputs.Length, .. hidden, outputs.Length])) { Name = "Neural" };
        module.Ports.AddRange([ClockSensor.SinPort, ClockSensor.CosPort, .. TargetPorts, TargetSensor.DirectionZPort,
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
    public static readonly int[] SnakeHiddenLayers = [8];

    /// <summary>Wyjścia sieci węża: Yaw0, Pitch0, Yaw1, Pitch1, …</summary>
    public static string[] SnakeNetworkOutputs(int aJoints) =>
        [.. Enumerable.Range(0, aJoints).SelectMany(aJoint => new[] { SpineActuator.YawPort(aJoint), SpineActuator.PitchPort(aJoint) })];

    /// <summary>Wąż z własną siecią neuronową (losowe wagi) — uczy się pełzać bez gotowego CPG, z zegarem rytmu.</summary>
    public static SnakeCreature CreateNeuralSnake(Vector3 aPosition, float aYaw, Guid? aTargetId, int aSegments = DefaultSnakeSegments) =>
        CreateSnake(aPosition, aYaw, NeuralSnakeColor, aTargetId, CreateSnakeNeuralModule(aSegments), aSegments);

    /// <summary>Kolor węża z siecią (odróżnia go od węża z CPG).</summary>
    public static readonly Vector3 NeuralSnakeColor = new(0.35f, 0.75f, 0.45f);
}
