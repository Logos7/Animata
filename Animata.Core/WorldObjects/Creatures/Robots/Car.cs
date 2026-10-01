using System.Numerics;
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
                Min = 1, Max = WorldObjectCatalog.MaxWhiskers, Step = 2, Reshapes = true, Slots = "Whiskers", Derived = true,
                Tip = "Liczba wąsów (nieparzysta, wachlarz 120°). Mózg dopasowuje się sam: sieć dostaje przeliczone wagi, a nie losowe."
            }, WorldObjectCatalog.DefaultWhiskers)
            {
                Get = aCar => WorldObjectCatalog.WhiskerCountOf(aCar),
                Set = (aCar, aValue) => WhiskerRewiring.SetCount(aCar, Convert.ToInt32(aValue, System.Globalization.CultureInfo.InvariantCulture))
            }
        ],
        Presets = aCar =>
        [
            new("Sieć neuronowa", "wejścia: cel i wąsy, 2 warstwy ukryte, losowe wagi",
                () => WorldObjectCatalog.CreateCarNeuralModule(WorldObjectCatalog.WhiskerCountOf(aCar), WorldObjectCatalog.DefaultCarHidden)),
            new("Sterownik omijania", "AvoidAndSeek: omija przeszkody wąsami i jedzie do celu",
                () => WorldObjectCatalog.CreateAvoidController(WorldObjectCatalog.WhiskerCountOf(aCar)), true)
        ],
        TrainingRig = aCar => WorldObjectCatalog.IsValidWhiskerCount(WorldObjectCatalog.WhiskerCountOf(aCar))
            ? SeekRigs.CarWith(WorldObjectCatalog.WhiskerCountOf(aCar))
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
