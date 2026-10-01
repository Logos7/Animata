using System.Numerics;
using Animata.Core.Actuators;
using Animata.Core.Sensors;

namespace Animata.Core.Bodies;

public class Body
{
    public List<Sensor> Sensors { get; } = [];
    public List<Actuator> Actuators { get; } = [];
    public Vector3 Position { get; set; }
    public Quaternion Rotation { get; set; } = Quaternion.Identity;

    /// <summary>Sensor w danym slocie albo null.</summary>
    public Sensor? FindSensor(string aSlot) => Sensors.Find(aSensor => aSensor.Slot == aSlot);

    /// <summary>Aktuator w danym slocie albo null.</summary>
    public Actuator? FindActuator(string aSlot) => Actuators.Find(aActuator => aActuator.Slot == aSlot);

    /// <summary>Wszystkie gniazda: zmysły, potem napędy (slot, obiekt — np. do ustawień).</summary>
    public IEnumerable<(string Slot, object Owner)> Slots =>
        Sensors.Select(aSensor => (aSensor.Slot, (object)aSensor)).Concat(Actuators.Select(aActuator => (aActuator.Slot, (object)aActuator)));
}
