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

    /// <summary>Humanoid mięśniowy ze sterownikiem (sensory → sterownik → mięśnie „Muscles”), patrzy na cel.</summary>
    public static Creature CreateMuscleHumanoid(Vector3 aPosition, float aYaw, Guid? aTargetId, BrainModule aController) =>
        new Spawn(MuscleHumanoid.Design.Type)
        {
            Slots = Spawn.Aim(aTargetId),
            Brain = Spawn.Controller(aController),
            Pose = Spawn.At(aPosition, aYaw)
        }.Build<Creature>();

    /// <summary>Humanoid mięśniowy z gotowym mózgiem o podanej nazwie (<see cref="MuscleHumanoidBrains.Presets"/>).</summary>
    public static Creature CreateMuscleHumanoid(Vector3 aPosition, float aYaw, Guid? aTargetId, string aPreset) =>
        new Spawn(MuscleHumanoid.Design.Type)
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

    /// <summary>
    /// Scena humanoida mięśniowego: podłoga, kula tuż przed nim (stoi), humanoid mięśniowy z automatem stanów (stanie
    /// i chód na mięśniach) i dla porównania humanoid na serwach z ręcznymi modułami. Kula odsunięta dalej niż ok. 1 m
    /// włącza chód — na mięśniach na razie kończy się upadkiem.
    /// </summary>
    public static DemoScene CreateMuscleHumanoidScene()
    {
        var world = new World();
        world.Add(CreateFloor(30, 30));
        var target = CreateSphere(new Vector3(1, -1, 0));
        target.Name = "Kula";
        world.Add(target);
        Terrain.Snap(world, target);
        var muscled = CreateMuscleHumanoid(new Vector3(0, -1, 0), 0, target.Id, MuscleHumanoidBrains.HandPreset);
        muscled.Name = "Humanoid mięśniowy";
        world.Add(muscled);
        var servo = CreateHumanoid(new Vector3(0, 1, 0), 0, target.Id, HumanoidBrains.HandPreset);
        servo.Name = "Humanoid";
        world.Add(servo);
        return new DemoScene(world, [muscled, servo]);
    }
}
