using Avalonia;
using Avalonia.Controls;
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

/// <summary>Menu główne: tytuł, sceny (kafle z żywą miniaturą i „od nowa”), wygląd, skróty.</summary>
public sealed class MenuPanel : StudioPanel
{
    private readonly IReadOnlyList<MenuScene> _scenes;
    private readonly List<(MenuScene Scene, SceneMiniMap Map, TextBlock Info, Button Tile)> _tiles = [];
    private Button? _dark;
    private Button? _light;
    private readonly List<Border> _swatches = [];

    public MenuPanel(IReadOnlyList<MenuScene> aScenes) : base(aScenes[0].Session, "Animata")
    {
        _scenes = aScenes;
    }

    protected override Control Build()
    {
        var root = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), Margin = new Thickness(96, 0) };

        // Lewa kolumna: tożsamość i główne akcje.
        var title = Ui.Text("Animata", 96, "Studio.Text", FontWeight.Bold);
        var tagline = Ui.Text("Symulator sztucznego życia. Stwory z ciałem, zmysłami i mózgiem, który się uczy.", 20, "Studio.Text2");
        tagline.TextWrapping = TextWrapping.Wrap;
        tagline.TextTrimming = TextTrimming.None;
        tagline.MaxWidth = 460;
        var enter = Ui.Button("Wejdź do sceny", () => Enter(_scenes[0].Panel(), _tiles.Count > 0 ? _tiles[0].Tile : null), Icons.Enter, aAccent: true);
        enter.Height = 40;
        var hint = Ui.Text("Dwuklik dowolnego elementu wjeżdża do środka · Esc, Alt+← albo przycisk „wstecz” myszy wraca", 12.5, "Studio.Text3");
        hint.TextWrapping = TextWrapping.Wrap;
        hint.TextTrimming = TextTrimming.None;
        hint.MaxWidth = 460;

        var left = Ui.VStack(18,
            Ui.HStack(12, Ui.Icon(Icons.Logo, 40, "Studio.Accent", aThickness: 1.4), Ui.Header("Studio")),
            title, tagline, enter, hint);
        left.VerticalAlignment = VerticalAlignment.Center;
        left.Margin = new Thickness(0, 0, 48, 0);
        root.Children.Add(left);

        // Prawa kolumna: kafle scen, wygląd, skróty.
        _tiles.Clear();
        var tiles = new Grid { ColumnDefinitions = new ColumnDefinitions(string.Join(",", _scenes.Select(_ => "*"))), ColumnSpacing = 16 };
        for (var index = 0; index < _scenes.Count; index++)
        {
            var tile = SceneTile(_scenes[index]);
            Grid.SetColumn(tile, index);
            tiles.Children.Add(tile);
        }

        _dark = Ui.Button("Ciemny", () => StudioTheme.Apply(true, StudioTheme.AccentIndex));
        _light = Ui.Button("Jasny", () => StudioTheme.Apply(false, StudioTheme.AccentIndex));
        var swatches = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        for (var index = 0; index < StudioTheme.DarkAccents.Length; index++)
        {
            var accent = index;
            var swatch = new Border
            {
                Width = 26,
                Height = 26,
                CornerRadius = new CornerRadius(13),
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
        var themeRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        themeRow.Children.Add(Ui.VStack(4, Ui.Text("Wygląd", 15, "Studio.Text", FontWeight.SemiBold),
            Ui.Text("FluentTheme · jasny/ciemny wariant i kolor akcentu", 12.5, "Studio.Text3")));
        var themeControls = Ui.VStack(10, Ui.HStack(4, _dark, _light), swatches);
        themeControls.HorizontalAlignment = HorizontalAlignment.Right;
        Grid.SetColumn(themeControls, 1);
        themeRow.Children.Add(themeControls);

        var keys = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto") };
        (string Key, string What)[] shortcuts =
        [
            ("Dwuklik · PPM", "wejdź w stwora, podgraf · menu wstawiania w 3D"),
            ("Esc · Alt+←", "wróć poziom wyżej"),
            ("Ctrl+G", "zgrupuj zaznaczone moduły w podgraf"),
            ("Ctrl+S · Z", "snapshot mózgu · cofnij do poprzedniego")
        ];
        for (var row = 0; row < shortcuts.Length; row++)
        {
            var key = Ui.Kbd(shortcuts[row].Key);
            key.Margin = new Thickness(0, 4, 12, 4);
            key.HorizontalAlignment = HorizontalAlignment.Left;
            Grid.SetRow(key, row);
            keys.Children.Add(key);
            var what = Ui.Text(shortcuts[row].What, 12.5, "Studio.Text2");
            Grid.SetRow(what, row);
            Grid.SetColumn(what, 1);
            keys.Children.Add(what);
        }

        var right = Ui.VStack(20, tiles, Ui.Card(themeRow, 18), Ui.Card(keys, 18));
        right.VerticalAlignment = VerticalAlignment.Center;
        right.MaxWidth = 620;
        Grid.SetColumn(right, 1);
        root.Children.Add(right);

        StudioTheme.Changed += UpdateTheme;
        UpdateTheme();
        UpdateInfo();
        return root;
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

    /// <summary>Kafel sceny: żywa miniatura, nazwa, stan; pod spodem „od nowa”.</summary>
    private Control SceneTile(MenuScene aScene)
    {
        var map = new SceneMiniMap { World = aScene.Session.World, Height = 190 };
        var info = Ui.Text(string.Empty, 12.5, "Studio.Text3");
        info.TextWrapping = TextWrapping.Wrap;
        info.TextTrimming = TextTrimming.None;
        var text = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(18, 14) };
        text.Children.Add(Ui.VStack(4, Ui.Text(aScene.Session.Name, 17, "Studio.Text", FontWeight.SemiBold), info));
        var open = Ui.Chip("Otwórz");
        open.VerticalAlignment = VerticalAlignment.Top;
        Grid.SetColumn(open, 1);
        text.Children.Add(open);
        var content = new DockPanel();
        DockPanel.SetDock(text, Dock.Bottom);
        content.Children.Add(text);
        content.Children.Add(new Border { Child = map, ClipToBounds = true, CornerRadius = new CornerRadius(12, 12, 0, 0) });
        var tile = new Button
        {
            Content = content,
            Padding = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            CornerRadius = new CornerRadius(12),
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
        return Ui.VStack(8, tile, reset);
    }

    private void UpdateInfo()
    {
        foreach (var (scene, map, info, _) in _tiles)
        {
            var session = scene.Session;
            map.World = session.World;
            var creatures = session.Creatures.Count();
            var obstacles = session.World.Entities.OfType<Obstacle>().Count();
            var targets = session.World.Entities.OfType<TargetBall>().Count();
            var learning = session.Creatures.Count(session.IsTraining);
            info.Text = $"{creatures} stwory · {targets} cele · {obstacles} słupki" + (learning > 0 ? $" · {learning} uczą się" : string.Empty);
        }
    }

    public override void Refresh(float aDelta)
    {
        UpdateInfo();
        foreach (var tile in _tiles)
            tile.Map.InvalidateVisual();
    }

    public override void OnShown() => UpdateInfo();

    public override bool HandleKey(KeyEventArgs aEvent)
    {
        if (aEvent.Key != Key.Enter)
            return false;
        Enter(_scenes[0].Panel(), _tiles.Count > 0 ? _tiles[0].Tile : null);
        return true;
    }
}
