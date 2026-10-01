using System.Numerics;
using Animata.Core.Bodies;
using Animata.Core.Brains;
using Animata.Core.Physics;
using BepuPhysics;
using BepuPhysics.Collidables;
using BepuPhysics.Constraints;

namespace Animata.Core.Entities;

/// <summary>
/// Stwór z części (<see cref="BodyPlan"/>) poruszający się w fizyce Bepu. Każda część to bryła. Staw kulowy to BallSocket
/// z serwem kątowym (AngularServo), które trzyma zadany skręt i pochylenie; staw sztywny to Weld; koło to zawieszenie
/// (LinearAxisServo + PointOnLineServo), zawias osi (AngularHinge, skręt = obrót osi wokół pionu) i silnik
/// (AngularAxisMotor) — jak w demie samochodu Bepu; staw bierny — przegub, więzy osi, ograniczniki i tłumik; mięśnie —
/// w <c>ArticulatedCreature.Muscles.cs</c>.
/// Układ stwora (Body.Position / Rotation) wynika z korzenia: to poza, w której korzeń byłby w swojej pozie
/// spoczynkowej — dla leżącego węża to punkt na ziemi pod głową, obrócony jak głowa.
/// Ręczna zmiana Body.Position / Rotation (mysz, panel) albo <see cref="Place"/> stawia całe ciało w pozie
/// spoczynkowej w nowym miejscu i zeruje prędkości.
/// Tarcie kierunkowe części (<see cref="PartPlan.LateralFriction"/>, <see cref="PartPlan.BackwardFriction"/>) jest liczone
/// tutaj, przed każdym krokiem fizyki, dla części, które w poprzednim kroku czegoś dotykały — to ono pozwala wężowi
/// pełzać falowaniem.
/// </summary>
public partial class ArticulatedCreature : ActiveEntity, IPhysicalEntity
{
    /// <summary>Kapsuła Bepu leży wzdłuż lokalnej osi Y, a część planu wzdłuż X: poza Bepu = poza części · ten obrót.</summary>
    private static readonly Quaternion CapsuleFix = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, -MathF.PI / 2);

    /// <summary>Sprężyna więzów, które mają trzymać sztywno (przeguby, spawy, zawiasy, prowadnice).</summary>
    private static readonly SpringSettings Rigid = new(30, 1);

    private static readonly float Gravity = -PhysicsWorld.Gravity.Z;

    /// <summary>Sztywność serw stawów (częstotliwość sprężyny, Hz). Zmiana działa od następnej zmiany celu stawu.</summary>
    public float ServoFrequency { get; set; } = 30;

    /// <summary>Stan stawu: części, poza spoczynkowa, więzy w fizyce, cele, wysłane cele i zmierzone kąty.</summary>
    private struct JointState
    {
        public int Parent;
        public int Child;
        public Quaternion RestRelative;
        public ConstraintHandle Servo;      // serwo stawu kulowego, zawias koła albo tłumik stawu biernego
        public ConstraintHandle? Motor;     // silnik koła napędzanego
        public float TargetYaw, TargetPitch, SentYaw, SentPitch;
        public float Yaw, Pitch;
        public float WheelSteer, WheelSpeed, WheelTorque, SentSteer, SentSpeed, SentTorque;
    }

    private PhysicsWorld? _physics;
    private BodyHandle[] _bodies = [];
    private Vector3[] _positions = [];
    private Quaternion[] _orientations = [];
    private JointState[] _joints = [];
    private Vector3 _publishedPosition;
    private Quaternion _publishedRotation = Quaternion.Identity;

    public ArticulatedCreature(BodyPlan aPlan, Brain? aBrain = null) : base(new Body(), aBrain)
    {
        Plan = aPlan;
        SetPlan(aPlan);
    }

    public BodyPlan Plan { get; private set; }

    [Setting("Kolor", Color = true, Tip = "Kolor nic nie znaczy — to tylko wygląd.")]
    public Vector3 Color { get; set; } = new(0.36f, 0.72f, 0.42f);

    public override float BoundingRadius => Plan.Root.Radius;

    public override EntityCategory Category => EntityCategory.Creature;

    public int JointCount => Plan.Joints.Count;

    /// <summary>Pozycje części w świecie (po ostatnim kroku fizyki albo z pozy spoczynkowej przed podpięciem).</summary>
    public IReadOnlyList<Vector3> PartPositions => _positions;

    /// <summary>Orientacje części w świecie (oś X części = jej długość).</summary>
    public IReadOnlyList<Quaternion> PartOrientations => _orientations;

    /// <summary>
    /// Pchnięcie: dodaje prędkość (m/s) wszystkim częściom — jak uderzenie w cały stwór. Działa tylko w świecie z fizyką;
    /// zwraca false, gdy stwór nie jest w fizyce.
    /// </summary>
    public bool Push(Vector3 aVelocity)
    {
        if (_physics is not { } physics)
            return false;
        foreach (var handle in _bodies)
        {
            var body = physics.Body(handle);
            body.Velocity.Linear += aVelocity;
            body.Awake = true;
        }
        return true;
    }

    /// <summary>Czy część czegoś dotykała w ostatnim kroku fizyki (klocka, cylindra, innej części).</summary>
    public bool IsPartTouching(int aPart) => _physics is { } physics && aPart < _bodies.Length && physics.IsTouching(_bodies[aPart]);

    /// <summary>Zmierzony skręt stawu (rad, wokół osi Z dziecka w pozie spoczynkowej).</summary>
    public float JointYaw(int aJoint) => _joints[aJoint].Yaw;

    /// <summary>Zmierzone pochylenie stawu (rad, wokół osi Y dziecka w pozie spoczynkowej).</summary>
    public float JointPitch(int aJoint) => _joints[aJoint].Pitch;

    /// <summary>
    /// Zadaje koło: kąt skrętu (rad, dodatni = w lewo; tylko koła skrętne), prędkość obwodowa (m/s, dodatnia = do przodu)
    /// i największy moment silnika (N·m; 0 = koło toczy się swobodnie). Działa od następnego kroku fizyki.
    /// </summary>
    public void SetWheelTarget(int aJoint, float aSteer, float aSpeed, float aTorque)
    {
        var joint = Plan.Joints[aJoint];
        if (joint.Kind != JointKind.Wheel)
            return;
        ref var state = ref _joints[aJoint];
        state.WheelSteer = joint.Steerable && float.IsFinite(aSteer) ? aSteer : 0;
        state.WheelSpeed = joint.Driven && float.IsFinite(aSpeed) ? aSpeed : 0;
        state.WheelTorque = joint.Driven && float.IsFinite(aTorque) ? MathF.Max(0, aTorque) : 0;
    }

    /// <summary>
    /// Zadaje staw kulowy: ułamki [-1, 1] zakresu z planu (ujemne — do dolnej granicy, dodatnie — do górnej; 0 = poza
    /// spoczynkowa). Staw jednokierunkowy nie wychodzi poza swój zakres. Działa od następnego kroku fizyki.
    /// </summary>
    public void SetJointTarget(int aJoint, float aYaw, float aPitch)
    {
        var joint = Plan.Joints[aJoint];
        if (joint.Kind != JointKind.Ball)
            return;
        _joints[aJoint].TargetYaw = JointPlan.Angle(Math.Clamp(float.IsFinite(aYaw) ? aYaw : 0, -1, 1), joint.YawMin, joint.MaxYaw);
        _joints[aJoint].TargetPitch = JointPlan.Angle(Math.Clamp(float.IsFinite(aPitch) ? aPitch : 0, -1, 1), joint.PitchMin, joint.MaxPitch);
    }

    public override void Place(Vector3 aPosition, Quaternion aRotation)
    {
        Body.Position = aPosition;
        Body.Rotation = NormalizedIfNeeded(aRotation);
        for (var part = 0; part < Plan.Parts.Count; part++)
            (_positions[part], _orientations[part]) = RestPose(part);
        PushPoses();
        for (var joint = 0; joint < _joints.Length; joint++)
            (_joints[joint].Yaw, _joints[joint].Pitch) = (0, 0);
        for (var muscle = 0; muscle < _muscles.Length; muscle++)
            _muscles[muscle].Activation = 0;
        MeasureMuscles();
        _publishedPosition = Body.Position;
        _publishedRotation = Body.Rotation;
    }

    /// <summary>
    /// Stawia ciało wygięte: korzeń w pozie (<paramref name="aPosition"/>, <paramref name="aRotation"/>), a każdy staw kulowy
    /// zgięty o (skręt, pochylenie) w radianach — części liczone od korzenia (kinematyka prosta, ta sama konwencja osi co
    /// serwa). Cele serw dostają te same kąty, więc ciało zostaje w tej pozie, dopóki mózg go nie zmieni (np. wąż owinięty
    /// wokół pnia na starcie próby wspinania). Prędkości zerowane.
    /// </summary>
    public void PlaceBent(Vector3 aPosition, Quaternion aRotation, Func<int, (float Yaw, float Pitch)> aBend)
    {
        Place(aPosition, aRotation);
        var done = new bool[Plan.Parts.Count];
        done[0] = true;
        var remaining = Plan.Joints.Count;
        while (remaining > 0)
        {
            var progress = false;
            for (var joint = 0; joint < Plan.Joints.Count; joint++)
            {
                ref var state = ref _joints[joint];
                var parent = state.Parent;
                var child = state.Child;
                if (done[child] || !done[parent])
                    continue;
                var plan = Plan.Joints[joint];
                var (yaw, pitch) = plan.Bends ? aBend(joint) : (0f, 0f);
                yaw = Math.Clamp(yaw, plan.YawMin, plan.MaxYaw);
                pitch = Math.Clamp(pitch, plan.PitchMin, plan.MaxPitch);
                var parentPart = Plan.Parts[parent];
                var childPart = Plan.Parts[child];
                var toAnchor = Vector3.Transform(plan.Anchor - parentPart.Position, Quaternion.Inverse(parentPart.Orientation));
                var fromAnchor = Vector3.Transform(childPart.Position - plan.Anchor, Quaternion.Inverse(childPart.Orientation));
                var orientation = Quaternion.Normalize(_orientations[parent] * state.RestRelative * Bend(yaw, pitch));
                var anchor = _positions[parent] + Vector3.Transform(toAnchor, _orientations[parent]);
                _positions[child] = anchor + Vector3.Transform(fromAnchor, orientation);
                _orientations[child] = orientation;
                if (plan.Bends)
                    (state.TargetYaw, state.TargetPitch, state.Yaw, state.Pitch) = (yaw, pitch, yaw, pitch);
                done[child] = true;
                remaining--;
                progress = true;
            }
            if (!progress)
                break;
        }
        PushPoses();
    }

    /// <summary>
    /// Stawia części dokładnie w podanych pozach świata (np. wczytany wąż owinięty wokół pnia). Układ stwora wynika
    /// z korzenia, cele serw — ze zmierzonych kątów stawów (ciało zostaje w tej pozie). Prędkości zerowane.
    /// </summary>
    public void PlaceParts(IReadOnlyList<Vector3> aPositions, IReadOnlyList<Quaternion> aOrientations)
    {
        if (aPositions.Count != Plan.Parts.Count || aOrientations.Count != Plan.Parts.Count)
            throw new ArgumentException("One pose per part is required.");
        for (var part = 0; part < Plan.Parts.Count; part++)
        {
            _positions[part] = aPositions[part];
            _orientations[part] = NormalizedIfNeeded(aOrientations[part]);
        }
        PushPoses();
        MeasureJoints();
        for (var joint = 0; joint < _joints.Length; joint++)
            (_joints[joint].TargetYaw, _joints[joint].TargetPitch) = (_joints[joint].Yaw, _joints[joint].Pitch);
        PublishFromRoot();
    }

    /// <summary>
    /// Nowy plan ciała (np. inna liczba segmentów). Ciało staje w pozie spoczynkowej tam, gdzie jest stwór,
    /// cele stawów się zerują. Aktuatory, sensory i mózg dopasowuje wołający.
    /// </summary>
    public void Rebuild(BodyPlan aPlan)
    {
        var physics = _physics;
        if (physics is not null)
            ((IPhysicalEntity)this).DetachPhysics(physics);
        Plan = aPlan;
        SetPlan(aPlan);
        if (physics is not null)
            ((IPhysicalEntity)this).AttachPhysics(physics);
    }

    // ---------- fizyka ----------

    bool IPhysicalEntity.IsDynamic => true;

    void IPhysicalEntity.AttachPhysics(PhysicsWorld aPhysics)
    {
        _physics = aPhysics;
        for (var index = 0; index < Plan.Parts.Count; index++)
        {
            var part = Plan.Parts[index];
            // Bieżąca poza części: spoczynkowa (Place) albo wygięta (PlaceBent) przed dodaniem do świata.
            var pose = new RigidPose(_positions[index], _orientations[index] * FixOf(part));
            _bodies[index] = part.Shape switch
            {
                PartShape.Capsule => aPhysics.AddBody(new Capsule(part.Size.X, part.Size.Y), part.Mass, pose, part.Friction),
                PartShape.Sphere => aPhysics.AddBody(new Sphere(part.Size.X), part.Mass, pose, part.Friction),
                PartShape.Cylinder => aPhysics.AddBody(new Cylinder(part.Size.X, part.Size.Y), part.Mass, pose, part.Friction),
                _ => aPhysics.AddBody(new Box(part.Size.X, part.Size.Y, part.Size.Z), part.Mass, pose, part.Friction)
            };
        }

        for (var index = 0; index < Plan.Joints.Count; index++)
        {
            var joint = Plan.Joints[index];
            ref var state = ref _joints[index];
            var parent = Plan.Parts[state.Parent];
            var child = Plan.Parts[state.Child];
            var childBepu = child.Orientation * FixOf(child);
            var toParent = Quaternion.Inverse(parent.Orientation * FixOf(parent));
            var a = _bodies[state.Parent];
            var b = _bodies[state.Child];
            state.Motor = null;
            switch (joint.Kind)
            {
                case JointKind.Fixed:
                    aPhysics.AddConstraint(a, b, new Weld
                    {
                        LocalOffset = Vector3.Transform(child.Position - parent.Position, toParent),
                        LocalOrientation = Quaternion.Normalize(toParent * childBepu),
                        SpringSettings = Rigid
                    });
                    break;
                case JointKind.Wheel:
                {
                    var down = Vector3.Transform(-Vector3.UnitZ, toParent);
                    var top = Vector3.Transform(joint.Anchor - parent.Position, toParent);
                    aPhysics.AddConstraint(a, b, new LinearAxisServo
                    {
                        LocalPlaneNormal = down,
                        TargetOffset = joint.Suspension,
                        LocalOffsetA = top,
                        LocalOffsetB = default,
                        ServoSettings = ServoSettings.Default,
                        SpringSettings = new SpringSettings(joint.SuspensionFrequency, 0.7f)
                    });
                    aPhysics.AddConstraint(a, b, new PointOnLineServo
                    {
                        LocalDirection = down,
                        LocalOffsetA = top,
                        LocalOffsetB = default,
                        ServoSettings = ServoSettings.Default,
                        SpringSettings = Rigid
                    });
                    state.Servo = aPhysics.AddConstraint(a, b, Hinge(index, 0));
                    if (joint.Driven)
                        state.Motor = aPhysics.AddConstraint(b, a, Motor(index, 0, 0));
                    (state.SentSteer, state.SentSpeed, state.SentTorque) = (0, 0, 0);
                    break;
                }
                case JointKind.Passive:
                    AddPassiveJoint(aPhysics, index, a, b, toParent, childBepu);
                    break;
                default:
                    aPhysics.AddConstraint(a, b, Socket(joint, parent, child, toParent, childBepu, Rigid));
                    state.Servo = aPhysics.AddConstraint(a, b, Servo(index, 0, 0));
                    (state.SentYaw, state.SentPitch) = (0, 0);
                    break;
            }
        }

        AttachMuscles(aPhysics);

        // Części odległe w drzewie stawów o 1 albo 2 (staw, albo wspólny sąsiad — np. udo pająka i tułów przez
        // przyspawane biodro) nie zderzają się ze sobą; dalsze tak (wąż nie przenika sam przez siebie).
        foreach (var (first, second) in NearPairs())
            aPhysics.IgnoreCollision(_bodies[first], _bodies[second]);

        _publishedPosition = Body.Position;
        _publishedRotation = Body.Rotation;
    }

    /// <summary>Pary części w odległości 1 albo 2 w drzewie stawów.</summary>
    private HashSet<(int, int)> NearPairs()
    {
        var neighbours = Enumerable.Range(0, Plan.Parts.Count).Select(_ => new HashSet<int>()).ToArray();
        foreach (var joint in _joints)
        {
            neighbours[joint.Parent].Add(joint.Child);
            neighbours[joint.Child].Add(joint.Parent);
        }
        var pairs = new HashSet<(int, int)>();
        for (var part = 0; part < neighbours.Length; part++)
            foreach (var near in neighbours[part])
            {
                pairs.Add((Math.Min(part, near), Math.Max(part, near)));
                foreach (var far in neighbours[near])
                    if (far != part)
                        pairs.Add((Math.Min(part, far), Math.Max(part, far)));
            }
        return pairs;
    }

    void IPhysicalEntity.DetachPhysics(PhysicsWorld aPhysics)
    {
        foreach (var body in _bodies)
            aPhysics.RemoveBody(body);
        Array.Clear(_bodies);
        for (var joint = 0; joint < _joints.Length; joint++)
            (_joints[joint].Servo, _joints[joint].Motor) = (default, null);
        for (var muscle = 0; muscle < _muscles.Length; muscle++)
            _muscles[muscle].Motor = default;
        _physics = null;
    }

    /// <summary>
    /// Ręczna zmiana Body.Position / Rotation (mysz, panel) — całe ciało staje w nowym miejscu. Wołane przed krokiem
    /// fizyki i przez renderer (żeby przeciągany w pauzie stwór od razu się przesuwał).
    /// </summary>
    public void ApplyExternalMove()
    {
        if (Body.Position != _publishedPosition || Body.Rotation != _publishedRotation)
            Place(Body.Position, Body.Rotation);
    }

    void IPhysicalEntity.BeforePhysicsStep(PhysicsWorld aPhysics, float aDelta)
    {
        ApplyExternalMove();

        for (var joint = 0; joint < _joints.Length; joint++)
        {
            ref var state = ref _joints[joint];
            switch (Plan.Joints[joint].Kind)
            {
                case JointKind.Ball:
                    if (state.TargetYaw == state.SentYaw && state.TargetPitch == state.SentPitch)
                        continue;
                    aPhysics.UpdateConstraint(state.Servo, Servo(joint, state.TargetYaw, state.TargetPitch));
                    (state.SentYaw, state.SentPitch) = (state.TargetYaw, state.TargetPitch);
                    break;
                case JointKind.Passive:
                    UpdateDamper(aPhysics, joint);
                    break;
                case JointKind.Wheel:
                    if (state.WheelSteer != state.SentSteer)
                    {
                        aPhysics.UpdateConstraint(state.Servo, Hinge(joint, state.WheelSteer));
                        state.SentSteer = state.WheelSteer;
                    }
                    if (state.Motor is { } motor && (state.WheelSpeed != state.SentSpeed || state.WheelTorque != state.SentTorque))
                    {
                        aPhysics.UpdateConstraint(motor, Motor(joint, state.WheelSpeed, state.WheelTorque));
                        (state.SentSpeed, state.SentTorque) = (state.WheelSpeed, state.WheelTorque);
                    }
                    break;
            }
        }

        UpdateMuscles(aPhysics, aDelta);

        // Łuski: tarcie Coulomba w bok i do tyłu wzdłuż podłoża — prędkość maleje najwyżej o μ·g·Δt (do zera).
        for (var index = 0; index < _bodies.Length; index++)
        {
            var part = Plan.Parts[index];
            if ((part.LateralFriction <= 0 && part.BackwardFriction <= 0) || !aPhysics.IsTouching(_bodies[index]))
                continue;
            var axis = Vector3.Transform(Vector3.UnitX, _orientations[index]);
            var flat = new Vector2(axis.X, axis.Y);
            if (flat.LengthSquared() < 1e-6f)
                continue;
            flat = Vector2.Normalize(flat);
            var body = aPhysics.Body(_bodies[index]);
            var velocity = new Vector2(body.Velocity.Linear.X, body.Velocity.Linear.Y);
            var along = Vector2.Dot(velocity, flat);
            var lateral = velocity - flat * along;
            var change = -lateral * Limit(lateral.Length(), part.LateralFriction * Gravity * aDelta);
            if (along < 0)
                change -= flat * (along * Limit(-along, part.BackwardFriction * Gravity * aDelta));
            body.Velocity.Linear += new Vector3(change, 0);
        }
    }

    void IPhysicalEntity.AfterPhysicsStep(PhysicsWorld aPhysics)
    {
        for (var index = 0; index < _bodies.Length; index++)
        {
            var pose = aPhysics.Body(_bodies[index]).Pose;
            _positions[index] = pose.Position;
            _orientations[index] = Quaternion.Normalize(pose.Orientation * Quaternion.Inverse(FixOf(Plan.Parts[index])));
        }

        MeasureJoints();
        MeasureMuscles();
        PublishFromRoot();
    }

    // ---------- stawy bierne ----------

    /// <summary>
    /// Tłumik stawu biernego: c = <see cref="JointPlan.Strength"/> (N·m·s/rad). Bepu skaluje tłumienie bezwładnością,
    /// więc dzieli się c przez bezwładność efektywną wokół osi pochylenia (odwrotności bezwładności obu członów).
    /// </summary>
    private void UpdateDamper(PhysicsWorld aPhysics, int aJoint)
    {
        var state = _joints[aJoint];
        var damping = Plan.Joints[aJoint].Strength;
        var a = aPhysics.Body(_bodies[state.Parent]);
        var b = aPhysics.Body(_bodies[state.Child]);
        var axis = Vector3.Transform(Vector3.UnitY, _orientations[state.Child]);
        var inverse = InverseInertia(a, Vector3.Transform(axis, Quaternion.Inverse(a.Pose.Orientation)))
            + InverseInertia(b, Vector3.Transform(axis, Quaternion.Inverse(b.Pose.Orientation)));
        aPhysics.UpdateConstraint(state.Servo, new AngularMotor
        {
            TargetVelocityLocalA = Vector3.Zero,
            Settings = new MotorSettings(MaxDamperTorque, 1 / (damping * inverse))
        });
    }

    /// <summary>Największy moment tłumika stawu (N·m).</summary>
    private const float MaxDamperTorque = 1000;

    /// <summary>v · I⁻¹ · v dla wektora w lokalnym układzie ciała (odwrotność bezwładności wokół tej osi, razy |v|²).</summary>
    private static float InverseInertia(BodyReference aBody, Vector3 aLocal)
    {
        var tensor = aBody.LocalInertia.InverseInertiaTensor;
        var rotated = new Vector3(
            tensor.XX * aLocal.X + tensor.YX * aLocal.Y + tensor.ZX * aLocal.Z,
            tensor.YX * aLocal.X + tensor.YY * aLocal.Y + tensor.ZY * aLocal.Z,
            tensor.ZX * aLocal.X + tensor.ZY * aLocal.Y + tensor.ZZ * aLocal.Z);
        return Vector3.Dot(aLocal, rotated);
    }

    /// <summary>
    /// Staw bierny: przegub kulowy, więzy osi (zawias — samo pochylenie, obrotnica — sam skręt, przegub Cardana — oba)
    /// i ograniczniki skrętu i pochylenia (TwistLimit wokół osi Z i Y dziecka w pozie spoczynkowej — dla obrotu
    /// Rz(skręt)·Ry(pochylenie) skręt wokół Z to dokładnie skręt, a wokół Y dokładnie pochylenie).
    /// </summary>
    private void AddPassiveJoint(PhysicsWorld aPhysics, int aJoint, BodyHandle aA, BodyHandle aB, Quaternion aToParent, Quaternion aChildBepu)
    {
        var joint = Plan.Joints[aJoint];
        var child = Plan.Parts[_joints[aJoint].Child];
        var limit = new SpringSettings(LimitFrequency, 1);
        aPhysics.AddConstraint(aA, aB, Socket(joint, Plan.Parts[_joints[aJoint].Parent], child, aToParent, aChildBepu, limit));
        // Baza dziecka w pozie spoczynkowej w układzie Bepu rodzica (A) i dziecka (B).
        var restInA = Quaternion.Normalize(aToParent * child.Orientation);
        var restInB = Quaternion.Inverse(FixOf(child));
        var toY = Quaternion.CreateFromAxisAngle(Vector3.UnitX, -MathF.PI / 2);   // oś Z bazy → oś Y dziecka
        if (joint.HasYaw && joint.HasPitch)
            aPhysics.AddConstraint(aA, aB, new AngularSwivelHinge
            {
                LocalSwivelAxisA = Vector3.Transform(Vector3.UnitZ, restInA),
                LocalHingeAxisB = Vector3.Transform(Vector3.UnitY, restInB),
                SpringSettings = Rigid
            });
        else
        {
            var axis = joint.HasYaw ? Vector3.UnitZ : Vector3.UnitY;
            aPhysics.AddConstraint(aA, aB, new AngularHinge
            {
                LocalHingeAxisA = Vector3.Transform(axis, restInA),
                LocalHingeAxisB = Vector3.Transform(axis, restInB),
                SpringSettings = Rigid
            });
        }
        if (joint.HasYaw)
            aPhysics.AddConstraint(aA, aB, new TwistLimit
            {
                LocalBasisA = restInA,
                LocalBasisB = restInB,
                MinimumAngle = joint.YawMin,
                MaximumAngle = joint.MaxYaw,
                SpringSettings = limit
            });
        // Tłumienie stawu (tkanki, maź): tłumik kątowy o współczynniku Strength (N·m·s/rad), liczony w solverze.
        _joints[aJoint].Servo = aPhysics.AddConstraint(aA, aB, new AngularMotor
        {
            TargetVelocityLocalA = Vector3.Zero,
            Settings = new MotorSettings(0, 1)
        });
        if (joint.HasPitch)
            aPhysics.AddConstraint(aA, aB, new TwistLimit
            {
                LocalBasisA = Quaternion.Normalize(restInA * toY),
                LocalBasisB = Quaternion.Normalize(restInB * toY),
                MinimumAngle = joint.PitchMin,   // ogranicznik wokół osi Y mierzy pochylenie z tym samym znakiem (pomiar)
                MaximumAngle = joint.MaxPitch,
                SpringSettings = limit
            });
    }

    /// <summary>Sztywność ograniczników stawów biernych (Hz).</summary>
    public const float LimitFrequency = 120;

    // ---------- pomocnicze ----------

    /// <summary>Przegub kulowy w kotwicy stawu (przesunięcia w układach Bepu rodzica i dziecka).</summary>
    private static BallSocket Socket(JointPlan aJoint, PartPlan aParent, PartPlan aChild, Quaternion aToParent, Quaternion aChildBepu,
        SpringSettings aSpring) => new()
    {
        LocalOffsetA = Vector3.Transform(aJoint.Anchor - aParent.Position, aToParent),
        LocalOffsetB = Vector3.Transform(aJoint.Anchor - aChild.Position, Quaternion.Inverse(aChildBepu)),
        SpringSettings = aSpring
    };

    /// <summary>Bieżące pozy części do brył fizyki (jeśli stwór w niej jest), z zerowymi prędkościami.</summary>
    private void PushPoses()
    {
        if (_physics is not { } physics)
            return;
        for (var part = 0; part < Plan.Parts.Count; part++)
        {
            var body = physics.Body(_bodies[part]);
            body.Pose = new RigidPose(_positions[part], _orientations[part] * FixOf(Plan.Parts[part]));
            body.Velocity = default;
        }
    }

    /// <summary>Układ stwora z korzenia: poza, w której korzeń byłby w swojej pozie spoczynkowej.</summary>
    private void PublishFromRoot()
    {
        var root = Plan.Root;
        Body.Rotation = Quaternion.Normalize(_orientations[0] * Quaternion.Inverse(root.Orientation));
        Body.Position = _positions[0] - Vector3.Transform(root.Position, Body.Rotation);
        _publishedPosition = Body.Position;
        _publishedRotation = Body.Rotation;
    }

    private static Quaternion NormalizedIfNeeded(Quaternion aRotation) =>
        MathF.Abs(aRotation.LengthSquared() - 1) > 1e-4f ? Quaternion.Normalize(aRotation) : aRotation;

    /// <summary>Kąty stawów kulowych z bieżących orientacji części.</summary>
    private void MeasureJoints()
    {
        for (var joint = 0; joint < _joints.Length; joint++)
        {
            if (!Plan.Joints[joint].Bends)
                continue;
            ref var state = ref _joints[joint];
            var relative = Quaternion.Inverse(_orientations[state.Parent]) * _orientations[state.Child];
            var bend = Quaternion.Inverse(state.RestRelative) * relative;
            var axis = Vector3.Transform(Vector3.UnitX, bend);
            state.Yaw = MathF.Atan2(axis.Y, axis.X);
            state.Pitch = MathF.Atan2(-axis.Z, MathF.Sqrt(axis.X * axis.X + axis.Y * axis.Y));
        }
    }

    private void SetPlan(BodyPlan aPlan)
    {
        var parts = aPlan.Parts.Count;
        _bodies = new BodyHandle[parts];
        _positions = new Vector3[parts];
        _orientations = new Quaternion[parts];
        _joints = new JointState[aPlan.Joints.Count];
        for (var index = 0; index < _joints.Length; index++)
        {
            var joint = aPlan.Joints[index];
            ref var state = ref _joints[index];
            state.Parent = aPlan.IndexOf(joint.Parent);
            state.Child = aPlan.IndexOf(joint.Child);
            state.RestRelative = Quaternion.Inverse(aPlan.Parts[state.Parent].Orientation) * aPlan.Parts[state.Child].Orientation;
        }
        for (var index = 0; index < parts; index++)
            (_positions[index], _orientations[index]) = RestPose(index);
        SetMuscles(aPlan);
    }

    /// <summary>Poza spoczynkowa części w świecie przy obecnej pozie stwora.</summary>
    private (Vector3 Position, Quaternion Orientation) RestPose(int aPart)
    {
        var part = Plan.Parts[aPart];
        return (Body.Position + Vector3.Transform(part.Position, Body.Rotation),
            Quaternion.Normalize(Body.Rotation * part.Orientation));
    }

    /// <summary>Obrót stawu: skręt wokół osi Z, potem pochylenie wokół osi Y (w pozie spoczynkowej dziecka).</summary>
    private static Quaternion Bend(float aYaw, float aPitch) =>
        Quaternion.CreateFromAxisAngle(Vector3.UnitZ, aYaw) * Quaternion.CreateFromAxisAngle(Vector3.UnitY, aPitch);

    /// <summary>
    /// Serwo stawu: cel B = A · T w lokalnym układzie Bepu rodzica, gdzie T = Fa⁻¹ · R0 · Rz(skręt) · Ry(pochylenie) · Fb
    /// (R0 — względny obrót w pozie spoczynkowej, F — poprawka osi kapsuły). Skręt i pochylenie są wokół osi dziecka
    /// w pozie spoczynkowej (Z i Y części), więc noga skierowana w bok zgina się tak samo jak noga skierowana do przodu.
    /// </summary>
    private AngularServo Servo(int aJoint, float aYaw, float aPitch)
    {
        var state = _joints[aJoint];
        var target = Quaternion.Inverse(FixOf(Plan.Parts[state.Parent])) * state.RestRelative * Bend(aYaw, aPitch) * FixOf(Plan.Parts[state.Child]);
        return new AngularServo
        {
            TargetRelativeRotationLocalA = Quaternion.Normalize(target),
            SpringSettings = new SpringSettings(ServoFrequency, 1),
            ServoSettings = new ServoSettings(float.MaxValue, 0, Plan.Joints[aJoint].Strength)
        };
    }

    /// <summary>Zawias koła: oś koła (jego Y) wzdłuż osi z pozy spoczynkowej obróconej o skręt wokół pionu rodzica.</summary>
    private AngularHinge Hinge(int aJoint, float aSteer)
    {
        var parent = Plan.Parts[_joints[aJoint].Parent];
        var child = Plan.Parts[_joints[aJoint].Child];
        var toParent = Quaternion.Inverse(parent.Orientation * FixOf(parent));
        var axle = Vector3.Transform(Vector3.Transform(Vector3.UnitY, child.Orientation * FixOf(child)), toParent);
        var up = Vector3.Transform(Vector3.UnitZ, toParent);
        return new AngularHinge
        {
            LocalHingeAxisA = Vector3.Normalize(Vector3.Transform(axle, Quaternion.CreateFromAxisAngle(up, aSteer))),
            LocalHingeAxisB = Vector3.UnitY,
            SpringSettings = Rigid
        };
    }

    /// <summary>Silnik koła: prędkość obwodowa → kątowa wokół osi koła (dodatnia = do przodu, sprawdzone pomiarem).</summary>
    private AngularAxisMotor Motor(int aJoint, float aSpeed, float aTorque)
    {
        var radius = MathF.Max(0.01f, Plan.Parts[_joints[aJoint].Child].Size.X);
        return new AngularAxisMotor
        {
            LocalAxisA = Vector3.UnitY,
            TargetVelocity = aSpeed / radius,
            Settings = new MotorSettings(MathF.Max(0, aTorque), 1e-6f)
        };
    }

    private static Quaternion FixOf(PartPlan aPart) => aPart.Shape == PartShape.Capsule ? CapsuleFix : Quaternion.Identity;

    /// <summary>Jaką część prędkości <paramref name="aSpeed"/> zabrać, gdy wolno zabrać najwyżej <paramref name="aMaxChange"/>.</summary>
    private static float Limit(float aSpeed, float aMaxChange) =>
        aSpeed <= 1e-6f ? 0 : MathF.Min(1, MathF.Max(0, aMaxChange) / aSpeed);
}
