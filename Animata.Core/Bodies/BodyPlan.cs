using System.Numerics;

namespace Animata.Core.Bodies;

/// <summary>Kształt części ciała.</summary>
public enum PartShape
{
    /// <summary>Kapsuła wzdłuż lokalnej osi X części. Size = (promień, długość walca między półkulami, —).</summary>
    Capsule,

    /// <summary>Prostopadłościan. Size = pełne wymiary (X, Y, Z).</summary>
    Box,

    /// <summary>Kula. Size = (promień, —, —).</summary>
    Sphere,

    /// <summary>Walec wzdłuż lokalnej osi Y części (jak oś koła). Size = (promień, szerokość, —).</summary>
    Cylinder
}

/// <summary>Rodzaj stawu.</summary>
public enum JointKind
{
    /// <summary>
    /// Przegub kulowy z serwem: dziecko trzyma zadany skręt („yaw”, wokół swojej osi Z w pozie spoczynkowej) i pochylenie
    /// („pitch”, wokół swojej osi Y — dodatnie opuszcza oś X części) względem pozy spoczynkowej; skręcania wokół własnej
    /// osi X nie ma. Dla części o tej samej orientacji co rodzic (wąż) to osie rodzica.
    /// </summary>
    Ball,

    /// <summary>Sztywne połączenie (spaw) — dziecko jest częścią rodzica, np. kółko podporowe.</summary>
    Fixed,

    /// <summary>
    /// Koło: dziecko (walec, oś wzdłuż jego Y) na zawieszeniu pod rodzicem — sprężyna i prowadnica pionowa, zawias osi,
    /// opcjonalnie skręt (obrót osi wokół pionu, <see cref="JointPlan.Steerable"/>) i silnik (<see cref="JointPlan.Driven"/>).
    /// <see cref="JointPlan.Anchor"/> to górny punkt zawieszenia; koło w spoczynku wisi <see cref="JointPlan.Suspension"/> niżej.
    /// Prędkość i skręt zadaje napęd przez <see cref="Entities.ArticulatedCreature.SetWheelTarget"/>.
    /// </summary>
    Wheel,

    /// <summary>
    /// Staw bierny: te same osie i zakresy co <see cref="Ball"/> (skręt wokół osi Z dziecka, pochylenie wokół jego osi Y,
    /// bez skręcania wokół osi X), ale bez serwa — zakres trzymają twarde ograniczniki w fizyce, a ruch robią mięśnie
    /// (<see cref="MusclePlan"/>) albo siły z zewnątrz. Nie ma portów napędu stawów; czucie stawów go widzi.
    /// </summary>
    Passive
}

/// <summary>
/// Część ciała w pozie spoczynkowej, w układzie stwora (X — przód, Y — lewo, Z — góra; początek na ziemi pod stworem).
/// Tarcie: <see cref="Friction"/> — zwykłe (izotropowe) tarcie kontaktu w fizyce; <see cref="LateralFriction"/> i
/// <see cref="BackwardFriction"/> — dodatkowe tarcie kierunkowe („łuski”) jako współczynniki Coulomba: gdy część dotyka
/// podłoża, jej prędkość w bok (i do tyłu wzdłuż osi) maleje najwyżej o μ·g·Δt na krok — dość, by w bok prawie się nie
/// ślizgała, ale nie tyle, by zablokować stawy. Wąż ma małe tarcie zwykłe i duże boczne: sunie wzdłuż ciała.
/// </summary>
public sealed record PartPlan(
    string Name,
    PartShape Shape,
    Vector3 Size,
    float Mass,
    Vector3 Position,
    Quaternion Orientation,
    float Friction = 0.6f,
    float LateralFriction = 0,
    float BackwardFriction = 0)
{
    /// <summary>Promień obrysu części (do zmysłów i szczeliny do celu).</summary>
    public float Radius => Shape switch
    {
        PartShape.Capsule => Size.X,
        PartShape.Sphere => Size.X,
        PartShape.Cylinder => Size.X,
        _ => MathF.Min(Size.X, MathF.Min(Size.Y, Size.Z)) / 2
    };
}

/// <summary>
/// Staw łączący rodzica z dzieckiem w punkcie <see cref="Anchor"/> (układ stwora, poza spoczynkowa).
/// Kąty graniczne w radianach, liczone od pozy spoczynkowej: skręt od <see cref="YawMin"/> do <see cref="MaxYaw"/>, pochylenie
/// od <see cref="PitchMin"/> do <see cref="MaxPitch"/>. Bez <see cref="MinYaw"/>/<see cref="MinPitch"/> zakres jest symetryczny
/// (±Max); podane — staw może być jednokierunkowy (np. kolano: zgina się tylko w jedną stronę, 0 = do wyprostu). Oś
/// o zakresie 0–0 jest zablokowana: staw z samym pochyleniem to zawias. <see cref="Strength"/> — największy moment serwa
/// albo silnika koła (N·m); w stawie biernym — tłumienie (N·m·s/rad).
/// Pola kół: <see cref="Steerable"/>, <see cref="Driven"/>, <see cref="Suspension"/> (skok zawieszenia, m),
/// <see cref="SuspensionFrequency"/> (sztywność sprężyny, Hz).
/// </summary>
public sealed record JointPlan(
    string Name,
    string Parent,
    string Child,
    Vector3 Anchor,
    JointKind Kind,
    float MaxYaw,
    float MaxPitch,
    float Strength,
    bool Steerable = false,
    bool Driven = false,
    float Suspension = 0.04f,
    float SuspensionFrequency = 6,
    float? MinYaw = null,
    float? MinPitch = null)
{
    /// <summary>Najmniejszy skręt (≤ 0): <see cref="MinYaw"/> albo −<see cref="MaxYaw"/>.</summary>
    public float YawMin => MinYaw ?? -MaxYaw;

    /// <summary>Najmniejsze pochylenie (≤ 0): <see cref="MinPitch"/> albo −<see cref="MaxPitch"/>.</summary>
    public float PitchMin => MinPitch ?? -MaxPitch;

    /// <summary>Czy staw się zgina (kulowy z serwem albo bierny) — ma kąty skrętu i pochylenia.</summary>
    public bool Bends => Kind is JointKind.Ball or JointKind.Passive;

    /// <summary>Czy skręt jest ruchomy (niezerowy zakres).</summary>
    public bool HasYaw => Bends && (MaxYaw > 0 || YawMin < 0);

    /// <summary>Czy pochylenie jest ruchome (niezerowy zakres).</summary>
    public bool HasPitch => Bends && (MaxPitch > 0 || PitchMin < 0);

    /// <summary>Czy oś skrętu ma serwo (port napędu stawów).</summary>
    public bool DrivesYaw => Kind == JointKind.Ball && HasYaw;

    /// <summary>Czy oś pochylenia ma serwo (port napędu stawów).</summary>
    public bool DrivesPitch => Kind == JointKind.Ball && HasPitch;

    /// <summary>Komenda [-1, 1] → kąt: ujemna część skaluje się dolną granicą, dodatnia górną (0 = poza spoczynkowa).</summary>
    public static float Angle(float aCommand, float aMin, float aMax) => aCommand >= 0 ? aCommand * aMax : aCommand * -aMin;

    /// <summary>Kąt → komenda [-1, 1] (odwrotność <see cref="Angle"/>); oś bez zakresu w danym kierunku daje 0.</summary>
    public static float Command(float aAngle, float aMin, float aMax) =>
        aAngle >= 0 ? (aMax > 0 ? aAngle / aMax : 0) : (aMin < 0 ? aAngle / -aMin : 0);
}

/// <summary>
/// Plan ciała z klocków: części i stawy (drzewo — każda część poza pierwszą ma dokładnie jednego rodzica).
/// Pierwsza część to korzeń: jej poza jest pozą stwora (Body.Position / Rotation), na niej zwykle siedzi głowa z okiem.
/// To czyste dane — da się je zapisać, wygenerować z parametrów (np. liczby segmentów) i przebudować nimi stwora.
/// Buduje się je przez <see cref="BodyPlanBuilder"/>, który sprawdza spójność.
/// </summary>
public sealed record BodyPlan(IReadOnlyList<PartPlan> Parts, IReadOnlyList<JointPlan> Joints, IReadOnlyList<MusclePlan>? Muscles = null)
{
    public PartPlan Root => Parts[0];

    /// <summary>Mięśnie ciała (puste, gdy ciało ich nie ma).</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public IReadOnlyList<MusclePlan> MuscleList => Muscles ?? [];

    public int IndexOf(string aPart)
    {
        for (var index = 0; index < Parts.Count; index++)
            if (Parts[index].Name == aPart)
                return index;
        return -1;
    }
}

/// <summary>
/// Budowanie ciała z klocków:
/// <code>
/// var plan = new BodyPlanBuilder()
///     .Part("Tułów", PartShape.Box, new(0.8f, 0.4f, 0.2f), 3, new(0, 0, 0.4f))
///     .Part("Noga", PartShape.Capsule, new(0.06f, 0.3f, 0), 0.5f, new(0.3f, 0.3f, 0.2f), Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2))
///     .Joint("Biodro", "Tułów", "Noga", new(0.3f, 0.3f, 0.35f), aMaxYaw: 0.6f, aMaxPitch: 0.8f, aStrength: 8)
///     .Build();
/// </code>
/// </summary>
public sealed class BodyPlanBuilder
{
    private static bool Finite(Vector3 aValue) => float.IsFinite(aValue.X) && float.IsFinite(aValue.Y) && float.IsFinite(aValue.Z);

    private static bool Rotation(Quaternion aValue) => float.IsFinite(aValue.LengthSquared()) && aValue.LengthSquared() > 1e-12f;

    private readonly List<PartPlan> _parts = [];
    private readonly List<JointPlan> _joints = [];
    private readonly List<MusclePlan> _muscles = [];

    public BodyPlanBuilder Part(string aName, PartShape aShape, Vector3 aSize, float aMass, Vector3 aPosition,
        Quaternion? aOrientation = null, float aFriction = 0.6f)
    {
        _parts.Add(new PartPlan(aName, aShape, aSize, aMass, aPosition, aOrientation ?? Quaternion.Identity, aFriction));
        return this;
    }

    public BodyPlanBuilder Part(PartPlan aPart)
    {
        _parts.Add(aPart);
        return this;
    }

    /// <summary>Koło: walec <paramref name="aWheel"/> na zawieszeniu pod <paramref name="aParent"/>; kotwica = środek koła + skok w górę.</summary>
    public BodyPlanBuilder Wheel(string aName, string aParent, string aWheel, bool aSteerable, bool aDriven, float aTorque,
        float aSuspension = 0.04f, float aFrequency = 6)
    {
        var wheel = _parts.Find(aPart => aPart.Name == aWheel)
            ?? throw new ArgumentException($"Wheel part \"{aWheel}\" must be added before its joint.");
        _joints.Add(new JointPlan(aName, aParent, aWheel, wheel.Position + new Vector3(0, 0, aSuspension), JointKind.Wheel, 0, 0, aTorque,
            aSteerable, aDriven, aSuspension, aFrequency));
        return this;
    }

    /// <summary>Sztywne połączenie dziecka z rodzicem.</summary>
    public BodyPlanBuilder Weld(string aName, string aParent, string aChild)
    {
        var child = _parts.Find(aPart => aPart.Name == aChild)
            ?? throw new ArgumentException($"Part \"{aChild}\" must be added before its joint.");
        _joints.Add(new JointPlan(aName, aParent, aChild, child.Position, JointKind.Fixed, 0, 0, 1));
        return this;
    }

    public BodyPlanBuilder Joint(string aName, string aParent, string aChild, Vector3 aAnchor,
        float aMaxYaw, float aMaxPitch, float aStrength, JointKind aKind = JointKind.Ball)
    {
        _joints.Add(new JointPlan(aName, aParent, aChild, aAnchor, aKind, aMaxYaw, aMaxPitch, aStrength));
        return this;
    }

    /// <summary>
    /// Zawias: samo pochylenie dziecka (wokół jego osi Y w pozie spoczynkowej) od <paramref name="aMinPitch"/> (≤ 0) do
    /// <paramref name="aMaxPitch"/> (≥ 0) — np. kolano zginane tylko w jedną stronę; skręt zablokowany.
    /// </summary>
    public BodyPlanBuilder Hinge(string aName, string aParent, string aChild, Vector3 aAnchor, float aMinPitch, float aMaxPitch, float aStrength)
    {
        _joints.Add(new JointPlan(aName, aParent, aChild, aAnchor, JointKind.Ball, 0, aMaxPitch, aStrength, MinYaw: 0, MinPitch: aMinPitch));
        return this;
    }

    /// <summary>Obrotnica: sam skręt dziecka (wokół jego osi Z w pozie spoczynkowej) od <paramref name="aMinYaw"/> do <paramref name="aMaxYaw"/>; pochylenie zablokowane.</summary>
    public BodyPlanBuilder Swivel(string aName, string aParent, string aChild, Vector3 aAnchor, float aMinYaw, float aMaxYaw, float aStrength)
    {
        _joints.Add(new JointPlan(aName, aParent, aChild, aAnchor, JointKind.Ball, aMaxYaw, 0, aStrength, MinYaw: aMinYaw, MinPitch: 0));
        return this;
    }

    /// <summary>
    /// Staw bierny (<see cref="JointKind.Passive"/>): zakresy skrętu i pochylenia (min ≤ 0 ≤ max; 0–0 — oś zablokowana),
    /// bez serwa — ruszają nim mięśnie. <paramref name="aDamping"/> — tłumienie stawu (N·m·s/rad), w planie jako Strength.
    /// </summary>
    public BodyPlanBuilder Passive(string aName, string aParent, string aChild, Vector3 aAnchor,
        float aMinYaw, float aMaxYaw, float aMinPitch, float aMaxPitch, float aDamping = 2)
    {
        _joints.Add(new JointPlan(aName, aParent, aChild, aAnchor, JointKind.Passive, aMaxYaw, aMaxPitch, aDamping, MinYaw: aMinYaw, MinPitch: aMinPitch));
        return this;
    }

    /// <summary>Mięsień (<see cref="MusclePlan"/>).</summary>
    public BodyPlanBuilder Muscle(MusclePlan aMuscle)
    {
        _muscles.Add(aMuscle);
        return this;
    }

    /// <summary>Mięśnie z gotowych opisów (np. plan wczytany z pliku).</summary>
    public BodyPlanBuilder Muscles(IEnumerable<MusclePlan> aMuscles)
    {
        _muscles.AddRange(aMuscles);
        return this;
    }

    /// <summary>Stawy z gotowych opisów (np. plan wczytany z pliku).</summary>
    public BodyPlanBuilder Joints(IEnumerable<JointPlan> aJoints)
    {
        _joints.AddRange(aJoints);
        return this;
    }

    /// <summary>Sprawdza plan (nazwy, rozmiary, masy, drzewo stawów) i zwraca go. Rzuca <see cref="ArgumentException"/> z powodem.</summary>
    public BodyPlan Build()
    {
        if (_parts.Count == 0)
            throw new ArgumentException("A body needs at least one part.");
        var names = new HashSet<string>();
        foreach (var part in _parts)
        {
            if (string.IsNullOrWhiteSpace(part.Name) || !names.Add(part.Name))
                throw new ArgumentException($"Part name \"{part.Name}\" is empty or repeated.");
            if (!Enum.IsDefined(part.Shape) || !float.IsFinite(part.Mass) || !Finite(part.Size) ||
                !Finite(part.Position) || !Rotation(part.Orientation) || !float.IsFinite(part.Friction) ||
                !float.IsFinite(part.LateralFriction) || !float.IsFinite(part.BackwardFriction))
                throw new ArgumentException($"Part \"{part.Name}\" has invalid geometry or non-finite values.");
            if (!(part.Mass > 0) || !(part.Size.X > 0) || (part.Shape == PartShape.Box && !(part.Size.Y > 0 && part.Size.Z > 0)) ||
                (part.Shape == PartShape.Capsule && !(part.Size.Y >= 0)) || (part.Shape == PartShape.Cylinder && !(part.Size.Y > 0)))
                throw new ArgumentException($"Part \"{part.Name}\" needs a positive mass and size.");
            if (!(part.Friction >= 0) || !(part.LateralFriction >= 0) || !(part.BackwardFriction >= 0))
                throw new ArgumentException($"Part \"{part.Name}\" needs non-negative friction.");
        }

        var parents = new Dictionary<string, string>();
        var jointNames = new HashSet<string>();
        foreach (var joint in _joints)
        {
            if (string.IsNullOrWhiteSpace(joint.Name) || !jointNames.Add(joint.Name))
                throw new ArgumentException($"Joint name \"{joint.Name}\" is empty or repeated.");
            if (!names.Contains(joint.Parent) || !names.Contains(joint.Child) || joint.Parent == joint.Child)
                throw new ArgumentException($"Joint \"{joint.Name}\" must connect two different existing parts.");
            if (joint.Child == _parts[0].Name)
                throw new ArgumentException($"Joint \"{joint.Name}\": the root part \"{joint.Child}\" cannot have a parent.");
            if (!parents.TryAdd(joint.Child, joint.Parent))
                throw new ArgumentException($"Part \"{joint.Child}\" has more than one parent.");
            if (!Enum.IsDefined(joint.Kind) || !Finite(joint.Anchor) || !float.IsFinite(joint.MaxYaw) ||
                !float.IsFinite(joint.MaxPitch) || !float.IsFinite(joint.YawMin) || !float.IsFinite(joint.PitchMin) ||
                !float.IsFinite(joint.Strength) || !float.IsFinite(joint.Suspension) || !float.IsFinite(joint.SuspensionFrequency))
                throw new ArgumentException($"Joint \"{joint.Name}\" has non-finite values.");
            if (joint.MaxYaw < 0 || joint.MaxPitch < 0 || joint.YawMin > 0 || joint.PitchMin > 0 || !(joint.Strength > 0))
                throw new ArgumentException($"Joint \"{joint.Name}\" needs limits around the rest pose (min ≤ 0 ≤ max) and positive strength.");
            if (joint.Kind == JointKind.Wheel &&
                (_parts.Find(aPart => aPart.Name == joint.Child)!.Shape != PartShape.Cylinder || !(joint.Suspension > 0) || !(joint.SuspensionFrequency > 0)))
                throw new ArgumentException($"Wheel joint \"{joint.Name}\" needs a cylinder part, a positive suspension and spring frequency.");
        }

        // Każda część dochodzi po rodzicach do korzenia (brak cykli i części „wiszących w powietrzu”).
        foreach (var part in _parts.Skip(1))
        {
            var current = part.Name;
            for (var steps = 0; current != _parts[0].Name; steps++)
            {
                if (steps > _parts.Count || !parents.TryGetValue(current, out var parent))
                    throw new ArgumentException($"Part \"{part.Name}\" is not connected to the root \"{_parts[0].Name}\".");
                current = parent;
            }
        }

        var muscleNames = new HashSet<string>();
        foreach (var muscle in _muscles)
        {
            if (string.IsNullOrWhiteSpace(muscle.Name) || !muscleNames.Add(muscle.Name))
                throw new ArgumentException($"Muscle name \"{muscle.Name}\" is empty or repeated.");
            if (!names.Contains(muscle.Origin) || !names.Contains(muscle.Insertion) || muscle.Origin == muscle.Insertion)
                throw new ArgumentException($"Muscle \"{muscle.Name}\" must connect two different existing parts.");
            if (!Finite(muscle.OriginPoint) || !Finite(muscle.InsertionPoint) || !float.IsFinite(muscle.MaxForce) ||
                !float.IsFinite(muscle.MaxVelocity) || muscle.OptimalLength is { } length && !float.IsFinite(length))
                throw new ArgumentException($"Muscle \"{muscle.Name}\" has non-finite values.");
            if (!(muscle.MaxForce > 0) || muscle.OptimalLength is <= 0 || !(muscle.MaxVelocity > 0) ||
                !(Vector3.Distance(muscle.OriginPoint, muscle.InsertionPoint) > 1e-3f))
                throw new ArgumentException($"Muscle \"{muscle.Name}\" needs a positive force, length and speed.");
        }

        return new BodyPlan([.. _parts.Select(aPart => MathF.Abs(aPart.Orientation.LengthSquared() - 1) > 1e-3f
            ? aPart with { Orientation = Quaternion.Normalize(aPart.Orientation) } : aPart)], [.. _joints], _muscles.Count > 0 ? [.. _muscles] : null);
    }
}

/// <summary>
/// Mięsień (model Hilla ze sztywnym ścięgnem): linia od przyczepu początkowego (<see cref="OriginPoint"/> na części
/// <see cref="Origin"/>) do końcowego (<see cref="InsertionPoint"/> na <see cref="Insertion"/>) — punkty w układzie stwora
/// w pozie spoczynkowej. Mięsień tylko ciągnie, z siłą F = <see cref="MaxForce"/> · (a · fL · fV + fP):
/// a — aktywacja (pobudzenie z mózgu z opóźnieniem), fL — siła od długości (najwięcej przy <see cref="OptimalLength"/>,
/// domyślnie długość w pozie spoczynkowej; krzywa Geyera i Herra 2010), fV — od szybkości: liniowa styczna do krzywej
/// Hilla, liczona niejawnie w solverze (skracanie z szybkością <see cref="MaxVelocity"/>/5 długości optymalnych na sekundę
/// zeruje siłę czynną, rozciąganie wzmacnia ją najwyżej do 1.5×), fP — bierna sprężystość, gdy rozciągnięty ponad
/// długość optymalną. Mięsień może przechodzić przez kilka stawów (dwustawowy).
/// </summary>
public sealed record MusclePlan(
    string Name,
    string Origin,
    Vector3 OriginPoint,
    string Insertion,
    Vector3 InsertionPoint,
    float MaxForce,
    float? OptimalLength = null,
    float MaxVelocity = 10)
{
    /// <summary>Długość w pozie spoczynkowej (m).</summary>
    public float RestLength => Vector3.Distance(OriginPoint, InsertionPoint);

    /// <summary>Długość optymalna (m): podana albo spoczynkowa.</summary>
    public float Optimal => OptimalLength ?? RestLength;

    /// <summary>Szerokość krzywej siła–długość (w długościach optymalnych) i stała jej kształtu (Geyer i Herr).</summary>
    public const float Width = 0.56f;

    private static readonly float LengthCurve = MathF.Log(0.05f);

    /// <summary>Siła od długości: 1 przy długości optymalnej, 0.05 przy odchyleniu o <see cref="Width"/>.</summary>
    public static float ForceLength(float aRelativeLength) =>
        MathF.Exp(LengthCurve * MathF.Pow(MathF.Abs((aRelativeLength - 1) / Width), 3));

    /// <summary>Bierna sprężystość: 0 do długości optymalnej, potem kwadratowo (1 przy +<see cref="Width"/>).</summary>
    public static float Passive(float aRelativeLength) =>
        aRelativeLength > 1 ? MathF.Pow((aRelativeLength - 1) / Width, 2) : 0;
}
