using System.Text.Json.Nodes;
using Animata.Core.Sensors;
using Animata.Core.WorldObjects;

namespace Animata.Core.Persistence;

/// <summary>
/// Przepisanie pliku świata z formatu 3 (osobny rekord na rodzaj obiektu: box, cylinder, sphere, car, cylinderCreature,
/// snake, spider — z polami jak Whiskers, Target, Drive) na format 4 (jeden kształt: rodzaj, ustawienia, sloty, części, mózg).
/// Mózg (BrainDocument) się nie zmienił. Działa na drzewie JSON, więc stare rekordy nie muszą istnieć w kodzie.
/// </summary>
public static class WorldFileMigration
{
    /// <summary>Format, który ta klasa przepisuje.</summary>
    public const int From = 3;

    public static JsonObject Migrate(JsonObject aWorld)
    {
        var entities = new JsonArray();
        foreach (var entity in aWorld["Entities"]?.AsArray() ?? [])
            if (entity is JsonObject old)
                entities.Add(MigrateEntity(old));
        return new JsonObject
        {
            ["Format"] = WorldFile.Format,
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
                var count = aOld["Whiskers"]?.GetValue<int>() ?? WorldObjectCatalog.DefaultWhiskers;
                var angles = WorldObjectCatalog.IsValidWhiskerCount(count)
                    ? WorldObjectCatalog.WhiskerAnglesFor(count)
                    : [.. RaySensor.Fan(Math.Max(1, count), WorldObjectCatalog.WhiskerSpread).Angles];
                sensors!["Whiskers"] = new JsonObject
                {
                    ["RayAngles"] = new JsonArray([.. angles.Select(aAngle => (JsonNode)aAngle)]),
                    ["Range"] = aOld["WhiskerRange"]?.DeepClone() ?? WorldObjectCatalog.WhiskerRange
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
