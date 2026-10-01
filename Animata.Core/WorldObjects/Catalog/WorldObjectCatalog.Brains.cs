using Animata.Core.Brains;
using Animata.Core.Brains.Modules;
using Animata.Core.Entities;

namespace Animata.Core.WorldObjects;

// Mózgi wymienne: to samo ciało, inny sterownik (wąż z CPG i wąż z siecią to ten sam wąż).
public static partial class WorldObjectCatalog
{
    /// <summary>
    /// Podmienia mózg stwora na świeży z gotowego: ten sam obiekt <see cref="Brain"/> (ciało i węzły ciała zostają),
    /// stare moduły, połączenia i snapshoty znikają. Sterownik z ręcznymi parametrami dostaje snapshot „ręczne parametry”.
    /// Zwraca wstawiony sterownik.
    /// </summary>
    public static BrainModule InstallBrain(ActiveEntity aCreature, BrainPreset aPreset)
    {
        var brain = aCreature.Brain ?? throw new InvalidOperationException($"{aCreature} nie ma mózgu.");
        var before = Persistence.WorldFile.CaptureBrain(brain);
        IReadOnlyList<BrainModule> modules;
        try
        {
            brain.Clear();
            if (aPreset.Build is { } build)
            {
                modules = build(brain);
                brain.Graph.Validate();
            }
            else
            {
                var controller = aPreset.Create();
                BuildBrain(brain, controller);
                modules = [controller];
            }
        }
        catch
        {
            Persistence.WorldFile.RestoreBrain(brain, before);
            throw;
        }
        var tuned = modules.Where(aModule => aModule is ITrainableModule || aModule.CaptureState() is not null).ToList();
        if (aPreset.HandTuned && tuned.Count > 0)
            brain.Capture("ręczne parametry", tuned);
        brain.Reset();
        return modules[0];
    }
}
