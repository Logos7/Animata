using Animata.Core.Entities;

namespace Animata.Core.Actuators;

/// <summary>
/// Efektor ciała. Przyjmuje znormalizowane komendy (zwykle [-1, 1] lub [0, 1])
/// i sam przelicza je na ruch z uwzględnieniem własnych limitów i kroku czasu.
/// </summary>
public abstract class Actuator
{
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>
    /// Nazwa miejsca w ciele („Wheels”, „Spine”). Mózg łączy się z aktuatorem po tej nazwie, nie po Id.
    /// Domyślnie nazwa typu.
    /// </summary>
    public string Slot { get; set; }

    protected Actuator() => Slot = GetType().Name;

    /// <summary>Porty komend, które aktuator rozumie.</summary>
    public abstract IReadOnlyList<string> InputPorts { get; }

    public abstract void Apply(Entity aOwner, IReadOnlyDictionary<string, float> aCommands, float aDelta);
}
