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
    Sphere
}

/// <summary>Rodzaj stawu.</summary>
public enum JointKind
{
    /// <summary>
    /// Przegub kulowy z serwem: dziecko trzyma zadany skręt (wokół osi Z rodzica, „yaw”) i pochylenie
    /// (wokół osi Y rodzica, „pitch”) względem pozy spoczynkowej; skręcania wokół własnej osi nie ma.
    /// </summary>
    Ball
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
        _ => MathF.Min(Size.X, MathF.Min(Size.Y, Size.Z)) / 2
    };
}

/// <summary>
/// Staw łączący rodzica z dzieckiem w punkcie <see cref="Anchor"/> (układ stwora, poza spoczynkowa).
/// Kąty graniczne w radianach; <see cref="Strength"/> — największy moment serwa (N·m).
/// </summary>
public sealed record JointPlan(
    string Name,
    string Parent,
    string Child,
    Vector3 Anchor,
    JointKind Kind,
    float MaxYaw,
    float MaxPitch,
    float Strength);

/// <summary>
/// Plan ciała z klocków: części i stawy (drzewo — każda część poza pierwszą ma dokładnie jednego rodzica).
/// Pierwsza część to korzeń: jej poza jest pozą stwora (Body.Position / Rotation), na niej zwykle siedzi głowa z okiem.
/// To czyste dane — da się je zapisać, wygenerować z parametrów (np. liczby segmentów) i przebudować nimi stwora.
/// Buduje się je przez <see cref="BodyPlanBuilder"/>, który sprawdza spójność.
/// </summary>
public sealed record BodyPlan(IReadOnlyList<PartPlan> Parts, IReadOnlyList<JointPlan> Joints)
{
    public PartPlan Root => Parts[0];

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
    private readonly List<PartPlan> _parts = [];
    private readonly List<JointPlan> _joints = [];

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

    public BodyPlanBuilder Joint(string aName, string aParent, string aChild, Vector3 aAnchor,
        float aMaxYaw, float aMaxPitch, float aStrength, JointKind aKind = JointKind.Ball)
    {
        _joints.Add(new JointPlan(aName, aParent, aChild, aAnchor, aKind, aMaxYaw, aMaxPitch, aStrength));
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
            if (!(part.Mass > 0) || !(part.Size.X > 0) || (part.Shape == PartShape.Box && !(part.Size.Y > 0 && part.Size.Z > 0)) ||
                (part.Shape == PartShape.Capsule && !(part.Size.Y >= 0)))
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
            if (joint.MaxYaw < 0 || joint.MaxPitch < 0 || !(joint.Strength > 0))
                throw new ArgumentException($"Joint \"{joint.Name}\" needs non-negative limits and positive strength.");
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

        return new BodyPlan([.. _parts], [.. _joints]);
    }
}
