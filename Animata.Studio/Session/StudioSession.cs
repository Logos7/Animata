using System.Numerics;
using Animata.Core.Brains;
using Animata.Core.Brains.Modules;
using Animata.Core.Entities;
using Animata.Core.Persistence;
using Animata.Core.Sensors;
using Animata.Core.Training;
using Animata.Core.WorldObjects;
using Animata.Core.Worlds;

namespace Animata.Studio.Session;

/// <summary>Zwierzątka, które da się wstawić do sceny — każde z uczonym mózgiem (sieć albo CPG), od razu się uczy.</summary>
public enum CreatureKind
{
    Car,
    Cylinder,
    Snake
}

/// <summary>
/// Jedna scena Studia: świat, symulacja (pauza, prędkość, błąd mózgu), nauka i snapshoty. Studio ma kilka scen
/// (demo, wąż), każdą z własną sesją; wszystkie idą naraz. Panele tylko czytają sesję i wołają jej akcje,
/// więc symulacja i nauka idą dalej, gdy użytkownik jest w menu albo w mózgu stwora. Wszystko wołane z wątku UI.
/// </summary>
public sealed class StudioSession : IDisposable
{
    public const float Delta = 1f / 30f;
    private const int HistoryLimit = 400;

    private readonly Dictionary<Brain, List<TrainingProgress>> _progress = [];
    private float _pending;

    private readonly Func<DemoScene> _factory;

    /// <param name="aName">Nazwa sceny (breadcrumb, kafel w menu).</param>
    /// <param name="aFactory">Świeża scena — na start i dla „od nowa”.</param>
    public StudioSession(string aName, Func<DemoScene> aFactory)
    {
        Name = aName;
        _factory = aFactory;
        Demo = aFactory();
        Training = new TrainingController(History);
    }

    public string Name { get; }

    public DemoScene Demo { get; private set; }
    public World World => Demo.World;
    public SnapshotHistory History { get; private set; } = new();
    public TrainingController Training { get; private set; }

    public bool Paused { get; private set; }
    public float Speed { get; private set; } = 1;
    public double SimTime { get; private set; }

    /// <summary>Ustawiane przez UI, np. w trakcie przeciągania encji — symulacja wtedy stoi.</summary>
    public bool Hold { get; set; }

    /// <summary>Błąd mózgu, który zatrzymał symulację, i moduł, który go rzucił.</summary>
    public string? Error { get; private set; }
    public Guid? ErrorModuleId { get; private set; }

    /// <summary>Ostatni komunikat dla użytkownika (np. „zapisano snapshot”).</summary>
    public string? Status { get; set; }

    /// <summary>Scena została zastąpiona nową (panele trzymające encje powinny się zamknąć).</summary>
    public event Action? SceneReset;

    public IEnumerable<ActiveEntity> Creatures => World.Entities.OfType<ActiveEntity>();
    public IEnumerable<ActiveEntity> NeuralCreatures => Creatures.Where(aCreature => TrainingController.FindTrainable(aCreature) is not null);

    public static string NameOf(Entity aEntity) => !string.IsNullOrWhiteSpace(aEntity.Name) ? aEntity.Name : aEntity switch
    {
        SnakeCreature => "Wąż",
        Floor floor => $"Podłoga {floor.Size.X:0.#} × {floor.Size.Y:0.#} m",
        CarCreature => "Autko",
        CylinderCreature => "Walec",
        TargetBall => "Kulka",
        Obstacle obstacle => $"Słupek r {obstacle.Radius:0.0#}",
        _ => aEntity.GetType().Name
    };

    public void StartTrainingAll()
    {
        foreach (var creature in NeuralCreatures)
            Training.Start(creature);
    }

    /// <summary>Wywoływane co klatkę UI: przenosi mistrzów z nauki, notuje postęp i przesuwa symulację.</summary>
    public void Tick()
    {
        if (Training.Poll(out var trainingError) && trainingError is not null)
            Status = trainingError;
        foreach (var creature in Creatures)
            if (creature.Brain is { } brain && Training.ProgressOf(brain) is { } progress)
            {
                if (!_progress.TryGetValue(brain, out var list))
                    _progress[brain] = list = [];
                if (list.Count == 0 || list[^1].Generation != progress.Generation)
                {
                    list.Add(progress);
                    if (list.Count > HistoryLimit)
                        list.RemoveAt(0);
                }
            }

        if (Paused || Hold)
            return;
        _pending += Speed;
        while (_pending >= 1 && !Paused)
        {
            _pending -= 1;
            Advance();
        }
    }

    /// <summary>Jeden krok symulacji (przycisk „krok” w pauzie).</summary>
    public void Step()
    {
        Error = null;
        ErrorModuleId = null;
        Advance();
    }

    public void TogglePause()
    {
        Paused = !Paused;
        if (!Paused)
        {
            Error = null;
            ErrorModuleId = null;
        }
    }

    public void Resume()
    {
        Paused = false;
        Error = null;
        ErrorModuleId = null;
    }

    private static readonly float[] Speeds = [0.25f, 0.5f, 1, 2, 4];

    public void CycleSpeed()
    {
        var index = Array.IndexOf(Speeds, Speed);
        Speed = Speeds[(index + 1) % Speeds.Length];
    }

    private void Advance()
    {
        try
        {
            World.Update(Delta);
            SimTime += Delta;
        }
        catch (BrainException exception)
        {
            Paused = true;
            Error = exception.Message;
            ErrorModuleId = exception.ModuleId;
        }
    }

    // ---------- nauka ----------

    /// <summary>Postęp nauki mózgu (od najstarszego pokolenia w tej sesji).</summary>
    public IReadOnlyList<TrainingProgress> ProgressHistory(Brain aBrain) =>
        _progress.TryGetValue(aBrain, out var list) ? list : [];

    public bool IsTraining(ActiveEntity aCreature) => aCreature.Brain is { } brain && Training.IsTraining(brain);

    /// <summary>Jeśli któryś stwór z zakresu się uczy — zatrzymuje; inaczej startuje wszystkie. Zwraca komunikat.</summary>
    public string ToggleTraining(IReadOnlyList<ActiveEntity> aScope)
    {
        if (aScope.Count == 0)
            return Status = "brak stwora z siecią";
        var running = aScope.Where(IsTraining).ToArray();
        if (running.Length > 0)
        {
            string? error = null;
            foreach (var creature in running)
                error = Training.Stop(creature.Brain!) ?? error;
            return Status = error ?? "nauka zatrzymana — mistrz zapisany w snapshocie";
        }
        foreach (var creature in aScope)
            Training.Start(creature);
        return Status = "nauka wznowiona";
    }

    public string StopTraining(Brain aBrain) => Status = Training.Stop(aBrain) ?? "nauka zatrzymana";

    public string Randomize(IReadOnlyList<ActiveEntity> aScope)
    {
        if (aScope.Count == 0)
            return Status = "brak stwora z siecią";
        foreach (var creature in aScope)
        {
            _progress.Remove(creature.Brain!);
            Training.RandomizeAndRestart(creature);
        }
        return Status = "wagi wylosowane — nauka od zera (Z cofa)";
    }

    // ---------- snapshoty ----------

    public string SaveSnapshot(Brain aBrain)
    {
        var snapshot = History.Capture(aBrain, $"ręczny {aBrain.Snapshots.Count + 1}");
        return Status = snapshot is not null
            ? $"zapisano: {snapshot.Label} ({snapshot.Modules.Count} mod.)"
            : $"bez zmian od: {aBrain.Snapshots[^1].Label}";
    }

    /// <summary>Cofa do poprzedniego snapshotu (w trakcie nauki najpierw ją zatrzymuje).</summary>
    public string StepBack(Brain aBrain)
    {
        var error = Training.Stop(aBrain);
        if (History.StepBack(aBrain) is not { } restored)
            return Status = error ?? (aBrain.Snapshots.Count == 0 ? "brak snapshotów" : "brak snapshotów innych niż stan bieżący");
        return Status = $"przywrócono {restored.Index + 1}/{aBrain.Snapshots.Count}: {restored.Snapshot.Label}";
    }

    public string Restore(Brain aBrain, BrainSnapshot aSnapshot)
    {
        var error = Training.Stop(aBrain);
        aBrain.Restore(aSnapshot);
        return Status = error ?? $"przywrócono: {aSnapshot.Label}";
    }

    // ---------- świat ----------

    public static string KindName(CreatureKind aKind) => aKind switch
    {
        CreatureKind.Car => "Autko",
        CreatureKind.Cylinder => "Walec",
        _ => "Wąż"
    };

    /// <summary>„1 segment”, „3 segmenty”, „8 segmentów”.</summary>
    public static string Segments(int aCount)
    {
        var word = aCount == 1 ? "segment"
            : aCount % 10 is >= 2 and <= 4 && aCount % 100 is not (>= 12 and <= 14) ? "segmenty"
            : "segmentów";
        return $"{aCount} {word}";
    }

    /// <summary>„1 wąs”, „3 wąsy”, „5 wąsów”, „23 wąsy”.</summary>
    public static string Whiskers(int aCount)
    {
        var word = aCount == 1 ? "wąs"
            : aCount % 10 is >= 2 and <= 4 && aCount % 100 is not (>= 12 and <= 14) ? "wąsy"
            : "wąsów";
        return $"{aCount} {word}";
    }

    /// <summary>
    /// Wstawia zwierzątko w punkcie podłoża: patrzy na najbliższą kulkę i na nią poluje, od razu się uczy
    /// (autko i walec — sieć neuronowa, wąż — CPG). Liczbę wąsów i segmentów zmienia się potem we właściwościach.
    /// </summary>
    public ActiveEntity AddCreature(CreatureKind aKind, Vector3 aPosition)
    {
        var position = aPosition with { Z = 0 };
        var target = NearestTarget(position);
        var yaw = target is null ? 0 : MathF.Atan2(target.Body.Position.Y - position.Y, target.Body.Position.X - position.X);
        ActiveEntity creature = aKind switch
        {
            CreatureKind.Car => WorldObjectCatalog.CreateNeuralCar(position, yaw, target?.Id),
            CreatureKind.Cylinder => WorldObjectCatalog.CreateLearningSeeker(position, target?.Id),
            _ => WorldObjectCatalog.CreateLearningSnake(position, yaw, target?.Id)
        };
        creature.Place(position, Quaternion.CreateFromAxisAngle(Vector3.UnitZ, yaw));
        creature.Name = UniqueName(KindName(aKind));
        World.Add(creature);
        if (TrainingController.FindTrainable(creature) is not null)
            Training.Start(creature);
        Status = target is null ? $"dodano: {creature.Name} (brak kulki — dodaj cel)" : $"dodano: {creature.Name}";
        return creature;
    }

    /// <summary>Nowa kulka; oczy, które nie mają celu (albo ich cel zniknął), patrzą na nią.</summary>
    public TargetBall AddTarget(Vector3 aPosition)
    {
        var target = WorldObjectCatalog.CreateTargetBall(aPosition with { Z = 0 });
        target.Name = UniqueName("Kulka");
        World.Add(target);
        foreach (var eye in Creatures.SelectMany(aCreature => aCreature.Body.Sensors.OfType<TargetSensor>()))
            if (eye.TargetId is not { } id || World.Find(id) is null)
                eye.TargetId = target.Id;
        Status = $"dodano: {target.Name}";
        return target;
    }

    public Obstacle AddObstacle(Vector3 aPosition)
    {
        var obstacle = WorldObjectCatalog.CreateObstacle(aPosition with { Z = 0 });
        World.Add(obstacle);
        Status = "dodano słupek";
        return obstacle;
    }

    /// <summary>Wszystkie oczy patrzą na podaną kulkę.</summary>
    public void AimAllEyes(TargetBall aTarget)
    {
        foreach (var eye in Creatures.SelectMany(aCreature => aCreature.Body.Sensors.OfType<TargetSensor>()))
            eye.TargetId = aTarget.Id;
        Status = $"wszystkie oczy patrzą na: {NameOf(aTarget)}";
    }

    /// <summary>
    /// Zmienia liczbę wąsów stwora w miejscu (ten sam stwór i mózg, patrz <see cref="WhiskerRewiring"/>): sieć zachowuje
    /// przeliczone wagi, AvoidAndSeek parametry, snapshoty są przeliczane. Trwająca nauka jest zatrzymywana i wznawiana
    /// już w ciele z nową liczbą wąsów. Zwraca false (z powodem w <see cref="Status"/>), gdy się nie da.
    /// </summary>
    public bool SetWhiskers(ActiveEntity aCreature, int aWhiskers)
    {
        if (!WorldObjectCatalog.IsValidWhiskerCount(aWhiskers))
        {
            Status = $"liczba wąsów musi być nieparzysta, od 1 do {WorldObjectCatalog.MaxWhiskers}";
            return false;
        }
        if (WorldObjectCatalog.WhiskerCountOf(aCreature) == aWhiskers)
            return true;

        var brain = aCreature.Brain;
        var training = brain is not null && Training.IsTraining(brain);
        if (training)
            Training.Stop(brain!);
        try
        {
            WhiskerRewiring.SetCount(aCreature, aWhiskers);
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException or BrainException)
        {
            Status = exception.Message;
            if (training)
                Training.Start(aCreature);
            return false;
        }

        if (brain is not null)
        {
            History.Forget(brain);
            _progress.Remove(brain);
        }
        if (training)
            Training.Start(aCreature);
        Status = $"{NameOf(aCreature)}: {Whiskers(aWhiskers)}" + (training ? " — nauka wznowiona od przeliczonych wag" : string.Empty);
        return true;
    }

    /// <summary>
    /// Zmienia liczbę segmentów węża w miejscu (<see cref="SnakeCreature.SetSegments"/>): parametry CPG zostają,
    /// trwająca nauka jest zatrzymywana i wznawiana w ciele o nowej długości.
    /// </summary>
    public bool SetSegments(SnakeCreature aSnake, int aSegments)
    {
        if (!WorldObjectCatalog.IsValidSnakeLength(aSegments))
        {
            Status = $"wąż ma od {WorldObjectCatalog.MinSnakeSegments} do {WorldObjectCatalog.MaxSnakeSegments} segmentów";
            return false;
        }
        if (aSnake.Segments == aSegments)
            return true;

        var brain = aSnake.Brain;
        var training = brain is not null && Training.IsTraining(brain);
        if (training)
            Training.Stop(brain!);
        try
        {
            aSnake.SetSegments(aSegments);
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException or BrainException)
        {
            Status = exception.Message;
            if (training)
                Training.Start(aSnake);
            return false;
        }

        if (brain is not null)
            _progress.Remove(brain);
        if (training)
            Training.Start(aSnake);
        Status = $"{NameOf(aSnake)}: {Segments(aSegments)}" + (training ? " — nauka wznowiona" : string.Empty);
        return true;
    }

    private TargetBall? NearestTarget(Vector3 aPosition) => World.Entities.OfType<TargetBall>()
        .MinBy(aTarget => Vector3.DistanceSquared(aTarget.Body.Position, aPosition));

    private string UniqueName(string aName)
    {
        var taken = World.Entities.Select(aEntity => aEntity.Name).ToHashSet();
        if (!taken.Contains(aName))
            return aName;
        var number = 2;
        while (taken.Contains($"{aName} {number}"))
            number++;
        return $"{aName} {number}";
    }

    public void Remove(Entity aEntity)
    {
        if (aEntity.IsFixed)
        {
            Status = $"{NameOf(aEntity)} jest nieruszalna — nie da się jej usunąć";
            return;
        }
        if (aEntity is ActiveEntity { Brain: { } brain })
        {
            Training.Stop(brain, aSnapshot: false);
            History.Forget(brain);
            _progress.Remove(brain);
        }
        World.Remove(aEntity);
    }

    /// <summary>Scena od zera: zatrzymuje nauki, czyści historię, startuje naukę stworów z uczonym modułem.</summary>
    public void ResetScene()
    {
        Replace(_factory(), 0);
        Status = $"{Name}: od nowa";
        StartTrainingAll();
    }

    /// <summary>Zapis sceny (świat, mózgi, snapshoty) do dokumentu JSON.</summary>
    public WorldDocument Save() => WorldFile.Capture(World, Name, SimTime);

    /// <summary>Wczytuje zapisany świat w miejsce obecnego. Nauka nie startuje sama (L ją włącza).</summary>
    public void Load(WorldDocument aDocument)
    {
        var scene = WorldFile.Restore(aDocument);
        Replace(scene, aDocument.Time);
        Status = $"wczytano: {aDocument.Name} ({scene.World.Entities.Count} encji) — L włącza naukę";
    }

    private void Replace(DemoScene aScene, double aTime)
    {
        Training.Dispose();
        _progress.Clear();
        var old = Demo.World;
        History = new SnapshotHistory();
        Demo = aScene;
        Training = new TrainingController(History);
        old.Dispose();
        SimTime = aTime;
        Paused = false;
        Error = null;
        ErrorModuleId = null;
        SceneReset?.Invoke();
    }

    public void Dispose()
    {
        Training.Dispose();
        World.Dispose();
    }
}
