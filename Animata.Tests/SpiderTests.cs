using System.Numerics;
using Animata.Core.Brains.Modules;
using Animata.Core.Persistence;
using Animata.Core.Sensors;
using Animata.Core.Training;
using Animata.Core.WorldObjects;
using Animata.Core.Worlds;

namespace Animata.Tests;

/// <summary>Pająk (czworonóg), generator kłusa, samokolizje stworów i czucie terenu.</summary>
public class SpiderTests
{
    private const float Delta = 1f / 30f;

    private static Creature Walk(World aWorld, Vector3 aTarget, float aSeconds)
    {
        aWorld.Add(WorldObjectCatalog.CreateFloor(30, 30));
        var target = WorldObjectCatalog.CreateSphere(aTarget);
        aWorld.Add(target);
        var spider = WorldObjectCatalog.CreateSpider(Vector3.Zero, 0, Vector3.One, target.Id, new GaitModule());
        aWorld.Add(spider);
        for (var tick = 0; tick < aSeconds / Delta; tick++)
            aWorld.Update(Delta);
        return spider;
    }

    [Fact]
    public void Spider_StandsOnItsLegs()
    {
        using var world = new World();
        world.Add(WorldObjectCatalog.CreateFloor(10, 10));
        var spider = new Creature(Spider.Design);
        world.Add(spider);
        for (var tick = 0; tick < 90; tick++)
            world.Update(Delta);
        Assert.InRange(spider.PartPositions[0].Z, 0.11f, Spider.BodyHeight);
        Assert.False(spider.IsPartTouching(0));
        Assert.Equal(12, spider.JointCount);
    }

    [Fact]
    public void Lift_RaisesTheLeg_AndSwingMovesItForward()
    {
        using var world = new World();
        world.Add(WorldObjectCatalog.CreateFloor(10, 10));
        var spider = new Creature(Spider.Design);
        world.Add(spider);
        for (var tick = 0; tick < 30; tick++)
            world.Update(Delta);
        var shin = spider.Plan.IndexOf("GoleńPP");
        var before = spider.PartPositions[shin].Z - spider.PartPositions[0].Z;
        spider.SetJointTarget(Spider.LiftJoint(1), 0, -1);   // udo przedniej prawej nogi w górę
        for (var tick = 0; tick < 20; tick++)
            world.Update(Delta);
        var raised = spider.PartPositions[shin].Z - spider.PartPositions[0].Z;
        Assert.True(raised > before + 0.02f, $"{before} → {raised}");
        var lifted = spider.PartPositions[shin].X - spider.PartPositions[0].X;
        spider.SetJointTarget(Spider.SwingJoint(1), 1, 0);   // i zamach: prawa noga, dodatni skręt → do przodu
        for (var tick = 0; tick < 20; tick++)
            world.Update(Delta);
        var swung = spider.PartPositions[shin].X - spider.PartPositions[0].X;
        Assert.True(swung > lifted + 0.05f, $"{lifted} → {swung}, {spider.JointYaw(Spider.SwingJoint(1))}");
    }

    [Fact]
    public void HandGait_WalksToTheTarget()
    {
        using var world = new World();
        var spider = Walk(world, new Vector3(4, 0, 0), 10);
        Assert.True(spider.Body.Position.X > 2.5f, $"x = {spider.Body.Position.X}");
        Assert.True(Vector3.Transform(Vector3.UnitZ, spider.PartOrientations[0]).Z > 0.8f);
    }

    [Fact]
    public void SpiderScene_SavesAndLoads()
    {
        using var world = WorldObjectCatalog.CreateSpiderScene().World;
        var json = WorldFile.ToJson(WorldFile.Capture(world, "t"));
        using var restored = WorldFile.Restore(WorldFile.FromJson(json)).World;
        Assert.Equal(json, WorldFile.ToJson(WorldFile.Capture(restored, "t")));
        Assert.Equal(2, restored.Entities.OfDesign(Spider.Design).Count());
        Assert.Single(restored.Entities.OfDesign(Spider.Design).SelectMany(aSpider => aSpider.Brain!.Graph.Modules.OfType<GaitModule>()));
        Assert.Same(SeekRigs.Spider, SeekRigs.For(restored.Entities.OfDesign(Spider.Design).First()));
        restored.Update(Delta);
    }

    [Fact]
    public void Touch_FeelsFeetOnTheGround_AndBellyWhenLyingDown()
    {
        using var world = new World();
        world.Add(WorldObjectCatalog.CreateFloor(10, 10));
        var spider = WorldObjectCatalog.CreateSpider(Vector3.Zero, 0, Vector3.One, null, new GaitModule { Stride = 0, Lift = 0, KneeSwing = 0 });
        world.Add(spider);
        for (var tick = 0; tick < 30; tick++)
            world.Update(Delta);
        var touch = spider.Body.Sensors.OfType<TouchSensor>().Single().Read(spider, world);
        Assert.All(WorldObjectCatalog.SpiderTouchPorts.Take(4), aPort => Assert.Equal(1f, touch[aPort]));
        Assert.Equal(0f, touch["Belly"]);
        Assert.Equal(0f, SeekRigs.SpiderPosture(spider));

        // Na grzbiecie: brzuch (tułów) dotyka ziemi, postawa najgorsza.
        spider.Place(new Vector3(0, 0, 0.1f), Quaternion.CreateFromAxisAngle(Vector3.UnitX, MathF.PI));
        for (var tick = 0; tick < 30; tick++)
            world.Update(Delta);
        Assert.Equal(1f, spider.Body.Sensors.OfType<TouchSensor>().Single().Read(spider, world)["Belly"]);
        Assert.Equal(1f, SeekRigs.SpiderPosture(spider));
        var feel = spider.Body.Sensors.OfType<FeelSensor>().Single().Read(spider, world);
        Assert.True(feel.ContainsKey(FeelSensor.HeadRollPort));
    }

    [Fact]
    public void HandGait_KeepsItsBellyOffTheGround()
    {
        var rig = SeekRigs.Spider;
        var results = SeekTargetTask.Run(new GaitModule(), SeekTargetTask.CreateValidationEpisodes(rig.DefaultOptions).Take(3).ToList(),
            rig.DefaultOptions, rig);
        Assert.All(results, aResult => Assert.True(aResult.PostureTime < 0.1f * rig.DefaultOptions.EpisodeSeconds, $"{aResult.PostureTime} s"));
    }

    [Fact]
    public void Snake_DoesNotPassThroughItself()
    {
        using var world = new World();
        world.Add(WorldObjectCatalog.CreateFloor(20, 20));
        var snake = TestWorlds.BareSnake(12);
        world.Add(snake);
        for (var joint = 0; joint < snake.JointCount; joint++)
            snake.SetJointTarget(joint, 1, 0);   // wszystkie stawy w lewo do oporu — zwój ciaśniejszy niż pełne koło
        for (var tick = 0; tick < 120; tick++)
            world.Update(Delta);
        var closest = float.PositiveInfinity;
        for (var a = 0; a < snake.PartPositions.Count; a++)
            for (var b = a + 3; b < snake.PartPositions.Count; b++)
                closest = MathF.Min(closest, Vector3.Distance(snake.PartPositions[a], snake.PartPositions[b]));
        Assert.True(closest > Snake.SegmentRadius * 1.6f, $"closest = {closest}");
    }

    [Fact]
    public void Feel_ReportsAStepAheadOfTheHead()
    {
        using var world = new World();
        world.Add(WorldObjectCatalog.CreateFloor(20, 20));
        world.Add(WorldObjectCatalog.CreateBox(new Vector3(1.2f, 0, 0), new Vector3(1, 2, 0.08f)));
        var snake = WorldObjectCatalog.CreateNeuralSnake(new Vector3(0.5f, 0, 0), 0, null, 4);
        world.Add(snake);
        world.Update(Delta);
        var feel = snake.Body.Sensors.OfType<FeelSensor>().Single().Read(snake, world);
        Assert.Equal(0.8f, feel[FeelSensor.AheadPort], 1e-3f);
        Assert.InRange(feel[FeelSensor.TouchPort], 0.5f, 1);
        var network = snake.Brain!.Graph.Modules.OfType<NeuralNetworkModule>().Single();
        Assert.Contains(FeelSensor.AheadPort, network.Ports);
    }
}

/// <summary>Noga pająka z trzech członów: kolano zgina się tylko w jedną stronę.</summary>
public class SpiderLegTests
{
    private const float Delta = 1f / 30f;

    [Fact]
    public void Knee_BendsOnlyOneWay()
    {
        using var world = new World();
        world.Add(WorldObjectCatalog.CreateFloor(10, 10));
        var spider = new Creature(Spider.Design);
        world.Add(spider);
        for (var tick = 0; tick < 30; tick++)
            world.Update(Delta);
        var knee = Spider.KneeJoint(1);
        var shin = spider.Plan.IndexOf("GoleńPP");
        float Reach() => Vector2.Distance(new Vector2(spider.PartPositions[shin].X, spider.PartPositions[shin].Y),
            new Vector2(spider.PartPositions[0].X, spider.PartPositions[0].Y));
        var rest = Reach();

        spider.SetJointTarget(knee, 0, 1);    // zgięcie: stopa pod tułów
        for (var tick = 0; tick < 20; tick++)
            world.Update(Delta);
        Assert.InRange(spider.JointPitch(knee), Spider.KneeFlex - 0.15f, Spider.KneeFlex + 0.05f);
        Assert.True(Reach() < rest - 0.05f, $"{rest} → {Reach()}");

        spider.SetJointTarget(knee, 0, -1);   // „wyprost wstecz”: kolano zostaje na granicy
        for (var tick = 0; tick < 20; tick++)
            world.Update(Delta);
        Assert.InRange(spider.JointPitch(knee), -0.05f, 0.05f);
    }

    [Fact]
    public void LegJoints_ExposeOnlyTheirMovingAxes()
    {
        var spider = (Creature)Spider.Design.Type.Create();
        var legs = spider.Body.FindActuator("Legs")!;
        Assert.Equal(Spider.Joints, legs.InputPorts.Count);
        Assert.Contains("Yaw0", legs.InputPorts);         // zamach: skręt
        Assert.DoesNotContain("Pitch0", legs.InputPorts);  // zamach nie ma pochylenia
        Assert.DoesNotContain("Yaw2", legs.InputPorts);    // kolano nie ma skrętu
        Assert.Equal(legs.InputPorts, spider.Body.FindSensor("Joints")!.OutputPorts);
        Assert.Equal(legs.InputPorts.OrderBy(aPort => aPort), GaitModule.Outputs.OrderBy(aPort => aPort));
    }
}
