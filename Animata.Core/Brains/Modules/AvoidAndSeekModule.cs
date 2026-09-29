using Animata.Core.Actuators;
using Animata.Core.Sensors;

namespace Animata.Core.Brains.Modules;

/// <summary>
/// Ręczny sterownik autka: jedź do celu, omijając to, co czują wąsy.
/// Wejścia: Found, Gap, DirectionX, DirectionY (z <see cref="TargetSensor"/>) oraz Ray{i} (z <see cref="RaySensor"/>).
/// Wyjścia: Steer ∈ [-1, 1], Throttle ∈ [-1, 1] (jak <see cref="SteeringDriveActuator"/>).
/// Zasada (wybór wolnego kierunku):
/// - jeśli wąsy w stronę celu są wolne — kieruj prosto na cel,
/// - jeśli nie — wybierz wąs, który jest wolny i najbliżej celu; raz wybraną stronę (lewo/prawo)
///   trzymaj z premią, żeby nie zmieniać zdania co klatkę (inaczej autko drga przed przeszkodą),
/// - coś tuż przed maską albo cel blisko z boku (w promieniu skrętu) — cofaj przez chwilę z kontrą,
///   tak żeby przód obrócił się w wybraną stronę.
/// Parametry dobrane przeszukiwaniem na autku w fizyce (128 tras treningowych, sprawdzone na 192 innych: 192/192).
/// Moduł ma krótką pamięć (wybrana strona, licznik cofania) — to stan chwilowy, nie trafia do snapshotu;
/// czyści ją <see cref="Reset"/>.
/// </summary>
public sealed class AvoidAndSeekModule : BrainModule
{
    private static readonly string[] Outputs = [SteeringDriveActuator.SteerPort, SteeringDriveActuator.ThrottlePort];

    private float[] _rayAngles = [];
    private string[] _rayPorts = [];
    private string[] _inputs = [];
    private float[] _proximity = [];
    private float _pushScale;
    private readonly Dictionary<string, float> _command = new()
    {
        [SteeringDriveActuator.SteerPort] = 0,
        [SteeringDriveActuator.ThrottlePort] = 0
    };

    private float _side;
    private float _clearTime;
    private float _reverseTime;
    private float _reverseSteer;

    /// <param name="aRayAngles">Kąty wąsów w radianach, w tej samej kolejności co porty Ray{i} sensora.</param>
    public AvoidAndSeekModule(IEnumerable<float> aRayAngles) => SetRayAngles(aRayAngles);

    /// <summary>
    /// Nowe kąty wąsów (np. po zmianie ich liczby w ciele): porty Ray{i} idą za nimi, parametry zostają,
    /// pamięć chwilowa jest czyszczona. Połączenia w grafie poprawia wołający.
    /// </summary>
    public void SetRayAngles(IEnumerable<float> aRayAngles)
    {
        _rayAngles = aRayAngles.ToArray();
        _rayPorts = Enumerable.Range(0, _rayAngles.Length).Select(RaySensor.PortName).ToArray();
        _proximity = new float[_rayAngles.Length];
        // Odpychanie sumuje wąsy boczne; skala sprowadza je do 4 bocznych wąsów (5 wąsów, na których dobrano AvoidGain),
        // żeby gęstszy wachlarz nie pchał mocniej tylko dlatego, że ma więcej promieni.
        var sideRays = _rayAngles.Count(aAngle => MathF.Abs(aAngle) > 1e-3f);
        _pushScale = sideRays > 0 ? 4f / sideRays : 0;
        _inputs =
        [
            TargetSensor.FoundPort, TargetSensor.GapPort, TargetSensor.DirectionXPort, TargetSensor.DirectionYPort,
            .. _rayPorts
        ];
        Reset();
    }

    public IReadOnlyList<float> RayAngles => _rayAngles;

    /// <summary>Skręt na radian różnicy między kursem a wybranym kierunkiem.</summary>
    public float SteerGain { get; set; } = 1.42f;

    /// <summary>Siła odpychania od przeszkód czutych z boku (bliskość², uśrednione jak dla 4 wąsów bocznych).</summary>
    public float AvoidGain { get; set; } = 5.27f;

    /// <summary>Wąsy bliżej niż ten kąt (rad) od kierunku do celu decydują, czy droga jest wolna.</summary>
    public float PathWidth { get; set; } = 0.56f;

    /// <summary>Bliskość, poniżej której wąs uznaje się za wolny.</summary>
    public float ClearProximity { get; set; } = 0.31f;

    /// <summary>Premia (w skali bliskości) dla wąsów po raz wybranej stronie.</summary>
    public float SideCommitment { get; set; } = 0.38f;

    /// <summary>Wąsy w tym stożku (rad) liczą się jako „przed maską”.</summary>
    public float FrontAngle { get; set; } = 0.82f;

    /// <summary>Bliskość z przodu, od której autko cofa.</summary>
    public float ReverseProximity { get; set; } = 0.96f;

    /// <summary>Najkrótsze (s) cofnięcie.</summary>
    public float ReverseDuration { get; set; } = 0.56f;

    /// <summary>Cofanie trwa, dopóki bliskość z przodu nie spadnie poniżej tej wartości (lub minie 3 s).</summary>
    public float ReleaseProximity { get; set; } = 0.47f;

    /// <summary>Po tylu sekundach wolnej drogi do celu autko zapomina wybraną stronę objazdu.</summary>
    public float SideMemory { get; set; } = 1;

    public float StopGap { get; set; } = 0.1f;
    public float SlowdownGap { get; set; } = 1.19f;
    public float MinThrottle { get; set; } = 0.31f;

    /// <summary>Cel bliżej niż to i bardziej z boku niż <see cref="TurnAroundAngle"/> — cofnij, zamiast krążyć.</summary>
    public float TurnAroundGap { get; set; } = 0.91f;
    public float TurnAroundAngle { get; set; } = 1.24f;

    public override IReadOnlyList<string> InputPorts => _inputs;
    public override IReadOnlyList<string> OutputPorts => Outputs;

    public override IReadOnlyDictionary<string, float> Evaluate(
        IReadOnlyDictionary<string, float> aInputs, BrainContext aContext)
    {
        var steer = 0f;
        var throttle = 0f;

        var front = 0f;
        var push = 0f;
        for (var ray = 0; ray < _rayAngles.Length; ray++)
        {
            var proximity = Math.Clamp(aInputs.GetValueOrDefault(_rayPorts[ray]), 0, 1);
            _proximity[ray] = proximity;
            if (MathF.Abs(_rayAngles[ray]) <= FrontAngle)
                front = MathF.Max(front, proximity);
            // Odpychanie od boków: wąs po lewej pcha w prawo i odwrotnie, mocniej z bliska.
            if (MathF.Abs(_rayAngles[ray]) > 1e-3f)
                push -= MathF.Sign(_rayAngles[ray]) * proximity * proximity;
        }

        if (aInputs.GetValueOrDefault(TargetSensor.FoundPort) > 0)
        {
            var bearing = MathF.Atan2(
                aInputs.GetValueOrDefault(TargetSensor.DirectionYPort),
                aInputs.GetValueOrDefault(TargetSensor.DirectionXPort));
            var remaining = aInputs.GetValueOrDefault(TargetSensor.GapPort) - StopGap;

            if (remaining > 0)
            {
                var desired = ChooseDirection(bearing, aContext.Delta);
                steer = Math.Clamp(desired * SteerGain + push * _pushScale * AvoidGain, -1, 1);

                if (_reverseTime <= 0)
                {
                    if (front >= ReverseProximity)
                        StartReverse(desired);
                    else if (remaining < TurnAroundGap && MathF.Abs(bearing) > TurnAroundAngle)
                        StartReverse(bearing);
                }

                if (_reverseTime > 0)
                {
                    _reverseTime += aContext.Delta;
                    var done = _reverseTime >= ReverseDuration && front < ReleaseProximity
                        && !(remaining < TurnAroundGap && MathF.Abs(bearing) > TurnAroundAngle);
                    if (done || _reverseTime > 3)
                        _reverseTime = 0;
                    throttle = -0.6f;
                    steer = _reverseSteer;
                }
                else
                {
                    var slowdown = SlowdownGap > 0 ? Math.Clamp(remaining / SlowdownGap, 0, 1) : 1;
                    throttle = MathF.Max(MinThrottle, slowdown * (1 - 0.6f * front));
                }
            }
            else
            {
                _reverseTime = 0;
            }
        }

        _command[SteeringDriveActuator.SteerPort] = steer;
        _command[SteeringDriveActuator.ThrottlePort] = throttle;
        return _command;
    }

    /// <summary>Kierunek (rad, względem przodu), w który warto jechać.</summary>
    private float ChooseDirection(float aBearing, float aDelta)
    {
        var blocked = 0f;
        var nearestToBearing = 0;
        for (var ray = 0; ray < _rayAngles.Length; ray++)
        {
            var offset = MathF.Abs(_rayAngles[ray] - aBearing);
            if (offset <= PathWidth)
                blocked = MathF.Max(blocked, _proximity[ray]);
            if (offset < MathF.Abs(_rayAngles[nearestToBearing] - aBearing))
                nearestToBearing = ray;
        }
        // Cel poza wachlarzem wąsów: liczy się wąs najbliższy jego kierunkowi.
        blocked = MathF.Max(blocked, _proximity[nearestToBearing]);

        if (blocked < ClearProximity)
        {
            _clearTime += aDelta;
            if (_clearTime >= SideMemory)
                _side = 0;
            return aBearing;
        }
        _clearTime = 0;

        var best = 0;
        var bestScore = float.NegativeInfinity;
        for (var ray = 0; ray < _rayAngles.Length; ray++)
        {
            var angle = _rayAngles[ray];
            var score = 2 * (1 - _proximity[ray]) - MathF.Abs(angle - aBearing) / MathF.PI;
            if (_side != 0 && MathF.Sign(angle) == _side)
                score += SideCommitment;
            if (score > bestScore)
            {
                bestScore = score;
                best = ray;
            }
        }

        var chosen = _rayAngles[best];
        if (MathF.Abs(chosen) > 1e-3f)
            _side = MathF.Sign(chosen);
        else if (_side == 0)
            _side = aBearing >= 0 ? 1 : -1;
        return chosen;
    }

    /// <summary>
    /// Cofanie z kontrą: przy jeździe do tyłu skręt w prawo obraca przód w lewo i odwrotnie.
    /// Strona pochodzi z zapamiętanego objazdu, a bez niego — z kierunku, w który chcemy się obrócić.
    /// </summary>
    private void StartReverse(float aDirection)
    {
        var side = _side != 0 ? _side : MathF.Abs(aDirection) > 1e-3f ? MathF.Sign(aDirection) : 1;
        _side = side;
        _reverseSteer = -side;
        _reverseTime = 1e-6f;
    }

    public override void Reset()
    {
        _side = 0;
        _clearTime = 0;
        _reverseTime = 0;
        _reverseSteer = 0;
        Array.Clear(_proximity);
        _command[SteeringDriveActuator.SteerPort] = 0;
        _command[SteeringDriveActuator.ThrottlePort] = 0;
    }

    public override ModuleState CaptureState() => new AvoidAndSeekState(
        _rayAngles.ToArray(), SteerGain, AvoidGain, PathWidth, ClearProximity, SideCommitment, FrontAngle,
        ReverseProximity, ReverseDuration, ReleaseProximity, SideMemory, StopGap, SlowdownGap, MinThrottle, TurnAroundGap, TurnAroundAngle);

    public override void RestoreState(ModuleState aState)
    {
        var state = Expect<AvoidAndSeekState>(aState);
        // Kąty należą do ciała (liczba wąsów), nie do parametrów sterownika: snapshot z inną liczbą wąsów
        // przywraca tylko parametry, a kąty zostają takie, jak ma teraz ciało.
        SteerGain = state.SteerGain;
        AvoidGain = state.AvoidGain;
        PathWidth = state.PathWidth;
        ClearProximity = state.ClearProximity;
        SideCommitment = state.SideCommitment;
        FrontAngle = state.FrontAngle;
        ReverseProximity = state.ReverseProximity;
        ReverseDuration = state.ReverseDuration;
        ReleaseProximity = state.ReleaseProximity;
        SideMemory = state.SideMemory;
        StopGap = state.StopGap;
        SlowdownGap = state.SlowdownGap;
        MinThrottle = state.MinThrottle;
        TurnAroundGap = state.TurnAroundGap;
        TurnAroundAngle = state.TurnAroundAngle;
    }
}
