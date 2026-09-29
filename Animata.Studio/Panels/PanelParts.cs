using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Animata.Core.Actuators;
using Animata.Core.Entities;
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
    /// <summary>
    /// Edytowalne parametry napędu stwora (autko: prędkość, wsteczny, skręt kół, moment; walec: prędkość, obrót, moment)
    /// albo null, gdy stwór nie ma takiego napędu. Zmiana działa od razu w scenie; trwająca nauka bierze ustawienia
    /// z chwili startu — nowe obejmie ją po zatrzymaniu i wznowieniu (L).
    /// </summary>
    public static Control? DriveEditor(ActiveEntity aCreature)
    {
        var section = Ui.VStack(2, Ui.Header("Napęd — ustawienia"));
        switch (aCreature.Body.Actuators.FirstOrDefault(aActuator => aActuator is SteeringDriveActuator or DiskDriveActuator))
        {
            case SteeringDriveActuator steering:
                section.Children.Add(Ui.Row("Prędkość maks. [m/s]", Number(steering.MaxSpeed, 0.1f, 20, aValue => steering.MaxSpeed = aValue)));
                section.Children.Add(Ui.Row("Wstecz maks. [m/s]", Number(steering.MaxReverseSpeed, 0, 10, aValue => steering.MaxReverseSpeed = aValue)));
                section.Children.Add(Ui.Row("Skręt kół maks. [°]", Number(steering.MaxSteerAngle * 180 / MathF.PI, 1, 70,
                    aValue => steering.MaxSteerAngle = aValue * MathF.PI / 180)));
                section.Children.Add(Ui.Row("Moment koła [N·m]", Number(steering.DriveTorque, 0.1f, 50, aValue => steering.DriveTorque = aValue)));
                break;
            case DiskDriveActuator disk:
                section.Children.Add(Ui.Row("Prędkość maks. [m/s]", Number(disk.MaxSpeed, 0.1f, 20, aValue => disk.MaxSpeed = aValue)));
                section.Children.Add(Ui.Row("Obrót maks. [rad/s]", Number(disk.MaxTurnSpeed, 0.1f, 20, aValue => disk.MaxTurnSpeed = aValue)));
                section.Children.Add(Ui.Row("Moment koła [N·m]", Number(disk.DriveTorque, 0.1f, 50, aValue => disk.DriveTorque = aValue)));
                break;
            default:
                return null;
        }
        return section;
    }

    /// <summary>Pole liczby z zakresem: poza zakresem albo nie-liczba — odrzucone (pole wraca do poprzedniej wartości).</summary>
    private static TextBox Number(float aValue, float aMin, float aMax, Action<float> aSet) =>
        Ui.Field(Ui.F(aValue), aText =>
        {
            if (!Ui.TryParse(aText, out var value) || value < aMin || value > aMax)
                return false;
            aSet(value);
            return true;
        }, 96);

    /// <summary>
    /// Lista liczby segmentów węża (2…24). Zmiana w miejscu (<see cref="StudioSession.SetSegments"/>): ten sam wąż i mózg,
    /// parametry CPG zostają. Po niej — już po obsłudze zdarzenia listy — woła <paramref name="aChanged"/>.
    /// </summary>
    private static readonly System.Numerics.Vector3[] Swatches =
    [
        new(0.90f, 0.36f, 0.33f), new(0.95f, 0.62f, 0.25f), new(0.93f, 0.82f, 0.30f), new(0.45f, 0.78f, 0.36f),
        new(0.28f, 0.72f, 0.68f), new(0.33f, 0.60f, 0.92f), new(0.60f, 0.45f, 0.90f), new(0.88f, 0.45f, 0.72f)
    ];

    /// <summary>Kolor stwora: kilka gotowych i „losuj”. Kolor nic nie znaczy — to tylko wygląd.</summary>
    public static Control ColorPicker(ActiveEntity aCreature, Action? aChanged = null) => aCreature is ArticulatedCreature body
        ? ColorPicker(() => body.Color, aColor => body.Color = aColor, aChanged)
        : Ui.Text("—", 13);

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

    public static Control SegmentPicker(StudioSession aSession, SnakeCreature aSnake, Action? aChanged = null)
    {
        var picker = new ComboBox
        {
            ItemsSource = Enumerable.Range(WorldObjectCatalog.MinSnakeSegments,
                WorldObjectCatalog.MaxSnakeSegments - WorldObjectCatalog.MinSnakeSegments + 1).ToArray(),
            SelectedItem = aSnake.Segments,
            MinWidth = 96
        };
        ToolTip.SetTip(picker, "Liczba segmentów węża. Ciało przebudowuje się w miejscu, CPG zachowuje wyuczony chód.");
        picker.Tapped += (_, aEvent) => aEvent.Handled = true;
        picker.SelectionChanged += (_, _) =>
        {
            if (picker.SelectedItem is not int count || count == aSnake.Segments)
                return;
            Dispatcher.UIThread.Post(() =>
            {
                if (!aSession.SetSegments(aSnake, count))
                    picker.SelectedItem = aSnake.Segments;
                aChanged?.Invoke();
            });
        };
        return picker;
    }

    /// <summary>
    /// Lista liczby wąsów (1, 3, …, 25) stwora z wąsami. Zmiana idzie w miejscu (<see cref="StudioSession.SetWhiskers"/>),
    /// po niej — już po obsłudze zdarzenia listy — woła <paramref name="aChanged"/> (np. przebudowa panelu).
    /// </summary>
    public static Control WhiskerPicker(StudioSession aSession, ActiveEntity aCreature, Action? aChanged = null)
    {
        var picker = new ComboBox
        {
            ItemsSource = WorldObjectCatalog.WhiskerCounts,
            SelectedItem = WorldObjectCatalog.WhiskerCountOf(aCreature),
            MinWidth = 96
        };
        ToolTip.SetTip(picker, "Liczba wąsów (nieparzysta, wachlarz 120°). Mózg dopasowuje się sam: sieć dostaje przeliczone wagi, a nie losowe.");
        // Klik w listę nie może przejść do karty pod nią (karty w panelu stwora po kliknięciu wjeżdżają do grafu).
        picker.Tapped += (_, aEvent) => aEvent.Handled = true;
        picker.SelectionChanged += (_, _) =>
        {
            if (picker.SelectedItem is not int count || count == WorldObjectCatalog.WhiskerCountOf(aCreature))
                return;
            Dispatcher.UIThread.Post(() =>
            {
                if (!aSession.SetWhiskers(aCreature, count))
                    picker.SelectedItem = WorldObjectCatalog.WhiskerCountOf(aCreature);
                aChanged?.Invoke();
            });
        };
        return picker;
    }

    /// <summary>
    /// Wybór celu oka: „brak” albo jedna z kul sceny. Lista odświeża się przy każdym otwarciu (kule mogą przybyć lub zniknąć).
    /// </summary>
    public static Control TargetPicker(StudioSession aSession, TargetSensor aEye, Action? aChanged = null)
    {
        var picker = new ComboBox { MinWidth = 150 };
        ToolTip.SetTip(picker, "Na co patrzy oko. Nauka i tak ćwiczy na własnych celach — to zmienia tylko cel w scenie.");
        var updating = false;
        var shown = string.Empty;
        void Fill()
        {
            var current = aEye.TargetId is { } id ? aSession.World.Find(id) : null;
            var key = string.Join("|", aSession.World.Entities.OfType<Sphere>().Select(aSphere => $"{aSphere.Id}:{StudioSession.NameOf(aSphere)}")) +
                $"#{current?.Id}";
            if (key == shown)
                return;
            shown = key;
            updating = true;
            var choices = new List<TargetChoice> { new(null, "brak") };
            choices.AddRange(aSession.World.Entities.OfType<Sphere>().Select(aSphere => new TargetChoice(aSphere, StudioSession.NameOf(aSphere))));
            if (current is not null and not Sphere)
                choices.Add(new TargetChoice(current, StudioSession.NameOf(current)));
            picker.ItemsSource = choices;
            picker.SelectedItem = choices.FirstOrDefault(aChoice => ReferenceEquals(aChoice.Target, current)) ?? choices[0];
            updating = false;
        }
        Fill();
        picker.Tapped += (_, aEvent) => aEvent.Handled = true;
        picker.PointerEntered += (_, _) => Fill();
        picker.GotFocus += (_, _) => Fill();
        picker.SelectionChanged += (_, _) =>
        {
            if (updating || picker.SelectedItem is not TargetChoice choice)
                return;
            aEye.TargetId = choice.Target?.Id;
            aSession.Status = choice.Target is null ? "oko bez celu" : $"oko patrzy na: {choice.Name}";
            aChanged?.Invoke();
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
