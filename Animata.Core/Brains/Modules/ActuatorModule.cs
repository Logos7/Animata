using Animata.Core.Actuators;

namespace Animata.Core.Brains.Modules;

/// <summary>
/// Ujście: zbiera komendy w fazie Think, a w fazie Act wysyła je do aktuatora ciała (po Id).
/// Jedyny standardowy węzeł, który zmienia świat. Nieprzyłączone porty nie trafiają do aktuatora
/// (aktuator traktuje je jak 0).
/// </summary>
public sealed class ActuatorModule : BrainModule
{
    private static readonly Dictionary<string, float> NoOutputs = [];

    private readonly string[] _ports;
    private readonly Dictionary<string, float> _command = [];

    public ActuatorModule(Actuator aActuator) : this(aActuator.Id, aActuator.InputPorts)
    {
    }

    public ActuatorModule(Guid aActuatorId, IEnumerable<string> aPorts)
    {
        ActuatorId = aActuatorId;
        _ports = aPorts.ToArray();
    }

    public Guid ActuatorId { get; }

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
        var actuator = aContext.Owner.Body.Actuators.Find(aActuator => aActuator.Id == ActuatorId)
            ?? throw new InvalidOperationException($"Actuator {ActuatorId} is missing from this body.");
        actuator.Apply(aContext.Owner, _command, aContext.Delta);
    }
}
