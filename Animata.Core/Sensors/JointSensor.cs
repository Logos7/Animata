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

    public int Joints { get; private set; }

    public override IReadOnlyList<string> OutputPorts => _ports;

    public void SetJointCount(int aJoints)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(aJoints);
        Joints = aJoints;
        _ports = SpineActuator.PortsFor(aJoints);
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
            _readings[SpineActuator.YawPort(joint)] = plan.MaxYaw > 0 ? creature.JointYaw(joint) / plan.MaxYaw : 0;
            _readings[SpineActuator.PitchPort(joint)] = plan.MaxPitch > 0 ? creature.JointPitch(joint) / plan.MaxPitch : 0;
        }
        return _readings;
    }
}
