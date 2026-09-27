namespace Animata.Core.Brains;

/// <summary>Zapamiętany stan jednego modułu grafu.</summary>
public sealed record ModuleSnapshot(Guid ModuleId, string ModuleName, ModuleState State);

/// <summary>
/// Nazwany snapshot mózgu: stan jednego albo wielu modułów z jednej chwili.
/// Snapshot jednego modułu to po prostu BrainSnapshot z jednym wpisem.
/// </summary>
public sealed record BrainSnapshot(Guid Id, string Label, DateTime CreatedUtc, IReadOnlyList<ModuleSnapshot> Modules);
