using System.Text.Json.Nodes;
using Animata.Core.Sensors;
using Animata.Core.WorldObjects;

namespace Animata.Core.Persistence;

/// <summary>
/// Przepisanie starszych plików świata na bieżący format (<see cref="WorldFile.Format"/>), krok po kroku, na drzewie JSON —
/// stare rekordy nie muszą istnieć w kodzie:
/// <list type="bullet">
/// <item>3 → 4: osobny rekord na rodzaj obiektu (box, cylinder, sphere, car, cylinderCreature, snake, spider — z polami
/// jak Whiskers, Target, Drive) → jeden kształt (rodzaj, ustawienia, sloty, części, mózg);</item>
/// <item>4 → 5: moduły stała i router z własnymi rekordami → moduły opisane stanem (<see cref="ModulesAsStates"/>).</item>
/// </list>
/// </summary>
public static class WorldFileMigration
{
    /// <summary>Najstarszy format, który da się przepisać.</summary>
    public const int Oldest = 3;

    /// <summary>Plik w formacie od <see cref="Oldest"/> do bieżącego → bieżący (bieżący wraca bez zmian).</summary>
    public static JsonObject Migrate(JsonObject aWorld)
    {
        var world = aWorld;
        if (world["Format"]?.GetValue<int>() == 3)
            world = From3(world);
        if (world["Format"]?.GetValue<int>() == 4)
        {
            ModulesAsStates(world);
            world["Format"] = 5;
        }
        return world;
    }

    /// <summary>
    /// Moduły zapisane własnymi rekordami — stała ({"$type": "constant", Id, Name, Port, Value}) i router
    /// ({"$type": "router", Id, Name, Channels, Ports}) — na moduły opisane stanem ({"$type": "state", Id, Name, State}),
    /// w całym drzewie (także we wnętrzach podgrafów). Stany w snapshotach („$type” bez Id) zostają. Także dla pliku mózgu.
    /// </summary>
    public static void ModulesAsStates(JsonNode? aNode)
    {
        // Stany stałych w snapshotach dostają port swojej stałej — inaczej snapshot nie pasowałby do jej nowego stanu.
        var ports = new Dictionary<string, JsonNode?>();
        Walk(aNode, aObject =>
        {
            if (aObject["$type"]?.GetValue<string>() == "constant" && aObject["Id"] is { } id && aObject["Port"] is { } port)
                ports[id.ToString()] = port;
        });
        Walk(aNode, aObject =>
        {
            if (aObject["ModuleId"] is { } id && aObject["State"] is JsonObject state &&
                state["$type"]?.GetValue<string>() == "constant" && state["Port"] is null && ports.TryGetValue(id.ToString(), out var port))
                state["Port"] = port?.DeepClone();
        });
        Replace(aNode);
    }

    private static void Walk(JsonNode? aNode, Action<JsonObject> aVisit)
    {
        switch (aNode)
        {
            case JsonObject node:
                aVisit(node);
                foreach (var (_, value) in node)
                    Walk(value, aVisit);
                break;
            case JsonArray array:
                foreach (var item in array)
                    Walk(item, aVisit);
                break;
        }
    }

    private static void Replace(JsonNode? aNode)
    {
        switch (aNode)
        {
            case JsonObject node:
                foreach (var (name, value) in node.ToList())
                    if (name == "Modules" && value is JsonArray modules)
                        for (var index = 0; index < modules.Count; index++)
                        {
                            if (AsState(modules[index]) is { } state)
                                modules[index] = state;
                            else
                                Replace(modules[index]);
                        }
                    else
                        Replace(value);
                break;
            case JsonArray array:
                foreach (var item in array)
                    Replace(item);
                break;
        }
    }

    private static JsonObject? AsState(JsonNode? aModule)
    {
        if (aModule is not JsonObject module || module["Id"] is null)
            return null;
        JsonObject? state = module["$type"]?.GetValue<string>() switch
        {
            "constant" => new JsonObject
            {
                ["$type"] = "constant",
                ["Value"] = module["Value"]?.DeepClone(),
                ["Port"] = module["Port"]?.DeepClone()
            },
            "router" => new JsonObject
            {
                ["$type"] = "router",
                ["Channels"] = module["Channels"]?.DeepClone(),
                ["Ports"] = module["Ports"]?.DeepClone()
            },
            _ => null
        };
        return state is null
            ? null
            : new JsonObject { ["$type"] = "state", ["Id"] = module["Id"]!.DeepClone(), ["Name"] = module["Name"]?.DeepClone(), ["State"] = state };
    }

    private static JsonObject From3(JsonObject aWorld)
    {
        var entities = new JsonArray();
        foreach (var entity in aWorld["Entities"]?.AsArray() ?? [])
            if (entity is JsonObject old)
                entities.Add(MigrateEntity(old));
        return new JsonObject
        {
            ["Format"] = 4,
            ["Name"] = aWorld["Name"]?.DeepClone(),
            ["Time"] = aWorld["Time"]?.DeepClone() ?? 0,
            ["Entities"] = entities
        };
    }

    private static JsonObject MigrateEntity(JsonObject aOld)
    {
        var type = aOld["$type"]?.GetValue<string>() ?? throw new System.Text.Json.JsonException("Obiekt bez rodzaju w pliku formatu 3.");
        var settings = new JsonObject { ["Locked"] = aOld["Locked"]?.DeepClone() ?? false };
        JsonObject? sensors = null;
        JsonObject? actuators = null;

        void Copy(params string[] aNames)
        {
            foreach (var name in aNames)
                if (aOld[name] is { } value)
                    settings[name] = value.DeepClone();
        }
        void Eye()
        {
            sensors ??= [];
            sensors["Eye"] = new JsonObject { ["TargetId"] = aOld["Target"]?.DeepClone() };
        }

        switch (type)
        {
            case "box":
                Copy("Size", "Color");
                break;
            case "cylinder":
                Copy("Radius", "Height", "Grip", "Color");
                break;
            case "sphere":
                Copy("Radius");
                break;
            case "car":
            {
                Copy("Color");
                Eye();
                var count = aOld["Whiskers"]?.GetValue<int>() ?? Car.DefaultWhiskers;
                var angles = Car.IsValidWhiskerCount(count)
                    ? Car.WhiskerAnglesFor(count)
                    : [.. RaySensor.Fan(Math.Max(1, count), Car.WhiskerSpread).Angles];
                sensors!["Whiskers"] = new JsonObject
                {
                    ["RayAngles"] = new JsonArray([.. angles.Select(aAngle => (JsonNode)aAngle)]),
                    ["Range"] = aOld["WhiskerRange"]?.DeepClone() ?? Car.WhiskerRange
                };
                if (aOld["Drive"] is JsonObject drive)
                    actuators = new JsonObject
                    {
                        ["Wheels"] = new JsonObject
                        {
                            ["MaxSpeed"] = drive["MaxSpeed"]?.DeepClone(),
                            ["MaxReverseSpeed"] = drive["MaxReverseSpeed"]?.DeepClone(),
                            ["MaxSteerAngle"] = drive["MaxSteerAngle"]?.DeepClone(),
                            ["DriveTorque"] = drive["DriveTorque"]?.DeepClone()
                        }
                    };
                break;
            }
            case "cylinderCreature":
                Copy("Color");
                Eye();
                if (aOld["Drive"] is JsonObject disk)
                    actuators = new JsonObject
                    {
                        ["Wheels"] = new JsonObject
                        {
                            ["MaxSpeed"] = disk["MaxSpeed"]?.DeepClone(),
                            ["MaxTurnSpeed"] = disk["MaxTurnSpeed"]?.DeepClone(),
                            ["DriveTorque"] = disk["DriveTorque"]?.DeepClone()
                        }
                    };
                break;
            case "snake":
                Copy("Color", "Segments", "Climber");
                Eye();
                break;
            case "spider":
                Copy("Color");
                Eye();
                break;
            default:
                throw new NotSupportedException($"Nieznany rodzaj obiektu w pliku formatu 3: {type}.");
        }

        return new JsonObject
        {
            ["Id"] = aOld["Id"]?.DeepClone(),
            ["Type"] = type,
            ["Name"] = aOld["Name"]?.DeepClone() ?? string.Empty,
            ["Position"] = aOld["Position"]?.DeepClone(),
            ["Rotation"] = aOld["Rotation"]?.DeepClone(),
            ["Settings"] = settings,
            ["Sensors"] = sensors,
            ["Actuators"] = actuators,
            ["Parts"] = aOld["Parts"]?.DeepClone(),
            ["Brain"] = aOld["Brain"]?.DeepClone()
        };
    }
}
