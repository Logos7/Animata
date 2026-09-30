using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Animata.Core.Actuators;
using Animata.Core.Bodies;
using Animata.Core.Entities;
using Animata.Core.Sensors;

namespace Animata.Core.WorldObjects;

/// <summary>Część ciała, której dotyk czuje <see cref="TouchSensor"/>: port i nazwa części w planie.</summary>
public sealed record TouchPoint(string Port, string Part);

/// <summary>
/// Gniazdo w projekcie ciała: nazwa (<see cref="Slot"/>, po niej mózg łączy się z ciałem), rodzaj zmysłu albo napędu
/// (<see cref="Type"/> — nazwa z <see cref="SlotTypes"/>) i jego ustawienia (<see cref="SettingAttribute"/>, jak w pliku świata).
/// <see cref="Touch"/> — tylko dla <see cref="TouchSensor"/>: które części czuje.
/// </summary>
public sealed record SlotSpec(string Slot, string Type)
{
    public IReadOnlyDictionary<string, JsonElement>? Settings { get; init; }
    public IReadOnlyList<TouchPoint>? Touch { get; init; }

    /// <summary>Ustawienia gniazda z par (nazwa, wartość), np. <c>SlotSpec.Values(("Frequency", 2.5f))</c>.</summary>
    public static IReadOnlyDictionary<string, JsonElement> Values(params (string Name, object Value)[] aValues) =>
        aValues.ToDictionary(aPair => aPair.Name, aPair => JsonSerializer.SerializeToElement(aPair.Value, aPair.Value.GetType(), Entities.Settings.Json));
}

/// <summary>
/// Projekt ciała stwora jako dane: plan (części i stawy), gniazda zmysłów i napędów, sztywność serw, obrys i kolor.
/// Da się go zapisać do JSON i wczytać (<see cref="ToJson"/>, <see cref="FromJson"/>) — stwór z pliku nie potrzebuje
/// własnej klasy (<see cref="CreatureDesign.FromBlueprint"/>). Ciała zależne od parametru (liczba segmentów węża) buduje
/// projekt stwora z wartości swoich ustawień (<see cref="CreatureDesign.Blueprint"/>).
/// </summary>
public sealed record CreatureBlueprint(BodyPlan Plan, IReadOnlyList<SlotSpec> Sensors, IReadOnlyList<SlotSpec> Actuators)
{
    /// <summary>Sztywność serw stawów (Hz) — lekkie nogi dźwigające tułów potrzebują sztywniejszych (pająk: 60).</summary>
    public float ServoFrequency { get; init; } = 30;

    /// <summary>Promień obrysu do zmysłów i szczeliny do celu; null — promień korzenia planu.</summary>
    public float? BoundingRadius { get; init; }

    /// <summary>Kolor nowego stwora; null — domyślny.</summary>
    public Vector3? Color { get; init; }

    private static readonly JsonSerializerOptions Options = new(Entities.Settings.Json)
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() }
    };

    public string ToJson() => JsonSerializer.Serialize(this, Options);

    /// <summary>Projekt z JSON; plan jest sprawdzany jak z <see cref="BodyPlanBuilder"/> (błąd → <see cref="ArgumentException"/>).</summary>
    public static CreatureBlueprint FromJson(string aJson)
    {
        var blueprint = JsonSerializer.Deserialize<CreatureBlueprint>(aJson, Options) ?? throw new JsonException("Pusty projekt ciała.");
        var builder = new BodyPlanBuilder();
        foreach (var part in blueprint.Plan.Parts)
            builder.Part(part);
        builder.Joints(blueprint.Plan.Joints);
        return blueprint with { Plan = builder.Build() };
    }
}

/// <summary>
/// Rodzaje zmysłów i napędów, które projekt ciała może wskazać po nazwie. Czucie stawów i kręgosłup dostają tyle stawów,
/// ile ma plan. Nowy rodzaj (np. z testu albo innego projektu) dopisuje się przez <see cref="Register"/>.
/// </summary>
public static class SlotTypes
{
    private static readonly Dictionary<string, Func<SlotSpec, BodyPlan, Sensor>> SensorFactories = new()
    {
        [nameof(TargetSensor)] = (_, _) => new TargetSensor(),
        [nameof(RaySensor)] = (_, _) => new RaySensor(WorldObjectCatalog.WhiskerAnglesFor(WorldObjectCatalog.DefaultWhiskers), WorldObjectCatalog.WhiskerRange),
        [nameof(JointSensor)] = (_, aPlan) => new JointSensor(aPlan.Joints.Count),
        [nameof(ClockSensor)] = (_, _) => new ClockSensor(),
        [nameof(FeelSensor)] = (_, _) => new FeelSensor(),
        [nameof(TouchSensor)] = (aSpec, _) => new TouchSensor([.. (aSpec.Touch ?? []).Select(aPoint => (aPoint.Port, aPoint.Part))])
    };

    private static readonly Dictionary<string, Func<SlotSpec, BodyPlan, Actuator>> ActuatorFactories = new()
    {
        [nameof(DiskDriveActuator)] = (_, _) => new DiskDriveActuator(),
        [nameof(SteeringDriveActuator)] = (_, _) => new SteeringDriveActuator(),
        [nameof(SpineActuator)] = (_, aPlan) => new SpineActuator(aPlan.Joints.Count)
    };

    public static IEnumerable<string> Sensors
    {
        get
        {
            lock (SensorFactories)
                return [.. SensorFactories.Keys];
        }
    }

    public static IEnumerable<string> Actuators
    {
        get
        {
            lock (ActuatorFactories)
                return [.. ActuatorFactories.Keys];
        }
    }

    /// <summary>Dopisuje rodzaj zmysłu (nazwa zajęta → <see cref="ArgumentException"/>).</summary>
    public static void Register(string aName, Func<SlotSpec, BodyPlan, Sensor> aCreate)
    {
        lock (SensorFactories)
            if (!SensorFactories.TryAdd(aName, aCreate))
                throw new ArgumentException($"Zmysł „{aName}” już jest.", nameof(aName));
    }

    /// <summary>Dopisuje rodzaj napędu (nazwa zajęta → <see cref="ArgumentException"/>).</summary>
    public static void Register(string aName, Func<SlotSpec, BodyPlan, Actuator> aCreate)
    {
        lock (ActuatorFactories)
            if (!ActuatorFactories.TryAdd(aName, aCreate))
                throw new ArgumentException($"Napęd „{aName}” już jest.", nameof(aName));
    }

    public static bool IsRegistered(string aName)
    {
        lock (SensorFactories)
            lock (ActuatorFactories)
                return SensorFactories.ContainsKey(aName) || ActuatorFactories.ContainsKey(aName);
    }

    /// <summary>Zmysł z gniazda: rodzaj, nazwa gniazda, ustawienia.</summary>
    public static Sensor CreateSensor(SlotSpec aSpec, BodyPlan aPlan)
    {
        Func<SlotSpec, BodyPlan, Sensor>? create;
        lock (SensorFactories)
            SensorFactories.TryGetValue(aSpec.Type, out create);
        var sensor = (create ?? throw new NotSupportedException($"Nieznany zmysł: {aSpec.Type}."))(aSpec, aPlan);
        sensor.Slot = aSpec.Slot;
        Entities.Settings.Apply(sensor, aSpec.Settings);
        return sensor;
    }

    /// <summary>Napęd z gniazda: rodzaj, nazwa gniazda, ustawienia.</summary>
    public static Actuator CreateActuator(SlotSpec aSpec, BodyPlan aPlan)
    {
        Func<SlotSpec, BodyPlan, Actuator>? create;
        lock (ActuatorFactories)
            ActuatorFactories.TryGetValue(aSpec.Type, out create);
        var actuator = (create ?? throw new NotSupportedException($"Nieznany napęd: {aSpec.Type}."))(aSpec, aPlan);
        actuator.Slot = aSpec.Slot;
        Entities.Settings.Apply(actuator, aSpec.Settings);
        return actuator;
    }
}
