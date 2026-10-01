using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Animata.Core.WorldObjects;
using Animata.Studio.Controls;
using Animata.Studio.Kit;
using Animata.Studio.Navigation;
using Animata.Studio.Session;
using Animata.Studio.Theme;

namespace Animata.Studio.Panels;

/// <summary>Scena w menu: jej sesja i panel (jeden na scenę, żeby widok 3D przeżył powrót do menu).</summary>
public sealed record MenuScene(StudioSession Session, Func<StudioPanel> Panel);

/// <summary>
/// Menu główne: tytuł, sceny przykładowe (kafle z żywą miniaturą i opisem; tworzone od nowa przy każdym uruchomieniu),
/// pliki (otwórz, ostatnie) i wygląd. Całość jest ułożona na stałej planszy i skalowana do okna (<see cref="Viewbox"/>),
/// więc rośnie i maleje razem z nim. Esc zamyka program.
/// </summary>
public sealed class MenuPanel : StudioPanel
{
    /// <summary>Rozmiar planszy menu przed skalowaniem.</summary>
    private const double BoardWidth = 1440;
    private const double BoardHeight = 900;

    private readonly IReadOnlyList<MenuScene> _scenes;
    private readonly Action<string> _openFile;
    private readonly List<(MenuScene Scene, SceneMiniMap Map, TextBlock Info, Button Tile)> _tiles = [];
    private readonly StackPanel _recent = new() { Spacing = 2 };
    private Button? _dark;
    private Button? _light;
    private readonly List<Border> _swatches = [];

    /// <param name="aOpenFile">Otwiera plik świata jako scenę (okno tworzy dla niego sesję i panel).</param>
    public MenuPanel(IReadOnlyList<MenuScene> aScenes, Action<string> aOpenFile) : base(aScenes[0].Session, "Animata")
    {
        _scenes = aScenes;
        _openFile = aOpenFile;
        RecentFiles.Changed += RebuildRecent;
    }

    protected override Control Build()
    {
        // Plansza: u góry tytuł (z lewej) i pliki (z prawej), pod nimi kafle scen, na dole wygląd.
        var content = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto"),
            RowSpacing = 36,
            Margin = new Thickness(72, 48, 72, 36)
        };
        var board = new Border { Width = BoardWidth, Height = BoardHeight, Child = content };

        // ---------- tytuł ----------
        var title = Ui.Text("Animata", 80, "Studio.Text", FontWeight.Bold);
        var tagline = Ui.Text("Symulator sztucznego życia. Stwory z ciałem, zmysłami i mózgiem, który się uczy.", 20, "Studio.Text2");
        tagline.TextWrapping = TextWrapping.Wrap;
        tagline.TextTrimming = TextTrimming.None;
        tagline.MaxWidth = 560;
        var identity = Ui.VStack(8, Ui.HStack(12, Ui.Icon(Icons.Logo, 36, "Studio.Accent", aThickness: 1.4), Ui.Header("Studio")), title, tagline);

        // ---------- pliki ----------
        var open = Ui.Button("Otwórz plik…", () => _ = PickAndOpenAsync(), Icons.Open, aAccent: true);
        open.Height = 38;
        var files = Ui.VStack(12, Ui.HStack(16, Ui.Text("Pliki", 18, "Studio.Text", FontWeight.SemiBold), open), _recent);
        files.Width = 520;
        files.Margin = new Thickness(32, 12, 0, 0);

        var head = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        head.Children.Add(identity);
        Grid.SetColumn(files, 1);
        head.Children.Add(files);
        content.Children.Add(head);
        RebuildRecent();

        // ---------- sceny przykładowe ----------
        _tiles.Clear();
        var tiles = new UniformGrid { Columns = _scenes.Count, Rows = 1 };
        foreach (var scene in _scenes)
            tiles.Children.Add(SceneTile(scene));
        var scenes = Ui.VStack(14,
            Ui.HStack(12, Ui.Text("Sceny przykładowe", 18, "Studio.Text", FontWeight.SemiBold),
                Ui.Text("tworzone od nowa przy każdym uruchomieniu", 13, "Studio.Text3")),
            tiles);
        Grid.SetRow(scenes, 1);
        content.Children.Add(scenes);

        // ---------- wygląd ----------
        var look = ThemeControls();
        look.HorizontalAlignment = HorizontalAlignment.Right;
        Grid.SetRow(look, 3);
        content.Children.Add(look);

        StudioTheme.Changed += UpdateTheme;
        UpdateTheme();
        UpdateInfo();
        return new Viewbox { Child = board, Stretch = Stretch.Uniform };
    }

    private Control ThemeControls()
    {
        var dark = _dark = Ui.Button("Ciemny", () => StudioTheme.Apply(true, StudioTheme.AccentIndex));
        var light = _light = Ui.Button("Jasny", () => StudioTheme.Apply(false, StudioTheme.AccentIndex));
        var swatches = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
        _swatches.Clear();
        for (var index = 0; index < StudioTheme.DarkAccents.Length; index++)
        {
            var accent = index;
            var swatch = new Border
            {
                Width = 24,
                Height = 24,
                CornerRadius = new CornerRadius(12),
                BorderThickness = new Thickness(2),
                Cursor = new Cursor(StandardCursorType.Hand)
            };
            swatch.PointerPressed += (_, aEvent) =>
            {
                StudioTheme.Apply(StudioTheme.IsDark, accent);
                aEvent.Handled = true;
            };
            ToolTip.SetTip(swatch, "Kolor akcentu");
            _swatches.Add(swatch);
            swatches.Children.Add(swatch);
        }
        return Ui.HStack(16, Ui.Text("Wygląd", 13, "Studio.Text3"), Ui.HStack(4, dark, light), swatches);
    }

    private void UpdateTheme()
    {
        if (_dark is null || _light is null)
            return;
        _dark.Classes.Set("accent", StudioTheme.IsDark);
        _light.Classes.Set("accent", !StudioTheme.IsDark);
        var accents = StudioTheme.IsDark ? StudioTheme.DarkAccents : StudioTheme.LightAccents;
        for (var index = 0; index < _swatches.Count; index++)
        {
            _swatches[index].Background = new SolidColorBrush(accents[index]);
            _swatches[index].BorderBrush = index == StudioTheme.AccentIndex ? Ui.Brush(StudioTheme.Palette.Text) : Brushes.Transparent;
        }
    }

    /// <summary>Kafel sceny: żywa miniatura, nazwa, opis, stan; pod spodem „od nowa”.</summary>
    private Control SceneTile(MenuScene aScene)
    {
        var map = new SceneMiniMap { World = aScene.Session.World, Height = 180 };
        var description = Ui.Text(aScene.Session.Description, 13, "Studio.Text2");
        description.TextWrapping = TextWrapping.Wrap;
        description.TextTrimming = TextTrimming.None;
        var info = Ui.Text(string.Empty, 12, "Studio.Text3");
        info.TextWrapping = TextWrapping.Wrap;
        info.TextTrimming = TextTrimming.None;
        var text = Ui.VStack(6, Ui.Text(aScene.Session.Name, 17, "Studio.Text", FontWeight.SemiBold), description, info);
        text.Margin = new Thickness(16, 12, 16, 14);
        var content = new DockPanel();
        DockPanel.SetDock(text, Dock.Bottom);
        content.Children.Add(text);
        content.Children.Add(new Border { Child = map, ClipToBounds = true, CornerRadius = new CornerRadius(12, 12, 0, 0) });
        var tile = new Button
        {
            Content = content,
            Padding = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Top,
            CornerRadius = new CornerRadius(12),
            Height = 330,
            Focusable = false
        };
        tile.Res(Button.BackgroundProperty, "Studio.Card");
        tile.Click += (_, _) => Enter(aScene.Panel(), tile);
        _tiles.Add((aScene, map, info, tile));

        var reset = Ui.Button("Od nowa", () =>
        {
            aScene.Session.ResetScene();
            UpdateInfo();
        }, Icons.Shuffle, aGhost: true);
        reset.HorizontalAlignment = HorizontalAlignment.Left;
        var column = Ui.VStack(6, tile, reset);
        column.Margin = new Thickness(0, 0, 16, 0);
        return column;
    }

    /// <summary>Ostatnie pliki: nazwa i folder; klik otwiera. Pusto — krótka informacja.</summary>
    private void RebuildRecent()
    {
        _recent.Children.Clear();
        var paths = RecentFiles.Paths;
        if (paths.Count == 0)
        {
            _recent.Children.Add(Ui.Text("Brak ostatnich plików — zapisane i otwarte światy pojawią się tutaj.", 13, "Studio.Text3"));
            return;
        }
        foreach (var path in paths.Take(5))
        {
            var target = path;
            var row = new Button
            {
                Content = Ui.HStack(12, Ui.Icon(Icons.Open, 16), Ui.Text(JsonFiles.Worlds.NameOf(path), 14),
                    Ui.Text(Path.GetDirectoryName(path) ?? string.Empty, 12, "Studio.Text3")),
                Height = 34,
                Padding = new Thickness(10, 0),
                HorizontalAlignment = HorizontalAlignment.Left,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Focusable = false
            };
            ToolTip.SetTip(row, target);
            row.Click += (_, _) => _openFile(target);
            _recent.Children.Add(row);
        }
    }

    private async Task PickAndOpenAsync()
    {
        if (await JsonFiles.Worlds.PickOpenAsync(this) is not { } file)
            return;
        if (JsonFiles.LocalPath(file) is { } path)
            _openFile(path);
    }

    private void UpdateInfo()
    {
        foreach (var (scene, map, info, _) in _tiles)
        {
            var session = scene.Session;
            map.World = session.World;
            var creatures = session.Creatures.Count();
            var learning = session.Creatures.Count(session.IsTraining);
            info.Text = $"{creatures} stwory" + (learning > 0 ? $" · {learning} uczą się" : string.Empty);
        }
    }

    public override void Refresh(float aDelta)
    {
        UpdateInfo();
        foreach (var tile in _tiles)
            tile.Map.InvalidateVisual();
    }

    public override void OnShown()
    {
        UpdateInfo();
        RebuildRecent();
    }

    public override bool HandleKey(KeyEventArgs aEvent)
    {
        switch (aEvent.Key)
        {
            case Key.Escape:
                (TopLevel.GetTopLevel(this) as Window)?.Close();
                return true;
            case Key.Enter:
                Enter(_scenes[0].Panel(), _tiles.Count > 0 ? _tiles[0].Tile : null);
                return true;
            default:
                return false;
        }
    }
}
