using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Animata.Core.Persistence;

namespace Animata.Studio.Kit;

/// <summary>Okna wyboru pliku mózgu (*.brain.json).</summary>
public static class BrainFiles
{
    private static readonly FilePickerFileType FileType = new("Mózg Animaty")
    {
        Patterns = ["*" + BrainFile.Extension, "*.json"]
    };

    /// <summary>Plik mózgu do wczytania albo null (anulowano, brak okna).</summary>
    public static async Task<IStorageFile?> PickOpenAsync(Control aOwner)
    {
        if (TopLevel.GetTopLevel(aOwner)?.StorageProvider is not { } storage)
            return null;
        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Wczytaj mózg",
            AllowMultiple = false,
            FileTypeFilter = [FileType]
        });
        return files.Count > 0 ? files[0] : null;
    }

    /// <summary>Plik do zapisu mózgu albo null.</summary>
    public static async Task<IStorageFile?> PickSaveAsync(Control aOwner, string aSuggestedName)
    {
        if (TopLevel.GetTopLevel(aOwner)?.StorageProvider is not { } storage)
            return null;
        return await storage.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Zapisz mózg",
            SuggestedFileName = aSuggestedName + BrainFile.Extension,
            DefaultExtension = BrainFile.Extension,
            FileTypeChoices = [FileType],
            ShowOverwritePrompt = true
        });
    }

    /// <summary>Nazwa pliku bez .brain.json.</summary>
    public static string ShortName(string aName) =>
        aName.EndsWith(BrainFile.Extension, StringComparison.OrdinalIgnoreCase) ? aName[..^BrainFile.Extension.Length] : aName;
}
