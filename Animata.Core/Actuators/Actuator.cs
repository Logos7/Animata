using Animata.Core.Entities;

namespace Animata.Core.Actuators;

/// <summary>
/// Efektor ciała. Przyjmuje znormalizowane komendy (zwykle [-1, 1] lub [0, 1])
/// i sam przelicza je na ruch z uwzględnieniem własnych limitów i kroku czasu.
/// </summary>
public abstract class Actuator
{
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>Porty komend, które aktuator rozumie.</summary>
    public abstract IReadOnlyList<string> InputPorts { get; }

    public abstract void Apply(Entity aOwner, IReadOnlyDictionary<string, float> aCommands, float aDelta);
}
