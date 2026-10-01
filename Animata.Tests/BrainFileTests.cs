using System.Numerics;
using Animata.Core.Brains;
using Animata.Core.Brains.Modules;
using Animata.Core.Entities;
using Animata.Core.Persistence;
using Animata.Core.WorldObjects;

namespace Animata.Tests;

/// <summary>Mózgi wymienne: gotowe mózgi dla ciała, zapis i odczyt mózgu z pliku, nazwy snapshotów.</summary>
public class BrainFileTests
{
    private static Creature NeuralSnake(int aSegments = Snake.DefaultSegments) =>
        WorldObjectCatalog.CreateNeuralSnake(Vector3.Zero, 0, null, aSegments);

    [Fact]
    public void EveryCreature_HasBrainPresets_ThatInstallAndValidate()
    {
        ActiveEntity[] creatures =
        [
            WorldObjectCatalog.CreateNeuralCar(Vector3.Zero, 0, null, 7),
            WorldObjectCatalog.CreateNeuralSeeker(Vector3.Zero, null),
            NeuralSnake(11),
            WorldObjectCatalog.CreateNeuralSpider(Vector3.Zero, 0, null)
        ];
        foreach (var creature in creatures)
        {
            var presets = creature.BrainPresets;
            Assert.True(presets.Count >= 2, creature.GetType().Name);
            foreach (var preset in presets)
            {
                var controller = WorldObjectCatalog.InstallBrain(creature, preset);
                var brain = creature.Brain!;
                brain.Graph.Validate();
                Assert.Same(controller, brain.Graph.Modules.Single(aModule => aModule is not SensorModule and not ActuatorModule));
                // Każdy port napędu jest sterowany przez nowy sterownik.
                var drive = brain.Graph.Modules.OfType<ActuatorModule>().Single();
                Assert.All(drive.InputPorts, aPort => Assert.Contains(brain.Graph.Connections,
                    aLink => aLink.TargetId == drive.Id && aLink.TargetPort == aPort && aLink.SourceId == controller.Id));
                Assert.Equal(preset.HandTuned ? 1 : 0, brain.Snapshots.Count);
            }
        }
    }

    [Fact]
    public void Snake_SwapsBetweenCpgAndNetwork_KeepingTheSameBodyAndBrainObject()
    {
        var snake = NeuralSnake();
        var brain = snake.Brain!;
        var cpg = snake.BrainPresets.First(aPreset => aPreset.Name.StartsWith("CPG"));
        WorldObjectCatalog.InstallBrain(snake, cpg);
        Assert.Same(brain, snake.Brain);
        Assert.Single(brain.Graph.Modules.OfType<CpgModule>());
        Assert.Empty(brain.Graph.Modules.OfType<NeuralNetworkModule>());
        Assert.Equal(snake.Body.Sensors.Count, brain.Graph.Modules.OfType<SensorModule>().Count());
    }

    [Fact]
    public void BrainFile_RoundTrip_GivesTheSameBrainInAnotherBody()
    {
        var source = NeuralSnake();
        source.Brain!.Capture("wagi A");
        var json = BrainFile.ToJson(BrainFile.Capture(source.Brain, "wąż"));

        var target = WorldObjectCatalog.CreateLearningSnake(new Vector3(3, 0, 0), 0, null);
        var report = BrainFile.Load(target.Brain!, BrainFile.FromJson(json));

        Assert.True(report.Fits);
        Assert.Equal(json, BrainFile.ToJson(BrainFile.Capture(target.Brain!, "wąż")));
        Assert.Equal("wagi A", Assert.Single(target.Brain!.Snapshots).Label);
    }

    [Fact]
    public void BrainFile_IntoADifferentBody_DropsWhatDoesNotFit()
    {
        var snake = NeuralSnake();
        var document = BrainFile.Capture(snake.Brain!, "wąż");
        var car = WorldObjectCatalog.CreateNeuralCar(Vector3.Zero, 0, null);

        var report = BrainFile.Load(car.Brain!, document);

        Assert.False(report.Fits);
        Assert.Contains("Spine", report.MissingSlots);
        Assert.Contains("Clock", report.MissingSlots);
        Assert.True(report.DroppedConnections > 0);
        car.Brain!.Graph.Validate();
    }

    [Fact]
    public void BrainFile_ForAShorterSnake_DropsOnlyTheMissingJoints()
    {
        var longSnake = NeuralSnake(10);
        var report = BrainFile.Load(NeuralSnake(8).Brain!, BrainFile.Capture(longSnake.Brain!));
        Assert.Empty(report.MissingSlots);
        // 2 stawy mniej: po Yaw i Pitch do kręgosłupa i po dwa porty czucia stawów, jeśli były podłączone.
        Assert.True(report.DroppedConnections >= 4);
    }

    [Fact]
    public void Snapshots_CanBeRenamed_InPlace()
    {
        var snake = NeuralSnake();
        var brain = snake.Brain!;
        var first = brain.Capture("a");
        brain.Capture("b");

        var renamed = brain.RenameSnapshot(first, "  pełzanie po płytach ");

        Assert.NotNull(renamed);
        Assert.Equal("pełzanie po płytach", brain.Snapshots[0].Label);
        Assert.Equal(first.Id, brain.Snapshots[0].Id);
        Assert.Same(first.Modules, brain.Snapshots[0].Modules);
        Assert.Throws<ArgumentException>(() => brain.RenameSnapshot(brain.Snapshots[0], " "));
        Assert.Null(brain.RenameSnapshot(first, "c"));
    }
}
