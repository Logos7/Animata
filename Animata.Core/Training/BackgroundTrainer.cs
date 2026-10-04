namespace Animata.Core.Training;

public sealed record TrainingProgress(int Generation, float BestFitness, float[] Champion, float ChampionScore, int ChampionGeneration);

public sealed class BackgroundTrainer : IDisposable, IAsyncDisposable
{
    private readonly IOptimizer _evolution;
    private readonly Func<float[], int, CancellationToken, float> _fitness;
    private readonly Func<float[], CancellationToken, float>? _validate;
    private readonly object _gate = new();
    private readonly ManualResetEventSlim _running = new(true);
    private CancellationTokenSource? _cancellation;
    private Task? _task;
    private Task? _cleanup;
    private TrainingProgress? _progress;
    private Exception? _error;
    private int _version;
    private bool _stopping;
    private bool _disposed;

    public BackgroundTrainer(IOptimizer aEvolution, Func<float[], int, float> aFitness, int aMaxGenerations = 300,
        Func<float[], float>? aValidate = null)
        : this(aEvolution, (aParameters, aGeneration, _) => aFitness(aParameters, aGeneration), aMaxGenerations,
            aValidate is null ? null : (aParameters, _) => aValidate(aParameters))
    {
    }

    public BackgroundTrainer(IOptimizer aEvolution, Func<float[], int, CancellationToken, float> aFitness,
        int aMaxGenerations = 300, Func<float[], CancellationToken, float>? aValidate = null)
    {
        _evolution = aEvolution;
        _fitness = aFitness;
        _validate = aValidate;
        MaxGenerations = aMaxGenerations;
    }

    public int MaxGenerations { get; }

    public bool IsRunning
    {
        get
        {
            lock (_gate)
                return !_stopping && _task is { IsCompleted: false };
        }
    }

    public Task Completion
    {
        get
        {
            lock (_gate)
                return _task ?? Task.CompletedTask;
        }
    }

    public bool Paused
    {
        get
        {
            lock (_gate)
                return !_disposed && !_running.IsSet;
        }
        set
        {
            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                if (value)
                    _running.Reset();
                else
                    _running.Set();
            }
        }
    }

    public Exception? Error
    {
        get
        {
            lock (_gate)
                return _error;
        }
    }

    public void Start()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_task is { IsCompleted: false })
                return;
            _cancellation?.Dispose();
            _cancellation = new CancellationTokenSource();
            var token = _cancellation.Token;
            _stopping = false;
            _error = null;
            _task = Task.Run(() => Run(token));
        }
    }

    private void Run(CancellationToken aCancellation)
    {
        try
        {
            _running.Wait(aCancellation);
            var champion = _evolution.Best;
            var championScore = Validate(champion, float.NegativeInfinity, aCancellation);
            var championGeneration = 0;
            while (_evolution.Generation < MaxGenerations)
            {
                _running.Wait(aCancellation);
                aCancellation.ThrowIfCancellationRequested();
                _evolution.NextGeneration((aParameters, aGeneration) =>
                    _fitness(aParameters, aGeneration, aCancellation), aCancellation);
                aCancellation.ThrowIfCancellationRequested();
                var best = _evolution.Best;
                var score = Validate(best, _evolution.BestFitness, aCancellation);
                if (_validate is null || score > championScore)
                {
                    champion = best;
                    championScore = score;
                    championGeneration = _evolution.Generation;
                }
                var progress = new TrainingProgress(
                    _evolution.Generation, _evolution.BestFitness, champion, championScore, championGeneration);
                lock (_gate)
                {
                    if (_stopping)
                        return;
                    _progress = progress;
                    _version++;
                }
            }
        }
        catch (OperationCanceledException) when (aCancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            lock (_gate)
                _error = exception is AggregateException { InnerException: { } inner } ? inner : exception;
        }
    }

    private float Validate(float[] aParameters, float aFallback, CancellationToken aCancellation) =>
        _validate is null ? aFallback : TrainingScheduler.Evaluate(() => _validate(aParameters, aCancellation), aCancellation);

    public void Stop()
    {
        lock (_gate)
        {
            if (_stopping)
                return;
            _stopping = true;
            _cancellation?.Cancel();
        }
    }

    public async Task StopAsync()
    {
        Stop();
        await Completion.ConfigureAwait(false);
    }

    public bool TryGetProgress(ref int aVersion, out TrainingProgress? aProgress)
    {
        lock (_gate)
        {
            aProgress = _progress;
            if (_version == aVersion)
                return false;
            aVersion = _version;
            return true;
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
                return;
            Stop();
            _disposed = true;
            _cleanup = CleanupAsync(_task ?? Task.CompletedTask, _cancellation);
        }
    }

    private async Task CleanupAsync(Task aTask, CancellationTokenSource? aCancellation)
    {
        await aTask.ConfigureAwait(false);
        aCancellation?.Dispose();
        _running.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        Dispose();
        await _cleanup!.ConfigureAwait(false);
    }
}
