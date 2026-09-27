using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Animata.Core.Brains;
using Animata.Core.Brains.Modules;
using Animata.Core.Entities;
using Animata.Studio.Controls;
using Animata.Studio.Kit;
using Animata.Studio.Navigation;
using Animata.Studio.Session;
using Animata.Studio.Theme;

namespace Animata.Studio.Panels;

/// <summary>
/// Edytor grafu mózgu (albo wnętrza podgrafu): paleta modułów z lewej, płótno w środku, inspektor z prawej.
/// Dwuklik podgrafu wjeżdża do jego wnętrza — ten sam panel dla grafu wewnętrznego, więc da się schodzić dowolnie głęboko.
/// Mózg działa dalej w trakcie edycji: wartości na portach i drutach są na żywo.
/// </summary>
public sealed class GraphPanel : StudioPanel
{
    private readonly ActiveEntity _creature;
    private readonly Brain _brain;
    private readonly BrainGraph _graph;
    private readonly CompositeModule? _owner;
    private readonly BrainGraph? _ownerParent;
    private readonly BrainModule? _focus;
    private readonly Border _inspectorHost = new();
    private readonly Canvas _dragLayer = new() { IsHitTestVisible = false };
    private GraphCanvas? _canvas;
    private ModuleInspector? _inspector;
    private TextBlock? _validation;
    private Control? _validationIcon;
    private TextBlock? _message;
    private Border? _errorBanner;
    private TextBlock? _errorText;
    private float _messageAge;

    // Przeciąganie z palety.
    private (string Name, Func<BrainModule> Create)? _dragItem;
    private Point _dragStart;
    private bool _dragging;
    private bool _suppressClick;
    private Border? _ghost;

    /// <param name="aOwner">Podgraf, którego wnętrze edytujemy (null = cały mózg).</param>
    /// <param name="aOwnerParent">Graf, w którym siedzi <paramref name="aOwner"/> (do zmian jego portów).</param>
    /// <param name="aFocus">Moduł do zaznaczenia na wejściu (np. sensor, z którego karty przyszliśmy).</param>
    public GraphPanel(StudioSession aSession, ActiveEntity aCreature, BrainGraph aGraph, CompositeModule? aOwner, BrainGraph? aOwnerParent,
        string aTitle, BrainModule? aFocus) : base(aSession, aTitle)
    {
        _creature = aCreature;
        _brain = aCreature.Brain!;
        _graph = aGraph;
        _owner = aOwner;
        _ownerParent = aOwnerParent;
        _focus = aFocus;
    }

    protected override Control Build()
    {
        _canvas = new GraphCanvas(_graph);
        _canvas.SelectionChanged += BuildInspector;
        _canvas.Changed += OnGraphChanged;
        _canvas.Message += ShowMessage;
        _canvas.NodeActivated += Activate;
        _canvas.Removing += aModules =>
        {
            if (aModules.OfType<NeuralNetworkModule>().Any() && Session.Training.IsTraining(_brain))
                Session.StopTraining(_brain);
        };

        _inspector = new ModuleInspector(Session, _creature, _graph, _owner, _ownerParent);
        _inspector.Changed += OnGraphChanged;
        _inspector.Message += ShowMessage;
        _inspector.DeleteRequested += () => _canvas.DeleteSelection();
        _inspector.UngroupRequested += () => _canvas.UngroupSelection();
        _inspector.EnterRequested += aModule => Activate(aModule, _canvas.ScreenCenterOf(aModule));

        // ---------- pasek ----------
        _validationIcon = Ui.IconColored(Icons.Check, StudioPalette.Good, 14, 2);
        _validation = Ui.Text(string.Empty, 12.5, "Studio.Text2");
        var titleExtra = _owner is not null ? Ui.Chip("podgraf · CompositeModule") : null;
        if (titleExtra is not null)
            titleExtra.Margin = new Thickness(8, 0, 0, 0);
        var right = Ui.HStack(8, Ui.HStack(6, _validationIcon, _validation), Ui.Separator(),
            Ui.Button("Snapshot", () => Session.SaveSnapshot(_brain), Icons.Camera, aShortcut: "Ctrl+S"));

        // ---------- paleta ----------
        var palette = Ui.VStack(14);
        palette.Margin = new Thickness(10, 14);
        var paletteHead = Ui.Header("Moduły · przeciągnij");
        paletteHead.Margin = new Thickness(10, 0);
        palette.Children.Add(paletteHead);
        foreach (var group in ModuleInspector.Palette(_creature, _brain).GroupBy(aItem => aItem.Group))
        {
            var stack = Ui.VStack(2);
            var title = Ui.Text(group.Key, 12, "Studio.Text3");
            title.Margin = new Thickness(10, 0, 0, 4);
            stack.Children.Add(title);
            foreach (var item in group)
                stack.Children.Add(PaletteButton(item.Name, item.Icon, item.Create, item.Disabled));
            palette.Children.Add(stack);
        }
        var left = PanelFrame.Side(Ui.Scroll(palette), true, 240);

        // ---------- płótno ----------
        _message = Ui.Text(string.Empty, 12.5, "Studio.Accent");
        var tools = Ui.HStack(4,
            Ui.Button("Zgrupuj", () => _canvas.GroupSelection(), Icons.Group, aShortcut: "Ctrl+G", aGhost: true),
            Ui.Button("Rozgrupuj", () => _canvas.UngroupSelection(), Icons.Ungroup, aShortcut: "Ctrl+Shift+G", aGhost: true),
            Ui.Separator(),
            Ui.Button("Ułóż", () => _canvas.AutoLayout(), Icons.Layout, aGhost: true),
            Ui.Button("Dopasuj", () => _canvas.FitToView(), Icons.Fit, aShortcut: "F", aGhost: true),
            Ui.Button("Usuń", () => _canvas.DeleteSelection(), Icons.Trash, aShortcut: "Del", aGhost: true));
        var toolbar = new DockPanel { Height = 48, Margin = new Thickness(8, 0, 12, 0) };
        DockPanel.SetDock(_message, Dock.Right);
        toolbar.Children.Add(_message);
        toolbar.Children.Add(tools);
        var toolbarBorder = new Border { Child = toolbar, BorderThickness = new Thickness(0, 0, 0, 1) };
        toolbarBorder.Res(Border.BackgroundProperty, "Studio.Layer");
        toolbarBorder.Res(Border.BorderBrushProperty, "Studio.Stroke");

        _errorText = Ui.Text(string.Empty, 13);
        _errorText.TextWrapping = TextWrapping.Wrap;
        _errorText.TextTrimming = TextTrimming.None;
        _errorText.MaxWidth = 480;
        _errorBanner = new Border
        {
            Child = Ui.HStack(12, Ui.IconColored(Icons.Warning, StudioPalette.Bad, 18), _errorText, Ui.Button("Wznów symulację", Session.Resume, aAccent: true)),
            Padding = new Thickness(14, 10),
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(StudioPalette.Bad),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(16),
            IsVisible = false
        };
        _errorBanner.Res(Border.BackgroundProperty, "Studio.Card");
        var canvasHost = new Grid();
        canvasHost.Children.Add(_canvas);
        canvasHost.Children.Add(_errorBanner);

        var center = new DockPanel();
        DockPanel.SetDock(toolbarBorder, Dock.Top);
        center.Children.Add(toolbarBorder);
        center.Children.Add(canvasHost);

        // ---------- inspektor ----------
        var rightSide = PanelFrame.Side(_inspectorHost, false, 320);

        var body = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
        body.Children.Add(left);
        Grid.SetColumn(center, 1);
        body.Children.Add(center);
        Grid.SetColumn(rightSide, 2);
        body.Children.Add(rightSide);
        Grid.SetColumnSpan(_dragLayer, 3);
        body.Children.Add(_dragLayer);

        if (_focus is not null && _graph.Modules.Contains(_focus))
            _canvas.Select(_focus);
        else
            BuildInspector();
        Validate();
        return PanelFrame.Create(this, body, titleExtra, aRight: right);
    }

    // ---------- paleta: klik dodaje na środku, przeciągnięcie — w miejscu upuszczenia ----------

    private Control PaletteButton(string aName, string aIcon, Func<BrainModule> aCreate, string? aDisabled)
    {
        var button = new Button
        {
            Content = Ui.HStack(10, Ui.Icon(aIcon, 15, "Studio.Accent"), Ui.Text(aName, 13)),
            Height = 32,
            Padding = new Thickness(10, 0),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Focusable = false,
            IsEnabled = aDisabled is null,
            Cursor = new Cursor(StandardCursorType.Hand)
        };
        ToolTip.SetTip(button, aDisabled ?? "Kliknij albo przeciągnij na płótno");
        button.Click += (_, _) =>
        {
            if (_suppressClick)
            {
                _suppressClick = false;
                return;
            }
            _canvas?.AddModule(aCreate());
        };
        button.AddHandler(PointerPressedEvent, (_, aEvent) =>
        {
            if (!aEvent.GetCurrentPoint(button).Properties.IsLeftButtonPressed)
                return;
            _dragItem = (aName, aCreate);
            _dragStart = aEvent.GetPosition(this);
            _dragging = false;
        }, RoutingStrategies.Tunnel, true);
        button.AddHandler(PointerMovedEvent, (_, aEvent) =>
        {
            if (_dragItem is not { } item)
                return;
            var point = aEvent.GetPosition(this);
            if (!_dragging && Math.Abs(point.X - _dragStart.X) + Math.Abs(point.Y - _dragStart.Y) > 6)
            {
                _dragging = true;
                _ghost = new Border
                {
                    Child = Ui.HStack(8, Ui.Icon(Icons.Plus, 14, "Studio.OnAccent"), Ui.Text(item.Name, 13, "Studio.OnAccent", FontWeight.SemiBold)),
                    Padding = new Thickness(12, 6),
                    CornerRadius = new CornerRadius(6),
                    Opacity = 0.9
                };
                _ghost.Res(Border.BackgroundProperty, "Studio.Accent");
                _dragLayer.Children.Add(_ghost);
            }
            if (_dragging && _ghost is not null)
            {
                var local = aEvent.GetPosition(_dragLayer);
                Canvas.SetLeft(_ghost, local.X + 12);
                Canvas.SetTop(_ghost, local.Y + 8);
            }
        }, RoutingStrategies.Tunnel, true);
        button.AddHandler(PointerReleasedEvent, (_, aEvent) =>
        {
            if (_dragItem is { } item && _dragging && _canvas is not null)
            {
                _suppressClick = true;
                var point = aEvent.GetPosition(_canvas);
                if (_canvas.ContainsScreen(point))
                    _canvas.AddModule(item.Create(), point);
            }
            if (_ghost is not null)
                _dragLayer.Children.Remove(_ghost);
            _ghost = null;
            _dragItem = null;
            _dragging = false;
        }, RoutingStrategies.Tunnel, true);
        return button;
    }

    // ---------- reakcje ----------

    private void BuildInspector()
    {
        if (_canvas is null || _inspector is null)
            return;
        _inspectorHost.Child = _inspector.Build(_canvas.Primary, _canvas.SelectedLink, _canvas.Selection.Count);
    }

    private void OnGraphChanged()
    {
        Validate();
        BuildInspector();
        _canvas?.InvalidateVisual();
    }

    private void Validate()
    {
        if (_validation is null || _validationIcon is null)
            return;
        try
        {
            _graph.Validate();
            _validation.Text = "Graf poprawny · skompilowany";
            _validationIcon.IsVisible = true;
        }
        catch (BrainException exception)
        {
            _validation.Text = "Błąd: " + exception.Message;
            _validationIcon.IsVisible = false;
        }
    }

    private void ShowMessage(string aMessage)
    {
        if (_message is null)
            return;
        _message.Text = aMessage;
        _messageAge = 0;
    }

    private void Activate(BrainModule aModule, Point aPoint)
    {
        if (_canvas is null || Navigator?.Active != this)
            return;
        StudioPanel? next = aModule switch
        {
            CompositeModule composite => new GraphPanel(Session, _creature, composite.Inner, composite, _graph, GraphCanvas.TitleOf(composite), null),
            NeuralNetworkModule network => new NetworkPanel(Session, _creature, network),
            _ => null
        };
        if (next is not null)
            Enter(next, OriginOf(_canvas, aPoint));
    }

    public override void Refresh(float aDelta)
    {
        _canvas?.Tick(aDelta);
        _inspector?.Refresh(aDelta);
        if (_canvas is not null)
            _canvas.ErrorModuleId = Session.ErrorModuleId;
        if (_errorBanner is not null && _errorText is not null)
        {
            _errorBanner.IsVisible = Session.Error is not null;
            _errorText.Text = Session.Error is { } error ? $"Symulacja zatrzymana: {error}" : string.Empty;
        }
        _messageAge += aDelta;
        if (_message is not null && _messageAge > 6)
            _message.Text = string.Empty;
    }

    public override void OnShown()
    {
        // Wracając z wnętrza podgrafu: porty i zawartość mogły się zmienić.
        OnGraphChanged();
    }

    public override bool HandleKey(KeyEventArgs aEvent)
    {
        if (_canvas is null)
            return false;
        var control = (aEvent.KeyModifiers & KeyModifiers.Control) != 0;
        var shift = (aEvent.KeyModifiers & KeyModifiers.Shift) != 0;
        switch (aEvent.Key)
        {
            case Key.Delete or Key.Back:
                _canvas.DeleteSelection();
                return true;
            case Key.G when control && shift:
                _canvas.UngroupSelection();
                return true;
            case Key.G when control:
                _canvas.GroupSelection();
                return true;
            case Key.A when control:
                _canvas.SelectAll();
                return true;
            case Key.S when control:
                Session.SaveSnapshot(_brain);
                return true;
            case Key.F:
                _canvas.FitToView();
                return true;
            case Key.Space:
                Session.TogglePause();
                return true;
            case Key.Enter when _canvas.Primary is { } primary and (CompositeModule or NeuralNetworkModule):
                Activate(primary, _canvas.ScreenCenterOf(primary));
                return true;
            case Key.Escape:
                return _canvas.Cancel();
            default:
                return false;
        }
    }
}
