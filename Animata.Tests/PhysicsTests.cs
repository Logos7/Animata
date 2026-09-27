using System.Numerics;
using Animata.Core.Actuators;
using Animata.Core.Sensors;
using Animata.Core.WorldObjects;
using Animata.Core.Worlds;

namespace Animata.Tests;

public class PhysicsTests
{
    /// <summary>Pełny skręt kół: autko jedzie po okręgu o promieniu WheelBase / tan(MaxSteerAngle) ≈ 1.14 m.</summary>
    [Fact]
    public void Steering_FullLock_DrivesMinimumTurnCircle()
    {
        var car = new CarCreature();
        var drive = new SteeringDriveActuator();
        var command = new Dictionary<string, float>
        {
            [SteeringDriveActuator.SteerPort] = 1,
            [SteeringDriveActuator.ThrottlePort] = 0.5f
        };
        var expected = drive.WheelBase / MathF.Tan(drive.MaxSteerAngle);
        var center = new Vector3(0, expected, 0); // skręt w lewo: środek po lewej (+Y)

        for (var tick = 0; tick < 300; tick++)
        {
            drive.Apply(car, command, 1f / 60f);
            Assert.Equal(expected, Vector3.Distance(car.Body.Position, center), 0.01f);
        }
    }

    [Fact]
    public void Steering_DoesNotTurnInPlace()
    {
        var car = new CarCreature();
        new SteeringDriveActuator().Apply(car, new Dictionary<string, float> { [SteeringDriveActuator.SteerPort] = 1 }, 1);
        Assert.Equal(Quaternion.Identity, car.Body.Rotation);
        Assert.Equal(Vector3.Zero, car.Body.Position);
    }

    [Fact]
    public void Collisions_PushOnlyTheMovableEntity()
    {
        var world = new World();
        var obstacle = WorldObjectCatalog.CreateObstacle(Vector3.Zero, 0.5f);
        var creature = WorldObjectCatalog.CreateControllerSeeker(new Vector3(0.6f, 0, 0), null);
        world.Add(obstacle);
        world.Add(creature);

        world.Update(0.01f);

        Assert.Equal(Vector3.Zero, obstacle.Body.Position);
        Assert.Equal(obstacle.Radius + creature.Radius, creature.Body.Position.X, 1e-4f);
    }

    [Fact]
    public void Whiskers_ReportProximity_AndIgnoreTargets()
    {
        var world = new World();
        var car = new CarCreature();
        var whiskers = new RaySensor([0f], aRange: 3);
        car.Body.Sensors.Add(whiskers);
        world.Add(car);
        world.Add(WorldObjectCatalog.CreateObstacle(new Vector3(car.BoundingRadius + 1.5f + 0.5f, 0, 0), 0.5f));
        world.Add(WorldObjectCatalog.CreateTargetBall(new Vector3(car.BoundingRadius + 0.5f, 0, 0)));

        var proximity = whiskers.Read(car, world)[RaySensor.PortName(0)];

        Assert.Equal(0.5f, proximity, 1e-4f);           // słupek 1.5 m od obrysu przy zasięgu 3 m
        Assert.Equal(1.5f, whiskers.LastDistances[0], 1e-4f);
    }

    [Fact]
    public void TargetSensor_UsesLocalFrame()
    {
        var world = new World();
        var target = WorldObjectCatalog.CreateTargetBall(new Vector3(0, 5, 0));
        var creature = WorldObjectCatalog.CreateControllerSeeker(Vector3.Zero, target.Id);
        creature.Body.Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI / 2); // przodem do +Y
        world.Add(target);
        world.Add(creature);

        var eye = creature.Body.Sensors.OfType<TargetSensor>().Single();
        var reading = eye.Read(creature, world);

        Assert.Equal(1f, reading[TargetSensor.DirectionXPort], 1e-5f);
        Assert.Equal(0f, reading[TargetSensor.DirectionYPort], 1e-5f);
        Assert.Equal(5 - creature.Radius - target.Radius, reading[TargetSensor.GapPort], 1e-5f);
    }
}
