using System.Numerics;
using Animata.Core.Brains;
using Animata.Core.Brains.Modules;
using Animata.Core.Brains.Neural;
using Animata.Core.Entities;
using Animata.Core.Persistence;
using Animata.Core.Sensors;
using Animata.Core.Training;
using Animata.Core.WorldObjects;
using Animata.Core.Worlds;

namespace Animata.Studio.Session;

/// <summary>Zwierzątka, które da się wstawić do sceny — każde z uczonym mózgiem (sieć albo CPG) z losowymi parametrami; uczy się dopiero po starcie przez użytkownika.</summary>
public enum CreatureKind
{
    Car,
    Cylinder,
    Snake,
    Spider
}

/// <summary>
/// Jedna scena Studia: świat, symulacja (pauza, prędkość, błąd mózgu), nauka i snapshoty. Studio ma kilka scen
/// (demo, węże, pająki, wspinaczka), każdą z własną sesją; żyje tylko otwarta (<see cref="Visible"/>). Panele tylko
/// czytają sesję i wołają jej akcje, więc w panelach stwora i mózgu scena dalej żyje. Wszystko wołane z wątku UI.
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
        Training = new TrainingController(History) { Paused = true };
    }

    public string Name { get; }

    /// <summary>Krótki opis sceny do kafla w menu (pusty — bez opisu).</summary>
    public string Description { get; init; } = string.Empty;

    public DemoScene Demo { get; private set; }
    public World World => Demo.World;
    public SnapshotHistory History { get; private set; } = new();
    public TrainingController Training { get; private set; }

    public bool Paused { get; private set; }
    public float Speed { get; private set; } = 1;

    /// <summary>Ustawiane przez UI, np. w trakcie przeciągania encji — symulacja wtedy stoi.</summary>
    public bool Hold { get; set; }

    /// <summary>
    /// Czy scena jest otwarta (jej panel jest na stosie nawigacji). Zamknięta scena stoi: nie tyka symulacja
    /// (<see cref="Tick"/> woła tylko okno dla otwartych scen), a trwająca nauka czeka między pokoleniami.
    /// </summary>
    public bool Visible
    {
        get => _visible;
        set
        {
            _visible = value;
            Training.Paused = !value;
        }
    }

    private bool _visible;

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
        SpiderCreature => "Pająk",
        CarCreature => "Autko",
        CylinderCreature => "Walec",
        Box box => $"Klocek {box.Size.X:0.##} × {box.Size.Y:0.##} × {box.Size.Z:0.##} m",
        Cylinder cylinder => $"Cylinder r {cylinder.Radius:0.0#}",
        Sphere => "Kula",
        _ => aEntity.GetType().Name
    };

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

        SnapStatics();
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
        CreatureKind.Snake => "Wąż",
        _ => "Pająk"
    };

    /// <summary>
    /// Przyciąganie do terenu (domyślnie włączone): kule i cylindry stoją na najwyższym klocku pod nimi, klocki — na klocku
    /// zablokowanym (podłodze); nowe i przeciągane encje (także stwory) lądują na tej wysokości. Wyłączone — wysokość się nie zmienia.
    /// </summary>
    public bool SnapToGround { get; set; } = true;

    /// <summary>Wysokość, na której staje encja w punkcie (przy wyłączonym przyciąganiu — 0 albo bez zmian).</summary>
    public float GroundAt(Vector3 aPoint, Entity? aEntity = null) =>
        !SnapToGround ? aEntity?.Body.Position.Z ?? 0
        : aEntity is Box ? Terrain.HeightAt(World, new Vector2(aPoint.X, aPoint.Y), aEntity, aLockedOnly: true)
        : Terrain.HeightAt(World, new Vector2(aPoint.X, aPoint.Y), aEntity);

    public string ToggleSnap()
    {
        SnapToGround = !SnapToGround;
        return Status = SnapToGround ? "przyciąganie do terenu: włączone" : "przyciąganie do terenu: wyłączone";
    }

    /// <summary>Kule, cylindry i klocki z powrotem na terenie (np. po zmianie pozycji w panelu albo przesunięciu klocka); zablokowanych nie rusza.</summary>
    private void SnapStatics()
    {
        if (!SnapToGround)
            return;
        foreach (var entity in World.Entities)
            if (entity is Box or Sphere or Cylinder)
                Terrain.Snap(World, entity);
    }

    /// <summary>„1 segment”, „3 segmenty”, „8 segmentów”.</summary>
    public static string Segments(int aCount) => Plural(aCount, "segment", "segmenty", "segmentów");

    /// <summary>„1 wąs”, „3 wąsy”, „5 wąsów”, „23 wąsy”.</summary>
    public static string Whiskers(int aCount) => Plural(aCount, "wąs", "wąsy", "wąsów");

    private static string Plural(int aCount, string aOne, string aFew, string aMany)
    {
        var word = aCount == 1 ? aOne
            : aCount % 10 is >= 2 and <= 4 && aCount % 100 is not (>= 12 and <= 14) ? aFew
            : aMany;
        return $"{aCount} {word}";
    }

    /// <summary>
    /// Wstawia zwierzątko w punkcie podłoża: patrzy na najbliższą kulę i na nią poluje; ma losowe parametry i nie uczy się, dopóki użytkownik nie włączy nauki
    /// (autko i walec — sieć neuronowa, wąż — CPG). Liczbę wąsów i segmentów zmienia się potem we właściwościach.
    /// </summary>
    public ActiveEntity AddCreature(CreatureKind aKind, Vector3 aPosition)
    {
        var position = aPosition with { Z = GroundAt(aPosition) };
        var target = NearestTarget(position);
        var yaw = target is null ? 0 : MathF.Atan2(target.Body.Position.Y - position.Y, target.Body.Position.X - position.X);
        // Każde zwierzątko dostaje sieć z dwiema warstwami ukrytymi i losowymi wagami (inny mózg — w grafie, z palety)
        // oraz losowy kolor (zmienia się go we właściwościach).
        var color = WorldObjectCatalog.RandomColor();
        ActiveEntity creature = aKind switch
        {
            CreatureKind.Car => WorldObjectCatalog.CreateCar(position, yaw, color, target?.Id,
                WorldObjectCatalog.CreateCarNeuralModule(WorldObjectCatalog.DefaultWhiskers, WorldObjectCatalog.DefaultCarHidden)),
            CreatureKind.Cylinder => WorldObjectCatalog.CreateSeeker(position, color, target?.Id,
                WorldObjectCatalog.CreateCylinderNeuralModule(WorldObjectCatalog.DefaultCylinderHidden)),
            CreatureKind.Snake => WorldObjectCatalog.CreateSnake(position, yaw, color, target?.Id,
                WorldObjectCatalog.CreateSnakeNeuralModule(WorldObjectCatalog.DefaultSnakeSegments, WorldObjectCatalog.SnakeHiddenLayers)),
            _ => WorldObjectCatalog.CreateSpider(position, yaw, color, target?.Id,
                WorldObjectCatalog.CreateSpiderNeuralModule(false, WorldObjectCatalog.DefaultSpiderHidden))
        };
        creature.Place(position, Quaternion.CreateFromAxisAngle(Vector3.UnitZ, yaw));
        creature.Name = UniqueName(KindName(aKind));
        World.Add(creature);
        Status = target is null ? $"dodano: {creature.Name} (brak kuli — dodaj cel)" : $"dodano: {creature.Name}";
        return creature;
    }

    /// <summary>Nowa kula; oczy, które nie mają celu (albo ich cel zniknął), patrzą na nią.</summary>
    public Sphere AddSphere(Vector3 aPosition)
    {
        var target = WorldObjectCatalog.CreateSphere(aPosition with { Z = GroundAt(aPosition) });
        target.Name = UniqueName("Kula");
        World.Add(target);
        foreach (var eye in Creatures.SelectMany(aCreature => aCreature.Body.Sensors.OfType<TargetSensor>()))
            if (eye.TargetId is not { } id || World.Find(id) is null)
                eye.TargetId = target.Id;
        Status = $"dodano: {target.Name}";
        return target;
    }

    /// <summary>Cylinder r 0.5 m, 0.8 m na terenie.</summary>
    public Cylinder AddCylinder(Vector3 aPosition)
    {
        var cylinder = WorldObjectCatalog.CreateCylinder(aPosition with { Z = GroundAt(aPosition) });
        cylinder.Name = UniqueName("Cylinder");
        World.Add(cylinder);
        Status = $"dodano: {cylinder.Name}";
        return cylinder;
    }

    /// <summary>
    /// Owija węża wokół najbliższego cylindra (jego podstawy) i robi z niego wspinacza — nauka będzie go uczyć wchodzenia
    /// na ten cylinder (trzyma się tylko cylindra z tarciem chwytnym). Zwraca false, gdy w scenie nie ma cylindra.
    /// </summary>
    public bool WrapAroundNearestCylinder(SnakeCreature aSnake)
    {
        var tree = World.Entities.OfType<Cylinder>()
            .MinBy(aTree => Vector2.Distance(new Vector2(aTree.Body.Position.X, aTree.Body.Position.Y), new Vector2(aSnake.Body.Position.X, aSnake.Body.Position.Y)));
        if (tree is null)
        {
            Status = "w scenie nie ma cylindra";
            return false;
        }
        if (WithTrainingPaused(aSnake, () =>
            {
                aSnake.WrapAround(tree);
                aSnake.Brain?.Reset();
            }) is null)
            return false;
        Status = $"{NameOf(aSnake)} owinięty wokół: {NameOf(tree)} — uczy się wspinać"
            + (tree.Grip > 0 ? string.Empty : " (cylinder bez tarcia chwytnego — wąż się zsunie; ustaw je we właściwościach)");
        return true;
    }

    /// <summary>Płaski klocek 1.5 × 1 × 0.06 m na podłodze.</summary>
    public Box AddBox(Vector3 aPosition)
    {
        var slab = WorldObjectCatalog.CreateBox(aPosition with { Z = 0 }, new Vector3(1.5f, 1, 0.06f));
        slab.Name = UniqueName("Klocek");
        World.Add(slab);
        if (SnapToGround)
            Terrain.Snap(World, slab);
        Status = $"dodano: {slab.Name}";
        return slab;
    }

    /// <summary>Wszystkie oczy patrzą na podaną encję (kulę, stwora, bryłę); stwór-cel nie patrzy sam na siebie.</summary>
    public void AimAllEyes(Entity aTarget)
    {
        foreach (var eye in Creatures.Where(aCreature => !ReferenceEquals(aCreature, aTarget))
                     .SelectMany(aCreature => aCreature.Body.Sensors.OfType<TargetSensor>()))
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

        var training = WithTrainingPaused(aCreature, () =>
        {
            WhiskerRewiring.SetCount(aCreature, aWhiskers);
            if (aCreature.Brain is { } brain)
                History.Forget(brain);
        });
        if (training is null)
            return false;
        Status = $"{NameOf(aCreature)}: {Whiskers(aWhiskers)}" + (training == true ? " — nauka wznowiona od przeliczonych wag" : string.Empty);
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

        var training = WithTrainingPaused(aSnake, () => aSnake.SetSegments(aSegments));
        if (training is null)
            return false;
        Status = $"{NameOf(aSnake)}: {Segments(aSegments)}" + (training == true ? " — nauka wznowiona" : string.Empty);
        return true;
    }

    /// <summary>
    /// Zmienia kształt sieci (<paramref name="aChange"/>: np. <see cref="NeuralNetwork.ResizeLayer"/>, InsertLayer, RemoveLayer).
    /// Przed zmianą zapisuje snapshot „przed zmianą sieci” (powrót do starego kształtu), trwającą naukę zatrzymuje
    /// i wznawia już dla nowego kształtu. Zwraca false (powód w <see cref="Status"/>), gdy się nie da.
    /// </summary>
    public bool ReshapeNetwork(ActiveEntity aCreature, NeuralNetworkModule aModule, Action<NeuralNetwork> aChange)
    {
        BrainSnapshot? before = null;
        var training = WithTrainingPaused(aCreature, () =>
        {
            if (aCreature.Brain is { } brain)
                before = History.Capture(brain, "przed zmianą sieci", aModule);
            aChange(aModule.Network);
            aCreature.Brain?.Graph.InvalidateDeep();
        });
        if (training is null)
            return false;
        Status = $"{NameOf(aCreature)}: sieć {string.Join(" → ", aModule.Network.Layers)}, {aModule.ParameterCount} parametrów"
            + (training == true ? " — nauka wznowiona" : before is not null ? " — stary kształt w snapshotach" : string.Empty);
        return true;
    }

    /// <summary>
    /// Zmiana ciała albo mózgu stwora przy zatrzymanej nauce: trwająca nauka staje (mistrz trafia do snapshotu), po zmianie
    /// rusza od nowa już dla nowego kształtu, a historia postępu jest czyszczona. Gdy zmiana rzuci (np. niepoprawny graf),
    /// powód trafia do <see cref="Status"/>, nauka wraca i zwracane jest null; inaczej — czy nauka szła.
    /// </summary>
    private bool? WithTrainingPaused(ActiveEntity aCreature, Action aChange)
    {
        var brain = aCreature.Brain;
        var training = brain is not null && Training.IsTraining(brain);
        if (training)
            Training.Stop(brain!);
        try
        {
            aChange();
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException or BrainException)
        {
            Status = exception.Message;
            if (training)
                Training.Start(aCreature);
            return null;
        }
        if (brain is not null)
            _progress.Remove(brain);
        if (training)
            Training.Start(aCreature);
        return training;
    }

    private Sphere? NearestTarget(Vector3 aPosition) => World.Entities.OfType<Sphere>()
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
        if (aEntity.Locked)
        {
            Status = $"{NameOf(aEntity)}: zablokowane — odblokuj we właściwościach, żeby usunąć";
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

    /// <summary>Usuwa wiele encji naraz (nieruszalne pomija). Zwraca liczbę usuniętych.</summary>
    public int Remove(IEnumerable<Entity> aEntities)
    {
        var removed = 0;
        foreach (var entity in aEntities.ToList())
        {
            if (entity.Locked || !World.Contains(entity))
                continue;
            Remove(entity);
            removed++;
        }
        Status = removed == 1 ? "usunięto 1 encję" : $"usunięto {removed} encji";
        return removed;
    }

    // ---------- schowek (wspólny dla wszystkich scen) ----------

    private static IReadOnlyList<EntityDocument> _clipboard = [];
    private static Vector3 _clipboardCenter;
    private int _pasteCount;

    public static bool HasClipboard => _clipboard.Count > 0;

    /// <summary>
    /// Kopiuje encje do schowka (całe: ciało, mózg, snapshoty, ustawienia — jak w pliku świata). Zablokowane (np. podłoga)
    /// są pomijane. Schowek jest wspólny dla scen — można wkleić w drugiej scenie.
    /// </summary>
    public int Copy(IEnumerable<Entity> aEntities)
    {
        var entities = aEntities.Where(aEntity => !aEntity.Locked && World.Contains(aEntity)).ToList();
        if (entities.Count == 0)
        {
            Status = "nic do skopiowania";
            return 0;
        }
        try
        {
            _clipboard = WorldFile.CaptureEntities(entities);
        }
        catch (NotSupportedException exception)
        {
            Status = $"nie skopiowano: {exception.Message}";
            return 0;
        }
        _clipboardCenter = entities.Aggregate(Vector3.Zero, (aSum, aEntity) => aSum + aEntity.Body.Position) / entities.Count;
        _pasteCount = 0;
        Status = entities.Count == 1 ? $"skopiowano: {NameOf(entities[0])}" : $"skopiowano {entities.Count} encji";
        return entities.Count;
    }

    /// <summary>Kopiuje do schowka i usuwa ze sceny.</summary>
    public int Cut(IEnumerable<Entity> aEntities)
    {
        var entities = aEntities.Where(aEntity => !aEntity.Locked && World.Contains(aEntity)).ToList();
        var copied = Copy(entities);
        if (copied == 0)
            return 0;
        foreach (var entity in entities)
            Remove(entity);
        Status = copied == 1 ? $"wycięto: {NameOf(entities[0])}" : $"wycięto {copied} encji";
        return copied;
    }

    /// <summary>
    /// Wkleja schowek: środek wklejanych encji w <paramref name="aAt"/> (np. punkt pod kursorem), a bez punktu — obok
    /// oryginałów (każde kolejne wklejenie o metr dalej). Kopie mają nowe Id i nazwy, nie uczą się; przy przyciąganiu
    /// stają na terenie. Zwraca wklejone encje.
    /// </summary>
    public IReadOnlyList<Entity> Paste(Vector3? aAt = null)
    {
        if (_clipboard.Count == 0)
        {
            Status = "schowek jest pusty";
            return [];
        }
        _pasteCount++;
        var offset = aAt is { } at
            ? new Vector3(at.X - _clipboardCenter.X, at.Y - _clipboardCenter.Y, 0)
            : new Vector3(_pasteCount, _pasteCount, 0);
        IReadOnlyList<Entity> copies;
        try
        {
            copies = WorldFile.RestoreCopies(_clipboard, offset);
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException or NotSupportedException or BrainException)
        {
            Status = $"nie wklejono: {exception.Message}";
            return [];
        }
        foreach (var copy in copies)
        {
            if (!string.IsNullOrWhiteSpace(copy.Name))
                copy.Name = UniqueName(copy.Name);
            World.Add(copy);
            if (SnapToGround)
            {
                var position = copy.Body.Position;
                copy.Place(position with { Z = GroundAt(position, copy) }, copy.Body.Rotation);
            }
        }
        Status = copies.Count == 1 ? $"wklejono: {NameOf(copies[0])}" : $"wklejono {copies.Count} encji";
        return copies;
    }

    /// <summary>Scena od zera: zatrzymuje nauki, czyści historię. Nauka nie startuje sama (L ją włącza).</summary>
    public void ResetScene()
    {
        Replace(_factory());
        Status = $"{Name}: od nowa — L włącza naukę";
    }

    /// <summary>
    /// Zapis sceny (świat, mózgi, snapshoty) do dokumentu JSON. Stwór z uczonym modułem, którego stan nie jest żadnym
    /// ze snapshotów, dostaje najpierw snapshot „zapis” — plik zawsze wskazuje bieżący snapshot, od którego stwór
    /// wystartuje po wczytaniu.
    /// </summary>
    public WorldDocument Save()
    {
        foreach (var creature in Creatures)
            if (creature.Brain is { } brain && TrainingController.FindTrainable(creature) is { } module && brain.CurrentSnapshot() is null)
                brain.Capture("zapis", module);
        return WorldFile.Capture(World, Name);
    }

    /// <summary>
    /// Wczytuje zapisany świat w miejsce obecnego; mózgi są w stanie bieżącego snapshotu z pliku.
    /// Nauka nie startuje sama (L ją włącza) i rusza od tego stanu.
    /// </summary>
    public void Load(WorldDocument aDocument)
    {
        var scene = WorldFile.Restore(aDocument);
        Replace(scene);
        Status = $"wczytano: {aDocument.Name} ({scene.World.Entities.Count} encji) — L włącza naukę";
    }

    private void Replace(DemoScene aScene)
    {
        Training.Dispose();
        _progress.Clear();
        var old = Demo.World;
        History = new SnapshotHistory();
        Demo = aScene;
        Training = new TrainingController(History) { Paused = !_visible };
        old.Dispose();
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
