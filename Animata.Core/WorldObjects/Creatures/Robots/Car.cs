using System.Numerics;
using System.Runtime.CompilerServices;
using Animata.Core.Actuators;
using Animata.Core.Bodies;
using Animata.Core.Entities;
using Animata.Core.Sensors;
using Animata.Core.Training;

namespace Animata.Core.WorldObjects;

/// <summary>
/// Autko w fizyce: nadwozie (pudło L × W × H) na czterech kołach-bryłach z zawieszeniem. Przednie koła skręcają,
/// tylne są napędzane (<see cref="SteeringDriveActuator"/> zadaje skręt i prędkość, resztę liczy fizyka: przyspieszenie,
/// poślizg, zderzenia; napęd na cztery koła szorował przednimi i poszerzał skręt). Nie obraca się w miejscu. Dla zmysłów
/// (wąsy, szczelina do celu) to okrąg o promieniu połowy długości. Gniazda: oko „Eye”, wąsy „Whiskers” (5 w wachlarzu
/// 120°, zasięg 3 m), kierownica „Wheels”.
/// </summary>
public static class Car
{
    public const float Length = 1.2f;
    public const float Width = 0.6f;
    public const float Height = 0.35f;
    public const float WheelRadius = 0.13f;
    public const float WheelWidth = 0.1f;

    /// <summary>Oś kół od środka: ±0.32 długości — rozstaw osi 0.77 m.</summary>
    public const float AxleOffset = 0.32f;

    /// <summary>Ustawienie „Wąsy”: liczba wąsów autka.</summary>
    public const string WhiskersSetting = "Whiskers";

    /// <summary>Domyślna liczba wąsów autka: 5 promieni co 30° (−60°…+60°) — na niej dobrano parametry sterownika.</summary>
    public const int DefaultWhiskers = 5;

    /// <summary>Najwięcej wąsów autka (co 5° w wachlarzu 120°).</summary>
    public const int MaxWhiskers = 25;

    /// <summary>Szerokość wachlarza wąsów autka (rad), niezależnie od ich liczby.</summary>
    public const float WhiskerSpread = 120 * MathF.PI / 180;

    public const float WhiskerRange = 3;

    /// <summary>Dozwolone liczby wąsów autka: 1, 3, …, <see cref="MaxWhiskers"/>.</summary>
    public static IReadOnlyList<int> WhiskerCounts { get; } =
        [.. Enumerable.Range(0, MaxWhiskers / 2 + 1).Select(aIndex => 2 * aIndex + 1)];

    /// <summary>Nieparzysta, od 1 do <see cref="MaxWhiskers"/> — środkowy wąs zawsze patrzy prosto przed maskę.</summary>
    public static bool IsValidWhiskerCount(int aCount) => aCount is >= 1 and <= MaxWhiskers && aCount % 2 == 1;

    /// <summary>Rzuca <see cref="ArgumentOutOfRangeException"/>, gdy liczba wąsów nie jest nieparzysta od 1 do <see cref="MaxWhiskers"/>.</summary>
    public static void CheckWhiskerCount(int aCount, [CallerArgumentExpression(nameof(aCount))] string? aName = null)
    {
        if (!IsValidWhiskerCount(aCount))
            throw new ArgumentOutOfRangeException(aName, aCount, $"Autko ma nieparzystą liczbę wąsów od 1 do {MaxWhiskers}.");
    }

    /// <summary>Kąty wąsów autka: <paramref name="aCount"/> promieni równo w wachlarzu <see cref="WhiskerSpread"/>.</summary>
    public static float[] WhiskerAnglesFor(int aCount)
    {
        CheckWhiskerCount(aCount);
        return [.. RaySensor.Fan(aCount, WhiskerSpread).Angles];
    }

    /// <summary>Liczba wąsów stwora (promieni jego pierwszego <see cref="RaySensor"/>), 0 — bez wąsów.</summary>
    public static int WhiskerCountOf(Entity aEntity) =>
        aEntity.Body.Sensors.OfType<RaySensor>().FirstOrDefault()?.Angles.Count ?? 0;

    private static readonly CreatureBlueprint Body = new(DefaultPlan(),
        [new SlotSpec("Eye", nameof(TargetSensor)), new SlotSpec("Whiskers", nameof(RaySensor))],
        [new SlotSpec("Wheels", nameof(SteeringDriveActuator))])
    {
        BoundingRadius = 0.5f * MathF.Max(Length, Width),
        Color = new Vector3(0.24f, 0.68f, 0.9f)
    };

    public static CreatureDesign Design { get; } = new()
    {
        Id = "car",
        Name = "Autko",
        Icon = "wheel",
        Blueprint = _ => Body,
        Settings =
        [
            // Liczba wąsów to widok kątów wąsów; zmiana idzie w miejscu (WhiskerRewiring.SetCount): ten sam stwór i mózg,
            // sieć dostaje przeliczone wagi. Nie zapisuje się — w pliku są kąty.
            new DesignSetting(WhiskersSetting, typeof(int), new SettingAttribute("Wąsy")
            {
                Min = 1, Max = MaxWhiskers, Step = 2, Reshapes = true, Slots = "Whiskers", Derived = true,
                Tip = "Liczba wąsów (nieparzysta, wachlarz 120°). Mózg dopasowuje się sam: sieć dostaje przeliczone wagi, a nie losowe."
            }, DefaultWhiskers)
            {
                Get = aCar => WhiskerCountOf(aCar),
                Set = (aCar, aValue) => WhiskerRewiring.SetCount(aCar, Convert.ToInt32(aValue, System.Globalization.CultureInfo.InvariantCulture))
            }
        ],
        Presets = aCar =>
        [
            new("Sieć neuronowa", "wejścia: cel i wąsy, 2 warstwy ukryte, losowe wagi",
                () => WorldObjectCatalog.CreateCarNeuralModule(WhiskerCountOf(aCar), WorldObjectCatalog.DefaultCarHidden)),
            new("Sterownik omijania", "AvoidAndSeek: omija przeszkody wąsami i jedzie do celu",
                () => WorldObjectCatalog.CreateAvoidController(WhiskerCountOf(aCar)), true)
        ],
        TrainingRig = aCar => IsValidWhiskerCount(WhiskerCountOf(aCar))
            ? SeekRigs.CarWith(WhiskerCountOf(aCar))
            : SeekRigs.Car,
        Describe = aCar => $"Autko · {Length:0.##} × {Width:0.##} × {Height:0.##} m · obrys r {aCar.BoundingRadius:0.##}"
    };

    /// <summary>
    /// Nadwozie 3 kg z dołem na wysokości środków kół; koła 0.3 kg (tarcie 1.2) na zawieszeniu 4 cm,
    /// przednie skrętne, tylne napędzane (moment z napędu).
    /// </summary>
    public static BodyPlan DefaultPlan()
    {
        var builder = new BodyPlanBuilder()
            .Part(new PartPlan("Nadwozie", PartShape.Box, new Vector3(Length, Width, Height), 3,
                new Vector3(0, 0, WheelRadius + Height / 2), Quaternion.Identity, 0.5f));
        foreach (var (name, x, y) in new[]
        {
            ("Koło LP", AxleOffset, 1f), ("Koło PP", AxleOffset, -1f), ("Koło LT", -AxleOffset, 1f), ("Koło PT", -AxleOffset, -1f)
        })
        {
            builder.Part(new PartPlan(name, PartShape.Cylinder, new Vector3(WheelRadius, WheelWidth, 0), 0.3f,
                new Vector3(x * Length, y * (Width / 2 + WheelWidth / 2 + 0.01f), WheelRadius), Quaternion.Identity, 1.2f));
            builder.Wheel($"Oś {name[5..]}", "Nadwozie", name, aSteerable: x > 0, aDriven: x < 0, aTorque: 3);
        }
        return builder.Build();
    }
}
