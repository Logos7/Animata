namespace Animata.Core.Training;

public static class TrainingScheduler
{
    public static int MaxParallelism { get; } = Math.Max(1, Environment.ProcessorCount - 1);

    private static readonly SemaphoreSlim Budget = new(MaxParallelism, MaxParallelism);

    internal static T Evaluate<T>(Func<T> aEvaluate, CancellationToken aCancellation)
    {
        Budget.Wait(aCancellation);
        try
        {
            aCancellation.ThrowIfCancellationRequested();
            return aEvaluate();
        }
        finally
        {
            Budget.Release();
        }
    }

    internal static void For(int aCount, int aParallelism, Action<int> aEvaluate, CancellationToken aCancellation)
    {
        if (aParallelism is 0 or < -1)
            throw new ArgumentOutOfRangeException(nameof(aParallelism));
        var options = new ParallelOptions
        {
            CancellationToken = aCancellation,
            MaxDegreeOfParallelism = aParallelism < 0 ? MaxParallelism : Math.Min(aParallelism, MaxParallelism)
        };
        Parallel.For(0, aCount, options, aIndex => Evaluate(() =>
        {
            aEvaluate(aIndex);
            return true;
        }, aCancellation));
    }
}
