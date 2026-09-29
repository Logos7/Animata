using System.Numerics;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Animata.Core.Entities;

/// <summary>
/// Ustawienie zapisywane w pliku świata i kopiowane razem z encją: właściwość stwora (np. liczba segmentów, kolor),
/// zmysłu (np. zasięg wąsów, cel oka) albo napędu (prędkość, moment). Właściwość musi mieć publiczny getter i setter
/// typu, który umie <see cref="Settings"/> (liczby, bool, enum, Guid?, float[], Vector3). Kolejność zapisu i odtwarzania
/// = kolejność deklaracji (klasa bazowa najpierw) — ustawienie, które przebudowuje ciało (segmenty), deklaruje się
/// przed tymi, które od niego zależą.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class SettingAttribute : Attribute;

/// <summary>Zapis i odtwarzanie ustawień <see cref="SettingAttribute"/> dowolnego obiektu jako słownik JSON.</summary>
public static class Settings
{
    /// <summary>Opcje JSON ustawień: Vector3 jako [x, y, z].</summary>
    public static readonly JsonSerializerOptions Json = new() { Converters = { new Vector3Converter() } };

    private static readonly Dictionary<Type, PropertyInfo[]> Cache = [];

    /// <summary>Właściwości-ustawienia typu: najpierw z klas bazowych, w kolejności deklaracji.</summary>
    public static IReadOnlyList<PropertyInfo> Of(Type aType)
    {
        lock (Cache)
        {
            if (Cache.TryGetValue(aType, out var cached))
                return cached;
            var chain = new List<Type>();
            for (var type = aType; type is not null && type != typeof(object); type = type.BaseType)
                chain.Insert(0, type);
            var properties = chain
                .SelectMany(aLevel => aLevel.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
                    .Where(aProperty => aProperty.GetCustomAttribute<SettingAttribute>() is not null)
                    .OrderBy(aProperty => aProperty.MetadataToken))
                .ToArray();
            Cache[aType] = properties;
            return properties;
        }
    }

    /// <summary>Ustawienia obiektu (nazwa → wartość JSON); pusty słownik, gdy obiekt ich nie ma.</summary>
    public static Dictionary<string, JsonElement> Capture(object aSource)
    {
        var values = new Dictionary<string, JsonElement>();
        foreach (var property in Of(aSource.GetType()))
            values[property.Name] = JsonSerializer.SerializeToElement(property.GetValue(aSource), property.PropertyType, Json);
        return values;
    }

    /// <summary>
    /// Nadaje obiektowi zapisane ustawienia (w kolejności deklaracji). Nieznane nazwy są pomijane (plik z innej wersji),
    /// wartość złego typu → <see cref="JsonException"/>.
    /// </summary>
    public static void Apply(object aTarget, IReadOnlyDictionary<string, JsonElement>? aValues)
    {
        if (aValues is null)
            return;
        foreach (var property in Of(aTarget.GetType()))
            if (aValues.TryGetValue(property.Name, out var value))
                property.SetValue(aTarget, value.Deserialize(property.PropertyType, Json));
    }

    /// <summary>Kopiuje ustawienia z obiektu na obiekt tego samego typu (z pominięciem podanych nazw).</summary>
    public static void Copy(object aSource, object aTarget, params string[] aSkip)
    {
        var values = Capture(aSource);
        foreach (var name in aSkip)
            values.Remove(name);
        Apply(aTarget, values);
    }

    private sealed class Vector3Converter : JsonConverter<Vector3>
    {
        public override Vector3 Read(ref Utf8JsonReader aReader, Type aType, JsonSerializerOptions aOptions)
        {
            var values = JsonSerializer.Deserialize<float[]>(ref aReader, aOptions);
            return values is { Length: 3 } ? new Vector3(values[0], values[1], values[2]) : throw new JsonException("Wektor musi mieć 3 liczby.");
        }

        public override void Write(Utf8JsonWriter aWriter, Vector3 aValue, JsonSerializerOptions aOptions) =>
            JsonSerializer.Serialize(aWriter, new[] { aValue.X, aValue.Y, aValue.Z }, aOptions);
    }
}
