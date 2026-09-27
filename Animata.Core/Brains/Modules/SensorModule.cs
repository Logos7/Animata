using Animata.Core.Sensors;

namespace Animata.Core.Brains.Modules;

/// <summary>
/// Źródło: odczytuje sensor ciała właściciela (po Id) i wystawia jego porty jako wyjścia.
/// Porty są kopiowane z sensora przy tworzeniu, więc mózg da się przenieść do innego ciała
/// z sensorem o tym samym Id i typie.
/// </summary>
public sealed class SensorModule : BrainModule
{
    private readonly string[] _ports;

    public SensorModule(Sensor aSensor) : this(aSensor.Id, aSensor.OutputPorts)
    {
    }

    public SensorModule(Guid aSensorId, IEnumerable<string> aPorts)
    {
        SensorId = aSensorId;
        _ports = aPorts.ToArray();
    }

    public Guid SensorId { get; }

    public override IReadOnlyList<string> InputPorts => [];
    public override IReadOnlyList<string> OutputPorts => _ports;

    public override IReadOnlyDictionary<string, float> Evaluate(
        IReadOnlyDictionary<string, float> aInputs, BrainContext aContext)
    {
        var sensor = aContext.Owner.Body.Sensors.Find(aSensor => aSensor.Id == SensorId)
            ?? throw new InvalidOperationException($"Sensor {SensorId} is missing from this body.");
        return sensor.Read(aContext.Owner, aContext.World);
    }
}
