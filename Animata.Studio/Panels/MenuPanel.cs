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

/// <summary>Menu główne: tytuł, wejście do sceny (kafel z żywą miniaturą), wygląd, skróty.</summary>
public sealed class MenuPanel : StudioPanel
{
    private readonly Func<StudioPanel> _scene;
    private SceneMiniMap? _miniMap;
    private TextBlock? _sceneInfo;
    private Button? _sceneTile;
    private Button? _dark;
    private Button? _light;
    private readonly List<Border> _swatches = [];

    /// <param name="aScene">Panel sceny (jeden na całą aplikację, żeby widok 3D przeżył powrót do menu).</param>
    public MenuPanel(StudioSession aSession, Func<StudioPanel> aScene) : base(aSession, "Animata")
    {
        _scene = aScene;
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
        var enter = Ui.Button("Wejdź do sceny", () => Enter(_scene(), _sceneTile), Icons.Enter, aAccent: true);
        enter.Height = 40;
        var reset = Ui.Button("Nowa scena demo", () =>
        {
            Session.ResetScene();
            UpdateInfo();
        });
        reset.Height = 40;
        var hint = Ui.Text("Dwuklik dowolnego elementu wjeżdża do środka · Esc, Alt+← albo przycisk „wstecz” myszy wraca", 12.5, "Studio.Text3");
        hint.TextWrapping = TextWrapping.Wrap;
        hint.TextTrimming = TextTrimming.None;
        hint.MaxWidth = 460;

        var left = Ui.VStack(18,
            Ui.HStack(12, Ui.Icon(Icons.Logo, 40, "Studio.Accent", aThickness: 1.4), Ui.Header("Studio")),
            title, tagline, Ui.HStack(10, enter, reset), hint);
        left.VerticalAlignment = VerticalAlignment.Center;
        left.Margin = new Thickness(0, 0, 48, 0);
        root.Children.Add(left);

        // Prawa kolumna: kafel sceny, wygląd, skróty.
        _miniMap = new SceneMiniMap { World = Session.World, Height = 250 };
        _sceneInfo = Ui.Text(string.Empty, 12.5, "Studio.Text3");
        var tileText = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(20, 16) };
        tileText.Children.Add(Ui.VStack(4, Ui.Text("Scena demo", 17, "Studio.Text", FontWeight.SemiBold), _sceneInfo));
        var open = Ui.Chip("Otwórz");
        Grid.SetColumn(open, 1);
        tileText.Children.Add(open);
        var tileContent = new DockPanel();
        DockPanel.SetDock(tileText, Dock.Bottom);
        tileContent.Children.Add(tileText);
        tileContent.Children.Add(new Border { Child = _miniMap, ClipToBounds = true, CornerRadius = new CornerRadius(12, 12, 0, 0) });
        _sceneTile = new Button
        {
            Content = tileContent,
            Padding = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            CornerRadius = new CornerRadius(12),
            Focusable = false
        };
        _sceneTile.Res(Button.BackgroundProperty, "Studio.Card");
        _sceneTile.Click += (_, _) => Enter(_scene(), _sceneTile);

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
            ("Dwuklik", "wejdź w stwora, podgraf, kartę"),
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

        var right = Ui.VStack(20, _sceneTile, Ui.Card(themeRow, 18), Ui.Card(keys, 18));
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

    private void UpdateInfo()
    {
        if (_miniMap is null || _sceneInfo is null)
            return;
        _miniMap.World = Session.World;
        var creatures = Session.Creatures.Count();
        var obstacles = Session.World.Entities.OfType<Obstacle>().Count();
        var targets = Session.World.Entities.OfType<TargetBall>().Count();
        var learning = Session.Creatures.Count(Session.IsTraining);
        _sceneInfo.Text = $"{creatures} stwory · {targets} cele · {obstacles} słupki" + (learning > 0 ? $" · {learning} uczą się" : string.Empty);
    }

    public override void Refresh(float aDelta)
    {
        UpdateInfo();
        _miniMap?.InvalidateVisual();
    }

    public override void OnShown() => UpdateInfo();

    public override bool HandleKey(KeyEventArgs aEvent)
    {
        if (aEvent.Key != Key.Enter)
            return false;
        Enter(_scene(), _sceneTile);
        return true;
    }
}
