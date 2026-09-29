using Animata.Core.Bodies;
using Animata.Core.Brains.Modules;
using Animata.Core.Entities;
using Animata.Core.Worlds;

namespace Animata.Core.Brains;

public class Brain
{
    private readonly List<BrainSnapshot> _snapshots = [];

    public BrainGraph Graph { get; } = new();

    /// <summary>Zapisane snapshoty, od najstarszego.</summary>
    public IReadOnlyList<BrainSnapshot> Snapshots => _snapshots;

    /// <summary>Ciało, którego zmysły i napędy są węzłami grafu (ustawia je stwór przy tworzeniu) albo null.</summary>
    public Body? Body { get; internal set; }

    /// <summary>
    /// Węzły ciała w grafie: po jednym <see cref="SensorModule"/> na sensor (na początku listy modułów) i jednym
    /// <see cref="ActuatorModule"/> na aktuator (na końcu), na najwyższym poziomie grafu. Węzły sensorów i aktuatorów, których
    /// ciało już nie ma, znikają razem z połączeniami; połączenia z portów, których sensor albo aktuator już nie ma, też.
    /// Węzły nie są przechowywane (ani w pliku) — to widok ciała; Id ze slotu, więc połączenia do nich przeżywają odtworzenie.
    /// Woła się po zmianie zestawu sensorów/aktuatorów (budowa stwora, wczytanie); samo Think robi to, gdy liczby się nie zgadzają.
    /// </summary>
    public void SyncBody()
    {
        if (Body is not { } body)
            return;
        var graph = Graph;
        foreach (var module in graph.Modules.ToList())
            switch (module)
            {
                case SensorModule sensor when body.FindSensor(sensor.Slot) is { } current:
                    sensor.Sensor = current;
                    break;
                case ActuatorModule actuator when body.FindActuator(actuator.Slot) is { } current:
                    actuator.Actuator = current;
                    break;
                case SensorModule or ActuatorModule:
                    graph.Remove(module);
                    break;
            }
        var index = 0;
        foreach (var sensor in body.Sensors)
        {
            if (graph.Modules.OfType<SensorModule>().All(aModule => aModule.Slot != sensor.Slot))
                graph.Modules.Insert(index, new SensorModule(sensor));
            index++;
        }
        foreach (var actuator in body.Actuators)
            if (graph.Modules.OfType<ActuatorModule>().All(aModule => aModule.Slot != actuator.Slot))
                graph.Modules.Add(new ActuatorModule(actuator));
        graph.Connections.RemoveAll(aLink =>
            (graph.Find(aLink.SourceId) is SensorModule source && !source.OutputPorts.Contains(aLink.SourcePort)) ||
            (graph.Find(aLink.TargetId) is ActuatorModule target && !target.InputPorts.Contains(aLink.TargetPort)));
        graph.Invalidate();
    }

    /// <summary>Sensory → logika. Świat nie jest zmieniany.</summary>
    public void Think(ActiveEntity aOwner, World aWorld, float aDelta)
    {
        if (Body is { } body && _synced != (body.Sensors.Count, body.Actuators.Count))
        {
            SyncBody();
            _synced = (body.Sensors.Count, body.Actuators.Count);
        }
        Graph.Think(new BrainContext(aOwner, aWorld, aDelta));
    }

    private (int Sensors, int Actuators) _synced = (-1, -1);

    /// <summary>Komendy → aktuatory.</summary>
    public void Act(ActiveEntity aOwner, World aWorld, float aDelta) =>
        Graph.Act(new BrainContext(aOwner, aWorld, aDelta));

    /// <summary>Czyści stan chwilowy modułów (np. przed nową próbą). Parametrów nie rusza.</summary>
    public void Reset() => Graph.Reset();

    /// <summary>Snapshot jednego modułu.</summary>
    public BrainSnapshot Capture(string aLabel, BrainModule aModule) => Capture(aLabel, [aModule]);

    /// <summary>Snapshot podanych modułów (domyślnie wszystkich, które mają stan). Dodawany zawsze.</summary>
    public BrainSnapshot Capture(string aLabel, IEnumerable<BrainModule>? aModules = null) =>
        Add(new BrainSnapshot(Guid.NewGuid(), aLabel, DateTime.UtcNow, Collect(aModules)));

    /// <summary>
    /// Jak <see cref="Capture(string, IEnumerable{BrainModule}?)"/>, ale nie dodaje nic, jeśli najnowszy snapshot
    /// już zawiera dokładnie ten stan tych modułów (wtedy zwraca null). Chroni przed seriami identycznych snapshotów.
    /// </summary>
    public BrainSnapshot? CaptureIfChanged(string aLabel, IEnumerable<BrainModule>? aModules = null)
    {
        var entries = Collect(aModules);
        if (_snapshots.Count > 0 && Contains(_snapshots[^1], entries))
            return null;
        return Add(new BrainSnapshot(Guid.NewGuid(), aLabel, DateTime.UtcNow, entries));
    }

    /// <summary>
    /// Bieżący snapshot: najnowszy, którego przywrócenie niczego by nie zmieniło (stan mózgu = ten snapshot), albo null.
    /// </summary>
    public BrainSnapshot? CurrentSnapshot()
    {
        for (var index = Snapshots.Count - 1; index >= 0; index--)
            if (Matches(Snapshots[index]))
                return Snapshots[index];
        return null;
    }

    /// <summary>
    /// Czy moduły mają dokładnie stan zapisany w snapshocie, czyli czy <see cref="Restore"/> niczego by nie zmienił.
    /// Wpisy modułów, których nie ma już w grafie, są pomijane.
    /// </summary>
    public bool Matches(BrainSnapshot aSnapshot)
    {
        foreach (var entry in aSnapshot.Modules)
        {
            var module = Graph.FindDeep(entry.ModuleId);
            if (module is not null && !entry.State.SameAs(module.CaptureState()))
                return false;
        }
        return true;
    }

    /// <summary>
    /// Przywraca stan modułów ze snapshotu (dopasowanie po Id modułu, także w podgrafach — moduł zgrupowany w podgraf
    /// po zrobieniu snapshotu nadal się przywróci). Moduły, których już nie ma w grafie, są pomijane. Zwraca liczbę przywróconych modułów.
    /// </summary>
    public int Restore(BrainSnapshot aSnapshot)
    {
        var restored = 0;
        foreach (var entry in aSnapshot.Modules)
        {
            var module = Graph.FindDeep(entry.ModuleId);
            if (module is null)
                continue;
            module.RestoreState(entry.State);
            restored++;
        }

        Graph.InvalidateDeep();
        return restored;
    }

    /// <summary>Dodaje snapshot z zewnątrz (np. wczytany z pliku).</summary>
    public void AddSnapshot(BrainSnapshot aSnapshot) => _snapshots.Add(aSnapshot);

    public bool RemoveSnapshot(BrainSnapshot aSnapshot) => _snapshots.Remove(aSnapshot);

    /// <summary>Podmienia snapshot na tym samym miejscu listy (np. przeliczony na nowy kształt sieci). False, gdy go nie było.</summary>
    public bool ReplaceSnapshot(BrainSnapshot aOld, BrainSnapshot aNew)
    {
        var index = _snapshots.IndexOf(aOld);
        if (index < 0)
            return false;
        _snapshots[index] = aNew;
        return true;
    }

    private List<ModuleSnapshot> Collect(IEnumerable<BrainModule>? aModules)
    {
        var entries = new List<ModuleSnapshot>();
        foreach (var module in aModules ?? Graph.Modules)
        {
            if (!Graph.ContainsDeep(module))
                throw new ArgumentException($"{module} is not part of this brain.", nameof(aModules));
            if (module.CaptureState() is { } state)
                entries.Add(new ModuleSnapshot(module.Id, module.Name, state));
        }
        return entries;
    }

    private BrainSnapshot Add(BrainSnapshot aSnapshot)
    {
        _snapshots.Add(aSnapshot);
        return aSnapshot;
    }

    /// <summary>Czy snapshot ma każdy z wpisów z tą samą treścią.</summary>
    private static bool Contains(BrainSnapshot aSnapshot, IReadOnlyList<ModuleSnapshot> aEntries) =>
        aEntries.All(aEntry => aSnapshot.Modules.Any(aExisting =>
            aExisting.ModuleId == aEntry.ModuleId && aExisting.State.SameAs(aEntry.State)));
}
