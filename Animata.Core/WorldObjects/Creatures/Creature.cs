using Animata.Core.Brains;
using Animata.Core.Brains.Modules;
using Animata.Core.Brains.Neural;
using Animata.Core.Entities;
using Animata.Core.Training;

namespace Animata.Core.WorldObjects;

/// <summary>
/// Ustawienie projektu stwora (np. liczba segmentów węża, wspinacz): opis do edytora (<see cref="Attribute"/>), typ,
/// wartość domyślna i — gdy zwykłe przechowanie wartości nie wystarcza — własny odczyt i zapis (np. zmiana liczby
/// segmentów przebudowuje ciało i mózg). Bez <see cref="Get"/>/<see cref="Set"/> wartość leży w <see cref="Creature.Values"/>.
/// </summary>
public sealed record DesignSetting(string Name, Type Type, SettingAttribute Attribute, object? Default)
{
    public Func<Creature, object?>? Get { get; init; }
    public Action<Creature, object?>? Set { get; init; }

    /// <summary>Zwykłe ustawienie przechowywane w stworze.</summary>
    public static DesignSetting Value<T>(string aName, T aDefault, SettingAttribute aAttribute) => new(aName, typeof(T), aAttribute, aDefault);
}

/// <summary>
/// Projekt stwora — wszystko, co odróżnia jeden rodzaj stwora od drugiego, bez własnej klasy: ciało (<see cref="Blueprint"/>,
/// budowane z wartości ustawień projektu), ustawienia (<see cref="Settings"/>), gotowe mózgi (<see cref="Presets"/>),
/// warunki nauki (<see cref="TrainingRig"/>) i opis. Każdy stwór to <see cref="Creature"/> z projektem. Rodzaj do rejestru
/// (<see cref="EntityTypes"/>) daje <see cref="Type"/>; stwór wczytany z pliku projektu ciała powstaje przez
/// <see cref="FromBlueprint"/>.
/// </summary>
public sealed class CreatureDesign
{
    /// <summary>Identyfikator w pliku świata (nie zmieniać — zapisane światy się do niego odwołują).</summary>
    public required string Id { get; init; }

    /// <summary>Nazwa do UI.</summary>
    public required string Name { get; init; }

    /// <summary>Podpowiedź ikony dla Studia.</summary>
    public string Icon { get; init; } = "creature";

    /// <summary>Ciało z wartości ustawień projektu (np. liczby segmentów). Dla ciała bez parametrów — zawsze to samo.</summary>
    public required Func<IReadOnlyDictionary<string, object?>, CreatureBlueprint> Blueprint { get; init; }

    /// <summary>Ustawienia projektu (po ustawieniach wspólnych stworów: Zablokowany, Kolor), w kolejności zapisu.</summary>
    public IReadOnlyList<DesignSetting> Settings { get; init; } = [];

    /// <summary>Gotowe mózgi dla tego ciała; pierwszy dostaje nowy stwór. Domyślnie ogólna sieć (<see cref="GeneralNetwork"/>).</summary>
    public Func<Creature, IReadOnlyList<BrainPreset>> Presets { get; init; } = aCreature =>
        [new("Sieć neuronowa", "wszystkie zmysły na wejściu, wszystkie napędy na wyjściu, losowe wagi", () => GeneralNetwork(aCreature))];

    /// <summary>Warunki nauki; null albo brak — ogólny rig z rejestru (<see cref="SeekRigs.Generic"/>).</summary>
    public Func<Creature, SeekRig?>? TrainingRig { get; init; }

    /// <summary>Jednolinijkowy opis ciała do UI; brak — nazwa, liczba części i stawów.</summary>
    public Func<Creature, string>? Describe { get; init; }

    private EntityType? _type;

    /// <summary>Rodzaj do rejestru: budowa = nowy <see cref="Creature"/> z tym projektem, wyposażony, z pustym mózgiem.</summary>
    public EntityType Type => _type ??= new EntityType(Id, Name, Icon, typeof(Creature), () =>
    {
        var creature = new Creature(this, new Brain());
        creature.Equip();
        return creature;
    })
    { Design = this };

    private IReadOnlyList<SettingInfo>? _settingInfos;

    /// <summary>Ustawienia projektu w postaci wspólnej dla edytora i zapisu.</summary>
    internal IReadOnlyList<SettingInfo> SettingInfos => _settingInfos ??= [.. Settings.Select(aSetting => new SettingInfo(
        aSetting.Name, aSetting.Type, aSetting.Attribute,
        aOwner => aSetting.Get is { } get ? get((Creature)aOwner) : ((Creature)aOwner).Values[aSetting.Name],
        (aOwner, aValue) =>
        {
            if (aSetting.Set is { } set)
                set((Creature)aOwner, aValue);
            else
                ((Creature)aOwner).SetValue(aSetting.Name, aValue);
        }))];

    /// <summary>Projekt ze stałego ciała (np. wczytanego z pliku JSON): ogólna sieć jako gotowy mózg, ogólny rig nauki.</summary>
    public static CreatureDesign FromBlueprint(string aId, string aName, CreatureBlueprint aBlueprint, string aIcon = "creature") => new()
    {
        Id = aId,
        Name = aName,
        Icon = aIcon,
        Blueprint = _ => aBlueprint
    };

    /// <summary>
    /// Ogólna sieć dla dowolnego ciała: każdy port każdego zmysłu na wejściu, każdy port każdego napędu na wyjściu,
    /// jedna warstwa ukryta, losowe wagi. Punkt startu dla stwora bez własnego sterownika.
    /// </summary>
    public static NeuralNetworkModule GeneralNetwork(ActiveEntity aCreature, params int[] aHidden)
    {
        var inputs = aCreature.Body.Sensors.SelectMany(aSensor => aSensor.OutputPorts).Distinct().ToArray();
        var outputs = aCreature.Body.Actuators.SelectMany(aActuator => aActuator.InputPorts).Distinct().ToArray();
        int[] hidden = aHidden.Length > 0 ? aHidden : [Math.Clamp((inputs.Length + outputs.Length) / 2, 4, 32)];
        var module = new NeuralNetworkModule(new NeuralNetwork([inputs.Length, .. hidden, outputs.Length])) { Name = "Neural" };
        module.Network.Randomize();
        module.Ports.AddRange(inputs);
        module.Inputs.AddRange(inputs.Select(aPort => new NeuralInput(aPort)));
        module.Outputs.AddRange(outputs.Select(aPort => new NeuralOutput(aPort)));
        return module;
    }

    public override string ToString() => $"CreatureDesign {Id}";
}

/// <summary>
/// Stwór z projektu (<see cref="CreatureDesign"/>): jedna klasa dla każdego rodzaju. Ciało, gniazda, ustawienia, gotowe
/// mózgi i warunki nauki bierze z projektu; wartości ustawień projektu trzyma w <see cref="Values"/>.
/// </summary>
public sealed class Creature : ArticulatedCreature, ISettingsProvider
{
    private readonly Dictionary<string, object?> _values;
    private float? _boundingRadius;

    public Creature(CreatureDesign aDesign, Brain? aBrain = null) : this(aDesign, Start(aDesign), aBrain)
    {
    }

    private Creature(CreatureDesign aDesign, (Dictionary<string, object?> Values, CreatureBlueprint Blueprint) aStart, Brain? aBrain)
        : base(aStart.Blueprint.Plan, aBrain)
    {
        Design = aDesign;
        _values = aStart.Values;
        Apply(aStart.Blueprint);
        if (aStart.Blueprint.Color is { } color)
            Color = color;
    }

    private static (Dictionary<string, object?>, CreatureBlueprint) Start(CreatureDesign aDesign)
    {
        var values = aDesign.Settings.ToDictionary(aSetting => aSetting.Name, aSetting => aSetting.Default);
        return (values, aDesign.Blueprint(values));
    }

    public CreatureDesign Design { get; }

    /// <summary>Wartości ustawień projektu (np. „Segments”, „Climber”).</summary>
    public IReadOnlyDictionary<string, object?> Values => _values;

    public T Value<T>(string aName) => (T)_values[aName]!;

    /// <summary>Zapisuje wartość ustawienia projektu bez przebudowy ciała (przebudowę robi ustawienie, które jej wymaga).</summary>
    public void SetValue(string aName, object? aValue) => _values[aName] = aValue;

    /// <summary>Projekt ciała dla bieżących wartości ustawień.</summary>
    public CreatureBlueprint Blueprint() => Design.Blueprint(_values);

    IReadOnlyList<SettingInfo> ISettingsProvider.ExtraSettings => Design.SettingInfos;

    public override float BoundingRadius => _boundingRadius ?? base.BoundingRadius;

    /// <summary>Gniazda zmysłów i napędów z projektu ciała.</summary>
    public override void Equip()
    {
        var blueprint = Blueprint();
        foreach (var sensor in blueprint.Sensors)
            Body.Sensors.Add(SlotTypes.CreateSensor(sensor, Plan));
        foreach (var actuator in blueprint.Actuators)
            Body.Actuators.Add(SlotTypes.CreateActuator(actuator, Plan));
        base.Equip();
    }

    /// <summary>
    /// Przebudowuje ciało w miejscu według projektu dla bieżących wartości (np. po zmianie liczby segmentów): plan, sztywność
    /// serw, obrys (kolor zostaje). Gniazd nie zmienia — ich porty dopasowuje ustawienie, które przebudowę zleciło (<see cref="PortRewiring"/>).
    /// </summary>
    public void Reshape()
    {
        var blueprint = Blueprint();
        Rebuild(blueprint.Plan);
        Apply(blueprint);
    }

    private void Apply(CreatureBlueprint aBlueprint)
    {
        ServoFrequency = aBlueprint.ServoFrequency;
        _boundingRadius = aBlueprint.BoundingRadius;
    }

    public override IReadOnlyList<BrainPreset> BrainPresets => Design.Presets(this);

    public override SeekRig? TrainingRig => Design.TrainingRig?.Invoke(this);

    public override string Describe() =>
        Design.Describe?.Invoke(this) ?? $"{Design.Name} · {Plan.Parts.Count} części · {JointCount} stawów · fizyka Bepu";

    public override string ToString() => $"{Design.Name} {Id}";
}

/// <summary>Wybór stworów danego projektu z listy encji.</summary>
public static class CreatureQueries
{
    /// <summary>Stwory z projektu <paramref name="aDesign"/>, w kolejności listy.</summary>
    public static IEnumerable<Creature> OfDesign(this IEnumerable<Entity> aEntities, CreatureDesign aDesign) =>
        aEntities.OfType<Creature>().Where(aCreature => aCreature.Design == aDesign);
}
