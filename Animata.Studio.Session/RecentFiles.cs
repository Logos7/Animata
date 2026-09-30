using System.Text.Json;

namespace Animata.Studio.Session;

/// <summary>
/// Ostatnio otwierane i zapisywane pliki świata (najnowszy pierwszy, najwyżej <see cref="Limit"/>), trzymane w pliku
/// recent.json w katalogu danych aplikacji (np. %AppData%\Animata). Pliki, których już nie ma, są pomijane przy odczycie.
/// </summary>
public static class RecentFiles
{
    public const int Limit = 8;

    private static readonly string StorePath =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Animata", "recent.json");

    private static List<string>? _paths;

    /// <summary>Zmiana listy (np. menu odświeża się po zapisie w scenie).</summary>
    public static event Action? Changed;

    public static IReadOnlyList<string> Paths
    {
        get
        {
            _paths ??= Read();
            _paths.RemoveAll(aPath => !File.Exists(aPath));
            return _paths;
        }
    }

    /// <summary>Plik na początek listy (otwarty albo zapisany).</summary>
    public static void Add(string aPath)
    {
        var path = Path.GetFullPath(aPath);
        _paths ??= Read();
        _paths.RemoveAll(aOther => string.Equals(aOther, path, StringComparison.OrdinalIgnoreCase));
        _paths.Insert(0, path);
        if (_paths.Count > Limit)
            _paths.RemoveRange(Limit, _paths.Count - Limit);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(StorePath)!);
            File.WriteAllText(StorePath, JsonSerializer.Serialize(_paths));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Lista ostatnich to wygoda — brak zapisu nie może psuć otwierania plików.
        }
        Changed?.Invoke();
    }

    private static List<string> Read()
    {
        try
        {
            return File.Exists(StorePath) ? JsonSerializer.Deserialize<List<string>>(File.ReadAllText(StorePath)) ?? [] : [];
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return [];
        }
    }
}
