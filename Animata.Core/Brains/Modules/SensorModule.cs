using Animata.Core.Sensors;

namespace Animata.Core.Brains.Modules;

/// <summary>
/// Źródło: odczytuje sensor ciała właściciela po nazwie slotu (<see cref="Sensor.Slot"/>) i wystawia jego porty
/// jako wyjścia. Porty są kopiowane z sensora przy tworzeniu, więc mózg da się przenieść do innego ciała
/// z sensorem w slocie o tej samej nazwie i z tymi samymi portami.
/// </summary>
public sealed class SensorModule : BrainModule
{
    private string[] _ports;

    public SensorModule(Sensor aSensor) : this(aSensor.Slot, aSensor.OutputPorts)
    {
    }

    public SensorModule(string aSlot, IEnumerable<string> aPorts)
    {
        if (string.IsNullOrWhiteSpace(aSlot))
            throw new ArgumentException("Sensor slot must not be empty.", nameof(aSlot));
        Slot = aSlot;
        _ports = aPorts.ToArray();
    }

    /// <summary>Slot sensora w ciele.</summary>
    public string Slot { get; }

    /// <summary>Kopiuje porty z sensora na nowo (np. po zmianie liczby wąsów). Połączenia poprawia wołający.</summary>
    public void SetPorts(IEnumerable<string> aPorts) => _ports = aPorts.ToArray();

    public override IReadOnlyList<string> InputPorts => [];
    public override IReadOnlyList<string> OutputPorts => _ports;

    public override IReadOnlyDictionary<string, float> Evaluate(
        IReadOnlyDictionary<string, float> aInputs, BrainContext aContext)
    {
        var sensor = aContext.Owner.Body.FindSensor(Slot)
            ?? throw new InvalidOperationException($"This body has no sensor in slot \"{Slot}\".");
        return sensor.Read(aContext.Owner, aContext.World);
    }
}
