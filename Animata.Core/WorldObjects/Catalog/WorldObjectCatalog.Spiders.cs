using System.Numerics;
using Animata.Core.Actuators;
using Animata.Core.Brains;
using Animata.Core.Brains.Modules;
using Animata.Core.Brains.Neural;
using Animata.Core.Sensors;

namespace Animata.Core.WorldObjects;

// Pająki: czworonóg z chodem (kłus) albo z siecią.
public static partial class WorldObjectCatalog
{

    /// <summary>
    /// Pająk: oko „Eye” (tułów), czucie stawów „Joints”, zegar „Clock”, czucie terenu „Feel”, dotyk „Touch”, nogi „Legs”
    /// (12 osi: zamach, uniesienie, kolano × 4). Mózg sensory → controller → nogi; controller musi mieć wyjścia wszystkich
    /// 12 portów nóg (np. <see cref="GaitModule"/>).
    /// </summary>
    public static Creature CreateSpider(Vector3 aPosition, float aYaw, Vector3 aColor, Guid? aTargetId, BrainModule aController)
    {
        return new Spawn(Spider.Design.Type)
        {
            Settings = Spawn.Configure<Creature>(aSpider => aSpider.Color = aColor),
            Slots = Spawn.Aim(aTargetId),
            Brain = Spawn.Controller(aController),
            Pose = Spawn.At(aPosition, aYaw)
        }.Build<Creature>();
    }

    /// <summary>Pająk z generatorem kłusa: ręczne parametry jako snapshot „ręczne parametry”, chód wylosowany.</summary>
    public static Creature CreateLearningSpider(Vector3 aPosition, float aYaw, Guid? aTargetId)
    {
        var gait = new GaitModule { Name = "Chód" };
        var spider = CreateSpider(aPosition, aYaw, RandomColor(), aTargetId, gait);
        spider.Brain!.Capture("ręczne parametry", gait);
        gait.Randomize();
        return spider;
    }

    /// <summary>
    /// Sieć pająka: wejścia Sin, Cos (zegar), kierunek do celu (bok, przód), szczelina/4, a z <paramref name="aSenses"/> —
    /// dotyk czterech stóp i brzucha oraz pochylenie i przechył tułowia (równowaga); warstwa ukryta 10; wyjścia — po trzy
    /// na nogę: zamach (Yaw), uniesienie i kolano (Pitch). Wagi losowe.
    /// </summary>
    public static NeuralNetworkModule CreateSpiderNeuralModule(bool aSenses = false, params int[] aHidden)
    {
        string[] basic = [ClockSensor.SinPort, ClockSensor.CosPort, "Found * DirectionY", "Found * DirectionX", "Found * Gap / 4"];
        string[] senses = aSenses ? [.. Spider.TouchPorts, FeelSensor.HeadPitchPort, FeelSensor.HeadRollPort] : [];
        string[] inputs = [.. basic, .. senses];
        var outputs = Enumerable.Range(0, Spider.Legs).SelectMany(aLeg => new[]
        {
            SpineActuator.YawPort(Spider.SwingJoint(aLeg)), SpineActuator.PitchPort(Spider.LiftJoint(aLeg)), SpineActuator.PitchPort(Spider.KneeJoint(aLeg))
        }).ToArray();
        int[] hidden = aHidden.Length > 0 ? aHidden : [10];
        var module = new NeuralNetworkModule(new NeuralNetwork([inputs.Length, .. hidden, outputs.Length])) { Name = "Neural" };
        module.Ports.AddRange([ClockSensor.SinPort, ClockSensor.CosPort, .. TargetSensor.SteeringPorts, .. senses]);
        module.Inputs.AddRange(inputs.Select(aExpression => new NeuralInput(aExpression)));
        module.Outputs.AddRange(outputs.Select(aPort => new NeuralOutput(aPort)));
        return module;
    }

    public static Creature CreateNeuralSpider(Vector3 aPosition, float aYaw, Guid? aTargetId) =>
        CreateSpider(aPosition, aYaw, RandomColor(), aTargetId, CreateSpiderNeuralModule());
}
