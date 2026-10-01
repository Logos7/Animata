using System.Numerics;
using Animata.Core.Actuators;
using Animata.Core.Bodies;
using Animata.Core.Sensors;
using Animata.Core.WorldObjects;

namespace Animata.Core.Brains.Modules;

/// <summary>
/// Geometria mięśni ciała w pozie spoczynkowej: ramię siły każdego mięśnia względem każdej osi każdego stawu, przez
/// który przechodzi (m; dodatnie — skurcz zwiększa kąt stawu). Liczone z linii mięśnia: r = ((P − C) × u) · oś, gdzie
/// C — środek stawu, P — przyczep po stronie dziecka stawu, u — kierunek od niego do drugiego przyczepu.
/// </summary>
public sealed class MuscleGeometry
{
    /// <summary>Oś stawu: numer stawu i czy to skręt (inaczej pochylenie).</summary>
    public readonly record struct JointAxis(int Joint, bool Yaw);

    public MuscleGeometry(BodyPlan aPlan)
    {
        Plan = aPlan;
        var axes = new List<JointAxis>();
        for (var joint = 0; joint < aPlan.Joints.Count; joint++)
        {
            var plan = aPlan.Joints[joint];
            if (plan.Kind != JointKind.Passive)
                continue;
            if (plan.HasYaw)
                axes.Add(new JointAxis(joint, true));
            if (plan.HasPitch)
                axes.Add(new JointAxis(joint, false));
        }
        Axes = axes;

        var parentOf = new int[aPlan.Parts.Count];
        Array.Fill(parentOf, -1);
        foreach (var joint in aPlan.Joints)
            parentOf[aPlan.IndexOf(joint.Child)] = aPlan.IndexOf(joint.Parent);
        bool InSubtree(int aPart, int aRoot)
        {
            for (var part = aPart; part >= 0; part = parentOf[part])
                if (part == aRoot)
                    return true;
            return false;
        }

        var muscles = aPlan.MuscleList;
        Arms = new float[muscles.Count, axes.Count];
        for (var muscle = 0; muscle < muscles.Count; muscle++)
        {
            var plan = muscles[muscle];
            var origin = aPlan.IndexOf(plan.Origin);
            var insertion = aPlan.IndexOf(plan.Insertion);
            for (var axis = 0; axis < axes.Count; axis++)
            {
                var joint = aPlan.Joints[axes[axis].Joint];
                var child = aPlan.IndexOf(joint.Child);
                var insertionBelow = InSubtree(insertion, child);
                if (insertionBelow == InSubtree(origin, child))
                    continue;   // mięsień nie przechodzi przez ten staw
                var (point, other) = insertionBelow ? (plan.InsertionPoint, plan.OriginPoint) : (plan.OriginPoint, plan.InsertionPoint);
                var direction = Vector3.Normalize(other - point);
                var childOrientation = aPlan.Parts[child].Orientation;
                var worldAxis = Vector3.Transform(axes[axis].Yaw ? Vector3.UnitZ : Vector3.UnitY, childOrientation);
                Arms[muscle, axis] = Vector3.Dot(Vector3.Cross(point - joint.Anchor, direction), worldAxis);
            }
        }
    }

    public BodyPlan Plan { get; }

    /// <summary>Osie stawów biernych (ruszanych mięśniami), w kolejności stawów: skręt, potem pochylenie.</summary>
    public IReadOnlyList<JointAxis> Axes { get; }

    /// <summary>Ramiona sił [mięsień, oś] (m).</summary>
    public float[,] Arms { get; }

    public int IndexOf(int aJoint, bool aYaw)
    {
        for (var axis = 0; axis < Axes.Count; axis++)
            if (Axes[axis].Joint == aJoint && Axes[axis].Yaw == aYaw)
                return axis;
        return -1;
    }

    /// <summary>Port czucia stawów dla osi.</summary>
    public string PortOf(int aAxis) => Axes[aAxis].Yaw ? SpineActuator.YawPort(Axes[aAxis].Joint) : SpineActuator.PitchPort(Axes[aAxis].Joint);

    /// <summary>Kąt osi (rad) z odczytu czucia stawów (ułamek zakresu).</summary>
    public float AngleOf(int aAxis, float aReading)
    {
        var joint = Plan.Joints[Axes[aAxis].Joint];
        return Axes[aAxis].Yaw ? JointPlan.Angle(aReading, joint.YawMin, joint.MaxYaw) : JointPlan.Angle(aReading, joint.PitchMin, joint.MaxPitch);
    }
}

/// <summary>
/// Wspólny rdzeń sterowników mięśniowych: regulator PD w przestrzeni stawów przełożony na mięśnie. Dla każdej osi
/// stawu biernego moment τ = Kp · (cel − kąt) − Kd · prędkość kątowa; moment rozkłada się na mięśnie, które go dają
/// (agonistów — ramię siły zgodne ze znakiem τ), proporcjonalnie do Fmax · ramię (rozwiązanie najmniejszych kwadratów
/// pobudzeń: Σ r·F = τ). Do tego stałe napięcie spoczynkowe (tonus) wszystkich mięśni. Kąty z czucia stawów,
/// prędkości — z różnicy odczytów. Cele i wzmocnienia daje klasa pochodna.
/// </summary>
public abstract class MuscleControlModule : BrainModule
{
    private readonly float[] _angles;
    private readonly float[] _lastAngles;
    private readonly float[] _rates;
    private readonly float[] _agonists;   // Σ (Fmax · r)² agonistów, osobno dla τ > 0 i τ < 0
    private readonly float[] _antagonists;
    private readonly Dictionary<string, float> _outputs;
    private bool _fresh = true;

    protected MuscleControlModule(MuscleGeometry aGeometry, IEnumerable<string> aExtraInputs)
    {
        Geometry = aGeometry;
        var aPlan = aGeometry.Plan;
        var axes = Geometry.Axes.Count;
        _angles = new float[axes];
        _lastAngles = new float[axes];
        _rates = new float[axes];
        _agonists = new float[axes];
        _antagonists = new float[axes];
        Targets = new float[axes];
        Kp = new float[axes];
        Kd = new float[axes];
        var muscles = aPlan.MuscleList;
        for (var axis = 0; axis < axes; axis++)
            for (var muscle = 0; muscle < muscles.Count; muscle++)
            {
                var push = muscles[muscle].MaxForce * Geometry.Arms[muscle, axis];
                if (push > 0)
                    _agonists[axis] += push * push;
                else
                    _antagonists[axis] += push * push;
            }
        InputPorts = [.. Enumerable.Range(0, axes).Select(Geometry.PortOf), .. aExtraInputs];
        OutputPorts = [.. muscles.Select(aMuscle => aMuscle.Name)];
        _outputs = OutputPorts.ToDictionary(aPort => aPort, _ => 0f);
    }

    public MuscleGeometry Geometry { get; }

    public override IReadOnlyList<string> InputPorts { get; }
    public override IReadOnlyList<string> OutputPorts { get; }

    /// <summary>Kąty osi (rad) i ich prędkości (rad/s) z ostatniego Evaluate.</summary>
    protected IReadOnlyList<float> Angles => _angles;
    protected IReadOnlyList<float> Rates => _rates;

    /// <summary>Cele osi (rad), wzmocnienia (N·m/rad, N·m·s/rad) i tonus — ustawia <see cref="Plan"/>.</summary>
    protected float[] Targets { get; }
    protected float[] Kp { get; }
    protected float[] Kd { get; }
    protected float Tone { get; set; }

    /// <summary>Ustawia <see cref="Targets"/>, <see cref="Kp"/>, <see cref="Kd"/>, <see cref="Tone"/> na ten krok.</summary>
    protected abstract void Plan(IReadOnlyDictionary<string, float> aInputs, BrainContext aContext);

    public override IReadOnlyDictionary<string, float> Evaluate(IReadOnlyDictionary<string, float> aInputs, BrainContext aContext)
    {
        var axes = _angles.Length;
        for (var axis = 0; axis < axes; axis++)
        {
            _angles[axis] = Geometry.AngleOf(axis, aInputs.GetValueOrDefault(Geometry.PortOf(axis)));
            // Prędkość z różnicy odczytów, wygładzona (stała czasowa ok. 2 kroków) — surowa różnica drga i wzmacnia drgania.
            var raw = _fresh || aContext.Delta <= 0 ? 0 : (_angles[axis] - _lastAngles[axis]) / aContext.Delta;
            _rates[axis] = _fresh ? 0 : _rates[axis] + (raw - _rates[axis]) * RateSmoothing;
            _lastAngles[axis] = _angles[axis];
        }
        _fresh = false;
        Plan(aInputs, aContext);

        var muscles = Geometry.Plan.MuscleList;
        for (var muscle = 0; muscle < muscles.Count; muscle++)
        {
            var excitation = Tone;
            for (var axis = 0; axis < axes; axis++)
            {
                var arm = Geometry.Arms[muscle, axis];
                if (arm == 0)
                    continue;
                var torque = Kp[axis] * (Targets[axis] - _angles[axis]) - Kd[axis] * _rates[axis];
                if (torque * arm <= 0)
                    continue;
                var sum = torque > 0 ? _agonists[axis] : _antagonists[axis];
                if (sum > 0)
                    excitation += torque * muscles[muscle].MaxForce * arm / sum;
            }
            _outputs[muscles[muscle].Name] = Math.Clamp(excitation, 0, 1);
        }
        return _outputs;
    }

    /// <summary>Waga nowego odczytu w wygładzaniu prędkości kątowych.</summary>
    public const float RateSmoothing = 0.5f;

    public override void Reset() => _fresh = true;

    /// <summary>Cele, sztywności i tłumienia osi (dla wspólnego kodu stania).</summary>
    internal (float[] Targets, float[] Kp, float[] Kd) Gains => (Targets, Kp, Kd);

    internal void SetTone(float aTone) => Tone = aTone;

    protected static float Clean(float aValue, float aMin, float aMax) => float.IsFinite(aValue) ? Math.Clamp(aValue, aMin, aMax) : 0;
}

/// <summary>
/// Stanie humanoida mięśniowego: cel — poza spoczynkowa (nogi proste) z pochyleniem w kostkach, przesunięta przez odruchy
/// równowagi z błędnika (jak <see cref="BalanceModule"/>: pochylenie → kostki i biodra, przechył → odwiedzenie bioder
/// i kostek). 12 uczonych parametrów: sztywność bioder, kolan i kostek (× 100 N·m/rad), tłumienie (s), tonus, P i D
/// równowagi dla kostek, bioder i przechyłu, pochylenie w kostkach (rad; ujemne — goleń do przodu, ciężar nad palcami,
/// jak u człowieka: wtedy stanie trzyma silny mięsień płaszczkowaty, a nie słaby piszczelowy przedni).
/// </summary>
public sealed class MuscleStandModule : MuscleControlModule, ITrainableModule
{
    public const int Parameters = 12;

    private static readonly string[] Senses =
        [BalanceSensor.PitchPort, BalanceSensor.RollPort, BalanceSensor.PitchRatePort, BalanceSensor.RollRatePort];

    private readonly float[] _parameters = Defaults;

    public MuscleStandModule() : base(MuscleHumanoid.Geometry, Senses) => Name = "Stanie";

    /// <summary>Domyślne parametry stania (przed optymalizacją).</summary>
    public static float[] Defaults => [4, 4, 4, 0.02f, 0.02f, 0, 0, 0, 0, 0, 0, -0.05f];

    protected override void Plan(IReadOnlyDictionary<string, float> aInputs, BrainContext aContext) =>
        StandTargets(this, _parameters, aInputs);

    /// <summary>Cele i wzmocnienia stania — wspólne z <see cref="MuscleGaitModule"/>, który dodaje do nich krok.</summary>
    internal static void StandTargets(MuscleControlModule aModule, ReadOnlySpan<float> p, IReadOnlyDictionary<string, float> aInputs)
    {
        var geometry = aModule.Geometry;
        var (targets, kp, kd) = aModule.Gains;
        var pitch = aInputs.GetValueOrDefault(BalanceSensor.PitchPort);
        var roll = aInputs.GetValueOrDefault(BalanceSensor.RollPort);
        var pitchRate = aInputs.GetValueOrDefault(BalanceSensor.PitchRatePort);
        var rollRate = aInputs.GetValueOrDefault(BalanceSensor.RollRatePort);
        Array.Clear(targets);
        aModule.SetTone(p[4]);
        for (var axis = 0; axis < geometry.Axes.Count; axis++)
        {
            var joint = geometry.Axes[axis].Joint;
            var stiffness = 100 * (joint == Humanoid.Knee(0) || joint == Humanoid.Knee(1) ? p[1]
                : joint == Humanoid.Ankle(0) || joint == Humanoid.Ankle(1) ? p[2] : p[0]);
            kp[axis] = stiffness;
            kd[axis] = stiffness * p[3];
        }
        for (var side = 0; side < 2; side++)
        {
            Set(geometry, targets, Humanoid.Ankle(side), false, p[11] + p[5] * pitch + p[6] * pitchRate);
            Set(geometry, targets, Humanoid.Hip(side), false, p[7] * pitch + p[8] * pitchRate);
            Set(geometry, targets, Humanoid.Ankle(side), true, p[9] * roll + p[10] * rollRate);
            Set(geometry, targets, Humanoid.Hip(side), true, p[9] * roll + p[10] * rollRate);
        }
    }

    private static void Set(MuscleGeometry aGeometry, float[] aTargets, int aJoint, bool aYaw, float aTarget)
    {
        var axis = aGeometry.IndexOf(aJoint, aYaw);
        if (axis >= 0)
            aTargets[axis] = aTarget;
    }

    /// <summary>Przycina parametry stania do zakresów.</summary>
    internal static void CleanInto(ReadOnlySpan<float> aSource, Span<float> aTarget)
    {
        for (var index = 0; index < Parameters; index++)
            aTarget[index] = index switch
            {
                < 3 => Clean(aSource[index], 0, 20),
                3 => Clean(aSource[index], 0, 1),
                4 => Clean(aSource[index], 0, 0.5f),
                11 => Clean(aSource[index], -0.5f, 0.5f),
                _ => Clean(aSource[index], -3, 3)
            };
    }

    public float[] GetParameters() => [.. _parameters];

    public void SetParameters(ReadOnlySpan<float> aParameters)
    {
        if (aParameters.Length != Parameters)
            throw new ArgumentException($"Expected {Parameters} parameters, got {aParameters.Length}.", nameof(aParameters));
        CleanInto(aParameters, _parameters);
    }

    public void Randomize(Random? aRandom = null)
    {
        var random = aRandom ?? Random.Shared;
        float Between(float aMin, float aMax) => aMin + random.NextSingle() * (aMax - aMin);
        SetParameters([Between(0.5f, 6), Between(0.5f, 6), Between(0.5f, 6), Between(0, 0.3f), Between(0, 0.1f),
            Between(-1, 1), Between(-1, 1), Between(-1, 1), Between(-1, 1), Between(-1, 1), Between(-1, 1), Between(-0.2f, 0.1f)]);
    }

    public override ModuleState CaptureState() => new MuscleStandState([.. _parameters]);

    public override void RestoreState(ModuleState aState) => SetParameters(Expect<MuscleStandState>(aState).Parameters);

    public static MuscleStandModule Create(ReadOnlySpan<float> aParameters, string aName = "Stanie")
    {
        var module = new MuscleStandModule { Name = aName };
        module.SetParameters(aParameters);
        return module;
    }
}

/// <summary>
/// Chód humanoida mięśniowego — stanie (<see cref="MuscleStandModule"/>: pochylenie w kostkach i odruchy równowagi) plus
/// automat kroku jak w SIMBICON (Yin i in. 2007) i u Geijtenbeeka i in. (2013): faza φ ∈ [0, 1) rośnie o 1/T na
/// sekundę; lewa noga jest przenoszona w φ &lt; ½, prawa w φ ≥ ½ (wczesna i późna część przenoszenia). Do celów stania
/// dochodzą przesunięcia:
/// - noga przenoszona: biodro do przodu o H1 / H2 (wcześnie / późno) i sprzężenie SIMBICON — za szybko do przodu
///   albo pochylony → dłuższy krok: + cV · (v − v*) + cP · pochylenie; kolano zgięte K1 / K2; kostka A;
///   w bok: + <see cref="SideGain"/> · prędkość w bok (noga stawiana tam, dokąd ciało leci);
/// - noga podporowa: kolano Ks, kostka As (odbicie), biodro trzyma miednicę (cel = kąt + kT · pochylenie + kD · szybkość
///   pochylania — zamiast odruchu biodra ze stania);
/// - skręt do celu: dłuższy krok nogą po zewnętrznej łuku.
/// Przy zerowych przesunięciach to dokładnie stanie — optymalizacja startuje z czegoś, co stoi, i stopniowo uczy się
/// kroku. 25 uczonych parametrów (<see cref="Names"/>): 13 kroku i 12 stania.
/// </summary>
public sealed class MuscleGaitModule : MuscleControlModule, ITrainableModule
{
    private const int Stand = 13;   // od tego indeksu — parametry stania w kolejności MuscleStandModule

    public static readonly string[] Names =
    [
        "Okres", "Biodro1", "Biodro2", "Kolano1", "Kolano2", "KostkaPrzen", "KolanoPodp", "KostkaPodp",
        "SprzęgV", "SprzęgP", "PrędkośćCel", "TułówP", "TułówD",
        "SztywnośćBiodra", "SztywnośćKolana", "SztywnośćKostki", "Tłumienie", "Tonus",
        "KostkaP", "KostkaD", "BiodroP", "BiodroD", "PrzechyłP", "PrzechyłD", "Pochylenie"
    ];

    public static int Parameters => Names.Length;

    /// <summary>Skręt: o tyle (ułamek kroku na kąt do celu / π) wydłuża się krok nogi po zewnętrznej łuku.</summary>
    public const float TurnGain = 0.4f;

    /// <summary>Sprzężenie kroku w bok z prędkości w bok (rad na m/s).</summary>
    public const float SideGain = 0.2f;

    private static readonly string[] Senses =
    [
        .. TargetSensor.SteeringPorts, BalanceSensor.PitchPort, BalanceSensor.RollPort, BalanceSensor.PitchRatePort,
        BalanceSensor.RollRatePort, BalanceSensor.VelocityXPort, BalanceSensor.VelocityYPort
    ];

    private readonly float[] _parameters = [0.8f, 0.2f, 0.1f, 0.4f, 0.1f, 0, 0, 0, 0.2f, 0.1f, 0.4f, 0, 0, .. MuscleStandModule.Defaults];
    private float _phase;

    public MuscleGaitModule() : base(MuscleHumanoid.Geometry, Senses) => Name = "Chód";

    /// <summary>Faza kroku [0, 1).</summary>
    public float Phase => _phase;

    protected override void Plan(IReadOnlyDictionary<string, float> aInputs, BrainContext aContext)
    {
        var p = _parameters;
        MuscleStandModule.StandTargets(this, p.AsSpan(Stand), aInputs);
        _phase = (_phase + Math.Max(0, aContext.Delta) / Math.Max(0.3f, p[0])) % 1;
        var found = aInputs.GetValueOrDefault(TargetSensor.FoundPort) > 0;
        var bearing = found
            ? MathF.Atan2(aInputs.GetValueOrDefault(TargetSensor.DirectionYPort), aInputs.GetValueOrDefault(TargetSensor.DirectionXPort)) / MathF.PI
            : 0;
        var pitch = aInputs.GetValueOrDefault(BalanceSensor.PitchPort);
        var velocityX = 2 * aInputs.GetValueOrDefault(BalanceSensor.VelocityXPort);
        var velocityY = 2 * aInputs.GetValueOrDefault(BalanceSensor.VelocityYPort);
        var turn = Math.Clamp(TurnGain * bearing, -0.5f, 0.5f);

        for (var side = 0; side < 2; side++)
        {
            var local = (_phase + (side == 0 ? 0 : 0.5f)) % 1;
            var outward = side == 0 ? 1f : -1f;
            if (local < 0.5f)
            {
                var early = local < 0.25f;
                var stride = (early ? p[1] : p[2]) * (1 + outward * turn) + p[8] * (velocityX - p[10]) + p[9] * pitch;
                // Cel uda w świecie (jak SIMBICON): kąt uda = pochylenie miednicy + kąt biodra (odczyt błędnika = 2 · kąt).
                Add(Humanoid.Hip(side), false, -stride - pitch / 2);
                Add(Humanoid.Knee(side), false, early ? p[3] : p[4]);
                Add(Humanoid.Ankle(side), false, p[5]);
                Add(Humanoid.Hip(side), true, SideGain * velocityY);
            }
            else
            {
                Add(Humanoid.Knee(side), false, p[6]);
                Add(Humanoid.Ankle(side), false, p[7]);
                // Biodro podporowe trzyma miednicę: moment zginacza przy stopie na ziemi pochyla ją do przodu, prostownika —
                // do tyłu; miednica odchylona do tyłu (pochylenie &lt; 0) → cel poniżej obecnego kąta.
                if (p[11] != 0 || p[12] != 0)
                {
                    var hip = Geometry.IndexOf(Humanoid.Hip(side), false);
                    Targets[hip] = Angles[hip] + p[11] * pitch + p[12] * aInputs.GetValueOrDefault(BalanceSensor.PitchRatePort);
                }
            }
        }
    }

    private void Add(int aJoint, bool aYaw, float aOffset)
    {
        var axis = Geometry.IndexOf(aJoint, aYaw);
        if (axis >= 0)
            Targets[axis] += aOffset;
    }

    public override void Reset()
    {
        base.Reset();
        _phase = 0;
    }

    public float[] GetParameters() => [.. _parameters];

    public void SetParameters(ReadOnlySpan<float> aParameters)
    {
        if (aParameters.Length != Parameters)
            throw new ArgumentException($"Expected {Parameters} parameters, got {aParameters.Length}.", nameof(aParameters));
        _parameters[0] = Clean(aParameters[0], 0.3f, 3);
        for (var index = 1; index < Stand; index++)
            _parameters[index] = Clean(aParameters[index], -3, 3);
        MuscleStandModule.CleanInto(aParameters[Stand..], _parameters.AsSpan(Stand));
    }

    public void Randomize(Random? aRandom = null)
    {
        var random = aRandom ?? Random.Shared;
        var values = GetParameters();
        for (var index = 1; index < Stand; index++)
            values[index] *= 0.5f + random.NextSingle();
        SetParameters(values);
    }

    public override ModuleState CaptureState() => new MuscleGaitState([.. _parameters]);

    public override void RestoreState(ModuleState aState) => SetParameters(Expect<MuscleGaitState>(aState).Parameters);

    public static MuscleGaitModule Create(ReadOnlySpan<float> aParameters, string aName = "Chód")
    {
        var module = new MuscleGaitModule { Name = aName };
        module.SetParameters(aParameters);
        return module;
    }
}
