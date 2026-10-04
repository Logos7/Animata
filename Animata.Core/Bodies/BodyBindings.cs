namespace Animata.Core.Bodies;

public static class BodyBindings
{
    public static string Qualified(string aSlot, string aPort) => aSlot + "." + aPort;

    public static string[] Ports(IEnumerable<(string Slot, string Port)> aPorts)
    {
        var ports = aPorts.ToArray();
        var counts = ports.GroupBy(aPort => aPort.Port).ToDictionary(aGroup => aGroup.Key, aGroup => aGroup.Count());
        var names = ports.Select(aPort => counts[aPort.Port] == 1
            ? aPort.Port : Qualified(aPort.Slot, aPort.Port)).ToArray();
        if (names.Distinct().Count() != names.Length)
            throw new InvalidOperationException("Body port names are ambiguous; rename the slots or ports.");
        return names;
    }
}
