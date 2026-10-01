using System.Numerics;
using Animata.Core.Bodies;
using Animata.Core.Physics;
using BepuPhysics;
using BepuPhysics.Constraints;

namespace Animata.Core.Entities;

// Mięśnie stwora z części (MusclePlan): przyczepy, aktywacja, model Hilla i silniki liniowe w fizyce.
public partial class ArticulatedCreature
{
    /// <summary>Stała czasowa narastania aktywacji mięśnia (s) — pobudzenie dochodzi do włókien z opóźnieniem.</summary>
    public const float ActivationTime = 0.01f;

    /// <summary>Stała czasowa wygasania aktywacji (s).</summary>
    public const float DeactivationTime = 0.04f;

    /// <summary>Mięsień: części i przyczepy (w układzie części planu i w układzie bryły Bepu), stan i silnik w fizyce.</summary>
    private struct MuscleState
    {
        public int Origin;
        public int Insertion;
        public Vector3 OriginLocal, InsertionLocal;
        public Vector3 OriginBepu, InsertionBepu;
        public float Excitation, Activation, Length, Speed, Force;
        public ConstraintHandle Motor;
    }

    private MuscleState[] _muscles = [];

    public int MuscleCount => _muscles.Length;

    /// <summary>Pobudzenie mięśnia [0, 1] z mózgu; działa od następnego kroku fizyki (przez aktywację z opóźnieniem).</summary>
    public void SetMuscleExcitation(int aMuscle, float aExcitation) =>
        _muscles[aMuscle].Excitation = float.IsFinite(aExcitation) ? Math.Clamp(aExcitation, 0, 1) : 0;

    /// <summary>Aktywacja mięśnia [0, 1] w ostatnim kroku.</summary>
    public float MuscleActivation(int aMuscle) => _muscles[aMuscle].Activation;

    /// <summary>Długość mięśnia (m) po ostatnim kroku.</summary>
    public float MuscleLength(int aMuscle) => _muscles[aMuscle].Length;

    /// <summary>Szybkość wydłużania mięśnia (m/s, ujemna = skracanie) po ostatnim kroku.</summary>
    public float MuscleSpeed(int aMuscle) => _muscles[aMuscle].Speed;

    /// <summary>Siła mięśnia (N) w ostatnim kroku.</summary>
    public float MuscleForce(int aMuscle) => _muscles[aMuscle].Force;

    /// <summary>Przyczepy mięśnia w świecie (początkowy, końcowy) — w bieżącej pozie ciała.</summary>
    public (Vector3 Origin, Vector3 Insertion) MuscleEnds(int aMuscle)
    {
        var muscle = _muscles[aMuscle];
        return (MusclePoint(muscle.Origin, muscle.OriginLocal), MusclePoint(muscle.Insertion, muscle.InsertionLocal));
    }

    /// <summary>Mięśnie z planu: części przyczepów, punkty w układzie części, długość spoczynkowa.</summary>
    private void SetMuscles(BodyPlan aPlan)
    {
        _muscles = new MuscleState[aPlan.MuscleList.Count];
        for (var index = 0; index < _muscles.Length; index++)
        {
            var plan = aPlan.MuscleList[index];
            ref var muscle = ref _muscles[index];
            muscle.Origin = aPlan.IndexOf(plan.Origin);
            muscle.Insertion = aPlan.IndexOf(plan.Insertion);
            var origin = aPlan.Parts[muscle.Origin];
            var insertion = aPlan.Parts[muscle.Insertion];
            muscle.OriginLocal = Vector3.Transform(plan.OriginPoint - origin.Position, Quaternion.Inverse(origin.Orientation));
            muscle.InsertionLocal = Vector3.Transform(plan.InsertionPoint - insertion.Position, Quaternion.Inverse(insertion.Orientation));
            muscle.OriginBepu = Vector3.Transform(muscle.OriginLocal, Quaternion.Inverse(FixOf(origin)));
            muscle.InsertionBepu = Vector3.Transform(muscle.InsertionLocal, Quaternion.Inverse(FixOf(insertion)));
            muscle.Length = plan.RestLength;
        }
    }

    /// <summary>Silniki liniowe mięśni (siła zero do pierwszego kroku) i pomiar długości.</summary>
    private void AttachMuscles(PhysicsWorld aPhysics)
    {
        for (var index = 0; index < _muscles.Length; index++)
        {
            ref var muscle = ref _muscles[index];
            muscle.Motor = aPhysics.AddConstraint(_bodies[muscle.Origin], _bodies[muscle.Insertion], new LinearAxisMotor
            {
                LocalOffsetA = muscle.OriginBepu,
                LocalOffsetB = muscle.InsertionBepu,
                LocalAxis = Vector3.UnitX,
                TargetVelocity = 0,
                Settings = new MotorSettings(0, 1e-6f)
            });
        }
        MeasureMuscles();
    }

    /// <summary>Punkt przyczepu w świecie: punkt w układzie części → bieżąca poza części.</summary>
    private Vector3 MusclePoint(int aPart, Vector3 aLocal) => _positions[aPart] + Vector3.Transform(aLocal, _orientations[aPart]);

    /// <summary>Długości i szybkości mięśni z bieżących póz (i prędkości ciał, jeśli stwór jest w fizyce).</summary>
    private void MeasureMuscles()
    {
        for (var index = 0; index < _muscles.Length; index++)
        {
            ref var muscle = ref _muscles[index];
            var origin = MusclePoint(muscle.Origin, muscle.OriginLocal);
            var insertion = MusclePoint(muscle.Insertion, muscle.InsertionLocal);
            var line = origin - insertion;
            var length = line.Length();
            muscle.Length = length;
            if (_physics is not { } physics || length < 1e-6f)
            {
                muscle.Speed = 0;
                continue;
            }
            var a = physics.Body(_bodies[muscle.Origin]);
            var b = physics.Body(_bodies[muscle.Insertion]);
            var velocityA = a.Velocity.Linear + Vector3.Cross(a.Velocity.Angular, origin - _positions[muscle.Origin]);
            var velocityB = b.Velocity.Linear + Vector3.Cross(b.Velocity.Angular, insertion - _positions[muscle.Insertion]);
            muscle.Speed = Vector3.Dot(line / length, velocityA - velocityB);
        }
    }

    /// <summary>
    /// Aktywacja goni pobudzenie (wykładniczo, szybciej w górę niż w dół). Siła z modelu Hilla (<see cref="MusclePlan"/>):
    /// F = F0 · (a · fL · fV + fP). Zależność od szybkości (fV) jest liczona niejawnie, przez solver fizyki: mięsień to
    /// silnik liniowy między przyczepami — tłumik o współczynniku c = a · fL · F0 / v₀, który dąży do skracania
    /// z szybkością v* = F / c (dodatnia prędkość celu skraca mięsień — pomiar). W bezruchu daje F; przy skracaniu siła
    /// maleje liniowo (do zera przy v₀ = vmax/5 — styczna do krzywej Hilla), przy rozciąganiu rośnie, najwyżej do
    /// 1.5 · a · fL · F0 + fP · F0. Tłumik liczony w każdym podkroku solvera jest stabilny; jawna fV zmieniana raz na krok
    /// świata (1/30 s) drgała. Współczynnik tłumienia Bepu jest skalowany masą — dzieli się go przez masę efektywną osi mięśnia.
    /// </summary>
    private void UpdateMuscles(PhysicsWorld aPhysics, float aDelta)
    {
        for (var index = 0; index < _muscles.Length; index++)
        {
            ref var muscle = ref _muscles[index];
            var plan = Plan.MuscleList[index];
            var time = muscle.Excitation > muscle.Activation ? ActivationTime : DeactivationTime;
            muscle.Activation += (muscle.Excitation - muscle.Activation) * (1 - MathF.Exp(-aDelta / time));

            var optimal = plan.Optimal;
            var relative = muscle.Length / optimal;
            var active = plan.MaxForce * muscle.Activation * MusclePlan.ForceLength(relative);
            var passive = plan.MaxForce * MusclePlan.Passive(relative);
            var isometric = active + passive;
            var linear = plan.MaxVelocity * optimal / 5;   // m/s: tu siła czynna spada do zera
            var damping = MathF.Max(active, 0.01f * plan.MaxForce) / linear;
            muscle.Force = Math.Clamp(isometric + damping * muscle.Speed, 0, 1.5f * active + passive);

            var origin = MusclePoint(muscle.Origin, muscle.OriginLocal);
            var insertion = MusclePoint(muscle.Insertion, muscle.InsertionLocal);
            var line = origin - insertion;
            if (line.LengthSquared() < 1e-12f)
                continue;
            var direction = Vector3.Normalize(line);
            var bodyA = aPhysics.Body(_bodies[muscle.Origin]);
            var bodyB = aPhysics.Body(_bodies[muscle.Insertion]);
            var inverseMass = InverseMass(bodyA, origin - _positions[muscle.Origin], direction)
                + InverseMass(bodyB, insertion - _positions[muscle.Insertion], direction);
            var orientationA = Quaternion.Normalize(_orientations[muscle.Origin] * FixOf(Plan.Parts[muscle.Origin]));
            aPhysics.UpdateConstraint(muscle.Motor, new LinearAxisMotor
            {
                LocalOffsetA = muscle.OriginBepu,
                LocalOffsetB = muscle.InsertionBepu,
                LocalAxis = Vector3.Transform(direction, Quaternion.Inverse(orientationA)),
                TargetVelocity = isometric > 0 ? isometric / damping : 0,
                Settings = isometric > 0
                    ? new MotorSettings(1.5f * active + passive, 1 / (damping * inverseMass))
                    : new MotorSettings(0, 1)
            });
        }
    }

    /// <summary>Odwrotność masy efektywnej ciała w punkcie (ramię <paramref name="aArm"/> od środka) wzdłuż kierunku.</summary>
    private static float InverseMass(BodyReference aBody, Vector3 aArm, Vector3 aDirection) =>
        aBody.LocalInertia.InverseMass +
        InverseInertia(aBody, Vector3.Transform(Vector3.Cross(aArm, aDirection), Quaternion.Inverse(aBody.Pose.Orientation)));
}
