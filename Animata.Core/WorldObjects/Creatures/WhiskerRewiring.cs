using Animata.Core.Brains;
using Animata.Core.Brains.Modules;
using Animata.Core.Brains.Neural;
using Animata.Core.Entities;
using Animata.Core.Sensors;

namespace Animata.Core.WorldObjects;

/// <summary>
/// Zmiana liczby wąsów stwora w miejscu: ten sam stwór, ten sam <see cref="RaySensor"/> (ten sam slot) i ten sam mózg.
/// - sensor dostaje nowe kąty (wachlarz <see cref="WorldObjectCatalog.WhiskerSpread"/>), porty Ray0…Ray{n−1};
/// - węzeł tego sensora w grafie ma porty sensora na żywo, więc od razu widzi nowe;
/// - AvoidAndSeek dostaje nowe kąty (parametry zostają);
/// - sieć, której wejście to dokładnie port wąsa („Ray3”), dostaje po jednym wejściu na nowy wąs; waga nowego wąsa
///   to waga starego wąsa o najbliższym kącie × (stara liczba / nowa liczba), więc sieć zachowuje się podobnie
///   i nie musi uczyć się od zera; pozostałe wagi i wejścia zostają;
/// - połączenia z nieistniejących już portów znikają, a moduł, który był karmiony wąsami „port do portu tej samej nazwy”,
///   dostaje połączenia także z nowych wąsów;
/// - snapshoty mózgu są przeliczane tak samo (inaczej przywrócenie starego wstawiłoby sieć o złym kształcie);
///   snapshot, którego nie da się przeliczyć, jest usuwany.
/// Nie jest przeliczana sieć, która używa wąsów w złożonym wyrażeniu (np. „Ray0 + Ray4”) — wtedy nic się nie zmienia
/// i leci <see cref="InvalidOperationException"/> z nazwą wyrażenia. Porty granic podgrafu nie są dopisywane:
/// wąsy przechodzące przez granicę podgrafu trzeba dołączyć ręcznie.
/// </summary>
public static class WhiskerRewiring
{
    public static void SetCount(ActiveEntity aCreature, int aCount)
    {
        var angles = WorldObjectCatalog.WhiskerAnglesFor(aCount);
        var sensor = aCreature.Body.Sensors.OfType<RaySensor>().FirstOrDefault()
            ?? throw new InvalidOperationException("Ten stwór nie ma wąsów.");
        if (sensor.Angles.Count == aCount)
            return;

        var brain = aCreature.Brain;
        var graphs = brain is null ? new List<BrainGraph>() : PortRewiring.GraphsOf(brain.Graph).ToList();

        // 1. Wszystko, co może rzucić, zanim cokolwiek się zmieni.
        var networks = graphs.SelectMany(aGraph => aGraph.Modules.OfType<NeuralNetworkModule>())
            .Where(aNetwork => aNetwork.Ports.Any(RaySensor.IsPortName)).ToList();
        var networkStates = networks.Select(aNetwork => Remap((NeuralNetworkState)aNetwork.CaptureState(), aCount, aNetwork.Name)).ToList();
        var snapshots = brain?.Snapshots.Select(aSnapshot => (Old: aSnapshot, New: Convert(aSnapshot, aCount))).ToList()
            ?? new List<(BrainSnapshot Old, BrainSnapshot? New)>();
        var fed = graphs.Select(aGraph => (Graph: aGraph, Pairs: FedByRays(aGraph, sensor.Slot))).ToList();

        // 2. Ciało i moduły.
        sensor.SetAngles(angles);
        foreach (var module in graphs.SelectMany(aGraph => aGraph.Modules))
            if (module is AvoidAndSeekModule avoid)
                avoid.SetRayAngles(angles);
        for (var index = 0; index < networks.Count; index++)
            networks[index].RestoreState(networkStates[index]);

        // 3. Połączenia, snapshoty, kompilacja.
        foreach (var (graph, pairs) in fed)
            PortRewiring.Rewire(graph, pairs);
        if (brain is null)
            return;
        foreach (var (old, converted) in snapshots)
            if (converted is null)
                brain.RemoveSnapshot(old);
            else if (!ReferenceEquals(converted, old))
                brain.ReplaceSnapshot(old, converted);
        brain.Graph.InvalidateDeep();
        brain.Graph.Validate();
        brain.Reset();
    }

    /// <summary>Pary (węzeł sensora, moduł) połączone wąsem „port do portu tej samej nazwy” (Ray3 → Ray3).</summary>
    private static List<(BrainModule Source, BrainModule Target)> FedByRays(BrainGraph aGraph, string aSlot)
    {
        var pairs = new List<(BrainModule Source, BrainModule Target)>();
        foreach (var link in aGraph.Connections)
        {
            if (!RaySensor.IsPortName(link.SourcePort) || link.SourcePort != link.TargetPort)
                continue;
            if (aGraph.Find(link.SourceId) is SensorModule source && source.Slot == aSlot &&
                aGraph.Find(link.TargetId) is { } target && !pairs.Contains((source, target)))
                pairs.Add((source, target));
        }
        return pairs;
    }

    // ---------- sieć ----------

    /// <summary>
    /// Stan sieci przeliczony na <paramref name="aCount"/> wąsów (opis w komentarzu klasy).
    /// Rzuca, gdy któreś wejście używa wąsów w złożonym wyrażeniu.
    /// </summary>
    public static NeuralNetworkState Remap(NeuralNetworkState aState, int aCount, string aName = "Neural")
    {
        var newAngles = WorldObjectCatalog.WhiskerAnglesFor(aCount);
        var oldRayPorts = aState.Ports.Where(RaySensor.IsPortName).ToList();
        IReadOnlyList<float> oldAngles = oldRayPorts.Count > 0
            ? RaySensor.Fan(oldRayPorts.Count, WorldObjectCatalog.WhiskerSpread).Angles
            : Array.Empty<float>();

        // Wejście sieci → numer wąsa, gdy wyrażenie to dokładnie port wąsa.
        var rayInputs = new Dictionary<int, int>();
        for (var input = 0; input < aState.InputExpressions.Length; input++)
        {
            var expression = aState.InputExpressions[input].Trim();
            if (RaySensor.IsPortName(expression) && int.TryParse(expression.AsSpan(3), out var ray) && ray < oldAngles.Count)
                rayInputs[input] = ray;
            else if (new NeuralInput(aState.InputExpressions[input]).Variables.Any(RaySensor.IsPortName))
                throw new InvalidOperationException(
                    $"Sieć „{aName}” używa wąsów w wyrażeniu „{aState.InputExpressions[input]}” — zmień je ręcznie " +
                    "(wejście = jeden wąs) albo usuń, zanim zmienisz liczbę wąsów.");
        }

        // Kolumny nowej pierwszej warstwy: (stara kolumna, mnożnik); porty wąsów wstawione tam, gdzie był pierwszy stary.
        var expressions = new List<string>();
        var columns = new List<(int Column, float Scale)>();
        var firstRay = rayInputs.Count > 0 ? rayInputs.Keys.Min() : int.MaxValue;
        for (var input = 0; input < aState.InputExpressions.Length; input++)
        {
            if (input == firstRay)
            {
                var scale = rayInputs.Count / (float)aCount;
                for (var ray = 0; ray < aCount; ray++)
                {
                    var nearest = rayInputs.MinBy(aPair => MathF.Abs(oldAngles[aPair.Value] - newAngles[ray])).Key;
                    expressions.Add(RaySensor.PortName(ray));
                    columns.Add((nearest, scale));
                }
            }
            if (rayInputs.ContainsKey(input))
                continue;
            expressions.Add(aState.InputExpressions[input]);
            columns.Add((input, 1));
        }

        var weights = aState.Weights.Select(aLayer => aLayer.Select(aNeuron => (float[])aNeuron.Clone()).ToArray()).ToArray();
        for (var neuron = 0; neuron < weights[0].Length; neuron++)
        {
            var old = aState.Weights[0][neuron];
            weights[0][neuron] = columns.Select(aColumn => old[aColumn.Column] * aColumn.Scale).ToArray();
        }
        var layers = (int[])aState.Layers.Clone();
        layers[0] = columns.Count;

        var ports = new List<string>();
        var raysPlaced = false;
        foreach (var port in aState.Ports)
        {
            if (!RaySensor.IsPortName(port))
            {
                ports.Add(port);
                continue;
            }
            if (raysPlaced)
                continue;
            ports.AddRange(Enumerable.Range(0, aCount).Select(RaySensor.PortName));
            raysPlaced = true;
        }

        return new NeuralNetworkState(layers, weights, aState.Biases.Select(aLayer => (float[])aLayer.Clone()).ToArray(),
            [.. ports], [.. expressions], [.. aState.Outputs]);
    }

    // ---------- snapshoty ----------

    /// <summary>Snapshot z sieciami przeliczonymi na nową liczbę wąsów; ten sam obiekt, gdy nic się nie zmienia; null, gdy się nie da.</summary>
    private static BrainSnapshot? Convert(BrainSnapshot aSnapshot, int aCount)
    {
        try
        {
            var modules = aSnapshot.Modules.Select(aModule => Convert(aModule, aCount)).ToArray();
            return modules.SequenceEqual(aSnapshot.Modules) ? aSnapshot : aSnapshot with { Modules = modules };
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private static ModuleSnapshot Convert(ModuleSnapshot aModule, int aCount)
    {
        switch (aModule.State)
        {
            case NeuralNetworkState network when network.Ports.Count(RaySensor.IsPortName) is > 0 and var rays && rays != aCount:
                return aModule with { State = Remap(network, aCount, aModule.ModuleName) };
            case CompositeState composite:
            {
                var inner = composite.Modules.Select(aInner => Convert(aInner, aCount)).ToArray();
                return inner.SequenceEqual(composite.Modules) ? aModule : aModule with { State = new CompositeState(inner) };
            }
            default:
                return aModule;
        }
    }
}
