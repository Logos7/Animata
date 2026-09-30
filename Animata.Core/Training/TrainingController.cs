using Animata.Core.Brains;
using Animata.Core.Brains.Modules;
using Animata.Core.Entities;

namespace Animata.Core.Training;

/// <summary>
/// Nauka stworów w scenie. Uczy się pierwszy uczony moduł mózgu (<see cref="ITrainableModule"/>: sieć neuronowa
/// albo CPG węża). Każdy stwór ma własny <see cref="BackgroundTrainer"/>: ewolucja idzie w ukrytych symulacjach,
/// a do modułu w scenie trafiają tylko parametry kolejnych mistrzów (lepszych na stałej walidacji).
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

    /// <summary>Pierwszy uczony moduł (sieć albo CPG) na najwyższym poziomie mózgu stwora albo null.</summary>
    public static BrainModule? FindTrainable(ActiveEntity aCreature) =>
        aCreature.Brain?.Graph.Modules.FirstOrDefault(aModule => aModule is ITrainableModule);

    /// <summary>Ostatni postęp nauki stwora albo null (nie uczy się albo jeszcze nie ma pokolenia).</summary>
    public TrainingProgress? ProgressOf(Brain aBrain) => SessionOf(aBrain)?.Last;

    /// <summary>
    /// Zaczyna naukę od bieżących wag sieci. Zwraca false, jeśli stwór nie ma sieci albo już się uczy.
    /// Ziarno dotyczy tylko losowania prób i mutacji — walidacja jest stała (patrz SeekTargetOptions.ValidationSeed).
    /// </summary>
    public bool Start(ActiveEntity aCreature, int? aSeed = null)
    {
        if (aCreature.Brain is not { } brain || FindTrainable(aCreature) is not { } module || IsTraining(brain))
            return false;

        var seed = aSeed ?? (Environment.TickCount ^ aCreature.Id.GetHashCode());
        var rig = SeekRigs.For(aCreature);
        var task = new SeekTargetTask(module.CaptureState()!, rig, _options(rig, seed), module.Name);
        var evolution = new Evolution(((ITrainableModule)module).GetParameters(), new EvolutionOptions { Seed = seed });
        _history.Capture(brain, "przed nauką", module);

        var trainer = new BackgroundTrainer(evolution, task.Evaluate, _maxGenerations, task.Validate);
        _sessions.Add(new Session(aCreature, brain, module, trainer, rig.Name, Setup(aCreature, module)));
        trainer.Paused = _paused;
        trainer.Start();
        return true;
    }

    /// <summary>
    /// Zatrzymuje naukę; sieć zostaje z wagami ostatniego mistrza, a ten (jeśli <paramref name="aSnapshot"/>)
    /// trafia do snapshotu. Zwraca komunikat błędu, jeśli nauka przerwała się wyjątkiem, inaczej null.
    /// </summary>
    public string? Stop(Brain aBrain, bool aSnapshot = true)
    {
        if (SessionOf(aBrain) is not { } session)
            return null;

        session.Trainer.Dispose();
        Poll(session);
        _sessions.Remove(session);
        if (aSnapshot && session.Last is { } last)
            _history.Capture(session.Brain,
                $"nauka: mistrz z gen {last.ChampionGeneration}, wynik {last.ChampionScore:F3}", session.Module);
        return session.Trainer.Error is { } error ? $"{session.Rig}: nauka przerwana: {error.Message}" : null;
    }

    /// <summary>Losowe wagi i nauka od zera. Poprzedni stan zostaje w snapshocie (Z cofa).</summary>
    public bool RandomizeAndRestart(ActiveEntity aCreature, int? aSeed = null)
    {
        if (aCreature.Brain is not { } brain || FindTrainable(aCreature) is not { } module)
            return false;

        Stop(brain);
        _history.Capture(brain, "przed losowaniem", module);
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
            changed |= Poll(session);
            if (!session.Trainer.IsRunning)
            {
                aMessage = Stop(session.Brain) ?? aMessage;
                changed = true;
                continue;
            }
            if (++session.SetupCheck % SetupCheckInterval != 0)
                continue;
            if (!ReferenceEquals(FindTrainable(session.Creature), session.Module))
            {
                aMessage = Stop(session.Brain, aSnapshot: false) ?? $"{session.Rig}: uczony moduł zniknął z mózgu — nauka zatrzymana";
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
        var builder = new System.Text.StringBuilder(aCreature.TrainingRig?.Name ?? aCreature.GetType().Name);
        if (aModule.CaptureState() is { } state)
            builder.Append('|').Append(TrainableModules.Create(state, new float[TrainableModules.ParameterCount(state)], aModule.Name)
                .CaptureState()?.ToJson());
        foreach (var owner in aCreature.Body.Sensors.Cast<object>().Concat(aCreature.Body.Actuators))
            foreach (var (name, value) in Settings.Capture(owner))
                if (Settings.Describe(owner.GetType()).First(aInfo => aInfo.Name == name).Type != typeof(Guid?))
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
