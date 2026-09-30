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
            Assert.NotNull(Animata.Core.Training.SeekRigs.For(creature));
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
        var twinCar = (Creature)restored.Find(car.Id)!;
        var twinSpider = (Creature)restored.Find(spider.Id)!;
        Assert.Equal(9, WorldObjectCatalog.WhiskerCountOf(twinCar));
        Assert.Equal(2.25f, twinCar.Body.Sensors.OfType<RaySensor>().Single().Range);
        Assert.Equal(0.5f, twinCar.Body.Actuators.OfType<SteeringDriveActuator>().Single().MaxReverseSpeed);
        Assert.Equal(1.8f, twinSpider.Body.Sensors.OfType<ClockSensor>().Single().Frequency);
        Assert.Equal(car.Id, twinSpider.Body.Sensors.OfType<TargetSensor>().Single().TargetId);

        // Kopia pająka razem z autkiem: oko przechodzi na kopię autka.
        var copies = WorldFile.RestoreCopies(WorldFile.CaptureEntities([car, spider]), Vector3.UnitX);
        var carCopy = copies.OfDesign(Car.Design).Single();
        Assert.Equal(carCopy.Id, copies.OfDesign(Spider.Design).Single().Body.Sensors.OfType<TargetSensor>().Single().TargetId);
    }

    [Fact]
    public void Format3File_IsMigrated_WithEverySetting()
    {
        var document = WorldFile.FromJson(File.ReadAllText(DataFile("format3.animata.json")));
        Assert.Equal(WorldFile.Format, document.Format);
        using var world = WorldFile.Restore(document).World;
        var ball = world.Entities.OfType<Sphere>().Single();

        var car = world.Entities.OfDesign(Car.Design).Single(aCar => aCar.Name == "Autko 7");
        Assert.Equal(7, WorldObjectCatalog.WhiskerCountOf(car));
        Assert.Equal(2.5f, car.Body.Sensors.OfType<RaySensor>().Single().Range);
        var steering = car.Body.Actuators.OfType<SteeringDriveActuator>().Single();
        Assert.Equal(3.1f, steering.MaxSpeed);
        Assert.Equal(4f, steering.DriveTorque);
        Assert.Equal(ball.Id, car.Body.Sensors.OfType<TargetSensor>().Single().TargetId);
        Assert.Single(car.Brain!.Graph.Modules.OfType<AvoidAndSeekModule>());
        Assert.Contains(car.Brain.Snapshots, aSnapshot => aSnapshot.Label == "ręczny 1");

        var disk = world.Entities.OfDesign(Disc.Design).Single();
        Assert.Equal(car.Id, disk.Body.Sensors.OfType<TargetSensor>().Single().TargetId);
        Assert.Equal(1.7f, disk.Body.Actuators.OfType<DiskDriveActuator>().Single().MaxTurnSpeed);

        var snake = world.Entities.OfDesign(Snake.Design).Single(aSnake => aSnake.Name == "Wąż 10");
        Assert.Equal(10, Snake.Segments(snake));
        Assert.Single(snake.Brain!.Graph.Modules.OfType<CpgModule>());
        var climber = world.Entities.OfDesign(Snake.Design).Single(aSnake => aSnake.Name == "Wspinacz");
        Assert.True(Snake.IsClimber(climber));
        Assert.True(climber.PartPositions[0].Z > 0.3f, "owinięty wąż wczytuje się owinięty (głowa nad ziemią)");

        Assert.Single(world.Entities.OfDesign(Spider.Design));
        Assert.True(world.Entities.OfType<Box>().Single().Locked);
        Assert.True(world.Time > 0);

        // Zapis w nowym formacie i odczyt dają to samo.
        var json = WorldFile.ToJson(WorldFile.Capture(world, "format4"));
        using var again = WorldFile.Restore(WorldFile.FromJson(json)).World;
        Assert.Equal(json, WorldFile.ToJson(WorldFile.Capture(again, "format4")));
    }

    [Fact]
    public void Format4File_And_Format1Brain_AreMigrated_ModulesBecomeStates()
    {
        var json4 = File.ReadAllText(DataFile("format4.animata.json"));
        using var world = WorldFile.Restore(WorldFile.FromJson(json4)).World;
        var car = world.Entities.OfDesign(Car.Design).Single();
        var graph = car.Brain!.Graph;
        Assert.Equal(0.25f, graph.Modules.OfType<ConstantModule>().Single().Value);
        Assert.Equal("Bias", graph.Modules.OfType<ConstantModule>().Single().Port);
        var composite = graph.Modules.OfType<CompositeModule>().Single();
        var router = composite.Children.OfType<RouterModule>().Single();
        Assert.Equal(2, router.Channels);
        Assert.Equal(1.5f, composite.Children.OfType<ConstantModule>().Single().Value);
        Assert.Single(graph.Positions, aEntry => aEntry.Value == new Vector2(10, 20));
        var snapshot = Assert.Single(car.Brain.Snapshots);
        Assert.True(car.Brain.Matches(snapshot));

        // W nowym formacie nie ma już osobnych rekordów stałej i routera, a zapis → odczyt daje ten sam plik.
        var json5 = WorldFile.ToJson(WorldFile.Capture(world, "format5"));
        Assert.Contains("\"$type\": \"router\"", json5);
        using var again = WorldFile.Restore(WorldFile.FromJson(json5)).World;
        Assert.Equal(json5, WorldFile.ToJson(WorldFile.Capture(again, "format5")));

        // Plik mózgu formatu 1 wczytuje się do nowego autka tak samo.
        var fresh = WorldObjectCatalog.CreateControllerCar(Vector3.Zero, 0, null);
        var report = BrainFile.Load(fresh.Brain!, BrainFile.FromJson(File.ReadAllText(DataFile("format1.brain.json"))));
        Assert.True(report.Fits);
        Assert.Equal(WorldFile.ToJson(new WorldDocument(WorldFile.Format, "", 0, WorldFile.CaptureEntities([car])))
                .Replace(car.Id.ToString(), "-").Split("\"Brain\"")[1],
            WorldFile.ToJson(new WorldDocument(WorldFile.Format, "", 0, WorldFile.CaptureEntities([fresh])))
                .Replace(fresh.Id.ToString(), "-").Split("\"Brain\"")[1]);
    }

    [Fact]
    public void UnknownFormat_IsRejected()
    {
        Assert.Throws<NotSupportedException>(() => WorldFile.FromJson("""{ "Format": 2, "Name": "", "Time": 0, "Entities": [] }"""));
    }
}

/// <summary>
/// Nowy stwór od zera bez żadnej klasy — sam projekt i jedna linijka rejestracji: toczek, klocek na dwóch kołach z okiem.
/// Ciało przechodzi przez plik JSON projektu (jak stwór zrobiony poza kodem). Zapis, kopiowanie, wymiana mózgu i nauka
/// działają bez żadnej zmiany w zapisie świata ani w nauce.
/// </summary>
public static class Roller
{
    public const string MoodSetting = "Mood";

    public static Animata.Core.Bodies.BodyPlan DefaultPlan()
    {
        var builder = new Animata.Core.Bodies.BodyPlanBuilder()
            .Part(new Animata.Core.Bodies.PartPlan("Kadłub", Animata.Core.Bodies.PartShape.Box, new Vector3(0.5f, 0.3f, 0.15f), 1,
                new Vector3(0, 0, 0.2f), Quaternion.Identity, 0.3f));
        foreach (var (name, side) in new[] { ("Koło L", 1f), ("Koło P", -1f) })
        {
            builder.Part(new Animata.Core.Bodies.PartPlan(name, Animata.Core.Bodies.PartShape.Cylinder, new Vector3(0.12f, 0.06f, 0), 0.2f,
                new Vector3(0, side * 0.22f, 0.12f), Quaternion.Identity, 1.2f));
            builder.Wheel($"Oś {name}", "Kadłub", name, aSteerable: false, aDriven: true, aTorque: 2);
        }
        return builder.Build();
    }

    /// <summary>Projekt ciała zapisany i wczytany z JSON — tak jak stwór opisany w pliku.</summary>
    public static CreatureBlueprint Blueprint { get; } = CreatureBlueprint.FromJson(new CreatureBlueprint(DefaultPlan(),
        [new SlotSpec("Eye", nameof(TargetSensor))], [new SlotSpec("Wheels", nameof(DiskDriveActuator))]).ToJson());

    public static CreatureDesign Design { get; } = new()
    {
        Id = "roller",
        Name = "Toczek",
        Icon = "wheel",
        Blueprint = _ => Blueprint,
        Settings = [DesignSetting.Value(MoodSetting, 0.5f, new SettingAttribute("Nastrój") { Min = 0, Max = 1 })],
        Presets = _ => [new("Sterownik celu", "skręca do celu i jedzie", () => new ApproachTargetModule { Name = "Approach" }, true)]
    };
}

public class NewCreatureTests
{
    static NewCreatureTests()
    {
        if (EntityTypes.Find("roller") is null)
            EntityTypes.Register(Roller.Design.Type);
    }

    [Fact]
    public void NewCreature_NeedsOnlyADesignAndARegistryLine()
    {
        using var world = new World();
        world.Add(WorldObjectCatalog.CreateFloor(20, 20));
        var ball = WorldObjectCatalog.CreateSphere(new Vector3(4, 0, 0));
        world.Add(ball);
        var roller = (Creature)EntityTypes.Find("roller")!.Create();
        WorldObjectCatalog.InstallBrain(roller, roller.BrainPresets[0]);
        WorldObjectCatalog.Aim(roller, ball.Id);
        roller.SetValue(Roller.MoodSetting, 0.9f);
        roller.Body.Actuators.OfType<DiskDriveActuator>().Single().MaxSpeed = 1.5f;
        roller.Place(new Vector3(-1, 0, 0), Quaternion.Identity);
        world.Add(roller);
        for (var tick = 0; tick < 30; tick++)
            world.Update(1f / 30);

        // Zapis i odczyt: rodzaj, ustawienia stwora i napędu, cel, mózg.
        var json = WorldFile.ToJson(WorldFile.Capture(world));
        using var restored = WorldFile.Restore(WorldFile.FromJson(json)).World;
        var twin = (Creature)restored.Find(roller.Id)!;
        Assert.Same(Roller.Design, twin.Design);
        Assert.Equal(0.9f, twin.Value<float>(Roller.MoodSetting));
        Assert.Equal(1.5f, twin.Body.Actuators.OfType<DiskDriveActuator>().Single().MaxSpeed);
        Assert.Equal(ball.Id, twin.Body.Sensors.OfType<TargetSensor>().Single().TargetId);
        Assert.Single(twin.Brain!.Graph.Modules.OfType<ApproachTargetModule>());
        Assert.Equal(json, WorldFile.ToJson(WorldFile.Capture(restored)));

        // Nauka: ogólny rig z rejestru buduje toczka z podanym sterownikiem i próba się odbywa.
        var rig = Animata.Core.Training.SeekRigs.For(roller);
        var result = Animata.Core.Training.SeekTargetTask.Run(new ApproachTargetModule(),
            Animata.Core.Training.SeekTargetTask.CreateEpisodes(rig.DefaultOptions with { EpisodesPerGeneration = 1, EpisodeSeconds = 2 }, 0),
            rig.DefaultOptions with { EpisodeSeconds = 2 }, rig);
        Assert.Single(result);
        Assert.True(float.IsFinite(result[0].Cost));
    }
}

/// <summary>Stwory to projekty: jedna klasa, ciało jako dane, które przechodzi przez JSON bez strat.</summary>
public class CreatureDesignTests
{
    [Fact]
    public void EveryBuiltInCreature_IsOneClass_WithItsOwnDesign()
    {
        var creatures = EntityTypes.Creatures.Where(aType => aType.Id is "car" or "cylinderCreature" or "snake" or "spider")
            .Select(aType => aType.Create()).ToList();
        Assert.Equal(4, creatures.Count);
        Assert.All(creatures, aCreature => Assert.IsType<Creature>(aCreature));
        Assert.Equal(4, creatures.Cast<Creature>().Select(aCreature => aCreature.Design).Distinct().Count());
    }

    [Fact]
    public void Blueprints_SurviveJson()
    {
        foreach (var design in new[] { Car.Design, Disc.Design, Snake.Design, Spider.Design })
        {
            var blueprint = new Creature(design).Blueprint();
            var json = blueprint.ToJson();
            var again = CreatureBlueprint.FromJson(json);
            Assert.Equal(json, again.ToJson());
            Assert.Equal(blueprint.Plan.Parts, again.Plan.Parts);
            Assert.Equal(blueprint.Plan.Joints, again.Plan.Joints);
        }
    }

    [Fact]
    public void DesignFromAJsonBlueprint_GetsAGeneralNetwork_ThatDrivesEveryActuator()
    {
        var design = CreatureDesign.FromBlueprint("jsonSpider", "Pająk z pliku", CreatureBlueprint.FromJson(new Creature(Spider.Design).Blueprint().ToJson()));
        var creature = (Creature)design.Type.Create();
        WorldObjectCatalog.InstallBrain(creature, creature.BrainPresets[0]);
        var network = creature.Brain!.Graph.Modules.OfType<NeuralNetworkModule>().Single();
        Assert.Equal(creature.Body.Actuators.SelectMany(aActuator => aActuator.InputPorts), network.OutputPorts);

        using var world = new World();
        world.Add(WorldObjectCatalog.CreateFloor(10, 10));
        world.Add(creature);
        for (var tick = 0; tick < 10; tick++)
            world.Update(1f / 30);
    }

    [Fact]
    public void UnknownSlotType_IsReported()
    {
        var blueprint = new CreatureBlueprint(Roller.DefaultPlan(), [new SlotSpec("Eye", "NoSuchSensor")], []);
        var design = CreatureDesign.FromBlueprint("broken", "Zepsuty", blueprint);
        Assert.Throws<NotSupportedException>(() => design.Type.Create());
    }
}
