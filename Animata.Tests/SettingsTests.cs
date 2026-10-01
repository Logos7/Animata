using System.Numerics;
using Animata.Core.Brains.Modules;
using Animata.Core.Entities;
using Animata.Core.Persistence;
using Animata.Core.Sensors;
using Animata.Core.WorldObjects;

namespace Animata.Tests;

/// <summary>Ustawienia opisują się same: z opisu powstaje edytor w Studiu, więc opis musi być kompletny i zgodny z wartościami.</summary>
public class SettingsTests
{
    /// <summary>Każdy obiekt z rejestru i każdy jego zmysł i napęd.</summary>
    private static IEnumerable<object> Owners()
    {
        // Tylko rodzaje z Core (testy dopisują do rejestru własne stwory).
        foreach (var type in EntityTypes.All.Where(aType => aType.ClrType.Assembly == typeof(Entity).Assembly))
        {
            var entity = type.Create();
            yield return entity;
            if (entity is ActiveEntity creature)
            {
                foreach (var sensor in creature.Body.Sensors)
                    yield return sensor;
                foreach (var actuator in creature.Body.Actuators)
                    yield return actuator;
            }
        }
    }

    [Fact]
    public void EverySetting_HasALabel_AndNumbersHaveARange_ThatHoldsTheDefault()
    {
        foreach (var owner in Owners())
            foreach (var setting in Settings.Of(owner))
            {
                var where = $"{owner.GetType().Name}.{setting.Name}";
                Assert.False(string.IsNullOrWhiteSpace(setting.Attribute.Label), $"{where}: brak nazwy do edytora");
                if (setting.Type != typeof(float) && setting.Type != typeof(int))
                    continue;
                Assert.True(double.IsFinite(setting.Attribute.Min) && double.IsFinite(setting.Attribute.Max), $"{where}: brak zakresu");
                // Wartość domyślna mieści się w zakresie — nadanie jej z powrotem nie rzuca i niczego nie zmienia.
                var before = setting.Get(owner);
                setting.Set(owner, setting.GetNumber(owner));
                Assert.Equal(before, setting.Get(owner));
            }
    }

    [Fact]
    public void Set_RejectsValuesOutsideTheRange_OrStep()
    {
        var cylinder = new Cylinder();
        var radius = Settings.Describe(typeof(Cylinder)).Single(aSetting => aSetting.Name == nameof(Cylinder.Radius));
        Assert.Throws<ArgumentOutOfRangeException>(() => radius.Set(cylinder, 6.0));
        radius.Set(cylinder, 1.25);
        Assert.Equal(1.25f, cylinder.Radius);

        var car = WorldObjectCatalog.CreateNeuralCar(Vector3.Zero, 0, null);
        var whiskers = Settings.Find(car, Car.WhiskersSetting)!;
        Assert.Equal(Car.WhiskerCounts, whiskers.Choices());
        Assert.Throws<ArgumentOutOfRangeException>(() => whiskers.Set(car, 4));
        whiskers.Set(car, 9);
        Assert.Equal(9, Car.WhiskerCountOf(car));
        Assert.Equal(3 + 9, car.Brain!.Graph.Modules.OfType<NeuralNetworkModule>().Single().Network.Layers[0]);

        // Stopnie w edytorze, radiany w ciele.
        var steering = car.Body.Actuators.OfType<Animata.Core.Actuators.SteeringDriveActuator>().Single();
        var angle = Settings.Describe(steering.GetType()).Single(aSetting => aSetting.Name == "MaxSteerAngle");
        angle.Set(steering, 45.0);
        Assert.Equal(45 * MathF.PI / 180, steering.MaxSteerAngle, 5);
        Assert.Equal(45, angle.GetNumber(steering), 3);
    }

    [Fact]
    public void DerivedSetting_IsEditable_ButNotSaved()
    {
        var car = WorldObjectCatalog.CreateNeuralCar(Vector3.Zero, 0, null, 7);
        Assert.DoesNotContain(Car.WhiskersSetting, Settings.Capture(car).Keys);
        using var world = new Animata.Core.Worlds.World();
        world.Add(car);
        using var restored = WorldFile.Restore(WorldFile.FromJson(WorldFile.ToJson(WorldFile.Capture(world)))).World;
        Assert.Equal(7, Car.WhiskerCountOf(restored.Entities.OfDesign(Car.Design).Single()));
    }

    [Fact]
    public void ReshapingSettings_NameTheirSlots()
    {
        var snake = (Creature)Snake.Design.Type.Create();
        var segments = Settings.Of(snake).Single(aSetting => aSetting.Attribute.Reshapes);
        Assert.True(segments.Concerns("Spine") && segments.Concerns("Joints"));
        Assert.False(segments.Concerns("Eye"));
        foreach (var owner in Owners().OfType<ActiveEntity>())
            foreach (var setting in Settings.Of(owner).Where(aSetting => aSetting.Attribute.Reshapes))
                Assert.All(setting.Attribute.Slots.Split(','), aSlot =>
                    Assert.True(owner.Body.FindSensor(aSlot.Trim()) is not null || owner.Body.FindActuator(aSlot.Trim()) is not null,
                        $"{owner.GetType().Name}.{setting.Name}: slot {aSlot} nie istnieje"));
        segments.Set(snake, 5);
        Assert.Equal(4, snake.Body.Sensors.OfType<JointSensor>().Single().Joints);
    }
}
