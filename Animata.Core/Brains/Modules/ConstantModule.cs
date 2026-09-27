namespace Animata.Core.Brains.Modules;

/// <summary>Źródło stałej wartości na jednym porcie (np. selektor routera ustawiany z UI).</summary>
public sealed class ConstantModule : BrainModule
{
    private readonly string[] _ports;
    private readonly Dictionary<string, float> _outputs;

    public ConstantModule(string aPort = "Value", float aValue = 0)
    {
        Port = aPort;
        _ports = [aPort];
        _outputs = new Dictionary<string, float> { [aPort] = aValue };
    }

    public string Port { get; }

    public float Value
    {
        get => _outputs[Port];
        set => _outputs[Port] = value;
    }

    public override IReadOnlyList<string> InputPorts => [];
    public override IReadOnlyList<string> OutputPorts => _ports;

    public override ModuleState CaptureState() => new ConstantState(Value);

    public override void RestoreState(ModuleState aState)
    {
        if (aState is not ConstantState state)
            throw new ArgumentException($"Expected {nameof(ConstantState)}, got {aState.GetType().Name}.", nameof(aState));
        Value = state.Value;
    }

    public override IReadOnlyDictionary<string, float> Evaluate(
        IReadOnlyDictionary<string, float> aInputs, BrainContext aContext) => _outputs;
}
