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
    private readonly string[] _parts;

    /// <param name="aParts">Pary (port, nazwa części w planie ciała).</param>
    public TouchSensor(IReadOnlyList<(string Port, string Part)> aParts)
    {
        _parts = [.. aParts.Select(aEntry => aEntry.Part)];
        SetPorts(aParts.Select(aEntry => aEntry.Port));
    }

    public override IReadOnlyDictionary<string, float> Read(Entity aOwner, World aWorld)
    {
        if (aOwner is not ArticulatedCreature creature)
            return Readings;
        for (var index = 0; index < _parts.Length; index++)
        {
            var part = creature.Plan.IndexOf(_parts[index]);
            Readings[OutputPorts[index]] = part >= 0 && creature.IsPartTouching(part) ? 1 : 0;
        }
        return Readings;
    }
}
