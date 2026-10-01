using Animata.Core.Entities;
using Animata.Core.Worlds;

namespace Animata.Core.Sensors;

/// <summary>
/// Czucie mięśni (gniazdo zwykle „MuscleSense”) — jak wrzeciona mięśniowe i narządy ścięgniste Golgiego. Dla każdego
/// mięśnia trzy porty: „Dł{mięsień}” — o ile jest dłuższy (+) albo krótszy (−) od długości optymalnej, × 5;
/// „Szyb{mięsień}” — szybkość wydłużania w długościach optymalnych na sekundę / 2; „Siła{mięsień}” — siła jako ułamek
/// siły maksymalnej. Wszystko w [-1, 1].
/// </summary>
public sealed class MuscleSensor : Sensor
{
    private string[] _names = [];
    private string[] _ports = [];
    private readonly Dictionary<string, float> _readings = [];

    public MuscleSensor(Bodies.BodyPlan aPlan) => SetMuscles(aPlan);

    public static string LengthPort(string aMuscle) => $"Dł{aMuscle}";
    public static string SpeedPort(string aMuscle) => $"Szyb{aMuscle}";
    public static string ForcePort(string aMuscle) => $"Siła{aMuscle}";

    public override IReadOnlyList<string> OutputPorts => _ports;

    public void SetMuscles(Bodies.BodyPlan aPlan)
    {
        _names = [.. aPlan.MuscleList.Select(aMuscle => aMuscle.Name)];
        _ports = [.. _names.Select(LengthPort), .. _names.Select(SpeedPort), .. _names.Select(ForcePort)];
        _readings.Clear();
        foreach (var port in _ports)
            _readings[port] = 0;
    }

    public override IReadOnlyDictionary<string, float> Read(Entity aOwner, World aWorld)
    {
        if (aOwner is not ArticulatedCreature creature)
            return _readings;
        var muscles = Math.Min(_names.Length, creature.MuscleCount);
        for (var muscle = 0; muscle < muscles; muscle++)
        {
            var plan = creature.Plan.MuscleList[muscle];
            var optimal = plan.Optimal;
            _readings[LengthPort(_names[muscle])] = Math.Clamp((creature.MuscleLength(muscle) / optimal - 1) * 5, -1, 1);
            _readings[SpeedPort(_names[muscle])] = Math.Clamp(creature.MuscleSpeed(muscle) / optimal / 2, -1, 1);
            _readings[ForcePort(_names[muscle])] = Math.Clamp(creature.MuscleForce(muscle) / plan.MaxForce, -1, 1);
        }
        return _readings;
    }
}
