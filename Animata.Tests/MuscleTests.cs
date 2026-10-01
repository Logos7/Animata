using System.Numerics;
using Animata.Core.Actuators;
using Animata.Core.Bodies;
using Animata.Core.Brains.Modules;
using Animata.Core.Persistence;
using Animata.Core.Sensors;
using Animata.Core.Training;
using Animata.Core.WorldObjects;
using Animata.Core.Worlds;
using static Animata.Tests.TestWorlds;

namespace Animata.Tests;

/// <summary>Stawy bierne, mięśnie (model Hilla), humanoid mięśniowy i CMA-ES.</summary>
public class MuscleTests
{
    private static Creature Stand(World aWorld)
    {
        aWorld.Add(WorldObjectCatalog.CreateFloor(10, 10));
        // Bez napędów (new Creature) — pobudzenia zadaje test, a nie mózg.
        var humanoid = new Creature(MuscleHumanoid.Design);
        aWorld.Add(humanoid);
        return humanoid;
    }

    [Fact]
    public void MuscleCurves_PeakAtOptimalLength_AndFallWithShortening()
    {
        Assert.Equal(1, MusclePlan.ForceLength(1), 4);
        Assert.Equal(0.05f, MusclePlan.ForceLength(1 + MusclePlan.Width), 3);
        Assert.Equal(0.05f, MusclePlan.ForceLength(1 - MusclePlan.Width), 3);
        Assert.Equal(0, MusclePlan.Passive(0.9f));
        Assert.Equal(1, MusclePlan.Passive(1 + MusclePlan.Width), 4);
    }

    [Fact]
    public void Design_HasLegMuscles_ServosOnlyAboveTheWaist_AndSensesPassiveJoints()
    {
        var plan = MuscleHumanoid.DefaultPlan();
        Assert.Equal(24, plan.MuscleList.Count);
        Assert.All(new[] { 1, 2, 3, 4, 5, 6 }, aJoint => Assert.Equal(JointKind.Passive, plan.Joints[aJoint].Kind));
        Assert.DoesNotContain(JointPorts.Pitch(Humanoid.Knee(0)), MuscleHumanoid.ServoPorts);
        Assert.Contains(JointPorts.Pitch(Humanoid.Waist), MuscleHumanoid.ServoPorts);

        var humanoid = (Creature)MuscleHumanoid.Design.Type.Create();
        Assert.Equal(24, humanoid.MuscleCount);
        Assert.Contains(JointPorts.Pitch(Humanoid.Knee(0)), humanoid.Body.Sensors.OfType<JointSensor>().Single().OutputPorts);
        Assert.Equal(72, humanoid.Body.Sensors.OfType<MuscleSensor>().Single().OutputPorts.Count);
        Assert.Equal(MuscleHumanoid.MusclePorts, humanoid.Body.Actuators.OfType<MuscleActuator>().Single().InputPorts);
    }

    [Fact]
    public void Blueprint_KeepsMuscles_ThroughJson()
    {
        var blueprint = MuscleHumanoid.Design.Blueprint(new Dictionary<string, object?>());
        var copy = CreatureBlueprint.FromJson(blueprint.ToJson());
        Assert.Equal(blueprint.Plan.MuscleList, copy.Plan.MuscleList);
        Assert.Equal(blueprint.Plan.Joints, copy.Plan.Joints);
        Assert.Null(Humanoid.Design.Blueprint(new Dictionary<string, object?>()).Plan.Muscles);
        Assert.DoesNotContain("Muscles", Humanoid.Design.Blueprint(new Dictionary<string, object?>()).ToJson());
    }

    [Fact]
    public void MomentArms_HaveAnatomicalSigns()
    {
        var geometry = new MuscleGeometry(MuscleHumanoid.DefaultPlan());
        float Arm(string aMuscle, int aJoint, bool aYaw = false) =>
            geometry.Arms[MuscleHumanoid.MusclePorts.ToList().IndexOf(aMuscle), geometry.IndexOf(aJoint, aYaw)];
        // Pochylenie: ujemne — kończyna do przodu (zgięcie biodra), dodatnie kolano — zgięcie, dodatnia kostka — palce w dół.
        Assert.True(Arm("BiodrowyL", Humanoid.Hip(0)) < -0.03f);
        Assert.True(Arm("PośladkowyL", Humanoid.Hip(0)) > 0.03f);
        Assert.True(Arm("ObszernyL", Humanoid.Knee(0)) < -0.03f);
        Assert.True(Arm("KulszowyL", Humanoid.Hip(0)) > 0.03f && Arm("KulszowyL", Humanoid.Knee(0)) > 0.03f);
        Assert.True(Arm("ProstyUdaL", Humanoid.Hip(0)) < -0.03f && Arm("ProstyUdaL", Humanoid.Knee(0)) < -0.03f);
        Assert.True(Arm("PłaszczkowatyL", Humanoid.Ankle(0)) > 0.03f);
        Assert.True(Arm("PiszczelowyPL", Humanoid.Ankle(0)) < -0.03f);
        Assert.Equal(0, Arm("PłaszczkowatyL", Humanoid.Knee(0)));
        // Odwodziciel lewej nogi — dodatni skręt biodra, prawej — ujemny.
        Assert.True(Arm("PośladkowyŚrL", Humanoid.Hip(0), true) > 0.03f);
        Assert.True(Arm("PośladkowyŚrP", Humanoid.Hip(1), true) < -0.03f);
    }

    [Fact]
    public void PassiveJoints_StayWithinTheirLimits_WhenTheBodyCollapses()
    {
        using var world = new World();
        var humanoid = Stand(world);
        var plan = humanoid.Plan;
        for (var tick = 0; tick < 90; tick++)
        {
            if (tick == 2)
                humanoid.Push(new Vector3(1.5f, 0, 0));
            world.Update(Delta);
            foreach (var joint in new[] { Humanoid.Hip(0), Humanoid.Knee(0), Humanoid.Ankle(0), Humanoid.Knee(1) })
                Assert.InRange(humanoid.JointPitch(joint), plan.Joints[joint].PitchMin - 0.15f, plan.Joints[joint].MaxPitch + 0.15f);
        }
        Assert.True(humanoid.PartPositions[0].Z < Humanoid.FallenHeight, "bez mięśni nogi się składają");
    }

    [Theory]
    [InlineData("BiodrowyL", -0.5f)]     // zgina biodro: noga do przodu
    [InlineData("KulszowyL", 0.5f)]      // zgina kolano
    public void ExcitedMuscle_MovesItsJoint(string aMuscle, float aExpected)
    {
        using var world = new World();
        var humanoid = Stand(world);
        var muscle = MuscleHumanoid.MusclePorts.ToList().IndexOf(aMuscle);
        var joint = aMuscle == "BiodrowyL" ? Humanoid.Hip(0) : Humanoid.Knee(0);
        var extreme = 0f;
        for (var tick = 0; tick < 12; tick++)
        {
            humanoid.SetMuscleExcitation(muscle, 1);
            world.Update(Delta);
            extreme = aExpected < 0 ? MathF.Min(extreme, humanoid.JointPitch(joint)) : MathF.Max(extreme, humanoid.JointPitch(joint));
        }
        Assert.True(aExpected < 0 ? extreme < aExpected : extreme > aExpected, $"{aMuscle}: {extreme}");
        Assert.True(humanoid.MuscleActivation(muscle) > 0.9f);
    }

    [Fact]
    public void Vasti_StraightenACrouch_ThatALimpBodySinksInto()
    {
        float Knee(bool aExcite)
        {
            using var world = new World();
            world.Add(WorldObjectCatalog.CreateFloor(10, 10));
            var humanoid = new Creature(MuscleHumanoid.Design);
            humanoid.PlaceBent(Vector3.Zero, Quaternion.Identity, aJoint =>
                aJoint == Humanoid.Knee(0) || aJoint == Humanoid.Knee(1) ? (0, 0.6f)
                : aJoint == Humanoid.Hip(0) || aJoint == Humanoid.Hip(1) || aJoint == Humanoid.Ankle(0) || aJoint == Humanoid.Ankle(1) ? (0, -0.3f)
                : (0, 0));
            world.Add(humanoid);
            for (var tick = 0; tick < 20; tick++)
            {
                if (aExcite)
                    for (var side = 0; side < 2; side++)
                        humanoid.SetMuscleExcitation(MuscleHumanoid.MusclePorts.ToList().IndexOf(MuscleHumanoid.Muscle("Obszerny", side)), 1);
                world.Update(Delta);
            }
            return humanoid.JointPitch(Humanoid.Knee(0));
        }
        var limp = Knee(false);
        var straightened = Knee(true);
        Assert.True(limp > 0.6f, $"luźne kolano {limp}");
        Assert.True(straightened < 0.3f, $"kolano z mięśniem obszernym {straightened}");
    }

    [Fact]
    public void MuscleStand_StandsStillForTenSeconds()
    {
        using var world = new World();
        world.Add(WorldObjectCatalog.CreateFloor(10, 10));
        var humanoid = WorldObjectCatalog.Create(MuscleHumanoid.Design, Vector3.Zero, 0, null, new MuscleStandModule().WithParameters(MuscleHumanoidBrains.HandStand));
        world.Add(humanoid);
        for (var tick = 0; tick < 300; tick++)
        {
            world.Update(Delta);
            Assert.True(Humanoid.Posture(humanoid) < 1, $"upadł w {tick * Delta:0.0} s");
        }
        Assert.True(new Vector2(humanoid.PartPositions[0].X, humanoid.PartPositions[0].Y).Length() < 0.3f);
    }

    [Fact]
    public void Scene_MuscleHumanoidStandsByTheBall()
    {
        var scene = WorldObjectCatalog.CreateMuscleHumanoidScene();
        using var world = scene.World;
        var humanoid = (Creature)world.Entities.Single(aEntity => aEntity.Name == "Humanoid mięśniowy");
        for (var tick = 0; tick < 150; tick++)
            world.Update(Delta);
        Assert.Equal(HumanoidBrains.StandState, humanoid.Brain!.Graph.Modules.OfType<StateMachineModule>().Single().CurrentName);
        Assert.Equal(0, Humanoid.Posture(humanoid));
    }

    [Fact]
    public void StateBrain_Installs_AndSurvivesSaveAndLoad()
    {
        var humanoid = WorldObjectCatalog.Create(MuscleHumanoid.Design, Vector3.Zero, 0, null, MuscleHumanoidBrains.HandPreset);
        var graph = humanoid.Brain!.Graph;
        Assert.Single(graph.Modules.OfType<MuscleStandModule>());
        Assert.Single(graph.Modules.OfType<MuscleGaitModule>());
        Assert.Equal(MuscleHumanoid.MusclePorts, graph.Modules.OfType<StateMachineModule>().Single().Ports);
        graph.Validate();
        var json = BrainFile.ToJson(BrainFile.Capture(humanoid.Brain, "mięśnie"));
        var copy = WorldObjectCatalog.Create(MuscleHumanoid.Design, Vector3.Zero, 0, null, MuscleHumanoidBrains.NetworkPreset);
        BrainFile.Load(copy.Brain!, BrainFile.FromJson(json));
        Assert.Equal(json, BrainFile.ToJson(BrainFile.Capture(copy.Brain!, "mięśnie")));
    }

    [Fact]
    public void Rigs_TrainStandingAndWalking_Separately()
    {
        var humanoid = WorldObjectCatalog.Create(MuscleHumanoid.Design, Vector3.Zero, 0, null, MuscleHumanoidBrains.HandPreset);
        var stand = humanoid.Brain!.Graph.Modules.OfType<MuscleStandModule>().Single();
        var walk = humanoid.Brain.Graph.Modules.OfType<MuscleGaitModule>().Single();
        Assert.Same(SeekRigs.MuscleStand, SeekRigs.For(humanoid, stand));
        Assert.Same(SeekRigs.MuscleWalk, SeekRigs.For(humanoid, walk));
        Assert.True(SeekRigs.MuscleStand.Algorithm == EvolutionAlgorithm.CmaEs);
    }

    [Theory]
    [InlineData(8)]
    [InlineData(80)]   // powyżej FullCovarianceLimit — tylko przekątna
    public void CmaEs_FindsTheMinimumOfAStretchedBowl(int aDimensions)
    {
        // f = −Σ sᵢ (xᵢ − 1)², skale sᵢ od 1 do 100 — źle uwarunkowana miska, start w zerze.
        float Fitness(float[] aX, int _)
        {
            var sum = 0f;
            for (var i = 0; i < aX.Length; i++)
            {
                var scale = MathF.Pow(100, i / (float)(aX.Length - 1));
                sum += scale * (aX[i] - 1) * (aX[i] - 1);
            }
            return -sum;
        }
        var evolution = Evolution.Create(new float[aDimensions], new EvolutionOptions
        {
            Algorithm = EvolutionAlgorithm.CmaEs, Seed = 2, MutationSigma = 0.5f, PopulationSize = 16, MaxParallelism = 1
        });
        var start = Fitness(new float[aDimensions], 0);
        for (var generation = 0; generation < (aDimensions < 20 ? 150 : 400); generation++)
            evolution.NextGeneration(Fitness);
        Assert.True(evolution.BestFitness > start * 1e-3f, $"{start} → {evolution.BestFitness}");
        Assert.All(evolution.Best, aValue => Assert.InRange(aValue, 0.8f, 1.2f));
    }
}
