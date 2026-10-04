namespace Animata.Core.Training;

/// <summary>Algorytm optymalizacji parametrów.</summary>
public enum EvolutionAlgorithm
{
    /// <summary>Algorytm genetyczny: elity, krzyżowanie, mutacja gaussowska z mnożnikami σ.</summary>
    Genetic,

    /// <summary>
    /// CMA-ES (Hansen): rozkład normalny, którego średnia, kowariancja i krok uczą się z najlepszych prób — dopasowuje się
    /// do kształtu i skali problemu (np. sztywności rzędu 1 obok kątów rzędu 0.1). Jak w pracy Geijtenbeeka i in. (2013).
    /// </summary>
    CmaEs
}

public sealed record EvolutionOptions
{
    /// <summary>Algorytm (domyślnie genetyczny).</summary>
    public EvolutionAlgorithm Algorithm { get; init; } = EvolutionAlgorithm.Genetic;

    public int PopulationSize { get; init; } = 48;

    /// <summary>Najlepsi przechodzą bez zmian do następnego pokolenia i są rodzicami.</summary>
    public int EliteCount { get; init; } = 6;

    /// <summary>Odchylenie standardowe mutacji gaussowskiej.</summary>
    public float MutationSigma { get; init; } = 0.2f;

    /// <summary>Szansa, że dziecko powstaje z krzyżowania dwóch elit (przed mutacją).</summary>
    public float CrossoverChance { get; init; } = 0.25f;

    /// <summary>
    /// Mnożniki siły mutacji: każde dziecko losuje jeden (σ · mnożnik) — część pokolenia szuka blisko elit (dostrajanie),
    /// część daleko (ucieczka z lokalnego optimum). Pomiar (sieć pająka, 30 pok., ziarna 3 i 5): z jednym σ 7/8 i 6/8
    /// dojść, z mnożnikami ¼, ½, 1, 2 — 8/8 i 8/8; chód pająka 7/8 → 8/8.
    /// </summary>
    public float[] MutationScales { get; init; } = [0.25f, 0.5f, 1, 2];

    public int Seed { get; init; } = 1;

    /// <summary>
    /// Limit jednej populacji, dodatkowo ograniczony wspólnym budżetem TrainingScheduler.
    /// Wynik nie zależy od tej liczby.
    /// </summary>
    public int MaxParallelism { get; init; } = Math.Max(1, Environment.ProcessorCount - 1);
}

/// <summary>
/// Optymalizator wektora parametrów, niezależny od zadania: fitness dostaje wektor i numer pokolenia (większy = lepszy).
/// Ocena populacji jest równoległa, więc funkcja fitness musi być bezpieczna wątkowo.
/// </summary>
public interface IOptimizer
{
    int Generation { get; }

    /// <summary>Najlepszy osobnik (kopia — nie zmienia się po następnych krokach).</summary>
    float[] Best { get; }

    float BestFitness { get; }

    /// <summary>Ocenia bieżące pokolenie i tworzy następne. Zwraca fitness najlepszego osobnika.</summary>
    float NextGeneration(Func<float[], int, float> aFitness, CancellationToken aCancellation = default);
}

/// <summary>
/// Prosty algorytm genetyczny na wektorach float z elitaryzmem i mutacją gaussowską (<see cref="EvolutionAlgorithm.Genetic"/>).
/// Optymalizator według <see cref="EvolutionOptions.Algorithm"/> daje <see cref="Create"/>.
/// </summary>
public sealed class Evolution : IOptimizer
{
    private readonly EvolutionOptions _options;
    private readonly Random _random;
    private float[][] _population;
    private readonly float[] _fitness;

    /// <summary>Optymalizator wybrany w opcjach: algorytm genetyczny albo CMA-ES.</summary>
    public static IOptimizer Create(ReadOnlySpan<float> aStart, EvolutionOptions aOptions) =>
        aOptions.Algorithm == EvolutionAlgorithm.CmaEs ? new CmaEs(aStart, aOptions) : new Evolution(aStart, aOptions);

    public Evolution(ReadOnlySpan<float> aStart, EvolutionOptions aOptions)
    {
        if (aOptions.Algorithm != EvolutionAlgorithm.Genetic)
            throw new ArgumentException($"{aOptions.Algorithm} — użyj {nameof(Evolution)}.{nameof(Create)}.", nameof(aOptions));
        if (aOptions.PopulationSize < 2 || aOptions.EliteCount < 1 || aOptions.EliteCount >= aOptions.PopulationSize)
            throw new ArgumentException("Population must be at least 2 and larger than the elite.", nameof(aOptions));

        _options = aOptions;
        _random = new Random(aOptions.Seed);
        _fitness = new float[aOptions.PopulationSize];
        var start = aStart.ToArray();
        _population = new float[aOptions.PopulationSize][];
        _population[0] = start;
        for (var index = 1; index < _population.Length; index++)
            _population[index] = Mutate((float[])start.Clone());
        Best = start;
    }

    public int Generation { get; private set; }
    public float[] Best { get; private set; }
    public float BestFitness { get; private set; } = float.NegativeInfinity;

    public float NextGeneration(Func<float[], int, float> aFitness, CancellationToken aCancellation = default)
    {
        aCancellation.ThrowIfCancellationRequested();
        var generation = Generation;
        TrainingScheduler.For(_population.Length, _options.MaxParallelism, aIndex =>
        {
            var fitness = aFitness(_population[aIndex], generation);
            _fitness[aIndex] = float.IsFinite(fitness) ? fitness : float.NegativeInfinity;
        }, aCancellation);

        var order = Enumerable.Range(0, _population.Length).OrderByDescending(aIndex => _fitness[aIndex]).ToArray();
        var elites = order.Take(_options.EliteCount).Select(aIndex => _population[aIndex]).ToArray();
        Best = (float[])elites[0].Clone();
        BestFitness = _fitness[order[0]];

        var next = new float[_population.Length][];
        for (var index = 0; index < elites.Length; index++)
            next[index] = (float[])elites[index].Clone();
        for (var index = elites.Length; index < next.Length; index++)
        {
            var child = (float[])elites[_random.Next(elites.Length)].Clone();
            if (_random.NextSingle() < _options.CrossoverChance)
            {
                var other = elites[_random.Next(elites.Length)];
                for (var gene = 0; gene < child.Length; gene++)
                    if (_random.Next(2) == 0)
                        child[gene] = other[gene];
            }
            next[index] = Mutate(child, _options.MutationScales[_random.Next(_options.MutationScales.Length)]);
        }

        _population = next;
        Generation++;
        return BestFitness;
    }

    private float[] Mutate(float[] aGenes, float aScale = 1)
    {
        var sigma = _options.MutationSigma * aScale;
        for (var gene = 0; gene < aGenes.Length; gene++)
            aGenes[gene] += Gaussian() * sigma;
        return aGenes;
    }

    private float Gaussian()
    {
        var u1 = 1 - _random.NextSingle();
        var u2 = _random.NextSingle();
        return MathF.Sqrt(-2 * MathF.Log(u1)) * MathF.Cos(2 * MathF.PI * u2);
    }
}
