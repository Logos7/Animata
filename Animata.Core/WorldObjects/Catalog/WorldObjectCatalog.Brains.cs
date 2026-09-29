using Animata.Core.Brains;
using Animata.Core.Brains.Modules;
using Animata.Core.Entities;

namespace Animata.Core.WorldObjects;

/// <summary>
/// Gotowy mózg dla ciała: nazwa do UI i budowa sterownika pasującego do tego ciała (bieżąca liczba wąsów, stawów).
/// <paramref name="HandTuned"/> — sterownik ma ręcznie dobrane parametry (zapisywane jako snapshot „ręczne parametry”).
/// </summary>
public sealed record BrainPreset(string Name, string Description, Func<BrainModule> Create, bool HandTuned = false);

// Mózgi wymienne: to samo ciało, inny sterownik (wąż z CPG i wąż z siecią to ten sam wąż).
public static partial class WorldObjectCatalog
{
    /// <summary>Gotowe mózgi pasujące do ciała stwora (pusta lista — nieznane ciało).</summary>
    public static IReadOnlyList<BrainPreset> BrainPresets(ActiveEntity aCreature) => aCreature switch
    {
        CarCreature car =>
        [
            new("Sieć neuronowa", "wejścia: cel i wąsy, 2 warstwy ukryte, losowe wagi",
                () => CreateCarNeuralModule(WhiskerCountOf(car), DefaultCarHidden)),
            new("Sterownik omijania", "AvoidAndSeek: omija przeszkody wąsami i jedzie do celu",
                () => CreateAvoidController(WhiskerCountOf(car)), true)
        ],
        CylinderCreature =>
        [
            new("Sieć neuronowa", "wejścia: cel, 2 warstwy ukryte, losowe wagi", () => CreateCylinderNeuralModule(DefaultCylinderHidden)),
            new("Sieć z ręcznymi wagami", "3-6-2 ustawiona ręcznie tak, że jedzie do celu", CreateCylinderNeuralModule, true),
            new("Sterownik celu", "ApproachTarget: skręca do celu i jedzie", () => new ApproachTargetModule { Name = "Approach" }, true)
        ],
        SnakeCreature snake =>
        [
            new("Sieć neuronowa", "zegar rytmu, cel i czucie terenu, 2 warstwy ukryte, losowe wagi",
                () => CreateSnakeNeuralModule(snake.Segments, SnakeHiddenLayers)),
            new("CPG · pełzanie", "generator fali: 6 parametrów (amplituda, częstotliwość, fala, skręt, pochylenie)",
                () => CreateCpg(snake.Segments), true),
            new("CPG · toczenie (wspinaczka)", "zwój toczy się po pniu w górę — dla węża owiniętego wokół cylindra",
                () => CreateClimbingCpg(snake.Segments), true)
        ],
        SpiderCreature =>
        [
            new("Sieć neuronowa", "zegar rytmu i cel, 2 warstwy ukryte, losowe wagi",
                () => CreateSpiderNeuralModule(false, DefaultSpiderHidden)),
            new("Generator chodu (kłus)", "6 parametrów: krok, uniesienie, kolano, częstotliwość, skręt", () => new GaitModule { Name = "Chód" }, true)
        ],
        _ => []
    };

    /// <summary>
    /// Podmienia mózg stwora na świeży z gotowego: ten sam obiekt <see cref="Brain"/> (ciało i węzły ciała zostają),
    /// stare moduły, połączenia i snapshoty znikają. Sterownik z ręcznymi parametrami dostaje snapshot „ręczne parametry”.
    /// Zwraca wstawiony sterownik.
    /// </summary>
    public static BrainModule InstallBrain(ActiveEntity aCreature, BrainPreset aPreset)
    {
        var brain = aCreature.Brain ?? throw new InvalidOperationException($"{aCreature} nie ma mózgu.");
        var controller = aPreset.Create();
        var before = Persistence.WorldFile.CaptureBrain(brain);
        try
        {
            brain.Clear();
            BuildBrain(brain, controller);
        }
        catch
        {
            Persistence.WorldFile.RestoreBrain(brain, before);
            throw;
        }
        if (aPreset.HandTuned && controller.CaptureState() is not null)
            brain.Capture("ręczne parametry", controller);
        brain.Reset();
        return controller;
    }
}
