using Animata.Core.Brains.Modules;
using Animata.Core.Entities;

namespace Animata.Core.Training;

internal sealed class TrainingSetup
{
    private readonly ActiveEntity _creature;
    private readonly BrainModule _module;
    private readonly Func<bool> _moduleMatches;
    private readonly string? _rig;
    private readonly (long Sensors, long Actuators) _slots;
    private readonly (object Owner, SettingInfo Setting, object? Value)[] _settings;

    public TrainingSetup(ActiveEntity aCreature, BrainModule aModule)
    {
        _creature = aCreature;
        _module = aModule;
        _moduleMatches = aModule.CaptureConfigurationCheck();
        _rig = aCreature.TrainingRigFor(aModule)?.Name;
        _slots = (aCreature.Body.Sensors.Revision, aCreature.Body.Actuators.Revision);
        var settings = new List<(object, SettingInfo, object?)>();
        Capture(aCreature, settings);
        foreach (var (_, owner) in aCreature.Body.Slots)
            Capture(owner, settings);
        _settings = [.. settings];
    }

    public bool Matches()
    {
        if (_rig != _creature.TrainingRigFor(_module)?.Name ||
            _slots != (_creature.Body.Sensors.Revision, _creature.Body.Actuators.Revision) || !_moduleMatches())
            return false;
        foreach (var (owner, setting, value) in _settings)
        {
            var current = setting.Get(owner);
            if (value is float[] values && current is float[] numbers)
            {
                if (!values.AsSpan().SequenceEqual(numbers))
                    return false;
            }
            else if (!Equals(value, current))
                return false;
        }
        return true;
    }

    private static void Capture(object aOwner, List<(object, SettingInfo, object?)> aSettings)
    {
        foreach (var setting in Settings.Of(aOwner))
        {
            if (setting.IsReference || setting.Attribute.Derived || setting.Attribute.Color || setting.Name == nameof(Entity.Locked))
                continue;
            var value = setting.Get(aOwner);
            aSettings.Add((aOwner, setting, value is float[] values ? values.Clone() : value));
        }
    }
}
