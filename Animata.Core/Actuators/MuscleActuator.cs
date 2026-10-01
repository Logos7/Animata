using Animata.Core.Entities;

namespace Animata.Core.Actuators;

/// <summary>
/// Mięśnie stwora (gniazdo zwykle „Muscles”): jeden port na mięsień planu, o nazwie mięśnia — pobudzenie [0, 1]
/// (0 — mięsień wiotki, 1 — skurcz z pełną siłą). Siłę liczy model Hilla w <see cref="ArticulatedCreature"/>.
/// </summary>
public sealed class MuscleActuator : Actuator
{
    private string[] _ports = [];

    public MuscleActuator(Bodies.BodyPlan aPlan) => SetMuscles(aPlan);

    public override IReadOnlyList<string> InputPorts => _ports;

    public void SetMuscles(Bodies.BodyPlan aPlan) => _ports = [.. aPlan.MuscleList.Select(aMuscle => aMuscle.Name)];

    public override void Apply(Entity aOwner, IReadOnlyDictionary<string, float> aCommands, float aDelta)
    {
        if (aOwner is not ArticulatedCreature creature)
            return;
        var muscles = Math.Min(_ports.Length, creature.MuscleCount);
        for (var muscle = 0; muscle < muscles; muscle++)
            creature.SetMuscleExcitation(muscle, aCommands.GetValueOrDefault(_ports[muscle]));
    }
}
