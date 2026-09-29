namespace Animata.Core.Training;

/// <summary>
/// Stan nauki po pokoleniu. BestFitness to wynik zwycięzcy bieżącego pokolenia (na jego losowych próbach —
/// szum, może być gorszy od poprzedniego). Champion to najlepszy dotąd osobnik na stałym zestawie prób
/// walidacyjnych — zmienia się tylko na lepsze; to jego warto pokazywać w scenie.
/// </summary>
public sealed record TrainingProgress(int Generation, float BestFitness, float[] Champion, float ChampionScore, int ChampionGeneration);

/// <summary>
/// Uruchamia <see cref="Evolution"/> na wątku w tle. Wątek UI co klatkę pyta o nowy postęp
/// (<see cref="TryGetProgress"/>) i sam przenosi parametry do sceny — trener nigdy nie dotyka świata sceny.
/// </summary>
public sealed class BackgroundTrainer : IDisposable
{
    private readonly Evolution _evolution;
    private readonly Func<float[], int, float> _fitness;
    private readonly Func<float[], float>? _validate;
    private readonly object _gate = new();
    private readonly ManualResetEventSlim _running = new(true);
    private CancellationTokenSource? _cancellation;
    private Task? _task;
    private TrainingProgress? _progress;
    private int _version;

    /// <param name="aValidate">
    /// Ocena na stałym zestawie prób (większa = lepsza). Bez niej mistrzem jest po prostu zwycięzca pokolenia.
    /// </param>
    public BackgroundTrainer(Evolution aEvolution, Func<float[], int, float> aFitness, int aMaxGenerations = 300,
        Func<float[], float>? aValidate = null)
    {
        _evolution = aEvolution;
        _fitness = aFitness;
        _validate = aValidate;
        MaxGenerations = aMaxGenerations;
    }

    public int MaxGenerations { get; }
    public bool IsRunning => _task is { IsCompleted: false };

    /// <summary>
    /// Wstrzymanie: bieżące pokolenie się dokańcza, następne czeka, aż pauza zniknie (np. scena, której nie widać,
    /// nie ewoluuje w tle). Nie zmienia postępu ani mistrza.
    /// </summary>
    public bool Paused
    {
        get => !_running.IsSet;
        set
        {
            if (value)
                _running.Reset();
            else
                _running.Set();
        }
    }

    /// <summary>Wyjątek, który przerwał naukę (np. błąd mózgu w próbie), albo null.</summary>
    public Exception? Error { get; private set; }

    public void Start()
    {
        if (IsRunning)
            return;
        Error = null;
        _cancellation = new CancellationTokenSource();
        var token = _cancellation.Token;
        _task = Task.Run(() =>
        {
            try
            {
                // Mistrz startowy = wagi, od których zaczynamy; zastąpi go tylko ktoś lepszy na walidacji.
                var champion = _evolution.Best;
                var championScore = _validate?.Invoke(champion) ?? float.NegativeInfinity;
                var championGeneration = 0;
                while (!token.IsCancellationRequested && _evolution.Generation < MaxGenerations)
                {
                    _running.Wait(token);
                    _evolution.Step(_fitness);
                    var best = _evolution.Best;
                    var score = _validate?.Invoke(best) ?? _evolution.BestFitness;
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
                        _progress = progress;
                        _version++;
                    }
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
            }
            catch (Exception exception)
            {
                Error = exception is AggregateException { InnerException: { } inner } ? inner : exception;
            }
        }, token);
    }

    public void Stop()
    {
        _cancellation?.Cancel();
        try
        {
            _task?.Wait();
        }
        catch (AggregateException)
        {
        }
    }

    /// <summary>Zwraca true, jeśli od ostatniego odczytu (aVersion) pojawił się nowy postęp.</summary>
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
        Stop();
        _cancellation?.Dispose();
        _running.Dispose();
    }
}
