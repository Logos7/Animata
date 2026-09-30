using System.Numerics;
using Animata.Core.Brains.Modules;
using Animata.Core.Entities;
using Animata.Core.Worlds;

namespace Animata.Core.WorldObjects;

// Humanoid: dwunóg z projektu Humanoid.
public static partial class WorldObjectCatalog
{
    /// <summary>Humanoid ze sterownikiem (sensory ciała → sterownik → napęd stawów „Body”), patrzy na cel.</summary>
    public static Creature CreateHumanoid(Vector3 aPosition, float aYaw, Guid? aTargetId, BrainModule aController) =>
        new Spawn(Humanoid.Design.Type)
        {
            Slots = Spawn.Aim(aTargetId),
            Brain = Spawn.Controller(aController),
            Pose = Spawn.At(aPosition, aYaw)
        }.Build<Creature>();

    /// <summary>Humanoid z gotowym mózgiem o podanej nazwie (<see cref="HumanoidBrains.Presets"/>), patrzy na cel.</summary>
    public static Creature CreateHumanoid(Vector3 aPosition, float aYaw, Guid? aTargetId, string aPreset) =>
        new Spawn(Humanoid.Design.Type)
        {
            Slots = Spawn.Aim(aTargetId),
            Brain = Spawn.Preset(aCreature => aCreature.BrainPresets.Single(aChoice => aChoice.Name == aPreset)),
            Pose = Spawn.At(aPosition, aYaw)
        }.Build<Creature>();

    /// <summary>
    /// Scena humanoidów: podłoga 30 × 30, kula 4 m dalej, dwa humanoidy z mózgiem ze stanami — jeden z ręcznym
    /// regulatorem stania i generatorem kroku, drugi z dwiema sieciami do nauczenia. Przesunięta kula blisko — stoją.
    /// </summary>
    public static DemoScene CreateHumanoidScene()
    {
        var world = new World();
        world.Add(CreateFloor(30, 30));
        var target = CreateSphere(new Vector3(4, 0, 0));
        target.Name = "Kula";
        world.Add(target);
        Terrain.Snap(world, target);
        var hand = CreateHumanoid(new Vector3(0, -1, 0), 0, target.Id, HumanoidBrains.HandPreset);
        hand.Name = "Humanoid";
        world.Add(hand);
        var neural = CreateHumanoid(new Vector3(0, 1, 0), 0, target.Id, HumanoidBrains.NetworkPreset);
        neural.Name = "Humanoid NN";
        world.Add(neural);
        return new DemoScene(world, [hand, neural]);
    }
}
