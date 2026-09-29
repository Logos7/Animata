namespace Animata.Core.Training;

public sealed record EvolutionOptions
{
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
}

/// <summary>
/// Prosty algorytm genetyczny na wektorach float z elitaryzmem i mutacją gaussowską.
/// Niezależny od zadania: fitness dostaje wektor parametrów i numer pokolenia (większy = lepszy).
/// Ocena populacji jest równoległa, więc funkcja fitness musi być bezpieczna wątkowo.
/// </summary>
public sealed class Evolution
{
    private readonly EvolutionOptions _options;
    private readonly Random _random;
    private float[][] _population;
    private readonly float[] _fitness;

    public Evolution(ReadOnlySpan<float> aStart, EvolutionOptions aOptions)
    {
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

    /// <summary>Ocenia bieżące pokolenie i tworzy następne. Zwraca fitness najlepszego osobnika.</summary>
    public float Step(Func<float[], int, float> aFitness)
    {
        var generation = Generation;
        Parallel.For(0, _population.Length, aIndex =>
        {
            var fitness = aFitness(_population[aIndex], generation);
            _fitness[aIndex] = float.IsFinite(fitness) ? fitness : float.NegativeInfinity;
        });

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
