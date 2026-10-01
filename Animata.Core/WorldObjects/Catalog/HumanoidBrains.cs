using Animata.Core.Brains;
using Animata.Core.Brains.Modules;
using Animata.Core.Brains.Neural;
using Animata.Core.Sensors;

namespace Animata.Core.WorldObjects;

/// <summary>
/// Gotowe mózgi humanoida. Główny to mózg ze stanami: sieć stania i sieć chodu, a między nimi automat stanów, który
/// patrzy na cel (Found, Gap) i płynnie przełącza, która sieć steruje ciałem — stoi, gdy celu nie ma albo jest blisko,
/// idzie, gdy cel jest dalej. Obie sieci uczą się osobno, każda w swoich warunkach (stanie — próby z pchnięciami, chód —
/// dojście do celu; <see cref="Humanoid.Design"/>.ModuleRig).
/// </summary>
public static class HumanoidBrains
{
    public const string StandName = "Stanie";
    public const string WalkName = "Chód";
    public const string StandState = "Stoję";
    public const string WalkState = "Idę";
    public const string NetworkPreset = "Stój i idź · dwie sieci";
    public const string HandPreset = "Stój i idź · ręcznie";

    /// <summary>Automat przechodzi do chodu, gdy cel jest dalej niż tyle (m szczeliny), i wraca do stania bliżej niż <see cref="StopGap"/>.</summary>
    public const float GoGap = 0.8f;
    public const float StopGap = 0.4f;

    public static readonly int[] StandHidden = [16];
    public static readonly int[] WalkHidden = [24, 16];

    /// <summary>
    /// Ręczne wzmocnienia stania (<see cref="BalanceModule"/>): kostki P, D, biodra P, D, przechył P, D — z ewolucji na
    /// próbach z pchnięciami i karą za odejście z miejsca (20 pokoleń): stoi 7/8 prób wobec 3/8 bez regulatora, a bez
    /// pchnięć stoi w miejscu co najmniej 40 s (bez kary za odejście ewolucja znalazła „stanie” drobiące 2 m w tył).
    /// </summary>
    public static readonly float[] HandBalance = [0.43f, 0.87f, -0.36f, -0.04f, -0.15f, 0.12f];

    public static IReadOnlyList<BrainPreset> Presets(Creature aHumanoid) =>
    [
        new(NetworkPreset, "automat stanów: sieć stania (błędnik) i sieć chodu (rytm, cel, błędnik), losowe wagi",
            () => StandNetwork(), false)
        {
            Build = aBrain => BuildStateBrain(aBrain, StandNetwork(), WalkNetwork())
        },
        new(HandPreset, "automat stanów: regulator równowagi i generator kroku z ręcznymi parametrami",
            () => new BalanceModule { Name = StandName }.WithParameters(HandBalance), true)
        {
            Build = aBrain => BuildStateBrain(aBrain, new BalanceModule { Name = StandName }.WithParameters(HandBalance), new BipedGaitModule { Name = WalkName })
        },
        new("Sieć stania", "sama sieć stania z błędnika, losowe wagi", () => StandNetwork()),
        new("Stanie ręczne", "regulator równowagi z błędnika (kostki i biodra)", () => new BalanceModule { Name = StandName }.WithParameters(HandBalance), true),
        new("Chód ręczny", "generator kroku: nogi w przeciwfazie, kołysanie, równowaga", () => new BipedGaitModule { Name = WalkName }, true)
    ];

    /// <summary>
    /// Mózg ze stanami: zmysły → sieć stania i sieć chodu → automat stanów (warunki: Found, Gap z oka) → napęd stawów.
    /// Zwraca dodane moduły (stanie, chód, automat).
    /// </summary>
    public static IReadOnlyList<BrainModule> BuildStateBrain(Brain aBrain, BrainModule aStand, BrainModule aWalk)
    {
        var graph = aBrain.Graph;
        var body = graph.Modules.OfType<ActuatorModule>().Single(aDrive => aDrive.Slot == "Body");
        var machine = StateMachine();
        var index = graph.Modules.OfType<SensorModule>().Count();
        graph.Modules.InsertRange(index, [aStand, aWalk, machine]);
        WorldObjectCatalog.ConnectSenses(aBrain, aStand);
        WorldObjectCatalog.ConnectSenses(aBrain, aWalk);
        var eye = graph.Modules.OfType<SensorModule>().Single(aSensor => aSensor.OutputPorts.Contains(TargetSensor.GapPort));
        foreach (var condition in machine.Conditions)
            graph.Connect(eye, condition, machine, condition);
        foreach (var port in Humanoid.Ports)
        {
            graph.Connect(aStand, port, machine, StateMachineModule.StatePortName(StandState, port));
            graph.Connect(aWalk, port, machine, StateMachineModule.StatePortName(WalkState, port));
            graph.Connect(machine, port, body, port);
        }
        graph.Invalidate();
        return [aStand, aWalk, machine];
    }

    /// <summary>
    /// Automat „Stoję” ↔ „Idę”: do chodu, gdy cel widać i szczelina &gt; <see cref="GoGap"/>; do stania, gdy szczelina
    /// &lt; <see cref="StopGap"/> albo celu nie widać (wyrażenie Gap + 10·(1 − Found)). Przejście 0.4 s, pobyt min. 1 s.
    /// </summary>
    public static StateMachineModule StateMachine() => new(
        [StandState, WalkState],
        [TargetSensor.FoundPort, TargetSensor.GapPort],
        Humanoid.Ports,
        [
            new StateTransition(StandState, WalkState, "Found * Gap", true, GoGap),
            new StateTransition(WalkState, StandState, "Gap + 10 * (1 - Found)", false, StopGap)
        ])
    { Name = "Automat" };

    /// <summary>Sieć stania: wejścia z błędnika (pochylenie, przechył, ich szybkości, wysokość), wyjścia — wszystkie stawy.</summary>
    public static NeuralNetworkModule StandNetwork(Random? aRandom = null) => Network(StandName,
        [BalanceSensor.PitchPort, BalanceSensor.RollPort, BalanceSensor.PitchRatePort, BalanceSensor.RollRatePort, BalanceSensor.HeightPort],
        [BalanceSensor.PitchPort, BalanceSensor.RollPort, BalanceSensor.PitchRatePort, BalanceSensor.RollRatePort, BalanceSensor.HeightPort],
        StandHidden, aRandom);

    /// <summary>Sieć chodu: rytm (Sin, Cos), kierunek i odległość celu, błędnik; wyjścia — wszystkie stawy.</summary>
    public static NeuralNetworkModule WalkNetwork(Random? aRandom = null) => Network(WalkName,
        [ClockSensor.SinPort, ClockSensor.CosPort, .. TargetSensor.SteeringPorts,
            BalanceSensor.PitchPort, BalanceSensor.RollPort, BalanceSensor.PitchRatePort, BalanceSensor.RollRatePort],
        [ClockSensor.SinPort, ClockSensor.CosPort, "Found * DirectionY", "Found * DirectionX", "Found * Gap / 4",
            BalanceSensor.PitchPort, BalanceSensor.RollPort, BalanceSensor.PitchRatePort, BalanceSensor.RollRatePort],
        WalkHidden, aRandom);

    /// <summary>
    /// Sieć o wyjściach wszystkich stawów, z cichym startem: wagi ostatniej warstwy ×0.1 i zerowe biasy wyjść — na początku
    /// sieć trzyma postawę spoczynkową (stoi jak bez sterowania), a nie wymachuje losowo kończynami.
    /// </summary>
    private static NeuralNetworkModule Network(string aName, string[] aPorts, string[] aInputs, int[] aHidden, Random? aRandom)
    {
        var module = new NeuralNetworkModule(new NeuralNetwork([aInputs.Length, .. aHidden, Humanoid.Ports.Length])) { Name = aName };
        module.Network.Randomize(aRandom);
        var weights = module.Network.Weights[^1];
        foreach (var neuron in weights)
            for (var input = 0; input < neuron.Length; input++)
                neuron[input] *= 0.1f;
        Array.Clear(module.Network.Biases[^1]);
        module.Ports.AddRange(aPorts);
        module.Inputs.AddRange(aInputs.Select(aExpression => new NeuralInput(aExpression)));
        module.Outputs.AddRange(Humanoid.Ports.Select(aPort => new NeuralOutput(aPort)));
        return module;
    }
}
