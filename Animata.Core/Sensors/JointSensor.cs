using Animata.Core.Actuators;
using Animata.Core.Entities;
using Animata.Core.Worlds;

namespace Animata.Core.Sensors;

/// <summary>
/// Czucie stawów (propriocepcja): Yaw{i} i Pitch{i} — zmierzone kąty stawów jako ułamek ich zakresu ([-1, 1],
/// jak komendy <see cref="SpineActuator"/>). Porty idą za liczbą stawów ciała (<see cref="SetJointCount"/>).
/// </summary>
public sealed class JointSensor : Sensor
{
    private string[] _ports = [];
    private readonly Dictionary<string, float> _readings = [];

    public JointSensor(int aJoints) => SetJointCount(aJoints);

    public JointSensor(Bodies.BodyPlan aPlan) => SetJoints(aPlan);

    public int Joints { get; private set; }

    public override IReadOnlyList<string> OutputPorts => _ports;

    public void SetJointCount(int aJoints)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(aJoints);
        Joints = aJoints;
        SetPorts(SpineActuator.PortsFor(aJoints));
    }

    /// <summary>Stawy i porty z planu ciała (tylko ruchome osie — zawias nie ma portu skrętu).</summary>
    public void SetJoints(Bodies.BodyPlan aPlan)
    {
        Joints = aPlan.Joints.Count;
        SetPorts(SpineActuator.SensedPortsFor(aPlan));
    }

    private void SetPorts(string[] aPorts)
    {
        _ports = aPorts;
        _readings.Clear();
        foreach (var port in _ports)
            _readings[port] = 0;
    }

    public override IReadOnlyDictionary<string, float> Read(Entity aOwner, World aWorld)
    {
        if (aOwner is not ArticulatedCreature creature)
            return _readings;
        var joints = Math.Min(Joints, creature.JointCount);
        for (var joint = 0; joint < joints; joint++)
        {
            var plan = creature.Plan.Joints[joint];
            if (plan.HasYaw)
                _readings[SpineActuator.YawPort(joint)] = Bodies.JointPlan.Command(creature.JointYaw(joint), plan.YawMin, plan.MaxYaw);
            if (plan.HasPitch)
                _readings[SpineActuator.PitchPort(joint)] = Bodies.JointPlan.Command(creature.JointPitch(joint), plan.PitchMin, plan.MaxPitch);
        }
        return _readings;
    }
}
