using Animata.Core.Brains;
using Animata.Core.Brains.Modules;
using Animata.Core.Entities;

namespace Animata.Core.WorldObjects;

// Mózgi wymienne: to samo ciało, inny sterownik (wąż z CPG i wąż z siecią to ten sam wąż).
public static partial class WorldObjectCatalog
{
    /// <summary>Gotowe mózgi pasujące do ciała stwora (definiuje je klasa stwora: <see cref="ActiveEntity.BrainPresets"/>).</summary>
    public static IReadOnlyList<BrainPreset> BrainPresets(ActiveEntity aCreature) => aCreature.BrainPresets;

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
