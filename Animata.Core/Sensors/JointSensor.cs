using Animata.Core.Actuators;
using Animata.Core.Bodies;
using Animata.Core.Entities;
using Animata.Core.Worlds;

namespace Animata.Core.Sensors;

/// <summary>
/// Czucie stawów (propriocepcja): Yaw{i} i Pitch{i} — zmierzone kąty stawów jako ułamek ich zakresu ([-1, 1],
/// jak komendy <see cref="SpineActuator"/>). Porty idą za planem ciała (<see cref="SetJoints"/>).
/// </summary>
public sealed class JointSensor : Sensor
{
    public JointSensor(Bodies.BodyPlan aPlan) => SetJoints(aPlan);

    public int Joints { get; private set; }

    /// <summary>Stawy i porty z planu ciała (tylko ruchome osie — zawias nie ma portu skrętu).</summary>
    public void SetJoints(Bodies.BodyPlan aPlan)
    {
        Joints = aPlan.Joints.Count;
        SetPorts(JointPorts.Sensed(aPlan));
    }

    public override IReadOnlyDictionary<string, float> Read(Entity aOwner, World aWorld)
    {
        if (aOwner is not ArticulatedCreature creature)
            return Readings;
        var joints = Math.Min(Joints, creature.JointCount);
        for (var joint = 0; joint < joints; joint++)
        {
            var plan = creature.Plan.Joints[joint];
            if (plan.HasYaw)
                Readings[JointPorts.Yaw(joint)] = Bodies.JointPlan.Command(creature.JointYaw(joint), plan.YawMin, plan.MaxYaw);
            if (plan.HasPitch)
                Readings[JointPorts.Pitch(joint)] = Bodies.JointPlan.Command(creature.JointPitch(joint), plan.PitchMin, plan.MaxPitch);
        }
        return Readings;
    }
}
