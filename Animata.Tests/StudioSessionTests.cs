using System.Numerics;
using Animata.Core.Entities;
using Animata.Core.Persistence;
using Animata.Core.WorldObjects;
using Animata.Studio.Session;

namespace Animata.Tests;

/// <summary>Logika sceny Studia bez UI: zegar, wstawianie, gotowe mózgi, zmiany ciała w trakcie nauki, schowek, zapis.</summary>
public class StudioSessionTests
{
    [Fact]
    public void Tick_FollowsRealTime_AndCapsLongFrames()
    {
        using var session = new StudioSession("t", WorldObjectCatalog.CreateSnakeScene) { Visible = true };
        for (var frame = 0; frame < 10; frame++)
            session.Tick(0.05f); // 0.5 s w klatkach po 50 ms (wolniej niż 30 Hz)
        Assert.InRange(session.World.Time, 14.5 / 30, 15.5 / 30);

        var before = session.World.Time;
        session.Tick(1); // bardzo długa klatka: najwyżej MaxStepsPerTick kroków
        Assert.Equal(StudioSession.MaxStepsPerTick, (int)Math.Round((session.World.Time - before) * 30));
    }

    [Fact]
    public void Speed_ScalesSteps()
    {
        using var session = new StudioSession("t", WorldObjectCatalog.CreateSnakeScene) { Visible = true };
        session.CycleSpeed(); // 1 → 2
        session.CycleSpeed(); // 2 → 4
        Assert.Equal(4, session.Speed);
        for (var frame = 0; frame < 30; frame++)
            session.Tick(1f / 30);
        Assert.InRange(session.World.Time, 3.9, 4.1);
    }

    [Fact]
    public void EveryCreature_WithEveryPreset_Runs()
    {
        using var session = new StudioSession("t", WorldObjectCatalog.CreateSnakeScene) { Visible = true };
        foreach (var type in EntityTypes.Creatures.Where(aType => aType.Id is "car" or "cylinderCreature" or "snake" or "spider"))
        {
            var creature = session.AddCreature(type, new Vector3(2, 2, 0));
            foreach (var preset in creature.BrainPresets)
            {
                Assert.True(session.InstallBrain(creature, preset), session.Status);
                for (var frame = 0; frame < 10; frame++)
                    session.Tick();
                Assert.True(session.Error is null, $"{type.Name} / {preset.Name}: {session.Error}");
            }
        }
    }

    [Fact]
    public void ReshapingSettings_WhileTraining_KeepsTrainingInTheNewBody()
    {
        using var session = new StudioSession("t", WorldObjectCatalog.CreateSnakeScene) { Visible = true };
        var snake = (Creature)session.AddCreature(Snake.Design.Type, new Vector3(2, 2, 0));
        session.ToggleTraining([snake]);
        var segments = Settings.Find(snake, Snake.SegmentsSetting)!;
        foreach (var count in new[] { 5, 12, 2 })
        {
            Assert.True(session.ChangeSetting(snake, snake, segments, (double)count), session.Status);
            session.Tick();
            Assert.Equal(count, Snake.Segments(snake));
            Assert.True(session.IsTraining(snake));
        }
        Assert.Null(session.Error);
    }

    [Fact]
    public void CopyPaste_AndSaveLoad_RoundTrip()
    {
        using var session = new StudioSession("t", WorldObjectCatalog.CreateSnakeScene) { Visible = true };
        var count = session.World.Entities.Count;
        var copied = session.Copy(session.World.Entities.ToList());
        var pasted = session.Paste(new Vector3(10, 10, 0));
        Assert.Equal(copied, pasted.Count);
        Assert.Equal(count + copied, session.World.Entities.Count);
        for (var frame = 0; frame < 30; frame++)
            session.Tick();

        var json = WorldFile.ToJson(session.Save());
        session.Load(WorldFile.FromJson(json));
        Assert.Equal(json, WorldFile.ToJson(session.Save()));
    }

    [Fact]
    public void ToggleTraining_SaysWhenNothingCanLearn()
    {
        using var session = new StudioSession("t", WorldObjectCatalog.CreateSnakeScene);
        var car = session.AddCreature(Car.Design.Type, new Vector3(2, 2, 0));
        session.InstallBrain(car, car.BrainPresets.First(aPreset => aPreset.HandTuned));
        Assert.Contains("nie ruszyła", session.ToggleTraining([car]));
    }

    [Theory]
    [InlineData(0, "0 kul")]
    [InlineData(1, "1 kula")]
    [InlineData(3, "3 kule")]
    [InlineData(5, "5 kul")]
    [InlineData(12, "12 kul")]
    [InlineData(22, "22 kule")]
    public void Plural_FollowsPolishRules(int aCount, string aExpected) =>
        Assert.Equal(aExpected, StudioSession.Plural(aCount, "kula", "kule", "kul"));
}
