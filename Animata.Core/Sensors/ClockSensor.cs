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

    public ClockSensor()
    {
        SetPorts([SinPort, CosPort]);
        Readings[CosPort] = 1;
    }

    /// <summary>Częstotliwość w Hz (domyślnie jak ręczne CPG węża).</summary>
    [Setting("Częstotliwość", Unit = "Hz", Min = 0.05, Max = 10)]
    public float Frequency { get; set; } = 1.2f;

    public override IReadOnlyDictionary<string, float> Read(Entity aOwner, World aWorld)
    {
        var phase = 2 * Math.PI * Frequency * aWorld.Time;
        Readings[SinPort] = (float)Math.Sin(phase);
        Readings[CosPort] = (float)Math.Cos(phase);
        return Readings;
    }
}
