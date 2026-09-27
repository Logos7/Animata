using Animata.Core.Entities;
using Animata.Core.Worlds;

namespace Animata.Core.Sensors;

/// <summary>
/// Czujnik ciała. Wystawia stały zestaw portów.
/// Kontrakt: słownik zwrócony z <see cref="Read"/> jest ważny do następnego wywołania Read tego sensora.
/// </summary>
public abstract class Sensor
{
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>
    /// Nazwa miejsca w ciele („Eye”, „Whiskers”). Mózg łączy się z sensorem po tej nazwie, nie po Id,
    /// więc ten sam mózg pasuje do każdego ciała z sensorami w tych samych slotach. Domyślnie nazwa typu.
    /// </summary>
    public string Slot { get; set; }

    protected Sensor() => Slot = GetType().Name;

    /// <summary>Porty odczytów, które sensor zawsze zwraca.</summary>
    public abstract IReadOnlyList<string> OutputPorts { get; }

    public abstract IReadOnlyDictionary<string, float> Read(Entity aOwner, World aWorld);
}
