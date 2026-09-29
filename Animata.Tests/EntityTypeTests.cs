using System.Numerics;
using Animata.Core.Actuators;
using Animata.Core.Brains.Modules;
using Animata.Core.Entities;
using Animata.Core.Persistence;
using Animata.Core.Sensors;
using Animata.Core.WorldObjects;
using Animata.Core.Worlds;

namespace Animata.Tests;

/// <summary>Jedna zasada budowy obiektów: rejestr rodzajów, ustawienia, zapis formatu 4 i odczyt formatu 3.</summary>
public class EntityTypeTests
{
    private static string DataFile(string aName) => Path.Combine(AppContext.BaseDirectory, "Data", aName);

    [Fact]
    public void Registry_CreatesEveryKind_AndCreaturesComeEquippedWithAnEmptyBrain()
    {
        Assert.Equal(EntityTypes.All.Count, EntityTypes.All.Select(aType => aType.Id).Distinct().Count());
        foreach (var type in EntityTypes.All)
        {
            var entity = type.Create();
            Assert.Equal(type.ClrType, entity.GetType());
            Assert.Same(type, EntityTypes.Of(entity));
            if (entity is not ActiveEntity creature)
                continue;
            Assert.NotEmpty(creature.Body.Sensors);
            Assert.NotEmpty(creature.Body.Actuators);
            Assert.NotEmpty(creature.BrainPresets);
            Assert.NotNull(creature.TrainingRig);
            // Pusty mózg: same węzły ciała, bez sterownika.
            Assert.All(creature.Brain!.Graph.Modules, aModule => Assert.True(aModule is SensorModule or ActuatorModule));
            Assert.Equal(creature.Body.Sensors.Count + creature.Body.Actuators.Count, creature.Brain.Graph.Modules.Count);
        }
    }

    [Fact]
    public void EveryCreature_SavesAndLoads_WithEachOfItsBrains()
    {
        foreach (var type in EntityTypes.Creatures)
            foreach (var preset in ((ActiveEntity)type.Create()).BrainPresets)
            {
                using var world = new World();
                var creature = (ActiveEntity)type.Create();
                WorldObjectCatalog.InstallBrain(creature, preset);
                creature.Place(new Vector3(1, 2, 0), Quaternion.CreateFromAxisAngle(Vector3.UnitZ, 0.4f));
                world.Add(creature);
                var json = WorldFile.ToJson(WorldFile.Capture(world));
                using var restored = WorldFile.Restore(WorldFile.FromJson(json)).World;
                Assert.Equal(json, WorldFile.ToJson(WorldFile.Capture(restored)));
            }
    }

    [Fact]
    public void Settings_OfSlots_AreSavedAndCopied()
    {
        using var world = new World();
        var car = WorldObjectCatalog.CreateControllerCar(Vector3.Zero, 0, null, 9);
        car.Body.Sensors.OfType<RaySensor>().Single().Range = 2.25f;
        car.Body.Actuators.OfType<SteeringDriveActuator>().Single().MaxReverseSpeed = 0.5f;
        var spider = WorldObjectCatalog.CreateLearningSpider(new Vector3(3, 0, 0), 0, car.Id);
        spider.Body.Sensors.OfType<ClockSensor>().Single().Frequency = 1.8f;
        world.Add(car);
        world.Add(spider);

        using var restored = WorldFile.Restore(WorldFile.FromJson(WorldFile.ToJson(WorldFile.Capture(world)))).World;
        var twinCar = (CarCreature)restored.Find(car.Id)!;
        var twinSpider = (SpiderCreature)restored.Find(spider.Id)!;
        Assert.Equal(9, WorldObjectCatalog.WhiskerCountOf(twinCar));
        Assert.Equal(2.25f, twinCar.Body.Sensors.OfType<RaySensor>().Single().Range);
        Assert.Equal(0.5f, twinCar.Body.Actuators.OfType<SteeringDriveActuator>().Single().MaxReverseSpeed);
        Assert.Equal(1.8f, twinSpider.Body.Sensors.OfType<ClockSensor>().Single().Frequency);
        Assert.Equal(car.Id, twinSpider.Body.Sensors.OfType<TargetSensor>().Single().TargetId);

        // Kopia pająka razem z autkiem: oko przechodzi na kopię autka.
        var copies = WorldFile.RestoreCopies(WorldFile.CaptureEntities([car, spider]), Vector3.UnitX);
        var carCopy = copies.OfType<CarCreature>().Single();
        Assert.Equal(carCopy.Id, copies.OfType<SpiderCreature>().Single().Body.Sensors.OfType<TargetSensor>().Single().TargetId);
    }

    [Fact]
    public void Format3File_IsMigrated_WithEverySetting()
    {
        var document = WorldFile.FromJson(File.ReadAllText(DataFile("format3.animata.json")));
        Assert.Equal(WorldFile.Format, document.Format);
        using var world = WorldFile.Restore(document).World;
        var ball = world.Entities.OfType<Sphere>().Single();

        var car = world.Entities.OfType<CarCreature>().Single(aCar => aCar.Name == "Autko 7");
        Assert.Equal(7, WorldObjectCatalog.WhiskerCountOf(car));
        Assert.Equal(2.5f, car.Body.Sensors.OfType<RaySensor>().Single().Range);
        var steering = car.Body.Actuators.OfType<SteeringDriveActuator>().Single();
        Assert.Equal(3.1f, steering.MaxSpeed);
        Assert.Equal(4f, steering.DriveTorque);
        Assert.Equal(ball.Id, car.Body.Sensors.OfType<TargetSensor>().Single().TargetId);
        Assert.Single(car.Brain!.Graph.Modules.OfType<AvoidAndSeekModule>());
        Assert.Contains(car.Brain.Snapshots, aSnapshot => aSnapshot.Label == "ręczny 1");

        var disk = world.Entities.OfType<CylinderCreature>().Single();
        Assert.Equal(car.Id, disk.Body.Sensors.OfType<TargetSensor>().Single().TargetId);
        Assert.Equal(1.7f, disk.Body.Actuators.OfType<DiskDriveActuator>().Single().MaxTurnSpeed);

        var snake = world.Entities.OfType<SnakeCreature>().Single(aSnake => aSnake.Name == "Wąż 10");
        Assert.Equal(10, snake.Segments);
        Assert.Single(snake.Brain!.Graph.Modules.OfType<CpgModule>());
        var climber = world.Entities.OfType<SnakeCreature>().Single(aSnake => aSnake.Name == "Wspinacz");
        Assert.True(climber.Climber);
        Assert.True(climber.PartPositions[0].Z > 0.3f, "owinięty wąż wczytuje się owinięty (głowa nad ziemią)");

        Assert.Single(world.Entities.OfType<SpiderCreature>());
        Assert.True(world.Entities.OfType<Box>().Single().Locked);
        Assert.True(world.Time > 0);

        // Zapis w nowym formacie i odczyt dają to samo.
        var json = WorldFile.ToJson(WorldFile.Capture(world, "format4"));
        using var again = WorldFile.Restore(WorldFile.FromJson(json)).World;
        Assert.Equal(json, WorldFile.ToJson(WorldFile.Capture(again, "format4")));
    }

    [Fact]
    public void UnknownFormat_IsRejected()
    {
        Assert.Throws<NotSupportedException>(() => WorldFile.FromJson("""{ "Format": 2, "Name": "", "Time": 0, "Entities": [] }"""));
    }
}
