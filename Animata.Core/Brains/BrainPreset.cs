using Animata.Core.Brains.Modules;

namespace Animata.Core.Brains;

/// <summary>
/// Gotowy mózg dla ciała: nazwa do UI i budowa sterownika pasującego do tego ciała (bieżąca liczba wąsów, stawów).
/// <paramref name="HandTuned"/> — sterownik ma ręcznie dobrane parametry (zapisywane jako snapshot „ręczne parametry”).
/// </summary>
public sealed record BrainPreset(string Name, string Description, Func<BrainModule> Create, bool HandTuned = false);
