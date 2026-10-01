namespace Animata.Core.Training;

/// <summary>
/// CMA-ES (Covariance Matrix Adaptation Evolution Strategy, N. Hansen, „The CMA Evolution Strategy: A Tutorial”):
/// pokolenie to λ próbek x = m + σ · B · D · z (z ~ N(0, I)); μ najlepszych przesuwa średnią m, ścieżki ewolucji
/// uczą kowariancję C = B · D² · Bᵀ i długość kroku σ. Do ok. 60 wymiarów pełna kowariancja (rozkład Jacobiego),
/// powyżej — tylko przekątna (sep-CMA-ES, Ros i Hansen 2008), bo sieci mają tysiące wag.
/// Najlepszy wynik to najlepsza próbka od początku (pierwsze pokolenie ocenia też punkt startowy).
/// </summary>
public sealed class CmaEs
{
    public const int FullCovarianceLimit = 60;

    private readonly int _n;
    private readonly int _lambda;
    private readonly int _mu;
    private readonly double[] _weights;
    private readonly double _muEff, _cc, _cs, _c1, _cmu, _damps, _chiN;
    private readonly bool _separable;
    private readonly double[] _mean;
    private double _sigma;
    private readonly double[] _pc;
    private readonly double[] _ps;
    private readonly double[,] _c;     // pełna kowariancja (albo tylko przekątna przy sep)
    private readonly double[,] _b;     // wektory własne (kolumny)
    private readonly double[] _d;      // pierwiastki wartości własnych
    private readonly Random _random;
    private readonly int _parallelism;

    public CmaEs(ReadOnlySpan<float> aStart, EvolutionOptions aOptions)
    {
        _n = aStart.Length;
        if (_n == 0)
            throw new ArgumentException("Nothing to optimize.", nameof(aStart));
        _lambda = Math.Max(4, aOptions.PopulationSize);
        _mu = _lambda / 2;
        _weights = new double[_mu];
        for (var index = 0; index < _mu; index++)
            _weights[index] = Math.Log(_mu + 0.5) - Math.Log(index + 1);
        var sum = _weights.Sum();
        for (var index = 0; index < _mu; index++)
            _weights[index] /= sum;
        _muEff = 1 / _weights.Sum(aWeight => aWeight * aWeight);
        _separable = _n > FullCovarianceLimit;
        double n = _n;
        _cc = (4 + _muEff / n) / (n + 4 + 2 * _muEff / n);
        _cs = (_muEff + 2) / (n + _muEff + 5);
        _c1 = 2 / ((n + 1.3) * (n + 1.3) + _muEff);
        _cmu = Math.Min(1 - _c1, 2 * (_muEff - 2 + 1 / _muEff) / ((n + 2) * (n + 2) + _muEff));
        if (_separable)
        {
            var scale = (n + 2) / 3;
            _c1 = Math.Min(1, _c1 * scale);
            _cmu = Math.Min(1 - _c1, _cmu * scale);
        }
        _damps = 1 + 2 * Math.Max(0, Math.Sqrt((_muEff - 1) / (n + 1)) - 1) + _cs;
        _chiN = Math.Sqrt(n) * (1 - 1 / (4 * n) + 1 / (21 * n * n));

        _mean = new double[_n];
        for (var index = 0; index < _n; index++)
            _mean[index] = aStart[index];
        _sigma = aOptions.MutationSigma;
        _pc = new double[_n];
        _ps = new double[_n];
        _c = new double[_n, _separable ? 1 : _n];
        _b = new double[_separable ? 1 : _n, _separable ? 1 : _n];
        _d = new double[_n];
        for (var index = 0; index < _n; index++)
        {
            if (_separable)
                _c[index, 0] = 1;
            else
            {
                _c[index, index] = 1;
                _b[index, index] = 1;
            }
            _d[index] = 1;
        }
        _random = new Random(aOptions.Seed);
        _parallelism = aOptions.MaxParallelism;
        Best = aStart.ToArray();
    }

    public float[] Best { get; private set; }
    public float BestFitness { get; private set; } = float.NegativeInfinity;

    /// <summary>Krok σ (do podglądu i testów).</summary>
    public double Sigma => _sigma;

    /// <summary>Jedno pokolenie: λ próbek, ocena (równoległa), aktualizacja rozkładu. Fitness — większy lepszy.</summary>
    public void Step(Func<float[], int, float> aFitness, int aGeneration)
    {
        var z = new double[_lambda][];
        var y = new double[_lambda][];
        var x = new float[_lambda][];
        for (var k = 0; k < _lambda; k++)
        {
            z[k] = new double[_n];
            for (var i = 0; i < _n; i++)
                z[k][i] = Gaussian();
            y[k] = Transform(z[k]);
            x[k] = new float[_n];
            for (var i = 0; i < _n; i++)
                x[k][i] = (float)(_mean[i] + _sigma * y[k][i]);
        }
        if (aGeneration == 0)
        {
            // Pierwsza próbka to sam punkt startowy — najlepszy wynik nigdy nie jest gorszy od startu.
            Array.Clear(z[0]);
            Array.Clear(y[0]);
            for (var i = 0; i < _n; i++)
                x[0][i] = (float)_mean[i];
        }

        var fitness = new float[_lambda];
        Parallel.For(0, _lambda, new ParallelOptions { MaxDegreeOfParallelism = _parallelism }, aIndex =>
        {
            var value = aFitness(x[aIndex], aGeneration);
            fitness[aIndex] = float.IsFinite(value) ? value : float.NegativeInfinity;
        });
        var order = Enumerable.Range(0, _lambda).OrderByDescending(aIndex => fitness[aIndex]).ToArray();
        if (fitness[order[0]] > BestFitness)
        {
            BestFitness = fitness[order[0]];
            Best = (float[])x[order[0]].Clone();
        }

        // Średnia i kierunek kroku.
        var yw = new double[_n];
        for (var k = 0; k < _mu; k++)
            for (var i = 0; i < _n; i++)
                yw[i] += _weights[k] * y[order[k]][i];
        for (var i = 0; i < _n; i++)
            _mean[i] += _sigma * yw[i];

        // Ścieżka σ: C^(−1/2) · yw = B · D⁻¹ · Bᵀ · yw.
        var whitened = InverseSqrt(yw);
        var csFactor = Math.Sqrt(_cs * (2 - _cs) * _muEff);
        for (var i = 0; i < _n; i++)
            _ps[i] = (1 - _cs) * _ps[i] + csFactor * whitened[i];
        var psNorm = Math.Sqrt(_ps.Sum(aValue => aValue * aValue));
        var hsig = psNorm / Math.Sqrt(1 - Math.Pow(1 - _cs, 2 * (aGeneration + 1))) / _chiN < 1.4 + 2.0 / (_n + 1) ? 1.0 : 0.0;
        var ccFactor = Math.Sqrt(_cc * (2 - _cc) * _muEff);
        for (var i = 0; i < _n; i++)
            _pc[i] = (1 - _cc) * _pc[i] + hsig * ccFactor * yw[i];

        // Kowariancja.
        var keep = 1 - _c1 - _cmu + (1 - hsig) * _c1 * _cc * (2 - _cc);
        if (_separable)
        {
            for (var i = 0; i < _n; i++)
            {
                var rankMu = 0.0;
                for (var k = 0; k < _mu; k++)
                    rankMu += _weights[k] * y[order[k]][i] * y[order[k]][i];
                _c[i, 0] = keep * _c[i, 0] + _c1 * _pc[i] * _pc[i] + _cmu * rankMu;
                _d[i] = Math.Sqrt(Math.Max(1e-20, _c[i, 0]));
            }
        }
        else
        {
            for (var i = 0; i < _n; i++)
                for (var j = 0; j <= i; j++)
                {
                    var rankMu = 0.0;
                    for (var k = 0; k < _mu; k++)
                        rankMu += _weights[k] * y[order[k]][i] * y[order[k]][j];
                    var value = keep * _c[i, j] + _c1 * _pc[i] * _pc[j] + _cmu * rankMu;
                    _c[i, j] = value;
                    _c[j, i] = value;
                }
            Decompose();
        }

        _sigma *= Math.Exp(_cs / _damps * (psNorm / _chiN - 1));
        _sigma = Math.Clamp(_sigma, 1e-8, 1e4);
    }

    /// <summary>y = B · D · z.</summary>
    private double[] Transform(double[] aZ)
    {
        var y = new double[_n];
        if (_separable)
        {
            for (var i = 0; i < _n; i++)
                y[i] = _d[i] * aZ[i];
            return y;
        }
        for (var i = 0; i < _n; i++)
        {
            var value = 0.0;
            for (var j = 0; j < _n; j++)
                value += _b[i, j] * _d[j] * aZ[j];
            y[i] = value;
        }
        return y;
    }

    /// <summary>C^(−1/2) · v = B · D⁻¹ · Bᵀ · v.</summary>
    private double[] InverseSqrt(double[] aV)
    {
        var result = new double[_n];
        if (_separable)
        {
            for (var i = 0; i < _n; i++)
                result[i] = aV[i] / _d[i];
            return result;
        }
        var projected = new double[_n];
        for (var j = 0; j < _n; j++)
        {
            var value = 0.0;
            for (var i = 0; i < _n; i++)
                value += _b[i, j] * aV[i];
            projected[j] = value / _d[j];
        }
        for (var i = 0; i < _n; i++)
        {
            var value = 0.0;
            for (var j = 0; j < _n; j++)
                value += _b[i, j] * projected[j];
            result[i] = value;
        }
        return result;
    }

    /// <summary>Rozkład własny C (metoda Jacobiego) → B, D.</summary>
    private void Decompose()
    {
        var a = (double[,])_c.Clone();
        var v = new double[_n, _n];
        for (var i = 0; i < _n; i++)
            v[i, i] = 1;
        for (var sweep = 0; sweep < 50; sweep++)
        {
            var off = 0.0;
            for (var p = 0; p < _n; p++)
                for (var q = p + 1; q < _n; q++)
                    off += a[p, q] * a[p, q];
            if (off < 1e-22)
                break;
            for (var p = 0; p < _n; p++)
                for (var q = p + 1; q < _n; q++)
                {
                    if (Math.Abs(a[p, q]) < 1e-30)
                        continue;
                    var theta = (a[q, q] - a[p, p]) / (2 * a[p, q]);
                    var t = Math.Sign(theta) / (Math.Abs(theta) + Math.Sqrt(theta * theta + 1));
                    if (theta == 0)
                        t = 1;
                    var c = 1 / Math.Sqrt(t * t + 1);
                    var s = t * c;
                    for (var k = 0; k < _n; k++)
                    {
                        var akp = a[k, p];
                        var akq = a[k, q];
                        a[k, p] = c * akp - s * akq;
                        a[k, q] = s * akp + c * akq;
                    }
                    for (var k = 0; k < _n; k++)
                    {
                        var apk = a[p, k];
                        var aqk = a[q, k];
                        a[p, k] = c * apk - s * aqk;
                        a[q, k] = s * apk + c * aqk;
                    }
                    for (var k = 0; k < _n; k++)
                    {
                        var vkp = v[k, p];
                        var vkq = v[k, q];
                        v[k, p] = c * vkp - s * vkq;
                        v[k, q] = s * vkp + c * vkq;
                    }
                }
        }
        for (var i = 0; i < _n; i++)
        {
            _d[i] = Math.Sqrt(Math.Max(1e-20, a[i, i]));
            for (var j = 0; j < _n; j++)
                _b[i, j] = v[i, j];
        }
    }

    private double Gaussian()
    {
        var u1 = 1 - _random.NextDouble();
        var u2 = _random.NextDouble();
        return Math.Sqrt(-2 * Math.Log(u1)) * Math.Cos(2 * Math.PI * u2);
    }
}
