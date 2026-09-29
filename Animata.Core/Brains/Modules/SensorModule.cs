using System.Security.Cryptography;
using System.Text;
using Animata.Core.Sensors;

namespace Animata.Core.Brains.Modules;

/// <summary>
/// Węzeł zmysłu w grafie mózgu: źródło, którego wyjścia to odczyty sensora ciała. Nie jest przechowywany w mózgu ani w pliku —
/// <see cref="Brain.SyncBody"/> tworzy po jednym takim węźle na każdy sensor ciała (na najwyższym poziomie grafu) i usuwa węzły
/// sensorów, których już nie ma. Porty są portami sensora na żywo (zmiana liczby wąsów zmienia je od razu), a Id wynika ze slotu
/// (<see cref="Sensor.Slot"/>), więc połączenia, położenia w edytorze i zapis mózgu przeżywają odtworzenie węzła
/// i przeniesienie mózgu do innego ciała z sensorem w tym samym slocie.
/// </summary>
public sealed class SensorModule : BrainModule
{
    public SensorModule(Sensor aSensor)
    {
        Sensor = aSensor;
        Id = BodyNodeId("sensor", aSensor.Slot);
        Name = aSensor.Slot;
    }

    /// <summary>Sensor ciała, z którego węzeł czyta.</summary>
    public Sensor Sensor { get; internal set; }

    /// <summary>Slot sensora w ciele.</summary>
    public string Slot => Sensor.Slot;

    public override IReadOnlyList<string> InputPorts => [];
    public override IReadOnlyList<string> OutputPorts => Sensor.OutputPorts;

    public override IReadOnlyDictionary<string, float> Evaluate(
        IReadOnlyDictionary<string, float> aInputs, BrainContext aContext) => Sensor.Read(aContext.Owner, aContext.World);

    /// <summary>Stałe Id węzła ciała: z rodzaju („sensor”, „actuator”) i slotu.</summary>
    internal static Guid BodyNodeId(string aKind, string aSlot) =>
        new(MD5.HashData(Encoding.UTF8.GetBytes($"animata:{aKind}:{aSlot}")));
}
