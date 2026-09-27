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

/// <summary>Uchwyty do demo: świat i stwory.</summary>
public sealed record DemoScene(World World, IReadOnlyList<ActiveEntity> Creatures);

public static class WorldObjectCatalog
{
    private static readonly string[] TargetPorts =
    [
        TargetSensor.FoundPort, TargetSensor.GapPort, TargetSensor.DirectionXPort, TargetSensor.DirectionYPort
    ];

    /// <summary>Domyślna liczba wąsów autka: 5 promieni co 30° (−60°…+60°) — na niej dobrano parametry sterownika.</summary>
    public const int DefaultWhiskers = 5;

    /// <summary>Najwięcej wąsów autka (co 5° w wachlarzu 120°).</summary>
    public const int MaxWhiskers = 25;

    /// <summary>Szerokość wachlarza wąsów autka (rad), niezależnie od ich liczby.</summary>
    public const float WhiskerSpread = 120 * MathF.PI / 180;

    public const float WhiskerRange = 3;

    /// <summary>Dozwolone liczby wąsów autka: 1, 3, …, <see cref="MaxWhiskers"/>.</summary>
    public static IReadOnlyList<int> WhiskerCounts { get; } =
        [.. Enumerable.Range(0, MaxWhiskers / 2 + 1).Select(aIndex => 2 * aIndex + 1)];

    /// <summary>Nieparzysta, od 1 do <see cref="MaxWhiskers"/> — środkowy wąs zawsze patrzy prosto przed maskę.</summary>
    public static bool IsValidWhiskerCount(int aCount) => aCount is >= 1 and <= MaxWhiskers && aCount % 2 == 1;

    /// <summary>Kąty wąsów autka: <paramref name="aCount"/> promieni równo w wachlarzu <see cref="WhiskerSpread"/>.</summary>
    public static float[] WhiskerAnglesFor(int aCount)
    {
        if (!IsValidWhiskerCount(aCount))
            throw new ArgumentOutOfRangeException(nameof(aCount), aCount,
                $"Autko ma nieparzystą liczbę wąsów od 1 do {MaxWhiskers}.");
        return [.. RaySensor.Fan(aCount, WhiskerSpread).Angles];
    }

    /// <summary>Liczba wąsów stwora (promieni jego pierwszego <see cref="RaySensor"/>), 0 — bez wąsów.</summary>
    public static int WhiskerCountOf(Entity aEntity) =>
        aEntity.Body.Sensors.OfType<RaySensor>().FirstOrDefault()?.Angles.Count ?? 0;

    public static TargetBall CreateTargetBall(Vector3 aPosition) => new()
    {
        Body = { Position = aPosition }
    };

    /// <summary>
    /// Sieć 3-6-2 z ręcznie ustawionymi wagami startowymi (punkt wyjścia do ewolucji):
    /// Turn ≈ tanh(2·tanh(3·DirectionY)), Step ≈ tanh(2·tanh(2·Gap) + 2·tanh(3·DirectionX) − 2).
    /// Step spada do zera przy styku z celem i gdy cel jest z boku lub z tyłu.
    /// </summary>
    public static NeuralNetworkModule CreateNeuralModule()
    {
        var module = new NeuralNetworkModule(new NeuralNetwork(3, 6, 2)) { Name = "Neural" };
        module.Ports.AddRange(TargetPorts);
        module.Inputs.Add(new NeuralInput("Found * DirectionY"));
        module.Inputs.Add(new NeuralInput("Found * Gap"));
        module.Inputs.Add(new NeuralInput("Found * DirectionX"));
        module.Outputs.Add(new NeuralOutput(DiskDriveActuator.TurnPort));
        module.Outputs.Add(new NeuralOutput(DiskDriveActuator.StepPort));

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

    public static readonly Vector3 ControllerColor = new(0.24f, 0.68f, 0.9f);
    public static readonly Vector3 NeuralColor = new(0.72f, 0.36f, 0.9f);

    /// <summary>
    /// Mózg: sensory → controller → napęd. Każde wejście controllera musi pochodzić z któregoś sensora
    /// (literówka w nazwie portu to błąd, a nie ciche zero), a controller musi mieć wszystkie wyjścia napędu.
    /// </summary>
    public static void BuildBrain(Brain aBrain, IReadOnlyList<(string Name, Sensor Sensor)> aSensors,
        BrainModule aController, Actuator aWheels)
    {
        var graph = aBrain.Graph;
        var sources = aSensors.Select(aEntry => graph.Add(new SensorModule(aEntry.Sensor) { Name = aEntry.Name })).ToArray();
        var controller = graph.Add(aController);
        var wheels = graph.Add(new ActuatorModule(aWheels) { Name = aWheels.Slot });

        var missing = new List<string>();
        foreach (var port in controller.InputPorts)
        {
            var source = sources.FirstOrDefault(aSource => aSource.OutputPorts.Contains(port));
            if (source is null)
                missing.Add(port);
            else
                graph.Connect(source, port, controller, port);
        }
        if (missing.Count > 0)
            throw new ArgumentException(
                $"{controller} has inputs no sensor provides: {string.Join(", ", missing)}.", nameof(aController));

        graph.Connect(controller, wheels, wheels.InputPorts.ToArray());
        graph.Validate();
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
        CreateSeeker(aPosition, ControllerColor, aTargetId, new ApproachTargetModule { Name = "Approach" });

    public static CylinderCreature CreateNeuralSeeker(Vector3 aPosition, Guid? aTargetId) =>
        CreateSeeker(aPosition, NeuralColor, aTargetId, CreateNeuralModule());

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

    // ---------- autka ----------

    public static Obstacle CreateObstacle(Vector3 aPosition, float aRadius = 0.5f) => new()
    {
        Radius = aRadius,
        Body = { Position = aPosition }
    };

    /// <summary>
    /// Autko z okiem (TargetSensor), <paramref name="aWhiskers"/> wąsami (RaySensor) i kierownicą (SteeringDriveActuator).
    /// Mózg: eye + whiskers → controller → wheels. Controller może czytać dowolne porty celu i Ray{i},
    /// więc musi być zrobiony dla tej samej liczby wąsów (inaczej <see cref="BuildBrain"/> wymieni brakujące porty).
    /// </summary>
    public static CarCreature CreateCar(Vector3 aPosition, float aYaw, Vector3 aColor, Guid? aTargetId, BrainModule aController,
        int aWhiskers = DefaultWhiskers)
    {
        var brain = new Brain();
        var car = new CarCreature(brain) { Color = aColor };
        car.Place(aPosition, Quaternion.CreateFromAxisAngle(Vector3.UnitZ, aYaw));
        var eye = new TargetSensor { Slot = "Eye", TargetId = aTargetId };
        var whiskers = new RaySensor(WhiskerAnglesFor(aWhiskers), WhiskerRange) { Slot = "Whiskers" };
        var wheels = new SteeringDriveActuator { Slot = "Wheels" };
        car.Body.Sensors.Add(eye);
        car.Body.Sensors.Add(whiskers);
        car.Body.Actuators.Add(wheels);
        BuildBrain(brain, [("Eye", eye), ("Whiskers", whiskers)], aController, wheels);
        return car;
    }

    public static AvoidAndSeekModule CreateAvoidController(int aWhiskers = DefaultWhiskers) =>
        new(WhiskerAnglesFor(aWhiskers)) { Name = "AvoidAndSeek" };

    /// <summary>
    /// Sieć autka (3 + wąsy)-8-2 z losowymi wagami: wejścia to kierunek do celu (bok, przód), szczelina/4
    /// i po jednym wejściu na wąs; wyjścia Steer, Throttle (tanh, więc także cofanie). Dla 5 wąsów: 8-8-2, 90 parametrów.
    /// </summary>
    public static NeuralNetworkModule CreateCarNeuralModule(int aWhiskers = DefaultWhiskers)
    {
        var rays = Enumerable.Range(0, WhiskerAnglesFor(aWhiskers).Length).Select(RaySensor.PortName).ToArray();
        var module = new NeuralNetworkModule(new NeuralNetwork(3 + rays.Length, 8, 2)) { Name = "Neural" };
        module.Ports.AddRange(TargetPorts);
        module.Ports.AddRange(rays);
        module.Inputs.Add(new NeuralInput("Found * DirectionY"));
        module.Inputs.Add(new NeuralInput("Found * DirectionX"));
        module.Inputs.Add(new NeuralInput("Found * Gap / 4"));
        foreach (var ray in rays)
            module.Inputs.Add(new NeuralInput(ray));
        module.Outputs.Add(new NeuralOutput(SteeringDriveActuator.SteerPort));
        module.Outputs.Add(new NeuralOutput(SteeringDriveActuator.ThrottlePort));
        return module;
    }

    public static CarCreature CreateControllerCar(Vector3 aPosition, float aYaw, Guid? aTargetId, int aWhiskers = DefaultWhiskers) =>
        CreateCar(aPosition, aYaw, ControllerColor, aTargetId, CreateAvoidController(aWhiskers), aWhiskers);

    public static CarCreature CreateNeuralCar(Vector3 aPosition, float aYaw, Guid? aTargetId, int aWhiskers = DefaultWhiskers) =>
        CreateCar(aPosition, aYaw, NeuralColor, aTargetId, CreateCarNeuralModule(aWhiskers), aWhiskers);

    // ---------- wąż ----------

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

    /// <summary>Płyta terenu: środek spodu w (x, y, z), wymiary, obrót wokół pionu.</summary>
    public static Slab CreateSlab(Vector3 aPosition, Vector3 aSize, float aYaw = 0) => new()
    {
        Size = aSize,
        Body = { Position = aPosition, Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, aYaw) }
    };

    /// <summary>
    /// Scena węży: podłoga 30 × 30 m, kulka na płycie, kilka płaskich płyt terenu (4–10 cm) i dwa węże —
    /// z CPG i z własną siecią. Oba mają losowe parametry i uczą się dopiero po starcie nauki.
    /// </summary>
    public static DemoScene CreateSnakeScene()
    {
        var world = new World();
        world.Add(Floor.At(30, 30));
        Slab[] slabs =
        [
            CreateSlab(new Vector3(0.5f, 0.8f, 0), new Vector3(2.2f, 1.6f, 0.06f), 0.3f),
            CreateSlab(new Vector3(-1.2f, 3.6f, 0), new Vector3(1.4f, 2.4f, 0.04f), -0.5f),
            CreateSlab(new Vector3(2.6f, -1.6f, 0), new Vector3(1.8f, 1.2f, 0.08f), 0.9f),
            CreateSlab(new Vector3(4.2f, 3.2f, 0), new Vector3(2, 2, 0.1f)),
            CreateSlab(new Vector3(-3.4f, 0.6f, 0), new Vector3(1.2f, 1.2f, 0.05f), 0.2f)
        ];
        for (var index = 0; index < slabs.Length; index++)
        {
            slabs[index].Name = $"Płyta {index + 1}";
            world.Add(slabs[index]);
        }

        var target = CreateTargetBall(new Vector3(4.2f, 3.2f, 0));
        target.Name = "Kulka";
        world.Add(target);
        Terrain.Snap(world, target);
        var snake = CreateLearningSnake(new Vector3(-4.5f, -2.5f, 0), 0.6f, target.Id);
        snake.Name = "Wąż CPG";
        world.Add(snake);
        var neural = CreateNeuralSnake(new Vector3(-5.5f, 3.5f, 0), -0.1f, target.Id);
        neural.Name = "Wąż NN";
        world.Add(neural);
        return new DemoScene(world, [snake, neural]);
    }

    // ---------- pająk ----------

    public static readonly Vector3 SpiderColor = new(0.55f, 0.35f, 0.25f);
    public static readonly Vector3 NeuralSpiderColor = new(0.72f, 0.45f, 0.85f);

    /// <summary>
    /// Pająk: oko „Eye” (tułów), czucie stawów „Joints”, zegar „Clock”, czucie terenu „Feel”, nogi „Legs” (8 stawów:
    /// biodro, kolano × 4). Mózg sensory → controller → nogi; controller musi mieć wyjścia Yaw/Pitch 0–7 (np. <see cref="GaitModule"/>).
    /// </summary>
    public static SpiderCreature CreateSpider(Vector3 aPosition, float aYaw, Vector3 aColor, Guid? aTargetId, BrainModule aController)
    {
        var brain = new Brain();
        var spider = new SpiderCreature(brain) { Color = aColor };
        var eye = new TargetSensor { Slot = "Eye", TargetId = aTargetId };
        var joints = new JointSensor(GaitModule.Joints) { Slot = "Joints" };
        var clock = new ClockSensor { Slot = "Clock", Frequency = 2.5f };
        var feel = new FeelSensor { Slot = "Feel" };
        var legs = new SpineActuator(GaitModule.Joints) { Slot = "Legs" };
        spider.Body.Sensors.Add(eye);
        spider.Body.Sensors.Add(joints);
        spider.Body.Sensors.Add(clock);
        spider.Body.Sensors.Add(feel);
        spider.Body.Actuators.Add(legs);
        BuildBrain(brain, [("Eye", eye), ("Joints", joints), ("Clock", clock), ("Feel", feel)], aController, legs);
        spider.Place(aPosition, Quaternion.CreateFromAxisAngle(Vector3.UnitZ, aYaw));
        return spider;
    }

    /// <summary>Pająk z generatorem kłusa: ręczne parametry jako snapshot „ręczne parametry”, chód wylosowany.</summary>
    public static SpiderCreature CreateLearningSpider(Vector3 aPosition, float aYaw, Guid? aTargetId)
    {
        var gait = new GaitModule { Name = "Chód" };
        var spider = CreateSpider(aPosition, aYaw, SpiderColor, aTargetId, gait);
        spider.Brain!.Capture("ręczne parametry", gait);
        gait.Randomize();
        return spider;
    }

    /// <summary>
    /// Sieć pająka: wejścia Sin, Cos (zegar), kierunek do celu (bok, przód), szczelina/4; warstwa ukryta 10; wyjścia
    /// Yaw/Pitch 0–7 (skręt kolan jest ignorowany przez ciało). Wagi losowe.
    /// </summary>
    public static NeuralNetworkModule CreateSpiderNeuralModule()
    {
        string[] inputs = [ClockSensor.SinPort, ClockSensor.CosPort, "Found * DirectionY", "Found * DirectionX", "Found * Gap / 4"];
        var outputs = SnakeNetworkOutputs(GaitModule.Joints);
        var module = new NeuralNetworkModule(new NeuralNetwork(inputs.Length, 10, outputs.Length)) { Name = "Neural" };
        module.Ports.AddRange([ClockSensor.SinPort, ClockSensor.CosPort, .. TargetPorts]);
        module.Inputs.AddRange(inputs.Select(aExpression => new NeuralInput(aExpression)));
        module.Outputs.AddRange(outputs.Select(aPort => new NeuralOutput(aPort)));
        return module;
    }

    public static SpiderCreature CreateNeuralSpider(Vector3 aPosition, float aYaw, Guid? aTargetId) =>
        CreateSpider(aPosition, aYaw, NeuralSpiderColor, aTargetId, CreateSpiderNeuralModule());

    /// <summary>Scena pająków: podłoga 30 × 30, kilka niskich płyt, kulka, pająk z chodem (CPG) i pająk z siecią.</summary>
    public static DemoScene CreateSpiderScene()
    {
        var world = new World();
        world.Add(Floor.At(30, 30));
        Slab[] slabs =
        [
            CreateSlab(new Vector3(0, 0.5f, 0), new Vector3(1.6f, 2.4f, 0.04f), 0.2f),
            CreateSlab(new Vector3(2.8f, -1.5f, 0), new Vector3(1.4f, 1.4f, 0.06f), -0.4f),
            CreateSlab(new Vector3(-2.2f, 2.8f, 0), new Vector3(2, 1, 0.05f), 0.8f)
        ];
        for (var index = 0; index < slabs.Length; index++)
        {
            slabs[index].Name = $"Płyta {index + 1}";
            world.Add(slabs[index]);
        }
        var target = CreateTargetBall(new Vector3(4, 2, 0));
        target.Name = "Kulka";
        world.Add(target);
        Terrain.Snap(world, target);
        var gait = CreateLearningSpider(new Vector3(-4, -2, 0), 0.4f, target.Id);
        gait.Name = "Pająk";
        world.Add(gait);
        var neural = CreateNeuralSpider(new Vector3(-4.5f, 1.5f, 0), 0, target.Id);
        neural.Name = "Pająk NN";
        world.Add(neural);
        return new DemoScene(world, [gait, neural]);
    }

    // ---------- demo ----------

    /// <summary>
    /// Dwa tory, żeby stwory nie tłoczyły się przy jednym celu:
    /// - dół: kulka za czterema słupkami i dwa autka z wąsami (sterownik i losowa sieć),
    /// - góra: wolny tor z własną kulką i dwa walce bez wąsów (sterownik i losowa sieć).
    /// Ręczne wagi sieci walca są zapisane w jej mózgu jako snapshot „ręczne wagi”.
    /// </summary>
    public static DemoScene CreateDemo()
    {
        var world = new World();
        world.Add(Floor.At(22, 16));
        var carTarget = CreateTargetBall(new Vector3(5, -1.5f, 0));
        var cylinderTarget = CreateTargetBall(new Vector3(5, 5, 0));
        carTarget.Name = "Kulka (dół)";
        cylinderTarget.Name = "Kulka (góra)";
        world.Add(carTarget);
        world.Add(cylinderTarget);

        world.Add(CreateObstacle(new Vector3(1.5f, -1.3f, 0), 0.8f));
        world.Add(CreateObstacle(new Vector3(-0.5f, 0.6f, 0), 0.5f));
        world.Add(CreateObstacle(new Vector3(-0.5f, -3.9f, 0), 0.6f));
        world.Add(CreateObstacle(new Vector3(3.4f, -3.5f, 0), 0.4f));

        var controllerCar = CreateControllerCar(new Vector3(-5, -2.7f, 0), 0, carTarget.Id);
        var neuralCar = CreateNeuralCar(new Vector3(-5, -0.3f, 0), 0, carTarget.Id);
        var controller = CreateControllerSeeker(new Vector3(-5, 3.8f, 0), cylinderTarget.Id);
        var neural = CreateLearningSeeker(new Vector3(-5, 6.2f, 0), cylinderTarget.Id);
        controllerCar.Name = "Autko sterownik";
        neuralCar.Name = "Autko NN";
        controller.Name = "Walec sterownik";
        neural.Name = "Walec NN";

        ActiveEntity[] creatures = [controllerCar, neuralCar, controller, neural];
        foreach (var creature in creatures)
            world.Add(creature);
        return new DemoScene(world, creatures);
    }
}
