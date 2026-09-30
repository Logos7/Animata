using Animata.Core.Brains.Modules;

namespace Animata.Core.Brains;

/// <summary>
/// Gotowy mózg dla ciała: nazwa do UI i budowa sterownika pasującego do tego ciała (bieżąca liczba wąsów, stawów).
/// <paramref name="HandTuned"/> — sterownik ma ręcznie dobrane parametry (zapisywane jako snapshot „ręczne parametry”).
/// Mózg z kilku modułów (np. dwie sieci i automat stanów) buduje <see cref="Build"/>: dostaje pusty mózg z węzłami ciała,
/// dokłada moduły i połączenia i zwraca dodane moduły; <paramref name="Create"/> daje wtedy moduł główny (np. do palety grafu).
/// </summary>
public sealed record BrainPreset(string Name, string Description, Func<BrainModule> Create, bool HandTuned = false)
{
    public Func<Brain, IReadOnlyList<BrainModule>>? Build { get; init; }
}
