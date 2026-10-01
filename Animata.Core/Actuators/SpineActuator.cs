using Animata.Core.Bodies;
using Animata.Core.Entities;

namespace Animata.Core.Actuators;

/// <summary>
/// Napęd stawów stwora z części (kręgosłup węża, nogi pająka, stawy humanoida): porty Yaw{i} (skręt) i Pitch{i}
/// (pochylenie) osi z serwem (<see cref="JointPorts.Driven"/>), oba ∈ [-1, 1] jako ułamek zakresu stawu. Aktuator tylko
/// zadaje cele serw; ruch liczy fizyka (<see cref="ArticulatedCreature"/>). Porty idą za ciałem — po przebudowie ciała
/// woła się <see cref="SetJoints"/>.
/// </summary>
public sealed class SpineActuator : Actuator
{
    private string[] _ports = [];

    public SpineActuator(BodyPlan aPlan) => SetJoints(aPlan);

    public int Joints { get; private set; }

    public override IReadOnlyList<string> InputPorts => _ports;

    /// <summary>Stawy i porty z planu ciała (tylko osie z serwem).</summary>
    public void SetJoints(BodyPlan aPlan)
    {
        Joints = aPlan.Joints.Count;
        _ports = JointPorts.Driven(aPlan);
    }

    public override void Apply(Entity aOwner, IReadOnlyDictionary<string, float> aCommands, float aDelta)
    {
        if (aOwner is not ArticulatedCreature creature)
            return;
        var joints = Math.Min(Joints, creature.JointCount);
        for (var joint = 0; joint < joints; joint++)
            creature.SetJointTarget(joint, aCommands.GetValueOrDefault(JointPorts.Yaw(joint)), aCommands.GetValueOrDefault(JointPorts.Pitch(joint)));
    }
}
