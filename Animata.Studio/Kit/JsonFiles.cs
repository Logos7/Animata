using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Animata.Core.Persistence;

namespace Animata.Studio.Kit;

/// <summary>Okna wyboru plików JSON Animaty jednego rodzaju (świat, mózg): otwórz, zapisz, nazwa bez rozszerzenia.</summary>
public sealed class JsonFiles(string aTypeName, string aExtension, string aOpenTitle, string aSaveTitle)
{
    public static JsonFiles Worlds { get; } = new("Świat Animaty", WorldFile.Extension, "Otwórz świat", "Zapisz świat");

    public static JsonFiles Brains { get; } = new("Mózg Animaty", BrainFile.Extension, "Wczytaj mózg", "Zapisz mózg");

    private readonly string _extension = aExtension;
    private readonly FilePickerFileType _type = new(aTypeName) { Patterns = ["*" + aExtension, "*.json"] };

    public async Task<IStorageFile?> PickOpenAsync(Control aOwner)
    {
        if (TopLevel.GetTopLevel(aOwner)?.StorageProvider is not { } storage)
            return null;
        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = aOpenTitle,
            AllowMultiple = false,
            FileTypeFilter = [_type]
        });
        return files.Count > 0 ? files[0] : null;
    }

    public async Task<IStorageFile?> PickSaveAsync(Control aOwner, string aSuggestedName)
    {
        if (TopLevel.GetTopLevel(aOwner)?.StorageProvider is not { } storage)
            return null;
        return await storage.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = aSaveTitle,
            SuggestedFileName = aSuggestedName + _extension,
            DefaultExtension = _extension,
            FileTypeChoices = [_type],
            ShowOverwritePrompt = true
        });
    }

    /// <summary>Nazwa pliku bez rozszerzenia rodzaju (albo bez zwykłego rozszerzenia), np. nazwa sceny.</summary>
    public string NameOf(string aPath)
    {
        var name = Path.GetFileName(aPath);
        return name.EndsWith(_extension, StringComparison.OrdinalIgnoreCase) ? name[..^_extension.Length] : Path.GetFileNameWithoutExtension(name);
    }

    /// <summary>Ścieżka pliku na dysku (null — plik nie jest lokalny).</summary>
    public static string? LocalPath(IStorageFile aFile) => aFile.Path is { IsAbsoluteUri: true, IsFile: true } uri ? uri.LocalPath : null;
}
