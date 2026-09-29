using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Animata.Core.Persistence;

namespace Animata.Studio.Kit;

/// <summary>Okna wyboru pliku świata (*.animata.json) — wspólne dla menu i sceny.</summary>
public static class WorldFiles
{
    private static readonly FilePickerFileType FileType = new("Świat Animaty")
    {
        Patterns = ["*" + WorldFile.Extension, "*.json"]
    };

    /// <summary>Plik do otwarcia albo null (anulowano, brak okna).</summary>
    public static async Task<IStorageFile?> PickOpenAsync(Control aOwner)
    {
        if (TopLevel.GetTopLevel(aOwner)?.StorageProvider is not { } storage)
            return null;
        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Otwórz świat",
            AllowMultiple = false,
            FileTypeFilter = [FileType]
        });
        return files.Count > 0 ? files[0] : null;
    }

    /// <summary>Plik do zapisu albo null.</summary>
    public static async Task<IStorageFile?> PickSaveAsync(Control aOwner, string aSuggestedName)
    {
        if (TopLevel.GetTopLevel(aOwner)?.StorageProvider is not { } storage)
            return null;
        return await storage.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Zapisz świat",
            SuggestedFileName = aSuggestedName + WorldFile.Extension,
            DefaultExtension = WorldFile.Extension,
            FileTypeChoices = [FileType],
            ShowOverwritePrompt = true
        });
    }

    /// <summary>Ścieżka pliku na dysku albo null (plik spoza systemu plików).</summary>
    public static string? LocalPath(IStorageFile aFile) => aFile.Path is { IsAbsoluteUri: true, IsFile: true } uri ? uri.LocalPath : null;

    /// <summary>Nazwa sceny z nazwy pliku (bez .animata.json).</summary>
    public static string SceneName(string aPath)
    {
        var name = Path.GetFileName(aPath);
        return name.EndsWith(WorldFile.Extension, StringComparison.OrdinalIgnoreCase) ? name[..^WorldFile.Extension.Length]
            : Path.GetFileNameWithoutExtension(name);
    }
}
