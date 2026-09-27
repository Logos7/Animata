using Animata.Core.Brains;
using Animata.Core.Entities;
using Animata.Core.Training;
using Animata.Core.WorldObjects;
using Animata.Core.Worlds;

namespace Animata.Studio.Session;

/// <summary>
/// Stan aplikacji niezależny od paneli: świat, symulacja (pauza, prędkość, błąd mózgu), nauka i snapshoty.
/// Panele tylko go czytają i wołają jego akcje, więc symulacja i nauka idą dalej, gdy użytkownik jest w menu
/// albo w mózgu stwora. Wszystko wołane z wątku UI.
/// </summary>
public sealed class StudioSession : IDisposable
{
    public const float Delta = 1f / 30f;
    private const int HistoryLimit = 400;

    private readonly Dictionary<Brain, List<TrainingProgress>> _progress = [];
    private float _pending;

    public StudioSession()
    {
        Demo = WorldObjectCatalog.CreateDemo();
        Training = new TrainingController(History);
    }

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
    public IEnumerable<ActiveEntity> NeuralCreatures => Creatures.Where(aCreature => TrainingController.FindNetwork(aCreature) is not null);

    public static string NameOf(Entity aEntity) => !string.IsNullOrWhiteSpace(aEntity.Name) ? aEntity.Name : aEntity switch
    {
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

    public void Remove(Entity aEntity)
    {
        if (aEntity is ActiveEntity { Brain: { } brain })
        {
            Training.Stop(brain, aSnapshot: false);
            History.Forget(brain);
            _progress.Remove(brain);
        }
        World.Remove(aEntity);
    }

    /// <summary>Nowa scena demo od zera: zatrzymuje nauki, czyści historię, startuje naukę fioletowych.</summary>
    public void ResetScene()
    {
        Training.Dispose();
        _progress.Clear();
        History = new SnapshotHistory();
        Demo = WorldObjectCatalog.CreateDemo();
        Training = new TrainingController(History);
        SimTime = 0;
        Paused = false;
        Error = null;
        ErrorModuleId = null;
        Status = "nowa scena demo";
        StartTrainingAll();
        SceneReset?.Invoke();
    }

    public void Dispose() => Training.Dispose();
}
