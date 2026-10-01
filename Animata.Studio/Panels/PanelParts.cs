using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Animata.Core.Actuators;
using Animata.Core.Entities;
using Animata.Core.Persistence;
using Animata.Core.Sensors;
using Animata.Core.Training;
using Animata.Core.WorldObjects;
using Animata.Studio.Controls;
using Animata.Studio.Kit;
using Animata.Studio.Session;

namespace Animata.Studio.Panels;

/// <summary>Kawałki UI wspólne dla kilku paneli.</summary>
public static class PanelParts
{
    private static readonly System.Numerics.Vector3[] Swatches =
    [
        new(0.90f, 0.36f, 0.33f), new(0.95f, 0.62f, 0.25f), new(0.93f, 0.82f, 0.30f), new(0.45f, 0.78f, 0.36f),
        new(0.28f, 0.72f, 0.68f), new(0.33f, 0.60f, 0.92f), new(0.60f, 0.45f, 0.90f), new(0.88f, 0.45f, 0.72f)
    ];

    /// <summary>Wybór koloru (gotowe odcienie i „losuj”) dowolnej encji z kolorem — stwora, klocka, cylindra.</summary>
    public static Control ColorPicker(Func<System.Numerics.Vector3> aGet, Action<System.Numerics.Vector3> aSet, Action? aChanged = null)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        void Set(System.Numerics.Vector3 aColor)
        {
            aSet(aColor);
            aChanged?.Invoke();
        }
        foreach (var swatch in Swatches)
        {
            var color = swatch;
            var button = new Button
            {
                Width = 20,
                Height = 20,
                Padding = new Thickness(0),
                CornerRadius = new CornerRadius(10),
                Background = new SolidColorBrush(Color.FromRgb((byte)(color.X * 255), (byte)(color.Y * 255), (byte)(color.Z * 255))),
                BorderThickness = new Thickness(aGet() == color ? 2 : 0),
                BorderBrush = Brushes.White,
                Focusable = false
            };
            button.Click += (_, _) => Set(color);
            row.Children.Add(button);
        }
        row.Children.Add(Ui.IconButton(Icons.Shuffle, "Losowy kolor", () => Set(WorldObjectCatalog.RandomColor())));
        return row;
    }

    /// <summary>
    /// Pozycje menu „Mózg” stwora: gotowe mózgi dla tego ciała (podmiana — nauka staje, stare snapshoty znikają),
    /// wczytanie mózgu z pliku i zapis do pliku. <paramref name="aOwner"/> — kontrolka, do której okna należą okna plików;
    /// <paramref name="aChanged"/> — po podmianie mózgu.
    /// </summary>
    public static List<Control> BrainMenuItems(StudioSession aSession, ActiveEntity aCreature, Control aOwner, Action? aChanged = null)
    {
        var items = new List<Control>();
        var header = new MenuItem { Header = "Nowy mózg (zastępuje obecny i jego snapshoty)", IsEnabled = false };
        items.Add(header);
        foreach (var preset in aCreature.BrainPresets)
        {
            var chosen = preset;
            var item = new MenuItem { Header = preset.Name, Icon = Ui.Icon(Icons.Brain, 14) };
            ToolTip.SetTip(item, preset.Description);
            item.Click += (_, _) =>
            {
                if (aSession.InstallBrain(aCreature, chosen))
                    aChanged?.Invoke();
            };
            items.Add(item);
        }
        items.Add(new Separator());
        var load = new MenuItem { Header = "Wczytaj mózg z pliku…", Icon = Ui.Icon(Icons.Open, 14) };
        load.Click += (_, _) => _ = LoadBrainAsync(aSession, aCreature, aOwner, aChanged);
        items.Add(load);
        var save = new MenuItem { Header = "Zapisz mózg do pliku…", Icon = Ui.Icon(Icons.Save, 14) };
        save.Click += (_, _) => _ = SaveBrainAsync(aSession, aCreature, aOwner);
        items.Add(save);
        return items;
    }

    private static async Task LoadBrainAsync(StudioSession aSession, ActiveEntity aCreature, Control aOwner, Action? aChanged)
    {
        if (await BrainFiles.PickOpenAsync(aOwner) is not { } file)
            return;
        try
        {
            await using var stream = await file.OpenReadAsync();
            using var reader = new StreamReader(stream);
            var document = BrainFile.FromJson(await reader.ReadToEndAsync());
            if (aSession.LoadBrain(aCreature, document, file.Name))
                aChanged?.Invoke();
        }
        catch (Exception exception) when (exception is IOException or NotSupportedException or System.Text.Json.JsonException
            or UnauthorizedAccessException)
        {
            aSession.Status = $"nie wczytano mózgu: {exception.Message}";
        }
    }

    private static async Task SaveBrainAsync(StudioSession aSession, ActiveEntity aCreature, Control aOwner)
    {
        try
        {
            var document = aSession.CaptureBrain(aCreature);
            if (await BrainFiles.PickSaveAsync(aOwner, StudioSession.NameOf(aCreature)) is not { } file)
                return;
            await using (var stream = await file.OpenWriteAsync())
            await using (var writer = new StreamWriter(stream))
            {
                if (stream.CanSeek)
                    stream.SetLength(0); // nadpisanie dłuższego pliku nie może zostawić jego końcówki
                await writer.WriteAsync(BrainFile.ToJson(document));
            }
            aSession.Status = $"zapisano mózg: {file.Name}";
        }
        catch (Exception exception) when (exception is IOException or NotSupportedException or UnauthorizedAccessException)
        {
            aSession.Status = $"nie zapisano mózgu: {exception.Message}";
        }
    }

    /// <summary>
    /// Wskazanie obiektu sceny (np. cel oka): „brak” albo dowolna encja sceny poza <paramref name="aOwner"/> — kula, inny stwór
    /// (pościg, ucieczka), cylinder, klocek. Lista odświeża się przy najechaniu (encje mogą przybyć, zniknąć albo zmienić nazwę).
    /// <paramref name="aSet"/> dostaje wybór już po zamknięciu listy.
    /// </summary>
    public static Control EntityPicker(StudioSession aSession, Entity aOwner, Func<Guid?> aGet, Action<Guid?> aSet)
    {
        var picker = new ComboBox { MinWidth = 150 };
        var updating = false;
        var shown = string.Empty;
        List<TargetChoice> choices = [];
        // Odświeżanie tylko przy zamkniętej liście i poza obsługą wyboru: podmiana ItemsSource albo przebudowa panelu
        // w trakcie zamykania listy wywracała ComboBox (ArgumentOutOfRangeException w Avalonii).
        void Fill()
        {
            if (picker.IsDropDownOpen)
                return;
            var candidates = aSession.World.Entities.Where(aEntity => !ReferenceEquals(aEntity, aOwner))
                .OrderBy(aEntity => aEntity switch { Sphere => 0, ActiveEntity => 1, Cylinder => 2, _ => 3 })
                .ToList();
            var key = string.Join("|", candidates.Select(aEntity => $"{aEntity.Id}:{StudioSession.NameOf(aEntity)}"));
            updating = true;
            if (key != shown)
            {
                shown = key;
                choices = [new(null, "brak"), .. candidates.Select(aEntity => new TargetChoice(aEntity, StudioSession.NameOf(aEntity)))];
                picker.ItemsSource = choices;
            }
            var current = aGet() is { } id ? aSession.World.Find(id) : null;
            var selected = choices.FirstOrDefault(aChoice => ReferenceEquals(aChoice.Target, current)) ?? choices[0];
            if (!ReferenceEquals(picker.SelectedItem, selected))
                picker.SelectedItem = selected;
            updating = false;
        }
        Fill();
        picker.PointerEntered += (_, _) => Dispatcher.UIThread.Post(Fill);
        picker.SelectionChanged += (_, _) =>
        {
            if (updating || picker.SelectedItem is not TargetChoice choice)
                return;
            // Zmiana po zamknięciu listy — reakcja (np. przebudowa inspektora) może usunąć ten ComboBox z drzewa.
            Dispatcher.UIThread.Post(() => aSet(choice.Target?.Id));
        };
        return picker;
    }

    private sealed record TargetChoice(Entity? Target, string Name)
    {
        public override string ToString() => Name;
    }

    /// <summary>
    /// Karta nauki stwora: pokolenie, mistrz, wykres, stop/wznów, losowanie. Odświeżanie dopisuje do <paramref name="aUpdaters"/>.
    /// </summary>
    public static Control TrainingCard(StudioSession aSession, ActiveEntity aCreature, List<Action> aUpdaters)
    {
        var brain = aCreature.Brain!;
        var generation = Ui.MonoText(string.Empty, 11.5);
        var champion = Ui.MonoText(string.Empty, 12);
        var sparkline = new Sparkline { Height = 56 };
        var toggleLabel = Ui.Text("Zatrzymaj", 13);
        var toggle = new Button
        {
            Content = toggleLabel,
            Height = 32,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            Focusable = false
        };
        toggle.Click += (_, _) => aSession.ToggleTraining([aCreature]);
        var randomize = Ui.Button("Losuj od zera", () => aSession.Randomize([aCreature]));
        randomize.HorizontalAlignment = HorizontalAlignment.Stretch;
        randomize.HorizontalContentAlignment = HorizontalAlignment.Center;
        var buttons = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), ColumnSpacing = 8 };
        buttons.Children.Add(toggle);
        Grid.SetColumn(randomize, 1);
        buttons.Children.Add(randomize);

        var head = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        head.Children.Add(Ui.Header("Nauka"));
        Grid.SetColumn(generation, 1);
        head.Children.Add(generation);

        var legend = Ui.Text("szary: najlepszy w pokoleniu · akcent: mistrz na stałych trasach", 11, "Studio.Text3");
        legend.TextWrapping = Avalonia.Media.TextWrapping.Wrap;
        legend.TextTrimming = Avalonia.Media.TextTrimming.None;

        aUpdaters.Add(() =>
        {
            var progress = aSession.Training.ProgressOf(brain);
            generation.Text = progress is not null ? $"gen {progress.Generation}" : "stoi";
            champion.Text = progress is not null ? $"gen {progress.ChampionGeneration} · {Ui.F(progress.ChampionScore, 3)}" : "—";
            toggleLabel.Text = aSession.IsTraining(aCreature) ? "Zatrzymaj" : "Wznów";
            sparkline.Points = aSession.ProgressHistory(brain);
        });
        return Ui.Card(Ui.VStack(10, head, sparkline, legend, Ui.Row("Mistrz", champion), buttons), 14);
    }

    public static bool HasNetwork(ActiveEntity aCreature) => TrainingController.FindTrainable(aCreature) is not null;
}
