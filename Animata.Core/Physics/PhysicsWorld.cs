using System.Numerics;
using System.Runtime.CompilerServices;
using BepuPhysics;
using BepuPhysics.Collidables;
using BepuPhysics.CollisionDetection;
using BepuPhysics.Constraints;
using BepuUtilities;
using BepuUtilities.Memory;

namespace Animata.Core.Physics;

/// <summary>
/// Fizyka brył (BepuPhysics 2) dla jednego świata. Układ jak w reszcie Animaty: Z w górę, grawitacja −Z.
/// Świat tworzy ją dopiero wtedy, gdy pojawi się pierwsza encja dynamiczna (<see cref="IPhysicalEntity.IsDynamic"/>),
/// więc sceny z samymi autkami i walcami w ogóle jej nie mają. Każdy świat (także każda ukryta próba w nauce)
/// ma własną symulację i własną pulę pamięci, a krok idzie na jednym wątku — wynik jest powtarzalny.
/// Grupy: części jednego stwora dostają wspólną grupę i nie zderzają się ze sobą (stawy i tak trzymają je razem;
/// wąż, który zwinie się na sobie, przenika — do poprawy, gdy będzie potrzebne). Po każdym kroku wiadomo, które ciała dotykały czegokolwiek (<see cref="IsTouching"/>).
/// Pula pamięci jest natywna — świat z fizyką trzeba zwolnić (<see cref="Dispose"/>, robi to World.Dispose).
/// </summary>
public sealed class PhysicsWorld : IDisposable
{
    public static readonly Vector3 Gravity = new(0, 0, -9.81f);

    /// <summary>Tarcie kontaktu dla ciał bez własnego (statyki: podłoga, słupki). Kontakt dwóch ciał bierze mniejsze z nich.</summary>
    public const float DefaultFriction = 0.6f;

    private readonly BufferPool _pool = new();
    private readonly PhysicsTags _tags = new();
    private readonly Dictionary<BodyHandle, TypedIndex> _bodyShapes = [];
    private readonly Dictionary<StaticHandle, TypedIndex> _staticShapes = [];
    private int _groups;
    private bool _disposed;

    /// <param name="aSubsteps">Podkroki solvera na jeden krok świata (stawy i kontakty są sztywniejsze przy większej liczbie).</param>
    public PhysicsWorld(int aSubsteps = 4, int aVelocityIterations = 2)
    {
        Simulation = Simulation.Create(_pool, new NarrowPhase(_tags), new PoseIntegrator(Gravity, 0.05f, 0.2f),
            new SolveDescription(aVelocityIterations, aSubsteps));
    }

    public Simulation Simulation { get; }

    /// <summary>Nowa grupa kolizyjna (jedna na stwora).</summary>
    public int NewGroup() => ++_groups;

    /// <summary>Ciało dynamiczne, które nigdy nie zasypia (stwory ruszają się same, a uśpione nie reagowałyby na silniki).</summary>
    /// <param name="aFriction">Tarcie kontaktu tego ciała (izotropowe, jak w Bepu).</param>
    public BodyHandle AddBody<TShape>(TShape aShape, float aMass, RigidPose aPose, int aGroup, int aIndex, float aFriction = DefaultFriction)
        where TShape : unmanaged, IConvexShape
    {
        var shape = Simulation.Shapes.Add(aShape);
        var handle = Simulation.Bodies.Add(BodyDescription.CreateDynamic(aPose, aShape.ComputeInertia(aMass), shape, -1f));
        _bodyShapes[handle] = shape;
        _tags.Set(handle.Value, aGroup, aIndex, aFriction);
        return handle;
    }

    /// <summary>Usuwa ciało razem z jego stawami i kształtem.</summary>
    public void RemoveBody(BodyHandle aHandle)
    {
        if (!Simulation.Bodies.BodyExists(aHandle))
            return;
        Simulation.Bodies.Remove(aHandle);
        _tags.Set(aHandle.Value, 0, 0, DefaultFriction);
        if (_bodyShapes.Remove(aHandle, out var shape))
            Simulation.Shapes.Remove(shape);
    }

    public StaticHandle AddStatic<TShape>(TShape aShape, RigidPose aPose) where TShape : unmanaged, IShape
    {
        var shape = Simulation.Shapes.Add(aShape);
        var handle = Simulation.Statics.Add(new StaticDescription(aPose, shape));
        _staticShapes[handle] = shape;
        return handle;
    }

    public void RemoveStatic(StaticHandle aHandle)
    {
        if (!_staticShapes.Remove(aHandle, out var shape))
            return;
        Simulation.Statics.Remove(aHandle);
        Simulation.Shapes.Remove(shape);
    }

    public ConstraintHandle AddConstraint<TDescription>(BodyHandle aA, BodyHandle aB, in TDescription aDescription)
        where TDescription : unmanaged, ITwoBodyConstraintDescription<TDescription> =>
        Simulation.Solver.Add(aA, aB, aDescription);

    public void UpdateConstraint<TDescription>(ConstraintHandle aHandle, in TDescription aDescription)
        where TDescription : unmanaged, IConstraintDescription<TDescription> =>
        Simulation.Solver.ApplyDescription(aHandle, aDescription);

    public BodyReference Body(BodyHandle aHandle) => Simulation.Bodies[aHandle];

    /// <summary>Czy ciało miało w ostatnim kroku kontakt (z podłożem, przeszkodą albo innym ciałem).</summary>
    public bool IsTouching(BodyHandle aHandle) => _tags.IsTouching(aHandle.Value);

    public void Step(float aDelta)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (aDelta <= 0)
            return;
        _tags.ClearTouching();
        Simulation.Timestep(aDelta);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        Simulation.Dispose();
        _pool.Clear();
    }

    // ---------- dane dla callbacków ----------

    /// <summary>Grupa, indeks i dotyk ciał, po wartości uchwytu. Callbacki są strukturami, więc dzielą je przez referencję.</summary>
    private sealed class PhysicsTags
    {
        private int[] _group = new int[64];
        private int[] _index = new int[64];
        private int[] _touching = new int[64];
        private float[] _friction = new float[64];

        public void Set(int aBody, int aGroup, int aIndex, float aFriction)
        {
            Ensure(aBody);
            _group[aBody] = aGroup;
            _index[aBody] = aIndex;
            _touching[aBody] = 0;
            _friction[aBody] = aFriction;
        }

        public float FrictionOf(CollidableReference aCollidable) =>
            aCollidable.Mobility == CollidableMobility.Dynamic && aCollidable.BodyHandle.Value < _friction.Length
                ? _friction[aCollidable.BodyHandle.Value]
                : DefaultFriction;

        public bool SameGroup(int aA, int aB) =>
            aA < _group.Length && aB < _group.Length && _group[aA] != 0 && _group[aA] == _group[aB];

        public int IndexOf(int aBody) => aBody < _index.Length ? _index[aBody] : -1;

        public void MarkTouching(int aBody)
        {
            if (aBody < _touching.Length)
                _touching[aBody] = 1;
        }

        public bool IsTouching(int aBody) => aBody < _touching.Length && _touching[aBody] != 0;

        public void ClearTouching() => Array.Clear(_touching);

        private void Ensure(int aBody)
        {
            if (aBody < _group.Length)
                return;
            var size = Math.Max(aBody + 1, _group.Length * 2);
            Array.Resize(ref _group, size);
            Array.Resize(ref _index, size);
            Array.Resize(ref _touching, size);
            Array.Resize(ref _friction, size);
        }
    }

    private struct NarrowPhase(PhysicsTags aTags) : INarrowPhaseCallbacks
    {
        private readonly PhysicsTags _tags = aTags;

        public void Initialize(Simulation aSimulation)
        {
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool AllowContactGeneration(int aWorkerIndex, CollidableReference aA, CollidableReference aB, ref float aSpeculativeMargin)
        {
            if (aA.Mobility != CollidableMobility.Dynamic && aB.Mobility != CollidableMobility.Dynamic)
                return false;
            if (aA.Mobility == CollidableMobility.Static || aB.Mobility == CollidableMobility.Static)
                return true;
            return !_tags.SameGroup(aA.BodyHandle.Value, aB.BodyHandle.Value);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool AllowContactGeneration(int aWorkerIndex, CollidablePair aPair, int aChildIndexA, int aChildIndexB) => true;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool ConfigureContactManifold<TManifold>(int aWorkerIndex, CollidablePair aPair, ref TManifold aManifold,
            out PairMaterialProperties aPairMaterial) where TManifold : unmanaged, IContactManifold<TManifold>
        {
            aPairMaterial.FrictionCoefficient = MathF.Min(_tags.FrictionOf(aPair.A), _tags.FrictionOf(aPair.B));
            aPairMaterial.MaximumRecoveryVelocity = 2f;
            aPairMaterial.SpringSettings = new SpringSettings(30, 1);
            for (var contact = 0; contact < aManifold.Count; contact++)
            {
                if (aManifold.GetDepth(contact) < -0.01f)
                    continue;
                Mark(aPair.A);
                Mark(aPair.B);
                break;
            }
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool ConfigureContactManifold(int aWorkerIndex, CollidablePair aPair, int aChildIndexA, int aChildIndexB,
            ref ConvexContactManifold aManifold) => true;

        private readonly void Mark(CollidableReference aCollidable)
        {
            if (aCollidable.Mobility == CollidableMobility.Dynamic)
                _tags.MarkTouching(aCollidable.BodyHandle.Value);
        }

        public void Dispose()
        {
        }
    }

    /// <summary>Grawitacja i lekkie tłumienie (jak w demach Bepu).</summary>
    private struct PoseIntegrator(Vector3 aGravity, float aLinearDamping, float aAngularDamping) : IPoseIntegratorCallbacks
    {
        private readonly Vector3 _gravity = aGravity;
        private readonly float _linearDamping = aLinearDamping;
        private readonly float _angularDamping = aAngularDamping;
        private Vector3Wide _gravityDt;
        private Vector<float> _linearDampingDt;
        private Vector<float> _angularDampingDt;

        public readonly AngularIntegrationMode AngularIntegrationMode => AngularIntegrationMode.Nonconserving;
        public readonly bool AllowSubstepsForUnconstrainedBodies => false;
        public readonly bool IntegrateVelocityForKinematics => false;

        public void Initialize(Simulation aSimulation)
        {
        }

        public void PrepareForIntegration(float aDt)
        {
            _linearDampingDt = new Vector<float>(MathF.Pow(Math.Clamp(1 - _linearDamping, 0, 1), aDt));
            _angularDampingDt = new Vector<float>(MathF.Pow(Math.Clamp(1 - _angularDamping, 0, 1), aDt));
            _gravityDt = Vector3Wide.Broadcast(_gravity * aDt);
        }

        public void IntegrateVelocity(Vector<int> aBodyIndices, Vector3Wide aPosition, QuaternionWide aOrientation,
            BodyInertiaWide aLocalInertia, Vector<int> aIntegrationMask, int aWorkerIndex, Vector<float> aDt, ref BodyVelocityWide aVelocity)
        {
            aVelocity.Linear = (aVelocity.Linear + _gravityDt) * _linearDampingDt;
            aVelocity.Angular = aVelocity.Angular * _angularDampingDt;
        }
    }
}
