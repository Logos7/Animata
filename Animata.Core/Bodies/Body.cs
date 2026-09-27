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
    public Vector3 Scale { get; set; } = Vector3.One;
}
