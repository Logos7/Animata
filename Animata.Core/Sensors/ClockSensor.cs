using Animata.Core.Entities;
using Animata.Core.Worlds;

namespace Animata.Core.Sensors;

/// <summary>
/// Zegar rytmu (slot „Clock”): Sin i Cos fazy 2π·f·t, t = czas świata. Daje sieci bez pamięci wewnętrzny takt —
/// fala stawów węża jest liniową kombinacją Sin i Cos (A·sin(φ − kλ) = A·cos kλ·Sin − A·sin kλ·Cos),
/// więc sieć może się nauczyć pełzania tak jak CPG, ale sama wymyśla przesunięcia faz i skręcanie.
/// </summary>
public sealed class ClockSensor : Sensor
{
    public const string SinPort = "Sin";
    public const string CosPort = "Cos";

    private static readonly string[] Ports = [SinPort, CosPort];
    private readonly Dictionary<string, float> _readings = new() { [SinPort] = 0, [CosPort] = 1 };

    /// <summary>Częstotliwość w Hz (domyślnie jak ręczne CPG węża).</summary>
    public float Frequency { get; set; } = 1.2f;

    public override IReadOnlyList<string> OutputPorts => Ports;

    public override IReadOnlyDictionary<string, float> Read(Entity aOwner, World aWorld)
    {
        var phase = 2 * Math.PI * Frequency * aWorld.Time;
        _readings[SinPort] = (float)Math.Sin(phase);
        _readings[CosPort] = (float)Math.Cos(phase);
        return _readings;
    }
}
