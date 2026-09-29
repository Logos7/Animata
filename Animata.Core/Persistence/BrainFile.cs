using System.Text.Json;
using Animata.Core.Bodies;
using Animata.Core.Brains;
using Animata.Core.Brains.Modules;

namespace Animata.Core.Persistence;

/// <summary>
/// Zapisany mózg (JSON) — do przenoszenia między ciałami. Mózg łączy się z ciałem po slotach (nazwach zmysłów
/// i napędów) i portach, więc pasuje do każdego ciała, które ma te sloty i porty. <see cref="Body"/> opisuje ciało,
/// z którego mózg zapisano — tylko informacyjnie i do raportu przy wczytaniu.
/// </summary>
public sealed record BrainFileDocument(int Format, string Name, IReadOnlyList<BodySlotDocument> Body, BrainDocument Brain);

/// <summary>Slot ciała: nazwa, czy to zmysł (inaczej napęd), typ i porty.</summary>
public sealed record BodySlotDocument(string Slot, bool Sensor, string Type, string[] Ports);

/// <summary>
/// Co nie pasowało przy wczytaniu mózgu do ciała: sloty z pliku, których ciało nie ma, i liczba połączeń odpiętych,
/// bo prowadziły do nieistniejących slotów albo portów (np. stawów, których ciało ma mniej).
/// </summary>
public sealed record BrainLoadReport(IReadOnlyList<string> MissingSlots, int DroppedConnections)
{
    public bool Fits => MissingSlots.Count == 0 && DroppedConnections == 0;
}

/// <summary>Zapis i odczyt samego mózgu: <see cref="Capture"/> → <see cref="ToJson"/> / <see cref="FromJson"/> → <see cref="Load"/>.</summary>
public static class BrainFile
{
    public const int Format = 1;

    /// <summary>Sugerowane rozszerzenie pliku mózgu.</summary>
    public const string Extension = ".brain.json";

    /// <summary>Mózg do zapisu: moduły, połączenia (także do węzłów ciała), położenia w edytorze, snapshoty, opis ciała.</summary>
    public static BrainFileDocument Capture(Brain aBrain, string aName = "")
    {
        var body = aBrain.Body is { } source ? DescribeBody(source) : [];
        return new BrainFileDocument(Format, aName, body, WorldFile.CaptureBrain(aBrain));
    }

    public static string ToJson(BrainFileDocument aDocument) => JsonSerializer.Serialize(aDocument, WorldFile.Options);

    public static BrainFileDocument FromJson(string aJson)
    {
        var document = JsonSerializer.Deserialize<BrainFileDocument>(aJson, WorldFile.Options)
            ?? throw new JsonException("Pusty plik mózgu.");
        if (document.Format != Format)
            throw new NotSupportedException($"Plik mózgu ma format {document.Format}, a ta wersja czyta tylko format {Format}.");
        if (document.Brain is null)
            throw new JsonException("W pliku nie ma mózgu.");
        return document;
    }

    /// <summary>
    /// Zastępuje mózg zapisanym (moduły, połączenia, snapshoty, bieżący snapshot). Połączenia do slotów i portów, których
    /// ciało nie ma, są odpinane — raport mówi, ile. Gdy zapisany mózg się nie składa (błąd walidacji), mózg zostaje
    /// taki jak przed wczytaniem, a wyjątek idzie dalej. Stan chwilowy modułów jest czyszczony.
    /// </summary>
    public static BrainLoadReport Load(Brain aBrain, BrainFileDocument aDocument)
    {
        var before = WorldFile.CaptureBrain(aBrain);
        try
        {
            WorldFile.RestoreBrain(aBrain, aDocument.Brain);
        }
        catch
        {
            WorldFile.RestoreBrain(aBrain, before);
            throw;
        }
        aBrain.Reset();
        var missing = aBrain.Body is { } body
            ? aDocument.Body.Where(aSlot => aSlot.Sensor ? body.FindSensor(aSlot.Slot) is null : body.FindActuator(aSlot.Slot) is null)
                .Select(aSlot => aSlot.Slot).ToList()
            : [.. aDocument.Body.Select(aSlot => aSlot.Slot)];
        return new BrainLoadReport(missing, Math.Max(0, aDocument.Brain.Connections.Count - aBrain.Graph.Connections.Count));
    }

    private static List<BodySlotDocument> DescribeBody(Body aBody) =>
    [
        .. aBody.Sensors.Select(aSensor => new BodySlotDocument(aSensor.Slot, true, aSensor.GetType().Name, [.. aSensor.OutputPorts])),
        .. aBody.Actuators.Select(aActuator => new BodySlotDocument(aActuator.Slot, false, aActuator.GetType().Name, [.. aActuator.InputPorts]))
    ];
}
