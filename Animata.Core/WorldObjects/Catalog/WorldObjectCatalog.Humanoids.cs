using System.Numerics;
using Animata.Core.Entities;
using Animata.Core.Worlds;

namespace Animata.Core.WorldObjects;

// Humanoidy: sceny (ciała — projekty Humanoid i MuscleHumanoid, mózgi — HumanoidBrains i MuscleHumanoidBrains).
public static partial class WorldObjectCatalog
{
    /// <summary>
    /// Scena humanoidów: podłoga 30 × 30, kula 4 m dalej, dwa humanoidy z mózgiem ze stanami — jeden z ręcznym
    /// regulatorem stania i generatorem kroku, drugi z dwiema sieciami do nauczenia. Przesunięta kula blisko — stoją.
    /// </summary>
    public static DemoScene CreateHumanoidScene()
    {
        var world = FloorWorld(30, 30);
        var target = AddTarget(world, new Vector3(4, 0, 0));
        var hand = AddNamed(world, Create(Humanoid.Design, new Vector3(0, -1, 0), 0, target.Id, HumanoidBrains.HandPreset), "Humanoid");
        var neural = AddNamed(world, Create(Humanoid.Design, new Vector3(0, 1, 0), 0, target.Id, HumanoidBrains.NetworkPreset), "Humanoid NN");
        return new DemoScene(world, [hand, neural]);
    }

    /// <summary>
    /// Scena humanoida mięśniowego: podłoga, kula tuż przed nim (stoi), humanoid mięśniowy z automatem stanów (stanie
    /// i chód na mięśniach) i dla porównania humanoid na serwach z ręcznymi modułami. Kula odsunięta dalej niż ok. 1 m
    /// włącza chód — na mięśniach na razie kończy się upadkiem.
    /// </summary>
    public static DemoScene CreateMuscleHumanoidScene()
    {
        var world = FloorWorld(30, 30);
        var target = AddTarget(world, new Vector3(1, -1, 0));
        var muscled = AddNamed(world, Create(MuscleHumanoid.Design, new Vector3(0, -1, 0), 0, target.Id, MuscleHumanoidBrains.HandPreset),
            "Humanoid mięśniowy");
        var servo = AddNamed(world, Create(Humanoid.Design, new Vector3(0, 1, 0), 0, target.Id, HumanoidBrains.HandPreset), "Humanoid");
        return new DemoScene(world, [muscled, servo]);
    }

    /// <summary>Nadaje nazwę i dodaje do świata.</summary>
    private static T AddNamed<T>(World aWorld, T aEntity, string aName) where T : Entity
    {
        aEntity.Name = aName;
        aWorld.Add(aEntity);
        return aEntity;
    }
}
