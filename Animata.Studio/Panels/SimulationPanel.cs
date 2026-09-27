using System.Numerics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Animata.Core.Brains.Modules;
using Animata.Core.Entities;
using Animata.Core.Sensors;
using Animata.Core.Training;
using Animata.Core.WorldObjects;
using Animata.Rendering.HelixToolkit;
using Animata.Studio.Controls;
using Animata.Studio.Kit;
using Animata.Studio.Navigation;
using Animata.Studio.Session;
using Animata.Studio.Theme;

namespace Animata.Studio.Panels;

/// <summary>
/// Panel sceny: widok 3D w środku, z lewej lista encji, z prawej właściwości zaznaczonej, u dołu stan nauki.
/// Dwuklik stwora (w 3D albo na liście) wjeżdża do niego — przejście wyrasta z miejsca, gdzie stwór jest na ekranie.
/// </summary>
public sealed class SimulationPanel : StudioPanel, IDisposable
{
    private static readonly IBrush OverlayBrush = new SolidColorBrush(Color.FromArgb(205, 16, 21, 31));
    private static readonly IBrush OverlayText = new SolidColorBrush(Color.FromRgb(216, 224, 234));

    private readonly SceneRenderer _renderer = new();
    private readonly StackPanel _list = new() { Spacing = 2 };
    private readonly StackPanel _properties = new() { Spacing = 18, Margin = new Thickness(18) };
    private readonly List<Action> _updaters = [];
    private readonly Dictionary<Entity, (Button Button, Border BadgeBox, TextBlock Badge)> _items = [];
    private Control? _content;
    private TextBox? _search;
    private TextBlock? _time;
    private TextBlock? _summary;
    private TextBlock? _status;
    private TextBlock? _counts;
    private TextBlock? _speed;
    private TextBlock? _trainingLabel;
    private Button? _pause;
    private Border? _errorBanner;
    private TextBlock? _errorText;
    private string _listKey = string.Empty;
    private Entity? _propertiesOf;
    private bool _propertiesBuilt;
    private bool? _pausedShown;

    public SimulationPanel(StudioSession aSession) : base(aSession, "Scena demo")
    {
        _renderer.SelectionChanged += _ =>
        {
            UpdateListSelection();
            BuildProperties();
        };
        _renderer.EntityActivated += aEntity =>
        {
            if (aEntity is ActiveEntity creature)
                EnterCreature(creature);
        };
        StudioTheme.Changed += UpdateListSelection;
    }

    public SceneRenderer Renderer => _renderer;

    public override bool KeepAlive => true;

    protected override Control Build()
    {
        if (_content is not null)
            return _content;

        // ---------- pasek ----------
        _pause = Ui.IconButton(Icons.Pause, "Pauza / wznów (Spacja)", Session.TogglePause);
        var step = Ui.IconButton(Icons.Step, "Jeden krok (w pauzie)", Session.Step);
        _speed = Ui.Text("1×", 12.5);
        var speed = new Button
        {
            Content = _speed,
            Height = 32,
            MinWidth = 44,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Focusable = false
        };
        speed.Click += (_, _) => Session.CycleSpeed();
        ToolTip.SetTip(speed, "Prędkość symulacji");
        var center = PanelFrame.Group(_pause, step, speed);

        _trainingLabel = Ui.Text("Nauka", 13);
        var training = new Button
        {
            Content = Ui.HStack(8, Ui.Icon(Icons.Learn), _trainingLabel, Ui.Kbd("L")),
            Height = 32,
            Padding = new Thickness(12, 0),
            Focusable = false
        };
        training.Click += (_, _) => Session.ToggleTraining(TrainingScope());
        var right = Ui.HStack(6,
            Ui.IconButton(Icons.Moon, "Jasny / ciemny motyw", StudioTheme.Toggle),
            Ui.Separator(),
            Ui.Button("Snapshot", SaveSnapshot, Icons.Camera, aShortcut: "Ctrl+S"),
            Ui.Button("Cofnij", StepBack, Icons.Undo, aShortcut: "Z"),
            training,
            Ui.Button("Losuj", () => Session.Randomize(TrainingScope()), Icons.Shuffle, aShortcut: "K"));

        // ---------- lista encji ----------
        _search = new TextBox { Watermark = "Szukaj w scenie", Margin = new Thickness(10, 14, 10, 6), MinHeight = 32 };
        _search.TextChanged += (_, _) => _listKey = string.Empty;
        var listDock = new DockPanel();
        DockPanel.SetDock(_search, Dock.Top);
        listDock.Children.Add(_search);
        _list.Margin = new Thickness(8, 4, 8, 12);
        listDock.Children.Add(Ui.Scroll(_list));
        var left = PanelFrame.Side(listDock, true, 280);

        // ---------- widok 3D z nakładkami ----------
        var view = new Grid();
        view.Children.Add(_renderer.View);
        _time = new TextBlock { FontFamily = Ui.Mono, FontSize = 12, Foreground = OverlayText, VerticalAlignment = VerticalAlignment.Center };
        view.Children.Add(Overlay(_time, HorizontalAlignment.Left, VerticalAlignment.Top));
        view.Children.Add(Overlay(new TextBlock
        {
            Text = "Dwuklik: wejdź w stwora · LPM: przesuń · PPM + WSADQE: kamera",
            FontSize = 12,
            Foreground = OverlayText
        }, HorizontalAlignment.Left, VerticalAlignment.Bottom));

        _errorText = Ui.Text(string.Empty, 13, "Studio.Text");
        _errorText.TextWrapping = TextWrapping.Wrap;
        _errorText.MaxWidth = 460;
        _errorBanner = new Border
        {
            Child = Ui.HStack(12, Ui.IconColored(Icons.Warning, StudioPalette.Bad, 18), _errorText, Ui.Button("Wznów", Session.Resume, aAccent: true)),
            Padding = new Thickness(14, 10),
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(StudioPalette.Bad),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(16),
            IsVisible = false
        };
        _errorBanner.Res(Border.BackgroundProperty, "Studio.Card");
        view.Children.Add(_errorBanner);

        _summary = Ui.Text(string.Empty, 12.5, "Studio.Text2");
        _status = Ui.Text(string.Empty, 12.5, "Studio.Accent");
        _counts = Ui.Text(string.Empty, 12.5, "Studio.Text3");
        var strip = new DockPanel { Height = 44, Margin = new Thickness(16, 0) };
        DockPanel.SetDock(_counts, Dock.Right);
        strip.Children.Add(_counts);
        strip.Children.Add(Ui.HStack(18, _summary, _status));
        var stripBorder = new Border { Child = strip, BorderThickness = new Thickness(0, 1, 0, 0) };
        stripBorder.Res(Border.BackgroundProperty, "Studio.Layer");
        stripBorder.Res(Border.BorderBrushProperty, "Studio.Stroke");
        var middle = new DockPanel();
        DockPanel.SetDock(stripBorder, Dock.Bottom);
        middle.Children.Add(stripBorder);
        middle.Children.Add(view);

        // ---------- właściwości ----------
        var rightSide = PanelFrame.Side(Ui.Scroll(_properties), false, 320);

        var body = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
        body.Children.Add(left);
        Grid.SetColumn(middle, 1);
        body.Children.Add(middle);
        Grid.SetColumn(rightSide, 2);
        body.Children.Add(rightSide);

        _content = PanelFrame.Create(this, body, aCenter: center, aRight: right);
        return _content;
    }

    private static Border Overlay(Control aChild, HorizontalAlignment aHorizontal, VerticalAlignment aVertical) => new()
    {
        Child = aChild,
        Background = OverlayBrush,
        CornerRadius = new CornerRadius(14),
        Padding = new Thickness(12, 5),
        Margin = new Thickness(16),
        HorizontalAlignment = aHorizontal,
        VerticalAlignment = aVertical,
        IsHitTestVisible = false
    };

    // ---------- odświeżanie ----------

    public override void Refresh(float aDelta)
    {
        Session.Hold = _renderer.IsDragging;
        _renderer.Sync(Session.World);
        _renderer.UpdateCamera(Math.Min(aDelta, 0.1f));

        if (_time is not null)
            _time.Text = $"t {Session.SimTime:0.0} s · 30 Hz · {Session.Speed:0.##}×" + (Session.Paused ? " · pauza" : string.Empty);
        if (_speed is not null)
            _speed.Text = $"{Session.Speed:0.##}×";
        if (_pause is not null && _pausedShown != Session.Paused)
        {
            _pausedShown = Session.Paused;
            _pause.Content = Ui.Icon(Session.Paused ? Icons.Play : Icons.Pause, 16, "Studio.Text", Session.Paused);
        }
        if (_errorBanner is not null && _errorText is not null)
        {
            _errorBanner.IsVisible = Session.Error is not null;
            _errorText.Text = Session.Error is { } error ? $"Symulacja zatrzymana: {error}" : string.Empty;
        }
        if (_trainingLabel is not null)
            _trainingLabel.Text = TrainingScope().Any(Session.IsTraining) ? "Nauka trwa" : "Nauka stoi";
        if (_summary is not null)
            _summary.Text = Session.Training.Count > 0 ? "nauka: " + Session.Training.Summary() : "nauka zatrzymana";
        if (_status is not null)
            _status.Text = Session.Status ?? string.Empty;
        if (_counts is not null)
            _counts.Text = $"{Session.Creatures.Count()} stwory · {Session.World.Entities.OfType<TargetBall>().Count()} cele · " +
                $"{Session.World.Entities.OfType<Obstacle>().Count()} słupki";

        RefreshList();
        if (_propertiesOf is not null && !Session.World.Contains(_propertiesOf) || !_propertiesBuilt)
            BuildProperties();
        foreach (var update in _updaters)
            update();
    }

    public override void OnShown()
    {
        _listKey = string.Empty;
        BuildProperties();
    }

    // ---------- lista ----------

    private void RefreshList()
    {
        var filter = _search?.Text?.Trim() ?? string.Empty;
        var entities = Session.World.Entities
            .Where(aEntity => filter.Length == 0 || StudioSession.NameOf(aEntity).Contains(filter, StringComparison.OrdinalIgnoreCase))
            .ToList();
        var key = filter + "|" + string.Join(",", entities.Select(aEntity => aEntity.Id.ToString("N")[..8] + StudioSession.NameOf(aEntity)));
        if (key != _listKey)
        {
            _listKey = key;
            _list.Children.Clear();
            _items.Clear();
            AddGroup("Stwory", entities.OfType<ActiveEntity>());
            AddGroup("Cele", entities.OfType<TargetBall>());
            AddGroup("Przeszkody", entities.OfType<Obstacle>());
            AddGroup("Inne", entities.Where(aEntity => aEntity is not ActiveEntity and not TargetBall and not Obstacle));
            UpdateListSelection();
        }

        foreach (var (entity, item) in _items)
        {
            var progress = entity is ActiveEntity { Brain: { } brain } ? Session.Training.ProgressOf(brain) : null;
            item.BadgeBox.IsVisible = progress is not null;
            if (progress is not null)
                item.Badge.Text = $"gen {progress.Generation}";
        }
    }

    private void AddGroup(string aTitle, IEnumerable<Entity> aEntities)
    {
        var entities = aEntities.ToList();
        if (entities.Count == 0)
            return;
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(10, 10, 10, 4) };
        header.Children.Add(Ui.Header(aTitle));
        var count = Ui.Text(entities.Count.ToString(), 11, "Studio.Text3");
        Grid.SetColumn(count, 1);
        header.Children.Add(count);
        _list.Children.Add(header);

        foreach (var entity in entities)
        {
            var badge = Ui.MonoText(string.Empty, 11, "Studio.Text");
            var badgeBorder = new Border { Child = badge, Padding = new Thickness(8, 1), CornerRadius = new CornerRadius(10) };
            badgeBorder.Res(Border.BackgroundProperty, "Studio.AccentSoft");
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 10 };
            row.Children.Add(Ui.Dot(Ui.ColorOf(entity)));
            var name = Ui.Text(StudioSession.NameOf(entity), 13);
            Grid.SetColumn(name, 1);
            row.Children.Add(name);
            Grid.SetColumn(badgeBorder, 2);
            row.Children.Add(badgeBorder);

            var button = new Button
            {
                Content = row,
                Height = 36,
                Padding = new Thickness(12, 0),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                VerticalContentAlignment = VerticalAlignment.Center,
                BorderThickness = new Thickness(0),
                Focusable = false
            };
            var target = entity;
            button.Click += (_, _) => _renderer.Select(target);
            button.DoubleTapped += (_, _) =>
            {
                if (target is ActiveEntity creature)
                    EnterCreature(creature, button);
            };
            _items[entity] = (button, badgeBorder, badge);
            _list.Children.Add(button);
        }
    }

    private void UpdateListSelection()
    {
        foreach (var (entity, item) in _items)
            item.Button.Background = entity == _renderer.SelectedEntity ? Ui.Brush(StudioTheme.Palette.AccentSoft) : Brushes.Transparent;
    }

    // ---------- właściwości ----------

    private void BuildProperties()
    {
        _propertiesBuilt = true;
        _updaters.Clear();
        _properties.Children.Clear();
        var entity = _renderer.SelectedEntity is { } selected && Session.World.Contains(selected) ? selected : null;
        _propertiesOf = entity;

        if (entity is null)
        {
            _properties.Children.Add(Ui.VStack(8,
                Ui.Text("Nic nie zaznaczono", 16, "Studio.Text", FontWeight.SemiBold),
                Wrap(Ui.Text("Kliknij encję w scenie albo na liście. Dwuklik stwora wjeżdża do jego wnętrza: zmysły, mózg, aktuatory.", 13, "Studio.Text3"))));
            return;
        }

        var swatch = new Border { Width = 36, Height = 36, CornerRadius = new CornerRadius(8), Background = new SolidColorBrush(Ui.ColorOf(entity)) };
        var title = Ui.Text(StudioSession.NameOf(entity), 16, "Studio.Text", FontWeight.SemiBold);
        _updaters.Add(() => title.Text = StudioSession.NameOf(entity));
        _properties.Children.Add(Ui.HStack(12, swatch, Ui.VStack(2, title, Ui.MonoText(entity.GetType().Name, 11.5, "Studio.Text3"))));

        if (entity is ActiveEntity { Brain: not null } creature)
        {
            var enter = Ui.Button("Wejdź do stwora", () => EnterCreature(creature), Icons.Enter, aAccent: true);
            enter.HorizontalAlignment = HorizontalAlignment.Stretch;
            enter.HorizontalContentAlignment = HorizontalAlignment.Center;
            enter.Height = 36;
            _properties.Children.Add(enter);
        }

        // Nazwa i transformacja.
        var name = Ui.Field(entity.Name, aText =>
        {
            entity.Name = aText.Trim();
            _listKey = string.Empty;
            return true;
        }, aMono: false);
        name.Watermark = StudioSession.NameOf(entity);
        var x = Ui.Field(Ui.F(entity.Body.Position.X), aText => SetPosition(entity, aText, true), 110);
        var y = Ui.Field(Ui.F(entity.Body.Position.Y), aText => SetPosition(entity, aText, false), 110);
        var yaw = Ui.Field(Ui.F(YawDegrees(entity), 0), aText =>
        {
            if (!Ui.TryParse(aText, out var degrees))
                return false;
            entity.Body.Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, degrees * MathF.PI / 180);
            return true;
        }, 110);
        _updaters.Add(() =>
        {
            Ui.SetIfIdle(x, Ui.F(entity.Body.Position.X));
            Ui.SetIfIdle(y, Ui.F(entity.Body.Position.Y));
            Ui.SetIfIdle(yaw, Ui.F(YawDegrees(entity), 0));
        });
        var transform = Ui.VStack(4, Ui.Header("Transformacja"), Ui.Row("Nazwa", name), Ui.Row("X [m]", x), Ui.Row("Y [m]", y));
        if (entity is ActiveEntity)
            transform.Children.Add(Ui.Row("Kierunek [°]", yaw));
        switch (entity)
        {
            case Obstacle obstacle:
                transform.Children.Add(Ui.Row("Promień [m]", Ui.Field(Ui.F(obstacle.Radius), aText => SetRadius(aText, aValue => obstacle.Radius = aValue), 110)));
                break;
            case TargetBall ball:
                transform.Children.Add(Ui.Row("Promień [m]", Ui.Field(Ui.F(ball.Radius), aText => SetRadius(aText, aValue => ball.Radius = aValue), 110)));
                break;
        }
        _properties.Children.Add(transform);

        if (entity is ActiveEntity active)
        {
            var details = Ui.VStack(2, Ui.Header("Budowa"));
            var controllers = active.Brain?.Graph.Modules.Where(aModule => aModule is not SensorModule and not ActuatorModule)
                .Select(GraphCanvas.TitleOf).ToList() ?? [];
            details.Children.Add(Ui.Row("Mózg", controllers.Count > 0 ? string.Join(", ", controllers) : "—"));
            details.Children.Add(Ui.Row("Zmysły", string.Join(", ", active.Body.Sensors.Select(SensorName))));
            details.Children.Add(Ui.Row("Napęd", string.Join(", ", active.Body.Actuators.Select(aActuator => aActuator.GetType().Name.Replace("Actuator", string.Empty)))));
            _properties.Children.Add(details);

            if (TrainingController.FindNetwork(active) is not null)
                _properties.Children.Add(PanelParts.TrainingCard(Session, active, _updaters));
        }
        else
        {
            var category = Ui.VStack(2, Ui.Header("Szczegóły"),
                Ui.Row("Kategoria", entity.Category.ToString()),
                Ui.Row("Ruchoma", entity.IsMovable ? "tak" : "nie (kolizje jej nie przesuwają)"));
            _properties.Children.Add(category);
        }
    }

    private static Control Wrap(TextBlock aText)
    {
        aText.TextWrapping = TextWrapping.Wrap;
        aText.TextTrimming = TextTrimming.None;
        return aText;
    }

    private static string SensorName(Sensor aSensor) => aSensor switch
    {
        TargetSensor => "Oko",
        RaySensor rays => $"Wąsy ×{rays.Angles.Count}",
        _ => aSensor.GetType().Name
    };

    private static float YawDegrees(Entity aEntity)
    {
        var heading = Vector3.Transform(Vector3.UnitX, aEntity.Body.Rotation);
        return MathF.Atan2(heading.Y, heading.X) * 180 / MathF.PI;
    }

    private static bool SetPosition(Entity aEntity, string aText, bool aX)
    {
        if (!Ui.TryParse(aText, out var value))
            return false;
        var position = aEntity.Body.Position;
        aEntity.Body.Position = aX ? position with { X = value } : position with { Y = value };
        return true;
    }

    private static bool SetRadius(string aText, Action<float> aSet)
    {
        if (!Ui.TryParse(aText, out var value) || value <= 0.05f || value > 5)
            return false;
        aSet(value);
        return true;
    }

    // ---------- akcje ----------

    private void EnterCreature(ActiveEntity aCreature, Control? aFrom = null)
    {
        if (aCreature.Brain is null || Navigator?.Active != this)
            return;
        _renderer.Select(aCreature);
        var origin = aFrom is not null ? OriginOf(aFrom)
            : _renderer.TryProject(aCreature, out var point) ? OriginOf(_renderer.View, point)
            : null;
        Enter(new CreaturePanel(Session, aCreature), origin);
    }

    /// <summary>Zaznaczony stwór z siecią albo — bez zaznaczenia stwora z siecią — wszystkie stwory z siecią.</summary>
    private List<ActiveEntity> TrainingScope() =>
        _renderer.SelectedEntity is ActiveEntity selected && TrainingController.FindNetwork(selected) is not null && Session.World.Contains(selected)
            ? [selected]
            : Session.NeuralCreatures.ToList();

    /// <summary>Mózg zaznaczonego stwora, a bez zaznaczenia — pierwszego stwora z siecią.</summary>
    private Core.Brains.Brain? TargetBrain() =>
        _renderer.SelectedEntity is ActiveEntity { Brain: { } selected } && Session.World.Contains(_renderer.SelectedEntity)
            ? selected
            : Session.NeuralCreatures.FirstOrDefault()?.Brain;

    private void SaveSnapshot()
    {
        if (TargetBrain() is { } brain)
            Session.SaveSnapshot(brain);
        else
            Session.Status = "zaznacz stwora";
    }

    private void StepBack()
    {
        if (TargetBrain() is { } brain)
            Session.StepBack(brain);
        else
            Session.Status = "zaznacz stwora";
    }

    public override bool HandleKey(KeyEventArgs aEvent)
    {
        var world = Session.World;
        switch (aEvent.Key)
        {
            case Key.Space:
                Session.TogglePause();
                return true;
            case Key.Delete when _renderer.SelectedEntity is { } selected:
                Session.Remove(selected);
                _renderer.Select(null);
                _renderer.Sync(world);
                return true;
            case Key.Insert:
                var target = WorldObjectCatalog.CreateTargetBall(_renderer.GroundPointAtCenter());
                world.Add(target);
                foreach (var eye in Eyes())
                    if (eye.TargetId is not { } id || world.Find(id) is null)
                        eye.TargetId = target.Id;
                _renderer.Sync(world);
                _renderer.Select(target);
                return true;
            case Key.O:
                var obstacle = WorldObjectCatalog.CreateObstacle(_renderer.GroundPointAtCenter());
                world.Add(obstacle);
                _renderer.Sync(world);
                _renderer.Select(obstacle);
                return true;
            case Key.T when _renderer.SelectedEntity is TargetBall chosen:
                foreach (var eye in Eyes())
                    eye.TargetId = chosen.Id;
                Session.Status = $"wszystkie oczy patrzą na: {StudioSession.NameOf(chosen)}";
                return true;
            case Key.L:
                Session.ToggleTraining(TrainingScope());
                return true;
            case Key.K:
                Session.Randomize(TrainingScope());
                return true;
            case Key.S when (aEvent.KeyModifiers & KeyModifiers.Control) != 0:
                SaveSnapshot();
                return true;
            case Key.Z:
                StepBack();
                return true;
            case Key.Enter when _renderer.SelectedEntity is ActiveEntity creature:
                EnterCreature(creature);
                return true;
            default:
                return false;
        }
    }

    private IEnumerable<TargetSensor> Eyes() =>
        Session.Creatures.SelectMany(aCreature => aCreature.Body.Sensors.OfType<TargetSensor>());

    public void Dispose()
    {
        StudioTheme.Changed -= UpdateListSelection;
        _renderer.Dispose();
    }
}
