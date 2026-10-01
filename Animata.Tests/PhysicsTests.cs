using System.Numerics;
using Animata.Core.Actuators;
using Animata.Core.Sensors;
using Animata.Core.WorldObjects;
using Animata.Core.Worlds;
using static Animata.Tests.TestWorlds;

namespace Animata.Tests;

public class PhysicsTests
{
    /// <summary>Jazda przez fizykę: autko z napędem, komenda co tick (bez mózgu).</summary>
    private static List<Vector2> Drive(Creature aCar, World aWorld, float aSteer, float aThrottle, float aSeconds)
    {
        var drive = aCar.Body.Actuators.OfType<SteeringDriveActuator>().Single();
        var command = new Dictionary<string, float>
        {
            [SteeringDriveActuator.SteerPort] = aSteer,
            [SteeringDriveActuator.ThrottlePort] = aThrottle
        };
        var path = new List<Vector2>();
        for (var tick = 0; tick < aSeconds * 30; tick++)
        {
            drive.Apply(aCar, command, Delta);
            aWorld.Update(Delta);
            path.Add(new Vector2(aCar.Body.Position.X, aCar.Body.Position.Y));
        }
        return path;
    }

    private static Creature PhysicalCar(World aWorld)
    {
        var car = new Creature(Car.Design);
        car.Body.Actuators.Add(new SteeringDriveActuator());
        aWorld.Add(car);
        return car;
    }

    [Fact]
    [Trait(KnownFailures.Trait, KnownFailures.BepuBeta29)]
    public void Car_DrivesStraightAtItsMaxSpeed()
    {
        using var world = TestWorlds.Floor(60);
        var car = PhysicalCar(world);
        Drive(car, world, 0, 1, 4);
        var speed = car.Body.Actuators.OfType<SteeringDriveActuator>().Single().MaxSpeed;
        Assert.InRange(car.Body.Position.X, speed * 4 * 0.8f, speed * 4);
        Assert.InRange(MathF.Abs(car.Body.Position.Y), 0, 0.1f);
    }

    /// <summary>Pełny skręt: koło o promieniu blisko rozstaw osi / tan(kąt) ≈ 1.1 m (fizyka dokłada trochę poślizgu).</summary>
    [Fact]
    public void Car_FullLock_DrivesATightLeftCircle()
    {
        using var world = TestWorlds.Floor(60);
        var car = PhysicalCar(world);
        var path = Drive(car, world, 1, 0.5f, 12).Skip(90).ToList();
        var center = new Vector2(path.Average(aPoint => aPoint.X), path.Average(aPoint => aPoint.Y));
        var radius = path.Average(aPoint => Vector2.Distance(aPoint, center));

        Assert.InRange(radius, 1.0f, 1.9f);
        Assert.True(center.Y > 0.5f, $"skręt w lewo: środek po lewej, a jest {center}");
    }

    [Fact]
    [Trait(KnownFailures.Trait, KnownFailures.BepuBeta29)]
    public void Cylinder_TurnsInPlaceToTheLeft()
    {
        using var world = TestWorlds.Floor(60);
        var cylinder = new Creature(Disc.Design);
        var drive = new DiskDriveActuator();
        cylinder.Body.Actuators.Add(drive);
        world.Add(cylinder);
        for (var tick = 0; tick < 15; tick++)
        {
            drive.Apply(cylinder, new Dictionary<string, float> { [DiskDriveActuator.TurnPort] = 1 }, Delta);
            world.Update(Delta);
        }
        var heading = Vector3.Transform(Vector3.UnitX, cylinder.Body.Rotation);
        Assert.InRange(MathF.Atan2(heading.Y, heading.X), 0.2f, 1.5f);
        Assert.InRange(new Vector2(cylinder.Body.Position.X, cylinder.Body.Position.Y).Length(), 0, 0.1f);
    }

    [Fact]
    public void Collisions_ArePhysical_AndPostsDoNotMove()
    {
        using var world = TestWorlds.Floor(60);
        var obstacle = WorldObjectCatalog.CreateCylinder(Vector3.Zero, 0.5f);
        var creature = WorldObjectCatalog.CreateControllerSeeker(new Vector3(0.9f, 0, 0), null);
        world.Add(obstacle);
        world.Add(creature);

        for (var tick = 0; tick < 30; tick++)
            world.Update(Delta);

        Assert.Equal(Vector3.Zero, obstacle.Body.Position);
        var distance = new Vector2(creature.Body.Position.X, creature.Body.Position.Y).Length();
        Assert.True(distance >= obstacle.Radius + Disc.Radius - 0.05f, $"distance {distance:0.00}");
    }

    [Fact]
    public void Whiskers_ReportProximity_AndIgnoreTargets()
    {
        using var world = new World();
        var car = new Creature(Car.Design);
        var whiskers = new RaySensor([0f], aRange: 3);
        car.Body.Sensors.Add(whiskers);
        world.Add(car);
        world.Add(WorldObjectCatalog.CreateCylinder(new Vector3(car.BoundingRadius + 1.5f + 0.5f, 0, 0), 0.5f));
        world.Add(WorldObjectCatalog.CreateSphere(new Vector3(car.BoundingRadius + 0.5f, 0, 0)));

        var proximity = whiskers.Read(car, world)[RaySensor.PortName(0)];

        Assert.Equal(0.5f, proximity, 1e-4f);           // słupek 1.5 m od obrysu przy zasięgu 3 m
        Assert.Equal(1.5f, whiskers.LastDistances[0], 1e-4f);
    }

}
