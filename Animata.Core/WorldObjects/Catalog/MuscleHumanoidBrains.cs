using Animata.Core.Brains;
using Animata.Core.Brains.Modules;
using Animata.Core.Brains.Neural;
using Animata.Core.Sensors;

namespace Animata.Core.WorldObjects;

/// <summary>
/// Gotowe mózgi humanoida mięśniowego. Główny: automat stanów jak u <see cref="Humanoid"/> — stanie i chód — tylko
/// że oba moduły sterują pobudzeniami 24 mięśni nóg, a nie serwami.
/// </summary>
public static class MuscleHumanoidBrains
{
    public const string StandName = "Stanie";
    public const string WalkName = "Chód";
    public const string HandPreset = "Stój i idź · mięśnie";
    public const string NetworkPreset = "Stój i idź · dwie sieci";

    public static IReadOnlyList<BrainPreset> Presets(Creature aCreature) =>
    [
        new(HandPreset, "automat stanów: regulator stania i automat kroku sterujące mięśniami (parametry z optymalizacji)",
            () => MuscleStandModule.Create(HandStand), true)
        {
            Build = aBrain => BuildStateBrain(aBrain, MuscleStandModule.Create(HandStand, StandName), MuscleGaitModule.Create(HandGait, WalkName))
        },
        new(NetworkPreset, "automat stanów: sieć stania i sieć chodu, każda steruje 24 mięśniami; losowe wagi",
            () => StandNetwork(), false)
        {
            Build = aBrain => BuildStateBrain(aBrain, StandNetwork(), WalkNetwork())
        },
        new("Stanie mięśniami", "regulator PD stawów przełożony na mięśnie + odruchy równowagi", () => MuscleStandModule.Create(HandStand), true),
        new("Chód mięśniami", "automat kroku (jak SIMBICON) przełożony na mięśnie", () => MuscleGaitModule.Create(HandGait, WalkName), true)
    ];

    /// <summary>Parametry stania (<see cref="MuscleStandModule"/>) — z pomiaru/optymalizacji.</summary>
    public static readonly float[] HandStand = MuscleStandModule.Create([3.67f, 4.16f, 3.87f, 0, 0.05f, 0.41f, 0.25f, -0.02f, 0.07f, -0.1f, 0.15f, -0.03f]).GetParameters();

    /// <summary>Parametry chodu (<see cref="MuscleGaitModule.Names"/>) — z pomiaru/optymalizacji.</summary>
    public static readonly float[] HandGait = MuscleGaitModule.Create([0.8f, 0.2f, 0.1f, 0.4f, 0.1f, 0, 0, 0, 0.2f, 0.1f, 0.4f, 0, 0, .. HandStand]).GetParameters();

    /// <summary>Zmysły → stanie i chód → automat (Found, Gap) → mięśnie „Muscles”.</summary>
    public static IReadOnlyList<BrainModule> BuildStateBrain(Brain aBrain, BrainModule aStand, BrainModule aWalk)
    {
        var graph = aBrain.Graph;
        var muscles = graph.Modules.OfType<ActuatorModule>().Single(aDrive => aDrive.Slot == "Muscles");
        var machine = new StateMachineModule(
            [HumanoidBrains.StandState, HumanoidBrains.WalkState],
            [TargetSensor.FoundPort, TargetSensor.GapPort],
            MuscleHumanoid.MusclePorts,
            [
                new StateTransition(HumanoidBrains.StandState, HumanoidBrains.WalkState, "Found * Gap", true, HumanoidBrains.GoGap),
                new StateTransition(HumanoidBrains.WalkState, HumanoidBrains.StandState, "Gap + 10 * (1 - Found)", false, HumanoidBrains.StopGap)
            ])
        { Name = "Automat" };
        var index = graph.Modules.OfType<SensorModule>().Count();
        graph.Modules.InsertRange(index, [aStand, aWalk, machine]);
        WorldObjectCatalog.ConnectSenses(aBrain, aStand);
        WorldObjectCatalog.ConnectSenses(aBrain, aWalk);
        var eye = graph.Modules.OfType<SensorModule>().Single(aSensor => aSensor.OutputPorts.Contains(TargetSensor.GapPort));
        foreach (var condition in machine.Conditions)
            graph.Connect(eye, condition, machine, condition);
        foreach (var port in MuscleHumanoid.MusclePorts)
        {
            graph.Connect(aStand, port, machine, StateMachineModule.StatePortName(HumanoidBrains.StandState, port));
            graph.Connect(aWalk, port, machine, StateMachineModule.StatePortName(HumanoidBrains.WalkState, port));
            graph.Connect(machine, port, muscles, port);
        }
        graph.Invalidate();
        return [aStand, aWalk, machine];
    }

    private static readonly string[] BalancePorts =
        [BalanceSensor.PitchPort, BalanceSensor.RollPort, BalanceSensor.PitchRatePort, BalanceSensor.RollRatePort];

    /// <summary>Kąty stawów nóg z czucia stawów.</summary>
    private static readonly string[] LegJointPorts = LegPorts();

    private static string[] LegPorts()
    {
        var geometry = new MuscleGeometry(MuscleHumanoid.DefaultPlan());
        return [.. Enumerable.Range(0, geometry.Axes.Count).Select(geometry.PortOf)];
    }

    /// <summary>Sieć stania: kąty nóg i błędnik → 24 pobudzenia (wyjścia 0.5 + 0.5·tanh — od połowy w dół i w górę).</summary>
    public static NeuralNetworkModule StandNetwork(Random? aRandom = null) =>
        Network(StandName, [.. LegJointPorts, .. BalancePorts], [.. LegJointPorts, .. BalancePorts], [24], aRandom);

    /// <summary>Sieć chodu: rytm, cel, kąty nóg, błędnik i prędkość → 24 pobudzenia.</summary>
    public static NeuralNetworkModule WalkNetwork(Random? aRandom = null) => Network(WalkName,
        [ClockSensor.SinPort, ClockSensor.CosPort, .. TargetSensor.SteeringPorts, .. LegJointPorts, .. BalancePorts,
            BalanceSensor.VelocityXPort, BalanceSensor.VelocityYPort],
        [ClockSensor.SinPort, ClockSensor.CosPort, "Found * DirectionY", "Found * DirectionX", "Found * Gap / 4", .. LegJointPorts,
            .. BalancePorts, BalanceSensor.VelocityXPort, BalanceSensor.VelocityYPort],
        [32, 24], aRandom);

    private static NeuralNetworkModule Network(string aName, string[] aPorts, string[] aInputs, int[] aHidden, Random? aRandom)
    {
        var module = new NeuralNetworkModule(new NeuralNetwork([aInputs.Length, .. aHidden, MuscleHumanoid.MusclePorts.Length])) { Name = aName };
        module.Network.Randomize(aRandom);
        foreach (var neuron in module.Network.Weights[^1])
            for (var input = 0; input < neuron.Length; input++)
                neuron[input] *= 0.1f;
        Array.Fill(module.Network.Biases[^1], -2f);   // na start mięśnie prawie luźne (tanh(−2) ≈ −0.96)
        module.Ports.AddRange(aPorts);
        module.Inputs.AddRange(aInputs.Select(aExpression => new NeuralInput(aExpression)));
        module.Outputs.AddRange(MuscleHumanoid.MusclePorts.Select(aPort => new NeuralOutput(aPort, 0.5f, 0.5f)));
        return module;
    }
}
