using Animata.Core.Entities;

namespace Animata.Core.Actuators;

/// <summary>
/// Kręgosłup stwora z części: po dwa porty na staw — Yaw{i} (skręt w bok) i Pitch{i} (pochylenie), oba ∈ [-1, 1]
/// jako ułamek zakresu stawu. Aktuator tylko zadaje cele serw; ruch liczy fizyka (<see cref="ArticulatedCreature"/>).
/// Liczba stawów idzie za ciałem — po przebudowie ciała woła się <see cref="SetJointCount"/>.
/// </summary>
public sealed class SpineActuator : Actuator
{
    private string[] _ports = [];

    public SpineActuator(int aJoints) => SetJointCount(aJoints);

    public int Joints { get; private set; }

    public override IReadOnlyList<string> InputPorts => _ports;

    public static string YawPort(int aJoint) => $"Yaw{aJoint}";
    public static string PitchPort(int aJoint) => $"Pitch{aJoint}";

    /// <summary>Porty dla <paramref name="aJoints"/> stawów: najpierw wszystkie Yaw, potem wszystkie Pitch.</summary>
    public static string[] PortsFor(int aJoints) =>
        [.. Enumerable.Range(0, aJoints).Select(YawPort), .. Enumerable.Range(0, aJoints).Select(PitchPort)];

    public void SetJointCount(int aJoints)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(aJoints);
        Joints = aJoints;
        _ports = PortsFor(aJoints);
    }

    public override void Apply(Entity aOwner, IReadOnlyDictionary<string, float> aCommands, float aDelta)
    {
        if (aOwner is not ArticulatedCreature creature)
            return;
        var joints = Math.Min(Joints, creature.JointCount);
        for (var joint = 0; joint < joints; joint++)
            creature.SetJointTarget(joint, aCommands.GetValueOrDefault(YawPort(joint)), aCommands.GetValueOrDefault(PitchPort(joint)));
    }
}
