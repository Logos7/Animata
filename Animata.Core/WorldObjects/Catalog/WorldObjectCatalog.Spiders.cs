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
    /// Pająk: oko „Eye” (tułów), czucie stawów „Joints”, zegar „Clock”, czucie terenu „Feel”, nogi „Legs” (8 stawów:
    /// biodro, kolano × 4). Mózg sensory → controller → nogi; controller musi mieć wyjścia Yaw/Pitch 0–7 (np. <see cref="GaitModule"/>).
    /// </summary>
    public static SpiderCreature CreateSpider(Vector3 aPosition, float aYaw, Vector3 aColor, Guid? aTargetId, BrainModule aController)
    {
        var brain = new Brain();
        var spider = new SpiderCreature(brain) { Color = aColor };
        var eye = new TargetSensor { Slot = "Eye", TargetId = aTargetId };
        var joints = new JointSensor(GaitModule.Joints) { Slot = "Joints" };
        var clock = new ClockSensor { Slot = "Clock", Frequency = 2.5f };
        var feel = new FeelSensor { Slot = "Feel" };
        var touch = CreateSpiderTouch();
        var legs = new SpineActuator(GaitModule.Joints) { Slot = "Legs" };
        spider.Body.Sensors.Add(eye);
        spider.Body.Sensors.Add(joints);
        spider.Body.Sensors.Add(clock);
        spider.Body.Sensors.Add(feel);
        spider.Body.Sensors.Add(touch);
        spider.Body.Actuators.Add(legs);
        BuildBrain(brain, aController);
        spider.Place(aPosition, Quaternion.CreateFromAxisAngle(Vector3.UnitZ, aYaw));
        return spider;
    }

    /// <summary>Pająk z generatorem kłusa: ręczne parametry jako snapshot „ręczne parametry”, chód wylosowany.</summary>
    public static SpiderCreature CreateLearningSpider(Vector3 aPosition, float aYaw, Guid? aTargetId)
    {
        var gait = new GaitModule { Name = "Chód" };
        var spider = CreateSpider(aPosition, aYaw, RandomColor(), aTargetId, gait);
        spider.Brain!.Capture("ręczne parametry", gait);
        gait.Randomize();
        return spider;
    }

    /// <summary>
    /// Sieć pająka: wejścia Sin, Cos (zegar), kierunek do celu (bok, przód), szczelina/4, a z <paramref name="aSenses"/> —
    /// dotyk czterech stóp i brzucha oraz pochylenie i przechył tułowia (równowaga); warstwa ukryta 10; wyjścia
    /// Yaw/Pitch 0–7 (skręt kolan jest ignorowany przez ciało). Wagi losowe.
    /// </summary>
    public static NeuralNetworkModule CreateSpiderNeuralModule(bool aSenses = false, params int[] aHidden)
    {
        string[] basic = [ClockSensor.SinPort, ClockSensor.CosPort, "Found * DirectionY", "Found * DirectionX", "Found * Gap / 4"];
        string[] senses = aSenses ? [.. SpiderTouchPorts, FeelSensor.HeadPitchPort, FeelSensor.HeadRollPort] : [];
        string[] inputs = [.. basic, .. senses];
        var outputs = JointNetworkOutputs(GaitModule.Joints);
        int[] hidden = aHidden.Length > 0 ? aHidden : [10];
        var module = new NeuralNetworkModule(new NeuralNetwork([inputs.Length, .. hidden, outputs.Length])) { Name = "Neural" };
        module.Ports.AddRange([ClockSensor.SinPort, ClockSensor.CosPort, .. TargetSensor.SteeringPorts, .. senses]);
        module.Inputs.AddRange(inputs.Select(aExpression => new NeuralInput(aExpression)));
        module.Outputs.AddRange(outputs.Select(aPort => new NeuralOutput(aPort)));
        return module;
    }

    /// <summary>Porty dotyku pająka: stopy (FootPL, FootPP, FootTL, FootTP) i brzuch (Belly).</summary>
    public static readonly string[] SpiderTouchPorts =
        [.. SpiderCreature.LegNames.Select(aLeg => "Foot" + aLeg), "Belly"];

    /// <summary>Dotyk pająka (slot „Touch”): golenie jako stopy i tułów jako brzuch.</summary>
    public static TouchSensor CreateSpiderTouch() => new(
        [.. SpiderCreature.LegNames.Select(aLeg => ("Foot" + aLeg, "Goleń" + aLeg)), ("Belly", "Tułów")]) { Slot = "Touch" };

    public static SpiderCreature CreateNeuralSpider(Vector3 aPosition, float aYaw, Guid? aTargetId) =>
        CreateSpider(aPosition, aYaw, RandomColor(), aTargetId, CreateSpiderNeuralModule());
}
