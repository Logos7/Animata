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

        PortRewiring.Change(aCreature,
            (aState, aName) => aState is NeuralNetworkState network && network.Ports.Count(RaySensor.IsPortName) is > 0 and var rays && rays != aCount
                ? Remap(network, aCount, aName)
                : aState,
            (aSource, aPort) => aSource is SensorModule source && source.Slot == sensor.Slot && RaySensor.IsPortName(aPort),
            aGraphs =>
            {
                sensor.SetAngles(angles);
                foreach (var module in aGraphs.SelectMany(aGraph => aGraph.Modules))
                    if (module is AvoidAndSeekModule avoid)
                        avoid.SetRayAngles(angles);
            });
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
}
