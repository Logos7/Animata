namespace Animata.Core.Brains.Modules;

/// <summary>
/// Węzeł grafu mózgu.
/// Kontrakt:
/// - porty są stałe od chwili dodania do grafu (graf waliduje połączenia przy kompilacji),
/// - nieprzyłączone porty wejściowe nie występują w słowniku wejść (czytaj przez GetValueOrDefault),
/// - słownik zwracany z Evaluate jest ważny do następnego Evaluate tego modułu,
/// - Evaluate nie zmienia świata; zmiany świata robi się wyłącznie w Commit (faza Act),
/// - pamięć między tickami (jeśli moduł ją ma) czyści <see cref="Reset"/>.
/// </summary>
public abstract class BrainModule
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;

    public abstract IReadOnlyList<string> InputPorts { get; }
    public abstract IReadOnlyList<string> OutputPorts { get; }

    /// <summary>Faza Think: czyste przeliczenie wejść na wyjścia.</summary>
    public abstract IReadOnlyDictionary<string, float> Evaluate(
        IReadOnlyDictionary<string, float> aInputs, BrainContext aContext);

    /// <summary>Faza Act: jedyne miejsce, gdzie moduł może zmienić świat. Domyślnie nic.</summary>
    public virtual void Commit(BrainContext aContext)
    {
    }

    /// <summary>
    /// Czyści stan chwilowy (pamięć z poprzednich ticków), np. przed nową próbą albo po przestawieniu stwora.
    /// Parametrów (tego, co trafia do snapshotu) nie rusza. Domyślnie nic — moduły bezstanowe nie muszą nadpisywać.
    /// </summary>
    public virtual void Reset()
    {
    }

    /// <summary>Walidacja wewnętrznej konfiguracji, wołana przy kompilacji grafu. Rzuca przy błędzie.</summary>
    public virtual void Validate()
    {
    }

    /// <summary>Stan do snapshotu albo null, jeśli moduł nie ma parametrów do zapamiętania.</summary>
    public virtual ModuleState? CaptureState() => null;

    /// <summary>Przywraca stan zapisany przez <see cref="CaptureState"/>. Rzuca przy niezgodnym typie.</summary>
    public virtual void RestoreState(ModuleState aState) =>
        throw new NotSupportedException($"{GetType().Name} has no restorable state.");

    public override string ToString() =>
        string.IsNullOrEmpty(Name) ? $"{GetType().Name} {Id}" : $"{Name} ({GetType().Name})";
}
