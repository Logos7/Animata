using System.Numerics;
using System.Runtime.CompilerServices;
using Animata.Core.Bodies;
using Animata.Core.Actuators;
using Animata.Core.Brains;
using Animata.Core.Brains.Modules;
using Animata.Core.Entities;
using Animata.Core.Sensors;
using Animata.Core.Training;

namespace Animata.Core.WorldObjects;

/// <summary>
/// Wąż: <see cref="Segments"/> kapsuł połączonych przegubami kulowymi (skręt i pochylenie — pełne 3D), głowa to Seg0
/// z okiem. Kręgosłup (<see cref="SpineActuator"/>, slot „Spine”) zadaje stawy, czucie stawów (<see cref="JointSensor"/>,
/// slot „Joints”) je mierzy. Pełza dzięki łuskom (tarcie kierunkowe segmentów) i fali stawów, zwykle z <see cref="CpgModule"/>.
/// Liczbę segmentów zmienia się w miejscu (<see cref="SetSegments"/>) — ten sam stwór i mózg.
/// </summary>
public sealed class SnakeCreature : ArticulatedCreature
{
    /// <summary>
    /// Zakres skrętu i pochylenia stawu węża (rad) i moment serwa (N·m). ±1.2 rad w obu osiach — tyle trzeba, żeby
    /// owinąć się ciasno wokół pnia r 0.25 m (odstęp segmentów 0.36 m); 8 N·m — żeby zwój ścisnął pień i utrzymał ciężar.
    /// </summary>
    public const float MaxYaw = 1.2f;
    public const float MaxPitch = 1.2f;
    public const float JointStrength = 8;

    public const int MinSegments = 2;
    public const int MaxSegments = 24;
    public const int DefaultSegments = 8;

    /// <summary>Promień segmentu węża i długość walca kapsuły (bez półkul).</summary>
    public const float SegmentRadius = 0.07f;
    public const float SegmentLength = 0.2f;

    /// <summary>Odstęp środków kolejnych segmentów: kapsuła + mała szczelina na staw.</summary>
    public const float SegmentSpacing = SegmentLength + 2 * SegmentRadius + 0.02f;

    /// <summary>Tarcie łusek węża: małe zwykłe (sunie do przodu), duże w bok, średnie do tyłu (Coulomb, patrz PartPlan).</summary>
    public const float Friction = 0.1f;
    public const float LateralFriction = 1.5f;
    public const float BackwardFriction = 0.5f;

    public static bool IsValidLength(int aSegments) => aSegments is >= MinSegments and <= MaxSegments;

    /// <summary>Rzuca <see cref="ArgumentOutOfRangeException"/>, gdy wąż miałby za mało albo za dużo segmentów.</summary>
    public static void CheckLength(int aSegments, [CallerArgumentExpression(nameof(aSegments))] string? aName = null)
    {
        if (!IsValidLength(aSegments))
            throw new ArgumentOutOfRangeException(aName, aSegments, $"Wąż ma od {MinSegments} do {MaxSegments} segmentów.");
    }

    /// <summary>
    /// Plan węża: Seg0 (głowa, korzeń) … Seg{n−1} wzdłuż −X, leżące na ziemi; staw J{k} między Seg{k} a Seg{k+1}
    /// w połowie odstępu. Stawy kulowe: skręt i pochylenie ±1.2 rad (±69°), moment 8 N·m. Każdy segment ma łuski:
    /// małe tarcie zwykłe <see cref="Friction"/>, duże w bok <see cref="LateralFriction"/> i średnie do tyłu
    /// <see cref="BackwardFriction"/> (współczynniki Coulomba, patrz <see cref="PartPlan"/>).
    /// </summary>
    public static BodyPlan DefaultPlan(int aSegments)
    {
        CheckLength(aSegments);
        var height = SegmentRadius + 0.005f;
        var builder = new BodyPlanBuilder();
        for (var segment = 0; segment < aSegments; segment++)
            builder.Part(new PartPlan($"Seg{segment}", PartShape.Capsule, new Vector3(SegmentRadius, SegmentLength, 0), 0.3f,
                new Vector3(-segment * SegmentSpacing, 0, height), Quaternion.Identity, Friction, LateralFriction, BackwardFriction));
        for (var joint = 0; joint < aSegments - 1; joint++)
            builder.Joint($"J{joint}", $"Seg{joint}", $"Seg{joint + 1}", new Vector3(-(joint + 0.5f) * SegmentSpacing, 0, height),
                aMaxYaw: MaxYaw, aMaxPitch: MaxPitch, aStrength: JointStrength);
        return builder.Build();
    }

    public SnakeCreature(int aSegments = DefaultSegments, Brain? aBrain = null)
        : base(DefaultPlan(aSegments), aBrain)
    {
        _segments = aSegments;
    }

    private int _segments;

    /// <summary>Oko „Eye” (głowa), czucie stawów „Joints”, zegar rytmu „Clock”, czucie terenu „Feel”, kręgosłup „Spine”.</summary>
    public override void Equip()
    {
        Body.Sensors.Add(new TargetSensor { Slot = "Eye" });
        Body.Sensors.Add(new JointSensor(Segments - 1) { Slot = "Joints" });
        Body.Sensors.Add(new ClockSensor { Slot = "Clock" });
        Body.Sensors.Add(new FeelSensor { Slot = "Feel" });
        Body.Actuators.Add(new SpineActuator(Segments - 1) { Slot = "Spine" });
        base.Equip();
    }

    public override IReadOnlyList<BrainPreset> BrainPresets =>
    [
        new("Sieć neuronowa", "zegar rytmu, cel i czucie terenu, 2 warstwy ukryte, losowe wagi",
            () => WorldObjectCatalog.CreateSnakeNeuralModule(Segments, WorldObjectCatalog.SnakeHiddenLayers)),
        new("CPG · pełzanie", "generator fali: 6 parametrów (amplituda, częstotliwość, fala, skręt, pochylenie)",
            () => WorldObjectCatalog.CreateCpg(Segments), true),
        new("CPG · toczenie (wspinaczka)", "zwój toczy się po pniu w górę — dla węża owiniętego wokół cylindra",
            () => WorldObjectCatalog.CreateClimbingCpg(Segments), true)
    ];

    public override SeekRig TrainingRig => Climber ? SeekRigs.ClimbWith(Segments) : SeekRigs.SnakeWith(Segments);

    public override string Describe() => $"SnakeCreature · {Segments} segm. · {JointCount} stawów · fizyka Bepu";

    [Setting("Segmenty", Min = MinSegments, Max = MaxSegments, Step = 1, Reshapes = true, Slots = "Spine,Joints", Tip = "Liczba segmentów węża. Ciało przebudowuje się w miejscu, CPG zachowuje wyuczony chód, sieć dostaje przeliczone wyjścia.")]
    public int Segments
    {
        get => _segments;
        set => SetSegments(value);
    }

    /// <summary>
    /// Wspinacz: uczy się wchodzić na pień — cylinder z tarciem chwytnym (próby zaczyna owinięty wokół pnia, cel na jego szczycie —
    /// <see cref="Training.SeekRigs.ClimbWith"/>), a nie pełzać po ziemi.
    /// </summary>
    [Setting("Wspinacz", Tip = "Nauka uczy wchodzenia na pień — cylinder z tarciem chwytnym (próby: wąż owinięty wokół niego, kula na szczycie) zamiast pełzania po ziemi.")]
    public bool Climber { get; set; }

    /// <summary>Owija węża wokół cylindra-pnia (<see cref="SnakeWrap.Around"/>) i robi z niego wspinacza.</summary>
    public void WrapAround(Cylinder aTrunk, float aAngle = 0)
    {
        SnakeWrap.Around(this, aTrunk.Body.Position, aTrunk.Radius, aAngle);
        Climber = true;
    }

    /// <summary>
    /// Nowa liczba segmentów w miejscu: ciało przebudowane w pozie spoczynkowej przy głowie, porty kręgosłupa,
    /// czucia stawów i CPG idą za liczbą stawów, a połączenia „port do portu tej samej nazwy” (Yaw3 → Yaw3)
    /// obejmują nowe stawy. Parametry CPG zostają (nie zależą od długości węża). Sieć sterująca stawami
    /// (wyjścia Yaw{k}/Pitch{k}) dostaje wyjścia nowych stawów z wagami ostatniego starego stawu, a wyjścia usuniętych
    /// stawów znikają — pozostałe wagi zostają; snapshoty są przeliczane tak samo.
    /// </summary>
    public void SetSegments(int aSegments)
    {
        CheckLength(aSegments);
        if (aSegments == Segments)
            return;

        var joints = aSegments - 1;
        PortRewiring.Change(this,
            (aState, _) => aState is NeuralNetworkState network ? RemapSpineOutputs(network, joints) : aState,
            (_, aPort) => IsSpinePort(aPort),
            aGraphs =>
            {
                Rebuild(DefaultPlan(aSegments));
                _segments = aSegments;
                foreach (var actuator in Body.Actuators.OfType<SpineActuator>())
                    actuator.SetJointCount(joints);
                foreach (var sensor in Body.Sensors.OfType<JointSensor>())
                    sensor.SetJointCount(joints);
                foreach (var module in aGraphs.SelectMany(aGraph => aGraph.Modules))
                    if (module is CpgModule cpg)
                        cpg.SetJointCount(joints);
            });
    }

    /// <summary>
    /// Stan sieci z wyjściami stawów przeliczony na <paramref name="aJoints"/> stawów: wyjścia stawów ≥ aJoints znikają,
    /// brakujące (tego samego rodzaju — Yaw albo Pitch — co były) są dopisywane na końcu (Yaw{k}, Pitch{k}, …)
    /// z wagami, biasem, skalą i przesunięciem ostatniego starego stawu tego rodzaju. Reszta sieci bez zmian.
    /// </summary>
    public static NeuralNetworkState RemapSpineOutputs(NeuralNetworkState aState, int aJoints)
    {
        var rows = aState.Weights[^1];
        var biases = aState.Biases[^1];
        var kept = new List<int>();
        var lastOf = new Dictionary<string, (int Row, int Joint)>();
        for (var index = 0; index < aState.Outputs.Length; index++)
        {
            var port = aState.Outputs[index].Port;
            if (!IsSpinePort(port))
            {
                kept.Add(index);
                continue;
            }
            var (kind, joint) = SplitSpinePort(port);
            if (!lastOf.TryGetValue(kind, out var last) || joint > last.Joint)
                lastOf[kind] = (index, joint);
            if (joint < aJoints)
                kept.Add(index);
        }

        var outputs = kept.Select(aIndex => aState.Outputs[aIndex]).ToList();
        var sources = new List<int>(kept);
        var present = outputs.Select(aOutput => aOutput.Port).ToHashSet();
        for (var joint = 0; joint < aJoints; joint++)
            foreach (var kind in new[] { "Yaw", "Pitch" })
            {
                var port = kind + joint;
                if (present.Contains(port) || !lastOf.TryGetValue(kind, out var last))
                    continue;
                outputs.Add(aState.Outputs[last.Row] with { Port = port });
                sources.Add(last.Row);
            }
        if (outputs.Count == aState.Outputs.Length && sources.SequenceEqual(Enumerable.Range(0, outputs.Count)))
            return aState;

        var weights = aState.Weights.Select(aLayer => aLayer.Select(aNeuron => (float[])aNeuron.Clone()).ToArray()).ToArray();
        var allBiases = aState.Biases.Select(aLayer => (float[])aLayer.Clone()).ToArray();
        weights[^1] = sources.Select(aRow => (float[])rows[aRow].Clone()).ToArray();
        allBiases[^1] = sources.Select(aRow => biases[aRow]).ToArray();
        var layers = (int[])aState.Layers.Clone();
        layers[^1] = outputs.Count;
        return aState with { Layers = layers, Weights = weights, Biases = allBiases, Outputs = [.. outputs] };
    }

    private static (string Kind, int Joint) SplitSpinePort(string aPort) =>
        aPort.StartsWith("Yaw", StringComparison.Ordinal) ? ("Yaw", int.Parse(aPort.AsSpan(3), System.Globalization.CultureInfo.InvariantCulture)) : ("Pitch", int.Parse(aPort.AsSpan(5), System.Globalization.CultureInfo.InvariantCulture));

    /// <summary>Port stawu: Yaw{i} albo Pitch{i}.</summary>
    public static bool IsSpinePort(string aPort) =>
        (aPort.StartsWith("Yaw", StringComparison.Ordinal) && aPort.Length > 3 && aPort[3..].All(char.IsAsciiDigit)) ||
        (aPort.StartsWith("Pitch", StringComparison.Ordinal) && aPort.Length > 5 && aPort[5..].All(char.IsAsciiDigit));
}
