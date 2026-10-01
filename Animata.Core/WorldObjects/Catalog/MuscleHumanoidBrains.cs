using Animata.Core.Brains;
using Animata.Core.Brains.Modules;
using Animata.Core.Brains.Neural;
using Animata.Core.Sensors;

namespace Animata.Core.WorldObjects;

/// <summary>
/// Gotowe mózgi humanoida mięśniowego. Główny: automat stanów jak u <see cref="Humanoid"/> (<see cref="HumanoidBrains.BuildStateBrain(Brain, BrainModule, BrainModule, string, IReadOnlyList{string})"/>)
/// — stanie i chód — tylko że oba moduły sterują pobudzeniami 24 mięśni nóg, a nie serwami.
/// </summary>
public static class MuscleHumanoidBrains
{
    public const string HandPreset = "Stój i idź · mięśnie";
    public const string NetworkPreset = "Stój i idź · dwie sieci";

    private static MuscleStandModule HandStandModule() => new MuscleStandModule { Name = HumanoidBrains.StandName }.WithParameters(HandStand);
    private static MuscleGaitModule HandGaitModule() => new MuscleGaitModule { Name = HumanoidBrains.WalkName }.WithParameters(HandGait);

    public static IReadOnlyList<BrainPreset> Presets(Creature aCreature) =>
    [
        new(HandPreset, "automat stanów: regulator stania i automat kroku sterujące mięśniami (parametry z optymalizacji)", HandStandModule, true)
        {
            Build = aBrain => BuildStateBrain(aBrain, HandStandModule(), HandGaitModule())
        },
        new(NetworkPreset, "automat stanów: sieć stania i sieć chodu, każda steruje 24 mięśniami; losowe wagi",
            () => StandNetwork(), false)
        {
            Build = aBrain => BuildStateBrain(aBrain, StandNetwork(), WalkNetwork())
        },
        new("Stanie mięśniami", "regulator PD stawów przełożony na mięśnie + odruchy równowagi", HandStandModule, true),
        new("Chód mięśniami", "automat kroku (jak SIMBICON) przełożony na mięśnie", HandGaitModule, true)
    ];

    /// <summary>Parametry stania (<see cref="MuscleStandModule"/>) — z pomiaru/optymalizacji.</summary>
    public static readonly float[] HandStand = new MuscleStandModule().WithParameters([3.67f, 4.16f, 3.87f, 0, 0.05f, 0.41f, 0.25f, -0.02f, 0.07f, -0.1f, 0.15f, -0.03f]).GetParameters();

    /// <summary>Parametry chodu (<see cref="MuscleGaitModule.Specs"/>) — z pomiaru/optymalizacji.</summary>
    public static readonly float[] HandGait = new MuscleGaitModule().WithParameters([0.8f, 0.2f, 0.1f, 0.4f, 0.1f, 0, 0, 0, 0.2f, 0.1f, 0.4f, 0, 0, .. HandStand]).GetParameters();

    /// <summary>Zmysły → stanie i chód → automat (Found, Gap) → mięśnie „Muscles”.</summary>
    public static IReadOnlyList<BrainModule> BuildStateBrain(Brain aBrain, BrainModule aStand, BrainModule aWalk) =>
        HumanoidBrains.BuildStateBrain(aBrain, aStand, aWalk, "Muscles", MuscleHumanoid.MusclePorts);

    private static readonly string[] BalancePorts =
        [BalanceSensor.PitchPort, BalanceSensor.RollPort, BalanceSensor.PitchRatePort, BalanceSensor.RollRatePort];

    /// <summary>Kąty stawów nóg z czucia stawów.</summary>
    private static readonly string[] LegJointPorts = [.. Enumerable.Range(0, MuscleHumanoid.Geometry.Axes.Count).Select(MuscleHumanoid.Geometry.PortOf)];

    /// <summary>Wyjścia: pobudzenia 0.5 + 0.5·tanh — od połowy w dół i w górę.</summary>
    private static NeuralOutput[] Excitations => [.. MuscleHumanoid.MusclePorts.Select(aPort => new NeuralOutput(aPort, 0.5f, 0.5f))];

    /// <summary>Sieć stania: kąty nóg i błędnik → 24 pobudzenia; na start mięśnie prawie luźne (bias −2, tanh(−2) ≈ −0.96).</summary>
    public static NeuralNetworkModule StandNetwork(Random? aRandom = null) => HumanoidBrains.QuietNetwork(HumanoidBrains.StandName,
        [.. LegJointPorts, .. BalancePorts], [.. LegJointPorts, .. BalancePorts], Excitations, -2, [24], aRandom);

    /// <summary>Sieć chodu: rytm, cel, kąty nóg, błędnik i prędkość → 24 pobudzenia.</summary>
    public static NeuralNetworkModule WalkNetwork(Random? aRandom = null) => HumanoidBrains.QuietNetwork(HumanoidBrains.WalkName,
        [.. HumanoidBrains.WalkPorts, .. LegJointPorts, .. BalancePorts, BalanceSensor.VelocityXPort, BalanceSensor.VelocityYPort],
        [.. HumanoidBrains.WalkInputs, .. LegJointPorts, .. BalancePorts, BalanceSensor.VelocityXPort, BalanceSensor.VelocityYPort],
        Excitations, -2, [32, 24], aRandom);
}
