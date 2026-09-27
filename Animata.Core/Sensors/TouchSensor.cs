using Animata.Core.Entities;
using Animata.Core.Worlds;

namespace Animata.Core.Sensors;

/// <summary>
/// Dotyk (slot „Touch”): po jednym porcie na wybraną część ciała stwora z części — 1, gdy w ostatnim kroku fizyki czegoś
/// dotykała (ziemi, płyty, przeszkody, innej części), inaczej 0. Pająk czuje tak cztery stopy i brzuch: wie, która noga
/// stoi, a która jest w powietrzu, i czy nie leży tułowiem na ziemi.
/// </summary>
public sealed class TouchSensor : Sensor
{
    private readonly string[] _ports;
    private readonly string[] _parts;
    private readonly Dictionary<string, float> _readings;

    /// <param name="aParts">Pary (port, nazwa części w planie ciała).</param>
    public TouchSensor(IReadOnlyList<(string Port, string Part)> aParts)
    {
        _ports = [.. aParts.Select(aEntry => aEntry.Port)];
        _parts = [.. aParts.Select(aEntry => aEntry.Part)];
        _readings = _ports.ToDictionary(aPort => aPort, _ => 0f);
    }

    public override IReadOnlyList<string> OutputPorts => _ports;

    /// <summary>Nazwa części czującej dany port.</summary>
    public string PartOf(string aPort) => _parts[Array.IndexOf(_ports, aPort)];

    public override IReadOnlyDictionary<string, float> Read(Entity aOwner, World aWorld)
    {
        if (aOwner is not ArticulatedCreature creature)
            return _readings;
        for (var index = 0; index < _ports.Length; index++)
        {
            var part = creature.Plan.IndexOf(_parts[index]);
            _readings[_ports[index]] = part >= 0 && creature.IsPartTouching(part) ? 1 : 0;
        }
        return _readings;
    }
}
