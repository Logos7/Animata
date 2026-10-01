using System.Numerics;
using Animata.Core.Entities;
using Animata.Core.Worlds;

namespace Animata.Core.Sensors;

/// <summary>
/// Czujnik ciała. Wystawia porty (<see cref="SetPorts"/>) i co odczyt wpisuje do <see cref="Readings"/> wartości każdego z nich.
/// Kontrakt: słownik zwrócony z <see cref="Read"/> jest ważny do następnego wywołania Read tego sensora.
/// </summary>
public abstract class Sensor
{
    private string[] _ports = [];

    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>
    /// Nazwa miejsca w ciele („Eye”, „Whiskers”). Mózg łączy się z sensorem po tej nazwie, nie po Id,
    /// więc ten sam mózg pasuje do każdego ciała z sensorami w tych samych slotach. Domyślnie nazwa typu.
    /// </summary>
    public string Slot { get; set; }

    protected Sensor() => Slot = GetType().Name;

    /// <summary>Porty odczytów, które sensor zawsze zwraca.</summary>
    public IReadOnlyList<string> OutputPorts => _ports;

    /// <summary>Odczyty po portach (zwracane z <see cref="Read"/>).</summary>
    protected Dictionary<string, float> Readings { get; } = [];

    /// <summary>Nowe porty; odczyty od zera.</summary>
    protected void SetPorts(IEnumerable<string> aPorts)
    {
        _ports = [.. aPorts];
        Readings.Clear();
        foreach (var port in _ports)
            Readings[port] = 0;
    }

    public abstract IReadOnlyDictionary<string, float> Read(Entity aOwner, World aWorld);

    /// <summary>Kierunek przodu (oś X orientacji) spłaszczony na ziemię i znormalizowany; pionowy przód — oś X świata.</summary>
    protected static Vector2 Heading(Vector3 aForward)
    {
        var flat = new Vector2(aForward.X, aForward.Y);
        return flat.LengthSquared() > 1e-6f ? Vector2.Normalize(flat) : Vector2.UnitX;
    }
}
