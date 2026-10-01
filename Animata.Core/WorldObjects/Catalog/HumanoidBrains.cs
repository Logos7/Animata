using Animata.Core.Brains;
using Animata.Core.Brains.Modules;
using Animata.Core.Brains.Neural;
using Animata.Core.Sensors;

namespace Animata.Core.WorldObjects;

/// <summary>
/// Gotowe mózgi humanoida. Główny to mózg ze stanami: sieć stania i sieć chodu, a między nimi automat stanów, który
/// patrzy na cel (Found, Gap) i płynnie przełącza, która sieć steruje ciałem — stoi, gdy celu nie ma albo jest blisko,
/// idzie, gdy cel jest dalej. Obie sieci uczą się osobno, każda w swoich warunkach (stanie — próby z pchnięciami, chód —
/// dojście do celu; <see cref="Humanoid.Design"/>.ModuleRig). Ten sam układ „stój i idź” (<see cref="BuildStateBrain"/>,
/// <see cref="StateMachine"/>) ma humanoid mięśniowy (<see cref="MuscleHumanoidBrains"/>), tylko na innym napędzie.
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

    private static BalanceModule HandStand() => new BalanceModule { Name = StandName }.WithParameters(HandBalance);

    public static IReadOnlyList<BrainPreset> Presets(Creature aHumanoid) =>
    [
        new(NetworkPreset, "automat stanów: sieć stania (błędnik) i sieć chodu (rytm, cel, błędnik), losowe wagi",
            () => StandNetwork(), false)
        {
            Build = aBrain => BuildStateBrain(aBrain, StandNetwork(), WalkNetwork())
        },
        new(HandPreset, "automat stanów: regulator równowagi i generator kroku z ręcznymi parametrami", HandStand, true)
        {
            Build = aBrain => BuildStateBrain(aBrain, HandStand(), new BipedGaitModule { Name = WalkName })
        },
        new("Sieć stania", "sama sieć stania z błędnika, losowe wagi", () => StandNetwork()),
        new("Stanie ręczne", "regulator równowagi z błędnika (kostki i biodra)", HandStand, true),
        new("Chód ręczny", "generator kroku: nogi w przeciwfazie, kołysanie, równowaga", () => new BipedGaitModule { Name = WalkName }, true)
    ];

    /// <summary>Mózg ze stanami humanoida na serwach: napęd stawów „Body”.</summary>
    public static IReadOnlyList<BrainModule> BuildStateBrain(Brain aBrain, BrainModule aStand, BrainModule aWalk) =>
        BuildStateBrain(aBrain, aStand, aWalk, "Body", Humanoid.Ports);

    /// <summary>
    /// Mózg ze stanami: zmysły → stanie i chód → automat stanów (warunki: Found, Gap z oka) → napęd <paramref name="aDrive"/>
    /// na portach <paramref name="aPorts"/>. Zwraca dodane moduły (stanie, chód, automat).
    /// </summary>
    public static IReadOnlyList<BrainModule> BuildStateBrain(Brain aBrain, BrainModule aStand, BrainModule aWalk, string aDrive,
        IReadOnlyList<string> aPorts)
    {
        var graph = aBrain.Graph;
        var drive = graph.Modules.OfType<ActuatorModule>().Single(aModule => aModule.Slot == aDrive);
        var machine = StateMachine(aPorts);
        var index = graph.Modules.OfType<SensorModule>().Count();
        graph.Modules.InsertRange(index, [aStand, aWalk, machine]);
        WorldObjectCatalog.ConnectSenses(aBrain, aStand);
        WorldObjectCatalog.ConnectSenses(aBrain, aWalk);
        var eye = graph.Modules.OfType<SensorModule>().Single(aSensor => aSensor.OutputPorts.Contains(TargetSensor.GapPort));
        foreach (var condition in machine.Conditions)
            graph.Connect(eye, condition, machine, condition);
        foreach (var port in aPorts)
        {
            graph.Connect(aStand, port, machine, StateMachineModule.StatePortName(StandState, port));
            graph.Connect(aWalk, port, machine, StateMachineModule.StatePortName(WalkState, port));
            graph.Connect(machine, port, drive, port);
        }
        graph.Invalidate();
        return [aStand, aWalk, machine];
    }

    /// <summary>
    /// Automat stanów „stój” ↔ „idź” na portach <paramref name="aPorts"/>: do chodu, gdy cel widać i szczelina &gt; <see cref="GoGap"/>;
    /// do stania, gdy szczelina &lt; <see cref="StopGap"/> albo celu nie widać (wyrażenie Gap + 10·(1 − Found)). Przejście 0.4 s,
    /// pobyt min. 1 s.
    /// </summary>
    public static StateMachineModule StateMachine(IReadOnlyList<string> aPorts, string aStand = StandState, string aWalk = WalkState) => new(
        [aStand, aWalk],
        [TargetSensor.FoundPort, TargetSensor.GapPort],
        aPorts,
        [
            new StateTransition(aStand, aWalk, "Found * Gap", true, GoGap),
            new StateTransition(aWalk, aStand, "Gap + 10 * (1 - Found)", false, StopGap)
        ])
    { Name = "Automat" };

    private static readonly string[] BalancePorts =
        [BalanceSensor.PitchPort, BalanceSensor.RollPort, BalanceSensor.PitchRatePort, BalanceSensor.RollRatePort];

    /// <summary>Wejścia chodu: rytm (Sin, Cos) i kurs do celu — porty i wyrażenia.</summary>
    internal static readonly string[] WalkPorts = [ClockSensor.SinPort, ClockSensor.CosPort, .. TargetSensor.SteeringPorts];
    internal static readonly string[] WalkInputs = [ClockSensor.SinPort, ClockSensor.CosPort, "Found * DirectionY", "Found * DirectionX", "Found * Gap / 4"];

    /// <summary>Sieć stania: wejścia z błędnika (pochylenie, przechył, ich szybkości, wysokość), wyjścia — wszystkie stawy.</summary>
    public static NeuralNetworkModule StandNetwork(Random? aRandom = null) =>
        QuietNetwork(StandName, [.. BalancePorts, BalanceSensor.HeightPort], [.. BalancePorts, BalanceSensor.HeightPort],
            [.. Humanoid.Ports.Select(aPort => new NeuralOutput(aPort))], 0, StandHidden, aRandom);

    /// <summary>Sieć chodu: rytm (Sin, Cos), kierunek i odległość celu, błędnik; wyjścia — wszystkie stawy.</summary>
    public static NeuralNetworkModule WalkNetwork(Random? aRandom = null) =>
        QuietNetwork(WalkName, [.. WalkPorts, .. BalancePorts], [.. WalkInputs, .. BalancePorts],
            [.. Humanoid.Ports.Select(aPort => new NeuralOutput(aPort))], 0, WalkHidden, aRandom);

    /// <summary>
    /// Sieć z cichym startem: wagi ostatniej warstwy ×0.1 i biasy wyjść <paramref name="aOutputBias"/> — na początku sieć
    /// trzyma postawę spoczynkową (stoi jak bez sterowania), a nie wymachuje losowo kończynami.
    /// </summary>
    internal static NeuralNetworkModule QuietNetwork(string aName, string[] aPorts, string[] aInputs, NeuralOutput[] aOutputs,
        float aOutputBias, int[] aHidden, Random? aRandom)
    {
        var module = NeuralNetworkModule.Build(aName, aPorts, aInputs, aOutputs, aHidden);
        module.Network.Randomize(aRandom);
        foreach (var neuron in module.Network.Weights[^1])
            for (var input = 0; input < neuron.Length; input++)
                neuron[input] *= 0.1f;
        Array.Fill(module.Network.Biases[^1], aOutputBias);
        return module;
    }
}
