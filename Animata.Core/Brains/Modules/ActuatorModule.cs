using Animata.Core.Actuators;

namespace Animata.Core.Brains.Modules;

/// <summary>
/// Węzeł napędu w grafie mózgu: ujście, które zbiera komendy w fazie Think, a w fazie Act wysyła je do aktuatora ciała.
/// Jedyny standardowy węzeł, który zmienia świat. Jak <see cref="SensorModule"/> — tworzony z ciała przez
/// <see cref="Brain.SyncBody"/> (jeden na aktuator, więc jeden aktuator ma jedno sterowanie), porty na żywo, Id ze slotu.
/// Nieprzyłączone porty nie trafiają do aktuatora (aktuator traktuje je jak 0).
/// </summary>
public sealed class ActuatorModule : BrainModule
{
    private static readonly Dictionary<string, float> NoOutputs = [];

    private readonly Dictionary<string, float> _command = [];

    public ActuatorModule(Actuator aActuator)
    {
        Actuator = aActuator;
        Id = SensorModule.BodyNodeId("actuator", aActuator.Slot);
        Name = aActuator.Slot;
    }

    /// <summary>Aktuator ciała, do którego idą komendy.</summary>
    public Actuator Actuator { get; internal set; }

    /// <summary>Slot aktuatora w ciele.</summary>
    public string Slot => Actuator.Slot;

    /// <summary>Ostatnia zebrana komenda (do podglądu).</summary>
    public IReadOnlyDictionary<string, float> LastCommand => _command;

    public override IReadOnlyList<string> InputPorts => Actuator.InputPorts;
    public override IReadOnlyList<string> OutputPorts => [];

    public override IReadOnlyDictionary<string, float> Evaluate(
        IReadOnlyDictionary<string, float> aInputs, BrainContext aContext)
    {
        _command.Clear();
        foreach (var (port, value) in aInputs)
            _command[port] = value;
        return NoOutputs;
    }

    public override void Commit(BrainContext aContext) => Actuator.Apply(aContext.Owner, _command, aContext.Delta);
}
