using Animata.Core.Actuators;

namespace Animata.Core.Brains.Modules;

/// <summary>
/// Ujście: zbiera komendy w fazie Think, a w fazie Act wysyła je do aktuatora ciała (po nazwie slotu).
/// Jedyny standardowy węzeł, który zmienia świat. Nieprzyłączone porty nie trafiają do aktuatora
/// (aktuator traktuje je jak 0).
/// </summary>
public sealed class ActuatorModule : BrainModule
{
    private static readonly Dictionary<string, float> NoOutputs = [];

    private string[] _ports;
    private readonly Dictionary<string, float> _command = [];

    public ActuatorModule(Actuator aActuator) : this(aActuator.Slot, aActuator.InputPorts)
    {
    }

    public ActuatorModule(string aSlot, IEnumerable<string> aPorts)
    {
        if (string.IsNullOrWhiteSpace(aSlot))
            throw new ArgumentException("Actuator slot must not be empty.", nameof(aSlot));
        Slot = aSlot;
        _ports = aPorts.ToArray();
    }

    /// <summary>Slot aktuatora w ciele. Jeden slot — jedno sterowanie (graf to waliduje).</summary>
    public string Slot { get; }

    /// <summary>Kopiuje porty z aktuatora na nowo (np. po zmianie liczby segmentów). Połączenia poprawia wołający.</summary>
    public void SetPorts(IEnumerable<string> aPorts) => _ports = aPorts.ToArray();

    /// <summary>Ostatnia zebrana komenda (do podglądu).</summary>
    public IReadOnlyDictionary<string, float> LastCommand => _command;

    public override IReadOnlyList<string> InputPorts => _ports;
    public override IReadOnlyList<string> OutputPorts => [];

    public override IReadOnlyDictionary<string, float> Evaluate(
        IReadOnlyDictionary<string, float> aInputs, BrainContext aContext)
    {
        _command.Clear();
        foreach (var (port, value) in aInputs)
            _command[port] = value;
        return NoOutputs;
    }

    public override void Commit(BrainContext aContext)
    {
        var actuator = aContext.Owner.Body.FindActuator(Slot)
            ?? throw new InvalidOperationException($"This body has no actuator in slot \"{Slot}\".");
        actuator.Apply(aContext.Owner, _command, aContext.Delta);
    }
}
