using System.Numerics;
using Animata.Core.Actuators;
using Animata.Core.Bodies;
using Animata.Core.Brains.Modules;
using Animata.Core.Sensors;

namespace Animata.Core.WorldObjects;

// Pająki: czworonóg z chodem (kłus) albo z siecią (ciało — projekt Spider).
public static partial class WorldObjectCatalog
{
    /// <summary>Pająk z generatorem kłusa: ręczne parametry jako snapshot „ręczne parametry”, chód wylosowany.</summary>
    public static Creature CreateLearningSpider(Vector3 aPosition, float aYaw, Guid? aTargetId)
    {
        var gait = new GaitModule { Name = "Chód" };
        return WithHandSnapshot(Create(Spider.Design, aPosition, aYaw, aTargetId, gait, RandomColor()), gait);
    }

    /// <summary>
    /// Sieć pająka: wejścia Sin, Cos (zegar), kierunek do celu (bok, przód), szczelina/4, a z <paramref name="aSenses"/> —
    /// dotyk czterech stóp i brzucha oraz pochylenie i przechył tułowia (równowaga); warstwa ukryta 10; wyjścia — po trzy
    /// na nogę: zamach (Yaw), uniesienie i kolano (Pitch). Wagi losowe.
    /// </summary>
    public static NeuralNetworkModule CreateSpiderNeuralModule(bool aSenses = false, params int[] aHidden)
    {
        string[] senses = aSenses ? [.. Spider.TouchPorts, FeelSensor.HeadPitchPort, FeelSensor.HeadRollPort] : [];
        var outputs = Enumerable.Range(0, Spider.Legs).SelectMany(aLeg => new[]
        {
            JointPorts.Yaw(Spider.SwingJoint(aLeg)), JointPorts.Pitch(Spider.LiftJoint(aLeg)), JointPorts.Pitch(Spider.KneeJoint(aLeg))
        });
        return NeuralNetworkModule.Build("Neural", [ClockSensor.SinPort, ClockSensor.CosPort, .. TargetSensor.SteeringPorts, .. senses],
            [ClockSensor.SinPort, ClockSensor.CosPort, "Found * DirectionY", "Found * DirectionX", "Found * Gap / 4", .. senses],
            outputs, aHidden.Length > 0 ? aHidden : [10]);
    }

    public static Creature CreateNeuralSpider(Vector3 aPosition, float aYaw, Guid? aTargetId) =>
        Create(Spider.Design, aPosition, aYaw, aTargetId, CreateSpiderNeuralModule(), RandomColor());
}
