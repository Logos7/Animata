using System.Numerics;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Animata.Core.Entities;

/// <summary>
/// Ustawienie zapisywane w pliku świata i kopiowane razem z encją: właściwość stwora (np. liczba segmentów, kolor),
/// zmysłu (np. zasięg wąsów, cel oka) albo napędu (prędkość, moment). Właściwość musi mieć publiczny getter i setter
/// typu, który umie <see cref="Settings"/> (liczby, bool, enum, Guid?, float[], Vector3). Kolejność zapisu = kolejność
/// deklaracji (klasa bazowa najpierw). Ustawienia jednego obiektu nie mogą zależeć od siebie nawzajem: ciało przebudowują
/// tylko ustawienia stwora (np. segmenty), a ustawienia zmysłów i napędów, które od kształtu ciała zależą, nadaje się
/// w osobnym, późniejszym kroku (<see cref="WorldObjects.Spawn"/>).
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class SettingAttribute(string aLabel = "") : Attribute
{
    /// <summary>Nazwa w edytorze (pusta — nazwa właściwości).</summary>
    public string Label { get; } = aLabel;

    /// <summary>Jednostka w edytorze (już po <see cref="Scale"/>), np. „m”, „°”.</summary>
    public string Unit { get; set; } = "";

    /// <summary>Podpowiedź w edytorze.</summary>
    public string Tip { get; set; } = "";

    /// <summary>Zakres liczby w jednostkach edytora (NaN — bez ograniczenia). Wartość spoza zakresu jest odrzucana.</summary>
    public double Min { get; set; } = double.NaN;

    public double Max { get; set; } = double.NaN;

    /// <summary>Krok liczby całkowitej (&gt; 0 — edytor pokazuje listę Min, Min + Step, …, Max i odrzuca wartości spoza niej).</summary>
    public double Step { get; set; }

    /// <summary>Wartość w edytorze = wartość właściwości × Scale (np. radiany → stopnie).</summary>
    public double Scale { get; set; } = 1;

    /// <summary>Wektor jako kolor (edytor: próbki), a nie trzy liczby.</summary>
    public bool Color { get; set; }

    /// <summary>
    /// Zmienia kształt ciała i porty (segmenty, liczba wąsów): mózg i snapshoty są przeliczane, trwająca nauka musi
    /// na czas zmiany stanąć. <see cref="Slots"/> — sloty, których porty zmienia (edytor pokazuje ustawienie także przy nich).
    /// </summary>
    public bool Reshapes { get; set; }

    /// <summary>Sloty (po przecinku), których dotyczy ustawienie stwora — np. segmenty węża: „Spine,Joints”.</summary>
    public string Slots { get; set; } = "";

    /// <summary>Widok innych ustawień (np. liczba wąsów = liczba kątów): edytowalne, ale nie zapisuje się ani nie kopiuje.</summary>
    public bool Derived { get; set; }
}

/// <summary>Ustawienie obiektu z opisem do edytora: właściwość i jej <see cref="SettingAttribute"/>.</summary>
public sealed record SettingInfo(PropertyInfo Property, SettingAttribute Attribute)
{
    public string Name => Property.Name;
    public string Label => Attribute.Label.Length > 0 ? Attribute.Label : Property.Name;
    public Type Type => Property.PropertyType;
    public bool IsReadOnly => Type == typeof(float[]);

    /// <summary>Czy ustawienie stwora dotyczy slotu (<see cref="SettingAttribute.Slots"/>).</summary>
    public bool Concerns(string aSlot) =>
        Attribute.Slots.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Contains(aSlot);

    public object? Get(object aOwner) => Property.GetValue(aOwner);

    /// <summary>Liczba w jednostkach edytora (× <see cref="SettingAttribute.Scale"/>).</summary>
    public double GetNumber(object aOwner) => System.Convert.ToDouble(Get(aOwner)) * Attribute.Scale;

    /// <summary>
    /// Nadaje wartość. Liczbę sprawdza z zakresem i krokiem (<see cref="ArgumentOutOfRangeException"/>), resztę sprawdza
    /// setter właściwości (np. wymiary klocka). Wartość tego samego typu co właściwość albo liczba w jednostkach edytora.
    /// </summary>
    public void Set(object aOwner, object? aValue)
    {
        if (IsReadOnly)
            throw new InvalidOperationException($"Ustawienie „{Label}” jest tylko do odczytu w edytorze.");
        if (Type == typeof(float) || Type == typeof(int) || Type == typeof(double))
        {
            var shown = System.Convert.ToDouble(aValue);
            var attribute = Attribute;
            if (!double.IsFinite(shown) || shown < attribute.Min || shown > attribute.Max ||
                (attribute.Step > 0 && Math.Abs((shown - (double.IsNaN(attribute.Min) ? 0 : attribute.Min)) / attribute.Step % 1) > 1e-9))
                throw new ArgumentOutOfRangeException(Name, aValue, $"„{Label}”: {Range()}.");
            var value = shown / attribute.Scale;
            if (Type == typeof(float))
                Property.SetValue(aOwner, (float)value);
            else if (Type == typeof(int))
                Property.SetValue(aOwner, (int)Math.Round(value));
            else
                Property.SetValue(aOwner, value);
            return;
        }
        Property.SetValue(aOwner, aValue);
    }

    /// <summary>Wartości listy liczby całkowitej z krokiem (Min, Min + Step, …, Max) albo null.</summary>
    public IReadOnlyList<int>? Choices() =>
        Type == typeof(int) && Attribute.Step > 0 && double.IsFinite(Attribute.Min) && double.IsFinite(Attribute.Max)
            ? [.. Enumerable.Range(0, (int)((Attribute.Max - Attribute.Min) / Attribute.Step) + 1).Select(aIndex => (int)(Attribute.Min + aIndex * Attribute.Step))]
            : null;

    /// <summary>Opis zakresu do komunikatu.</summary>
    public string Range()
    {
        var unit = Attribute.Unit.Length > 0 ? " " + Attribute.Unit : string.Empty;
        var step = Attribute.Step > 0 ? $" co {Attribute.Step:0.###}" : string.Empty;
        return (double.IsNaN(Attribute.Min), double.IsNaN(Attribute.Max)) switch
        {
            (false, false) => $"od {Attribute.Min:0.###} do {Attribute.Max:0.###}{unit}{step}",
            (false, true) => $"co najmniej {Attribute.Min:0.###}{unit}",
            (true, false) => $"najwyżej {Attribute.Max:0.###}{unit}",
            _ => "dowolna liczba"
        };
    }
}

/// <summary>Zapis i odtwarzanie ustawień <see cref="SettingAttribute"/> dowolnego obiektu jako słownik JSON.</summary>
public static class Settings
{
    /// <summary>Opcje JSON ustawień: Vector3 jako [x, y, z].</summary>
    public static readonly JsonSerializerOptions Json = new() { Converters = { new Vector3Converter() } };

    private static readonly Dictionary<Type, SettingInfo[]> Cache = [];

    /// <summary>Właściwości-ustawienia typu: najpierw z klas bazowych, w kolejności deklaracji.</summary>
    public static IReadOnlyList<PropertyInfo> Of(Type aType) => [.. Describe(aType).Select(aSetting => aSetting.Property)];

    /// <summary>Ustawienia typu z opisem do edytora: najpierw z klas bazowych, w kolejności deklaracji.</summary>
    public static IReadOnlyList<SettingInfo> Describe(Type aType)
    {
        lock (Cache)
        {
            if (Cache.TryGetValue(aType, out var cached))
                return cached;
            var chain = new List<Type>();
            for (var type = aType; type is not null && type != typeof(object); type = type.BaseType)
                chain.Insert(0, type);
            var settings = chain
                .SelectMany(aLevel => aLevel.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
                    .Select(aProperty => (Property: aProperty, Attribute: aProperty.GetCustomAttribute<SettingAttribute>()))
                    .Where(aPair => aPair.Attribute is not null)
                    .OrderBy(aPair => aPair.Property.MetadataToken)
                    .Select(aPair => new SettingInfo(aPair.Property, aPair.Attribute!)))
                .ToArray();
            Cache[aType] = settings;
            return settings;
        }
    }

    /// <summary>Ustawienia obiektu (nazwa → wartość JSON); pusty słownik, gdy obiekt ich nie ma. Pochodne są pomijane.</summary>
    public static Dictionary<string, JsonElement> Capture(object aSource)
    {
        var values = new Dictionary<string, JsonElement>();
        foreach (var setting in Describe(aSource.GetType()))
            if (!setting.Attribute.Derived)
                values[setting.Name] = JsonSerializer.SerializeToElement(setting.Get(aSource), setting.Type, Json);
        return values;
    }

    /// <summary>
    /// Nadaje obiektowi zapisane ustawienia (w kolejności deklaracji). Nieznane i pochodne nazwy są pomijane (plik z innej
    /// wersji), wartość złego typu → <see cref="JsonException"/>. Zakresów edytora nie sprawdza (to robi <see cref="SettingInfo.Set"/>).
    /// </summary>
    public static void Apply(object aTarget, IReadOnlyDictionary<string, JsonElement>? aValues)
    {
        if (aValues is null)
            return;
        foreach (var setting in Describe(aTarget.GetType()))
            if (!setting.Attribute.Derived && aValues.TryGetValue(setting.Name, out var value))
                setting.Property.SetValue(aTarget, value.Deserialize(setting.Type, Json));
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
