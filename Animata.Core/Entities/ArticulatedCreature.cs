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
/// (AngularAxisMotor) — jak w demie samochodu Bepu.
/// Układ stwora (Body.Position / Rotation) wynika z korzenia: to poza, w której korzeń byłby w swojej pozie
/// spoczynkowej — dla leżącego węża to punkt na ziemi pod głową, obrócony jak głowa.
/// Ręczna zmiana Body.Position / Rotation (mysz, panel) albo <see cref="Place"/> stawia całe ciało w pozie
/// spoczynkowej w nowym miejscu i zeruje prędkości.
/// Tarcie kierunkowe części (<see cref="PartPlan.LateralFriction"/>, <see cref="PartPlan.BackwardFriction"/>) jest liczone
/// tutaj, przed każdym krokiem fizyki, dla części, które w poprzednim kroku czegoś dotykały — to ono pozwala wężowi
/// pełzać falowaniem.
/// </summary>
public class ArticulatedCreature : ActiveEntity, IPhysicalEntity
{
    /// <summary>Kapsuła Bepu leży wzdłuż lokalnej osi Y, a część planu wzdłuż X: poza Bepu = poza części · ten obrót.</summary>
    private static readonly Quaternion CapsuleFix = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, -MathF.PI / 2);

    private const float Gravity = 9.81f;

    /// <summary>Sztywność serw stawów (częstotliwość sprężyny, Hz). Zmiana działa od następnej zmiany celu stawu.</summary>
    public float ServoFrequency { get; set; } = 30;

    private PhysicsWorld? _physics;
    private BodyHandle[] _bodies = [];
    private ConstraintHandle[] _servos = [];
    private ConstraintHandle?[] _motors = [];
    private float[] _wheelSteer = [];
    private float[] _wheelSpeed = [];
    private float[] _wheelTorque = [];
    private float[] _sentSteer = [];
    private float[] _sentSpeed = [];
    private float[] _sentTorque = [];
    private Vector3[] _positions = [];
    private Quaternion[] _orientations = [];
    private Quaternion[] _restRelative = [];
    private int[] _parents = [];
    private int[] _children = [];
    private float[] _targetYaw = [];
    private float[] _targetPitch = [];
    private float[] _sentYaw = [];
    private float[] _sentPitch = [];
    private float[] _yaw = [];
    private float[] _pitch = [];
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

    /// <summary>Czy część czegoś dotykała w ostatnim kroku fizyki (klocka, cylindra, innej części).</summary>
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

    public bool IsPartTouching(int aPart) => _physics is { } physics && aPart < _bodies.Length && physics.IsTouching(_bodies[aPart]);

    /// <summary>Zmierzony skręt stawu (rad, wokół osi Z dziecka w pozie spoczynkowej).</summary>
    public float JointYaw(int aJoint) => _yaw[aJoint];

    /// <summary>Zmierzone pochylenie stawu (rad, wokół osi Y dziecka w pozie spoczynkowej).</summary>
    public float JointPitch(int aJoint) => _pitch[aJoint];

    /// <summary>
    /// Zadaje koło: kąt skrętu (rad, dodatni = w lewo; tylko koła skrętne), prędkość obwodowa (m/s, dodatnia = do przodu)
    /// i największy moment silnika (N·m; 0 = koło toczy się swobodnie). Działa od następnego kroku fizyki.
    /// </summary>
    public void SetWheelTarget(int aJoint, float aSteer, float aSpeed, float aTorque)
    {
        var joint = Plan.Joints[aJoint];
        if (joint.Kind != JointKind.Wheel)
            return;
        _wheelSteer[aJoint] = joint.Steerable && float.IsFinite(aSteer) ? aSteer : 0;
        _wheelSpeed[aJoint] = joint.Driven && float.IsFinite(aSpeed) ? aSpeed : 0;
        _wheelTorque[aJoint] = joint.Driven && float.IsFinite(aTorque) ? MathF.Max(0, aTorque) : 0;
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
        _targetYaw[aJoint] = JointPlan.Angle(Math.Clamp(float.IsFinite(aYaw) ? aYaw : 0, -1, 1), joint.YawMin, joint.MaxYaw);
        _targetPitch[aJoint] = JointPlan.Angle(Math.Clamp(float.IsFinite(aPitch) ? aPitch : 0, -1, 1), joint.PitchMin, joint.MaxPitch);
    }

    public override void Place(Vector3 aPosition, Quaternion aRotation)
    {
        Body.Position = aPosition;
        Body.Rotation = NormalizedIfNeeded(aRotation);
        for (var part = 0; part < Plan.Parts.Count; part++)
            (_positions[part], _orientations[part]) = RestPose(part);
        PushPoses();
        Array.Clear(_yaw);
        Array.Clear(_pitch);
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
                var parent = _parents[joint];
                var child = _children[joint];
                if (done[child] || !done[parent])
                    continue;
                var plan = Plan.Joints[joint];
                var (yaw, pitch) = plan.Kind == JointKind.Ball ? aBend(joint) : (0f, 0f);
                yaw = Math.Clamp(yaw, plan.YawMin, plan.MaxYaw);
                pitch = Math.Clamp(pitch, plan.PitchMin, plan.MaxPitch);
                var parentPart = Plan.Parts[parent];
                var childPart = Plan.Parts[child];
                var toAnchor = Vector3.Transform(plan.Anchor - parentPart.Position, Quaternion.Inverse(parentPart.Orientation));
                var fromAnchor = Vector3.Transform(childPart.Position - plan.Anchor, Quaternion.Inverse(childPart.Orientation));
                var bend = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, yaw) * Quaternion.CreateFromAxisAngle(Vector3.UnitY, pitch);
                var orientation = Quaternion.Normalize(_orientations[parent] * _restRelative[joint] * bend);
                var anchor = _positions[parent] + Vector3.Transform(toAnchor, _orientations[parent]);
                _positions[child] = anchor + Vector3.Transform(fromAnchor, orientation);
                _orientations[child] = orientation;
                if (plan.Kind == JointKind.Ball)
                {
                    _targetYaw[joint] = yaw;
                    _targetPitch[joint] = pitch;
                    _yaw[joint] = yaw;
                    _pitch[joint] = pitch;
                }
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
        Array.Copy(_yaw, _targetYaw, _yaw.Length);
        Array.Copy(_pitch, _targetPitch, _pitch.Length);
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
            var position = _positions[index];
            var orientation = _orientations[index];
            var pose = new RigidPose(position, orientation * FixOf(part));
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
            var parent = Plan.Parts[_parents[index]];
            var child = Plan.Parts[_children[index]];
            var parentBepu = parent.Orientation * FixOf(parent);
            var childBepu = child.Orientation * FixOf(child);
            var toParent = Quaternion.Inverse(parentBepu);
            var a = _bodies[_parents[index]];
            var b = _bodies[_children[index]];
            _motors[index] = null;
            switch (joint.Kind)
            {
                case JointKind.Fixed:
                    aPhysics.AddConstraint(a, b, new Weld
                    {
                        LocalOffset = Vector3.Transform(child.Position - parent.Position, toParent),
                        LocalOrientation = Quaternion.Normalize(toParent * childBepu),
                        SpringSettings = new SpringSettings(30, 1)
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
                        SpringSettings = new SpringSettings(30, 1)
                    });
                    _servos[index] = aPhysics.AddConstraint(a, b, Hinge(index, 0));
                    if (joint.Driven)
                        _motors[index] = aPhysics.AddConstraint(b, a, Motor(index, 0, 0));
                    _sentSteer[index] = 0;
                    _sentSpeed[index] = 0;
                    _sentTorque[index] = 0;
                    break;
                }
                default:
                {
                    var socket = new BallSocket
                    {
                        LocalOffsetA = Vector3.Transform(joint.Anchor - parent.Position, toParent),
                        LocalOffsetB = Vector3.Transform(joint.Anchor - child.Position, Quaternion.Inverse(childBepu)),
                        SpringSettings = new SpringSettings(30, 1)
                    };
                    aPhysics.AddConstraint(a, b, socket);
                    _servos[index] = aPhysics.AddConstraint(a, b, Servo(index, 0, 0));
                    _sentYaw[index] = 0;
                    _sentPitch[index] = 0;
                    break;
                }
            }
        }

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
        for (var joint = 0; joint < Plan.Joints.Count; joint++)
        {
            neighbours[_parents[joint]].Add(_children[joint]);
            neighbours[_children[joint]].Add(_parents[joint]);
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
        Array.Clear(_servos);
        Array.Clear(_motors);
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

        for (var joint = 0; joint < _servos.Length; joint++)
        {
            switch (Plan.Joints[joint].Kind)
            {
                case JointKind.Ball:
                    if (_targetYaw[joint] == _sentYaw[joint] && _targetPitch[joint] == _sentPitch[joint])
                        continue;
                    aPhysics.UpdateConstraint(_servos[joint], Servo(joint, _targetYaw[joint], _targetPitch[joint]));
                    _sentYaw[joint] = _targetYaw[joint];
                    _sentPitch[joint] = _targetPitch[joint];
                    break;
                case JointKind.Wheel:
                    if (_wheelSteer[joint] != _sentSteer[joint])
                    {
                        aPhysics.UpdateConstraint(_servos[joint], Hinge(joint, _wheelSteer[joint]));
                        _sentSteer[joint] = _wheelSteer[joint];
                    }
                    if (_motors[joint] is { } motor && (_wheelSpeed[joint] != _sentSpeed[joint] || _wheelTorque[joint] != _sentTorque[joint]))
                    {
                        aPhysics.UpdateConstraint(motor, Motor(joint, _wheelSpeed[joint], _wheelTorque[joint]));
                        _sentSpeed[joint] = _wheelSpeed[joint];
                        _sentTorque[joint] = _wheelTorque[joint];
                    }
                    break;
            }
        }

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
        PublishFromRoot();
    }

    // ---------- pomocnicze ----------

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
        for (var joint = 0; joint < _yaw.Length; joint++)
        {
            if (Plan.Joints[joint].Kind != JointKind.Ball)
                continue;
            var relative = Quaternion.Inverse(_orientations[_parents[joint]]) * _orientations[_children[joint]];
            var bend = Quaternion.Inverse(_restRelative[joint]) * relative;
            var axis = Vector3.Transform(Vector3.UnitX, bend);
            _yaw[joint] = MathF.Atan2(axis.Y, axis.X);
            _pitch[joint] = MathF.Atan2(-axis.Z, MathF.Sqrt(axis.X * axis.X + axis.Y * axis.Y));
        }
    }

    private void SetPlan(BodyPlan aPlan)
    {
        var parts = aPlan.Parts.Count;
        var joints = aPlan.Joints.Count;
        _bodies = new BodyHandle[parts];
        _positions = new Vector3[parts];
        _orientations = new Quaternion[parts];
        _servos = new ConstraintHandle[joints];
        _motors = new ConstraintHandle?[joints];
        _wheelSteer = new float[joints];
        _wheelSpeed = new float[joints];
        _wheelTorque = new float[joints];
        _sentSteer = new float[joints];
        _sentSpeed = new float[joints];
        _sentTorque = new float[joints];
        _restRelative = new Quaternion[joints];
        _parents = new int[joints];
        _children = new int[joints];
        _targetYaw = new float[joints];
        _targetPitch = new float[joints];
        _sentYaw = new float[joints];
        _sentPitch = new float[joints];
        _yaw = new float[joints];
        _pitch = new float[joints];
        for (var index = 0; index < joints; index++)
        {
            var joint = aPlan.Joints[index];
            _parents[index] = aPlan.IndexOf(joint.Parent);
            _children[index] = aPlan.IndexOf(joint.Child);
            _restRelative[index] = Quaternion.Inverse(aPlan.Parts[_parents[index]].Orientation) * aPlan.Parts[_children[index]].Orientation;
        }
        for (var index = 0; index < parts; index++)
            (_positions[index], _orientations[index]) = RestPose(index);
    }

    /// <summary>Poza spoczynkowa części w świecie przy obecnej pozie stwora.</summary>
    private (Vector3 Position, Quaternion Orientation) RestPose(int aPart)
    {
        var part = Plan.Parts[aPart];
        return (Body.Position + Vector3.Transform(part.Position, Body.Rotation),
            Quaternion.Normalize(Body.Rotation * part.Orientation));
    }

    /// <summary>
    /// Serwo stawu: cel B = A · T w lokalnym układzie Bepu rodzica, gdzie T = Fa⁻¹ · R0 · Rz(skręt) · Ry(pochylenie) · Fb
    /// (R0 — względny obrót w pozie spoczynkowej, F — poprawka osi kapsuły). Skręt i pochylenie są wokół osi dziecka
    /// w pozie spoczynkowej (Z i Y części), więc noga skierowana w bok zgina się tak samo jak noga skierowana do przodu.
    /// </summary>
    private AngularServo Servo(int aJoint, float aYaw, float aPitch)
    {
        var joint = Plan.Joints[aJoint];
        var parent = Plan.Parts[_parents[aJoint]];
        var child = Plan.Parts[_children[aJoint]];
        var bend = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, aYaw) * Quaternion.CreateFromAxisAngle(Vector3.UnitY, aPitch);
        var target = Quaternion.Inverse(FixOf(parent)) * _restRelative[aJoint] * bend * FixOf(child);
        return new AngularServo
        {
            TargetRelativeRotationLocalA = Quaternion.Normalize(target),
            SpringSettings = new SpringSettings(ServoFrequency, 1),
            ServoSettings = new ServoSettings(float.MaxValue, 0, joint.Strength)
        };
    }

    /// <summary>Zawias koła: oś koła (jego Y) wzdłuż osi z pozy spoczynkowej obróconej o skręt wokół pionu rodzica.</summary>
    private AngularHinge Hinge(int aJoint, float aSteer)
    {
        var parent = Plan.Parts[_parents[aJoint]];
        var child = Plan.Parts[_children[aJoint]];
        var toParent = Quaternion.Inverse(parent.Orientation * FixOf(parent));
        var axle = Vector3.Transform(Vector3.Transform(Vector3.UnitY, child.Orientation * FixOf(child)), toParent);
        var up = Vector3.Transform(Vector3.UnitZ, toParent);
        return new AngularHinge
        {
            LocalHingeAxisA = Vector3.Normalize(Vector3.Transform(axle, Quaternion.CreateFromAxisAngle(up, aSteer))),
            LocalHingeAxisB = Vector3.UnitY,
            SpringSettings = new SpringSettings(30, 1)
        };
    }

    /// <summary>Silnik koła: prędkość obwodowa → kątowa wokół osi koła (znak sprawdzony pomiarem: dodatnia = do przodu).</summary>
    private AngularAxisMotor Motor(int aJoint, float aSpeed, float aTorque)
    {
        var radius = MathF.Max(0.01f, Plan.Parts[_children[aJoint]].Size.X);
        return new AngularAxisMotor
        {
            LocalAxisA = Vector3.UnitY,
            TargetVelocity = WheelSpin * aSpeed / radius,
            Settings = new MotorSettings(MathF.Max(0, aTorque), 1e-6f)
        };
    }

    /// <summary>Znak prędkości kątowej silnika koła względem kierunku jazdy (ustalony pomiarem w teście).</summary>
    internal const float WheelSpin = 1;

    private static Quaternion FixOf(PartPlan aPart) => aPart.Shape == PartShape.Capsule ? CapsuleFix : Quaternion.Identity;

    /// <summary>Jaką część prędkości <paramref name="aSpeed"/> zabrać, gdy wolno zabrać najwyżej <paramref name="aMaxChange"/>.</summary>
    private static float Limit(float aSpeed, float aMaxChange) =>
        aSpeed <= 1e-6f ? 0 : MathF.Min(1, MathF.Max(0, aMaxChange) / aSpeed);
}
