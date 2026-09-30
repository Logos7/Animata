using Animata.Core.Brains;
using Animata.Core.Brains.Modules;
using Animata.Core.Entities;

namespace Animata.Core.Training;

/// <summary>
/// Nauka stworów w scenie. Uczą się wszystkie uczone moduły mózgu (<see cref="ITrainableModule"/>: sieci, CPG, chody),
/// także w podgrafach — każdy osobno, we własnych warunkach (<see cref="ActiveEntity.TrainingRigFor"/>; np. sieć stania
/// humanoida na próbach z pchnięciami, sieć chodu — na dojściu do celu). Każdy moduł ma własny <see cref="BackgroundTrainer"/>:
/// ewolucja idzie w ukrytych symulacjach, w których moduł steruje ciałem sam, a do modułu w scenie trafiają tylko parametry
/// kolejnych mistrzów (lepszych na stałej walidacji).
/// Auto-snapshoty („przed nauką”, „nauka: mistrz…”, „przed losowaniem”) idą przez <see cref="SnapshotHistory"/>,
/// więc nie powstają serie identycznych snapshotów. Klasę woła się z jednego wątku (UI).
/// </summary>
public sealed class TrainingController : IDisposable
{
    private sealed class Session(ActiveEntity aCreature, Brain aBrain, BrainModule aModule, BackgroundTrainer aTrainer, string aRig,
        string aSetup)
    {
        public ActiveEntity Creature { get; } = aCreature;
        public Brain Brain { get; } = aBrain;
        public string Setup { get; } = aSetup;
        public int SetupCheck;
        public BrainModule Module { get; } = aModule;
        public ITrainableModule Trainable { get; } = (ITrainableModule)aModule;
        public BackgroundTrainer Trainer { get; } = aTrainer;
        public string Rig { get; } = aRig;
        public int Version;
        public int ShownChampion = -1;
        public TrainingProgress? Last;
    }

    private readonly List<Session> _sessions = [];
    private readonly SnapshotHistory _history;
    private readonly int _maxGenerations;
    private readonly Func<SeekRig, int, SeekTargetOptions> _options;

    /// <param name="aOptions">Opcje zadania dla rigu i ziarna nauki; domyślnie DefaultOptions rigu z tym ziarnem.</param>
    public TrainingController(SnapshotHistory aHistory, int aMaxGenerations = 300,
        Func<SeekRig, int, SeekTargetOptions>? aOptions = null)
    {
        _history = aHistory;
        _maxGenerations = aMaxGenerations;
        _options = aOptions ?? ((aRig, aSeed) => aRig.DefaultOptions with { Seed = aSeed });
    }

    public int Count => _sessions.Count;

    /// <summary>Wstrzymuje (true) albo wznawia wszystkie nauki tego kontrolera — także te uruchomione później.</summary>
    public bool Paused
    {
        get => _paused;
        set
        {
            _paused = value;
            foreach (var session in _sessions)
                session.Trainer.Paused = value;
        }
    }

    private bool _paused;

    public bool IsTraining(Brain aBrain) => SessionOf(aBrain) is not null;

    /// <summary>Pierwszy uczony moduł mózgu stwora (także w podgrafie) albo null.</summary>
    public static BrainModule? FindTrainable(ActiveEntity aCreature) => Trainables(aCreature).FirstOrDefault();

    /// <summary>Wszystkie uczone moduły mózgu stwora, także w podgrafach (rodzic przed dziećmi).</summary>
    public static IEnumerable<BrainModule> Trainables(ActiveEntity aCreature) =>
        aCreature.Brain?.Graph.Descendants().Where(aModule => aModule is ITrainableModule) ?? [];

    /// <summary>Ostatni postęp nauki stwora (pierwszego uczonego modułu) albo null.</summary>
    public TrainingProgress? ProgressOf(Brain aBrain) => SessionOf(aBrain)?.Last;

    /// <summary>Postęp nauki każdego uczonego modułu mózgu: nazwa modułu, rig i ostatni postęp (null — jeszcze bez pokolenia).</summary>
    public IReadOnlyList<(string Module, string Rig, TrainingProgress? Progress)> ProgressesOf(Brain aBrain) =>
        [.. _sessions.Where(aSession => aSession.Brain == aBrain).Select(aSession => (aSession.Module.Name, aSession.Rig, aSession.Last))];

    /// <summary>
    /// Zaczyna naukę każdego uczonego modułu stwora od jego bieżących parametrów. Zwraca false, jeśli stwór nie ma uczonego
    /// modułu, już się uczy albo żaden moduł nie da się uczyć w swoich warunkach (np. nie pasuje do ciała).
    /// Ziarno dotyczy tylko losowania prób i mutacji — walidacja jest stała (patrz SeekTargetOptions.ValidationSeed).
    /// </summary>
    public bool Start(ActiveEntity aCreature, int? aSeed = null)
    {
        if (aCreature.Brain is not { } brain || IsTraining(brain))
            return false;
        var modules = Trainables(aCreature).ToList();
        if (modules.Count == 0)
            return false;

        var seed = aSeed ?? (Environment.TickCount ^ aCreature.Id.GetHashCode());
        _history.Capture(brain, "przed nauką", [.. modules]);
        var started = 0;
        foreach (var module in modules)
        {
            SeekRig rig;
            SeekTargetTask task;
            try
            {
                rig = SeekRigs.For(aCreature, module);
                task = new SeekTargetTask(module.CaptureState()!, rig, _options(rig, seed), module.Name);
            }
            catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or BrainException or NotSupportedException)
            {
                LastStartError = $"{module.Name}: {exception.Message}";
                continue;
            }
            var evolution = new Evolution(((ITrainableModule)module).GetParameters(), new EvolutionOptions { Seed = seed + started });
            var trainer = new BackgroundTrainer(evolution, task.Evaluate, _maxGenerations, task.Validate);
            _sessions.Add(new Session(aCreature, brain, module, trainer, modules.Count > 1 ? $"{rig.Name} ({module.Name})" : rig.Name,
                Setup(aCreature, module)));
            trainer.Paused = _paused;
            trainer.Start();
            started++;
        }
        return started > 0;
    }

    /// <summary>Dlaczego ostatnio jakiś moduł nie ruszył z nauką (np. nie pasuje do ciała w swoich warunkach) albo null.</summary>
    public string? LastStartError { get; private set; }

    /// <summary>
    /// Zatrzymuje naukę wszystkich modułów mózgu; każdy zostaje z parametrami ostatniego mistrza, a ten (jeśli
    /// <paramref name="aSnapshot"/>) trafia do snapshotu. Zwraca komunikat błędu, jeśli nauka przerwała się wyjątkiem, inaczej null.
    /// </summary>
    public string? Stop(Brain aBrain, bool aSnapshot = true)
    {
        string? error = null;
        foreach (var session in _sessions.Where(aSession => aSession.Brain == aBrain).ToList())
            error = StopSession(session, aSnapshot) ?? error;
        return error;
    }

    private string? StopSession(Session aSession, bool aSnapshot = true)
    {
        aSession.Trainer.Dispose();
        Poll(aSession);
        _sessions.Remove(aSession);
        if (aSnapshot && aSession.Last is { } last)
            _history.Capture(aSession.Brain,
                $"nauka: mistrz z gen {last.ChampionGeneration}, wynik {last.ChampionScore:F3}", aSession.Module);
        return aSession.Trainer.Error is { } error ? $"{aSession.Rig}: nauka przerwana: {error.Message}" : null;
    }

    /// <summary>Losowe parametry wszystkich uczonych modułów i nauka od zera. Poprzedni stan zostaje w snapshocie (Z cofa).</summary>
    public bool RandomizeAndRestart(ActiveEntity aCreature, int? aSeed = null)
    {
        if (aCreature.Brain is not { } brain || Trainables(aCreature).ToList() is not { Count: > 0 } modules)
            return false;

        Stop(brain);
        _history.Capture(brain, "przed losowaniem", [.. modules]);
        foreach (var module in modules)
            ((ITrainableModule)module).Randomize();
        return Start(aCreature, aSeed);
    }

    /// <summary>
    /// Przenosi nowych mistrzów do sieci w scenie i sprząta zakończone nauki. Pilnuje też, żeby nauka uczyła tego, co jest
    /// w scenie: gdy zmieni się konfiguracja uczonego modułu (porty, wyrażenia wejść sieci…), ustawienia zmysłów i napędów
    /// (prędkość, zasięg wąsów…) albo rig stwora (np. wąż stał się wspinaczem), nauka rusza od nowa na nowych warunkach
    /// (mistrz dotychczasowej trafia do snapshotu); gdy uczony moduł zniknął z mózgu — staje.
    /// Zwraca true, jeśli coś się zmieniło; <paramref name="aMessage"/> — co się stało (błąd, restart) albo null.
    /// </summary>
    public bool Poll(out string? aMessage)
    {
        aMessage = null;
        var changed = false;
        foreach (var session in _sessions.ToArray())
        {
            if (!_sessions.Contains(session))
                continue;   // zatrzymana razem z innym modułem tego samego mózgu
            changed |= Poll(session);
            if (!session.Trainer.IsRunning)
            {
                aMessage = StopSession(session) ?? aMessage;
                changed = true;
                continue;
            }
            if (++session.SetupCheck % SetupCheckInterval != 0)
                continue;
            if (!session.Brain.Graph.ContainsDeep(session.Module))
            {
                aMessage = StopSession(session, aSnapshot: false) ?? $"{session.Rig}: uczony moduł zniknął z mózgu — nauka zatrzymana";
                changed = true;
            }
            else if (Setup(session.Creature, session.Module) != session.Setup)
            {
                var error = Stop(session.Brain);
                Start(session.Creature);
                aMessage = error ?? $"{session.Rig}: zmiana modułu albo ustawień ciała — nauka od nowa na nowych warunkach";
                changed = true;
            }
        }
        return changed;
    }

    /// <summary>Co ile odpytań sprawdzać, czy warunki nauki się zmieniły (~3 razy na sekundę przy 30 Hz).</summary>
    private const int SetupCheckInterval = 10;

    /// <summary>
    /// Warunki nauki stwora jako tekst: rig, konfiguracja uczonego modułu bez parametrów (kształt, porty, wyrażenia)
    /// i ustawienia zmysłów i napędów bez celów. Inny tekst = nauka uczy czegoś innego niż to, co jest w scenie.
    /// </summary>
    private static string Setup(ActiveEntity aCreature, BrainModule aModule)
    {
        var builder = new System.Text.StringBuilder(aCreature.TrainingRigFor(aModule)?.Name ?? aCreature.GetType().Name);
        if (aModule.CaptureState() is { } state)
            builder.Append('|').Append(TrainableModules.Create(state, new float[TrainableModules.ParameterCount(state)], aModule.Name)
                .CaptureState()?.ToJson());
        foreach (var owner in aCreature.Body.Sensors.Cast<object>().Concat(aCreature.Body.Actuators))
            foreach (var (name, value) in Settings.Capture(owner))
                if (Settings.Find(owner, name)?.Type != typeof(Guid?))
                    builder.Append('|').Append(name).Append('=').Append(value.GetRawText());
        return builder.ToString();
    }

    /// <summary>Krótki opis nauk do paska tytułu, np. „autko gen 12, mistrz z gen 9 (-0,31)”.</summary>
    public string Summary() => string.Join(", ", _sessions.Select(aSession => aSession.Last is { } last
        ? $"{aSession.Rig} gen {last.Generation}, mistrz z gen {last.ChampionGeneration} ({last.ChampionScore:F2})"
        : $"{aSession.Rig} start"));

    public void Dispose()
    {
        foreach (var session in _sessions)
            session.Trainer.Dispose();
        _sessions.Clear();
    }

    /// <summary>
    /// Pobiera postęp z trenera. Do sieci w scenie trafia tylko nowy mistrz (lepszy na stałych próbach),
    /// więc stwór zmienia zachowanie rzadko i zawsze na lepsze. Zwraca true, jeśli postęp się zmienił.
    /// </summary>
    private static bool Poll(Session aSession)
    {
        if (!aSession.Trainer.TryGetProgress(ref aSession.Version, out var progress) || progress is null)
            return false;
        aSession.Last = progress;
        if (progress.ChampionGeneration != aSession.ShownChampion)
        {
            aSession.ShownChampion = progress.ChampionGeneration;
            aSession.Trainable.SetParameters(progress.Champion);
        }
        return true;
    }

    private Session? SessionOf(Brain aBrain) => _sessions.Find(aSession => aSession.Brain == aBrain);
}
