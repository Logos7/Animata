using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Animata.Core.Actuators;
using Animata.Core.Brains;
using Animata.Core.Brains.Modules;
using Animata.Core.Entities;
using Animata.Core.Sensors;
using Animata.Studio.Controls;
using Animata.Studio.Kit;
using Animata.Studio.Navigation;
using Animata.Studio.Session;
using Animata.Studio.Theme;

namespace Animata.Studio.Panels;

/// <summary>
/// Wnętrze stwora: zmysły (z lewej), ciało i mózg (w środku), aktuatory, nauka i snapshoty (z prawej).
/// Klik w kartę zmysłu, aktuatora albo mózgu wjeżdża do grafu mózgu z tym węzłem zaznaczonym.
/// </summary>
public sealed class CreaturePanel : StudioPanel
{
    private readonly ActiveEntity _creature;
    private readonly Brain _brain;
    private readonly List<Action> _updaters = [];
    private readonly StackPanel _snapshots = new() { Spacing = 2 };
    private BodyDiagram? _body;
    private GraphCanvas? _preview;
    private TextBlock? _brainInfo;
    private int _snapshotCount = -1;
    private float _snapshotCheck;

    public CreaturePanel(StudioSession aSession, ActiveEntity aCreature) : base(aSession, StudioSession.NameOf(aCreature))
    {
        _creature = aCreature;
        _brain = aCreature.Brain!;
    }

    protected override Control Build()
    {
        _updaters.Clear();
        var controllers = _brain.Graph.Modules.Where(aModule => aModule is not SensorModule and not ActuatorModule)
            .Select(GraphCanvas.TitleOf).ToList();
        var titleExtra = Ui.HStack(8, Ui.Dot(Ui.ColorOf(_creature)),
            Ui.MonoText(controllers.Count > 0 ? string.Join(", ", controllers) : "bez sterownika", 12, "Studio.Text3"));
        titleExtra.Margin = new Thickness(6, 0, 0, 0);
        var right = Ui.HStack(6,
            Ui.Button("Snapshot", () => Session.SaveSnapshot(_brain), Icons.Camera, aShortcut: "Ctrl+S"),
            Ui.Button("Cofnij", () => Session.StepBack(_brain), Icons.Undo, aShortcut: "Z"));

        var body = new Grid { ColumnDefinitions = new ColumnDefinitions("340,*,340"), ColumnSpacing = 20, Margin = new Thickness(24) };

        // ---------- zmysły ----------
        var senses = Ui.VStack(14, Ui.Header("Zmysły"));
        foreach (var sensor in _creature.Body.Sensors)
            senses.Children.Add(SensorCard(sensor));
        if (_creature.Body.Sensors.Count == 0)
            senses.Children.Add(Ui.Text("Ten stwór nie ma zmysłów.", 13, "Studio.Text3"));
        body.Children.Add(Ui.Scroll(senses));

        // ---------- ciało i mózg ----------
        var center = new Grid { RowDefinitions = new RowDefinitions("*,*"), RowSpacing = 20 };
        _body = new BodyDiagram(_creature, Session.World);
        var bodyHead = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(16, 12) };
        bodyHead.Children.Add(Ui.Header("Ciało"));
        var bodyInfo = Ui.MonoText(BodyInfo(), 11.5, "Studio.Text3");
        Grid.SetColumn(bodyInfo, 1);
        bodyHead.Children.Add(bodyInfo);
        var bodyDock = new DockPanel();
        DockPanel.SetDock(bodyHead, Dock.Top);
        bodyDock.Children.Add(bodyHead);
        bodyDock.Children.Add(_body);
        center.Children.Add(Ui.Card(bodyDock, 0));

        _preview = new GraphCanvas(_brain.Graph, aReadOnly: true) { IsHitTestVisible = false };
        _brainInfo = Ui.Text(string.Empty, 12.5, "Studio.Text3");
        var brainHead = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 10, Margin = new Thickness(18, 12) };
        brainHead.Children.Add(Ui.HStack(10, Ui.Icon(Icons.Brain, 18, "Studio.Accent"), Ui.Text("Mózg", 16, "Studio.Text", FontWeight.SemiBold)));
        Grid.SetColumn(_brainInfo, 1);
        brainHead.Children.Add(_brainInfo);
        var enterChip = Ui.Chip("Wejdź  →");
        Grid.SetColumn(enterChip, 2);
        brainHead.Children.Add(enterChip);
        var brainDock = new DockPanel();
        DockPanel.SetDock(brainHead, Dock.Top);
        brainDock.Children.Add(brainHead);
        brainDock.Children.Add(new Border { Child = _preview, ClipToBounds = true, CornerRadius = new CornerRadius(0, 0, 8, 8) });
        var brainCard = Clickable(Ui.Card(brainDock, 0), aCard => EnterBrain(null, aCard));
        Grid.SetRow(brainCard, 1);
        center.Children.Add(brainCard);
        Grid.SetColumn(center, 1);
        body.Children.Add(center);

        // ---------- aktuatory, nauka, snapshoty ----------
        var effects = Ui.VStack(14, Ui.Header("Aktuatory"));
        foreach (var actuator in _creature.Body.Actuators)
            effects.Children.Add(ActuatorCard(actuator));
        if (PanelParts.HasNetwork(_creature))
            effects.Children.Add(PanelParts.TrainingCard(Session, _creature, _updaters));
        var snapshotsHead = Ui.Header("Snapshoty mózgu");
        snapshotsHead.Margin = new Thickness(0, 6, 0, 0);
        effects.Children.Add(snapshotsHead);
        effects.Children.Add(Ui.Card(_snapshots, 6));
        var right2 = Ui.Scroll(effects);
        Grid.SetColumn(right2, 2);
        body.Children.Add(right2);

        return PanelFrame.Create(this, body, titleExtra, aRight: right);
    }

    // ---------- karty ----------

    /// <summary>Owija kartę tak, że klik (i dwuklik) wjeżdża dalej; kursor ręki podpowiada, że się da.</summary>
    private static Border Clickable(Border aCard, Action<Border> aOpen)
    {
        aCard.Cursor = new Cursor(StandardCursorType.Hand);
        aCard.Tapped += (_, aEvent) =>
        {
            aOpen(aCard);
            aEvent.Handled = true;
        };
        aCard.PointerEntered += (_, _) => aCard.Res(Border.BorderBrushProperty, "Studio.Accent");
        aCard.PointerExited += (_, _) => aCard.Res(Border.BorderBrushProperty, "Studio.Stroke");
        return aCard;
    }

    private static Grid CardHead(string aIcon, string aTitle, string aType)
    {
        var head = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*"), ColumnSpacing = 10 };
        head.Children.Add(Ui.Icon(aIcon, 18, "Studio.Accent"));
        var title = Ui.Text(aTitle, 14, "Studio.Text", FontWeight.SemiBold);
        Grid.SetColumn(title, 1);
        head.Children.Add(title);
        var type = Ui.MonoText(aType, 11, "Studio.Text3");
        type.HorizontalAlignment = HorizontalAlignment.Right;
        Grid.SetColumn(type, 2);
        head.Children.Add(type);
        return head;
    }

    private Control SensorCard(Sensor aSensor)
    {
        var module = _brain.Graph.Descendants().OfType<SensorModule>().FirstOrDefault(aModule => aModule.SensorId == aSensor.Id);
        IReadOnlyDictionary<string, float> Read() =>
            module is not null && _brain.Graph.GraphOf(module)?.LastOutputs(module) is { } outputs
                ? outputs
                : aSensor.Read(_creature, Session.World);

        var content = Ui.VStack(10);
        switch (aSensor)
        {
            case TargetSensor eye:
            {
                content.Children.Add(CardHead(Icons.Eye, module is not null ? GraphCanvas.TitleOf(module) : "Oko", "TargetSensor"));
                var compass = new Compass();
                var rows = Ui.VStack(0);
                var values = new Dictionary<string, TextBlock>();
                foreach (var port in new[] { TargetSensor.FoundPort, TargetSensor.DistancePort, TargetSensor.GapPort, TargetSensor.DirectionXPort, TargetSensor.DirectionYPort })
                {
                    var value = Ui.MonoText("—", 12);
                    values[port] = value;
                    var row = Ui.Row(port, value, 22);
                    ((TextBlock)row.Children[0]).FontFamily = Ui.Mono;
                    ((TextBlock)row.Children[0]).FontSize = 12;
                    rows.Children.Add(row);
                }
                var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 14 };
                grid.Children.Add(compass);
                Grid.SetColumn(rows, 1);
                grid.Children.Add(rows);
                content.Children.Add(grid);
                var target = Ui.Text(string.Empty, 12, "Studio.Text3");
                content.Children.Add(target);
                _updaters.Add(() =>
                {
                    var readings = Read();
                    foreach (var (port, block) in values)
                        block.Text = port == TargetSensor.FoundPort
                            ? readings.GetValueOrDefault(port) > 0 ? "tak" : "nie"
                            : Ui.F(readings.GetValueOrDefault(port), port.StartsWith("Direction") ? 3 : 2);
                    compass.Set(MathF.Atan2(readings.GetValueOrDefault(TargetSensor.DirectionYPort), readings.GetValueOrDefault(TargetSensor.DirectionXPort)),
                        readings.GetValueOrDefault(TargetSensor.FoundPort) > 0);
                    target.Text = eye.TargetId is { } id && Session.World.Find(id) is { } found
                        ? $"cel: {StudioSession.NameOf(found)}"
                        : "cel: brak (w scenie: PPM → Kulka)";
                });
                break;
            }
            case RaySensor rays:
            {
                content.Children.Add(CardHead(Icons.Whiskers, module is not null ? GraphCanvas.TitleOf(module) : "Wąsy",
                    $"RaySensor ×{rays.Angles.Count} · {rays.Range:0.#} m"));
                var gauges = new List<(Gauge Gauge, TextBlock Value)>();
                for (var ray = 0; ray < rays.Angles.Count; ray++)
                {
                    var row = new Grid { ColumnDefinitions = new ColumnDefinitions("44,40,*,40"), ColumnSpacing = 8, Height = 20 };
                    row.Children.Add(Ui.MonoText(RaySensor.PortName(ray), 11.5, "Studio.Text3"));
                    var angle = Ui.MonoText($"{rays.Angles[ray] * 180 / MathF.PI:+0;−0;0}°", 11, "Studio.Text3");
                    Grid.SetColumn(angle, 1);
                    row.Children.Add(angle);
                    var gauge = new Gauge(aBipolar: false, aHitColors: true) { VerticalAlignment = VerticalAlignment.Center };
                    Grid.SetColumn(gauge, 2);
                    row.Children.Add(gauge);
                    var value = Ui.MonoText("0.00", 11.5);
                    value.HorizontalAlignment = HorizontalAlignment.Right;
                    Grid.SetColumn(value, 3);
                    row.Children.Add(value);
                    gauges.Add((gauge, value));
                    content.Children.Add(row);
                }
                _updaters.Add(() =>
                {
                    var readings = Read();
                    for (var ray = 0; ray < gauges.Count; ray++)
                    {
                        var proximity = readings.GetValueOrDefault(RaySensor.PortName(ray));
                        gauges[ray].Gauge.Value = proximity;
                        gauges[ray].Value.Text = Ui.F(proximity);
                    }
                });
                break;
            }
            default:
            {
                content.Children.Add(CardHead(Icons.Eye, aSensor.GetType().Name, "Sensor"));
                var values = aSensor.OutputPorts.ToDictionary(aPort => aPort, _ => Ui.MonoText("—", 12));
                foreach (var (port, value) in values)
                    content.Children.Add(Ui.Row(port, value, 22));
                _updaters.Add(() =>
                {
                    var readings = Read();
                    foreach (var (port, value) in values)
                        value.Text = Ui.F(readings.GetValueOrDefault(port));
                });
                break;
            }
        }
        return Clickable(Ui.Card(content), aCard => EnterBrain(module, aCard));
    }

    private Control ActuatorCard(Actuator aActuator)
    {
        var module = _brain.Graph.Descendants().OfType<ActuatorModule>().FirstOrDefault(aModule => aModule.ActuatorId == aActuator.Id);
        var content = Ui.VStack(12, CardHead(Icons.Wheel, module is not null ? GraphCanvas.TitleOf(module) : "Napęd",
            aActuator.GetType().Name.Replace("Actuator", string.Empty)));
        var rows = new List<(string Port, Gauge Gauge, TextBlock Value)>();
        foreach (var port in aActuator.InputPorts)
        {
            var bipolar = !(aActuator is DiskDriveActuator && port == DiskDriveActuator.StepPort);
            var value = Ui.MonoText("0.00", 12);
            var head = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
            head.Children.Add(Ui.MonoText(port, 12.5));
            Grid.SetColumn(value, 1);
            head.Children.Add(value);
            var gauge = new Gauge(bipolar);
            content.Children.Add(Ui.VStack(6, head, gauge));
            rows.Add((port, gauge, value));
        }
        if (module is null)
            content.Children.Add(Ui.Text("Żaden moduł mózgu nim nie steruje.", 12, "Studio.Text3"));
        _updaters.Add(() =>
        {
            var command = module?.LastCommand;
            foreach (var (port, gauge, value) in rows)
            {
                var amount = command?.GetValueOrDefault(port) ?? 0;
                gauge.Value = amount;
                value.Text = $"{Ui.F(amount)} · {Physical(aActuator, port, amount)}";
            }
        });
        return Clickable(Ui.Card(content), aCard => EnterBrain(module, aCard));
    }

    /// <summary>Komenda przeliczona na wielkość fizyczną aktuatora (kąt kół, prędkość).</summary>
    private static string Physical(Actuator aActuator, string aPort, float aValue)
    {
        var value = Math.Clamp(aValue, -1, 1);
        return (aActuator, aPort) switch
        {
            (SteeringDriveActuator steering, SteeringDriveActuator.SteerPort) => $"{Ui.F(value * steering.MaxSteerAngle * 180 / MathF.PI, 1)}° kół",
            (SteeringDriveActuator steering, SteeringDriveActuator.ThrottlePort) =>
                $"{Ui.F(value >= 0 ? value * steering.MaxSpeed : value * steering.MaxReverseSpeed)} m/s",
            (DiskDriveActuator disk, DiskDriveActuator.TurnPort) => $"{Ui.F(value * disk.MaxTurnSpeed)} rad/s",
            (DiskDriveActuator disk, DiskDriveActuator.StepPort) => $"{Ui.F(Math.Max(0, value) * disk.MaxSpeed)} m/s",
            _ => string.Empty
        };
    }

    private string BodyInfo() => _creature switch
    {
        Core.WorldObjects.CarCreature car => $"CarCreature · {car.Length:0.##} × {car.Width:0.##} × {car.Height:0.##} m · obrys r {car.BoundingRadius:0.##}",
        Core.WorldObjects.CylinderCreature cylinder => $"CylinderCreature · r {cylinder.Radius:0.##} · h {cylinder.Height:0.##} m",
        _ => _creature.GetType().Name
    };

    // ---------- snapshoty ----------

    private void RebuildSnapshots()
    {
        _snapshots.Children.Clear();
        if (_brain.Snapshots.Count == 0)
        {
            var empty = Ui.Text("Brak snapshotów (Ctrl+S zapisuje).", 12.5, "Studio.Text3");
            empty.Margin = new Thickness(10, 8);
            _snapshots.Children.Add(empty);
            return;
        }
        foreach (var snapshot in _brain.Snapshots.Reverse().Take(12))
        {
            var current = _brain.Matches(snapshot);
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 10 };
            row.Children.Add(Ui.Dot(current ? StudioTheme.Palette.Accent : StudioPalette.WithAlpha(StudioTheme.Palette.Text3, 0.5), 8));
            var label = Ui.Text(snapshot.Label, 12.5);
            Grid.SetColumn(label, 1);
            row.Children.Add(label);
            var time = Ui.MonoText(snapshot.CreatedUtc.ToLocalTime().ToString("HH:mm:ss"), 11, "Studio.Text3");
            Grid.SetColumn(time, 2);
            row.Children.Add(time);
            var button = new Button
            {
                Content = row,
                Padding = new Thickness(10, 7),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Background = current ? Ui.Brush(StudioTheme.Palette.AccentSoft) : Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Focusable = false
            };
            ToolTip.SetTip(button, current ? "Mózg ma teraz dokładnie ten stan" : "Przywróć ten snapshot (zatrzymuje naukę)");
            var target = snapshot;
            button.Click += (_, _) =>
            {
                Session.Restore(_brain, target);
                _snapshotCount = -1;
            };
            _snapshots.Children.Add(button);
        }
    }

    // ---------- nawigacja i odświeżanie ----------

    private void EnterBrain(BrainModule? aFocus, Control aFrom)
    {
        if (Navigator?.Active != this)
            return;
        Enter(new GraphPanel(Session, _creature, _brain.Graph, null, null, "Mózg", aFocus), aFrom);
    }

    public override void Refresh(float aDelta)
    {
        foreach (var update in _updaters)
            update();
        _body?.InvalidateVisual();
        _preview?.Tick(aDelta);
        if (_brainInfo is not null)
        {
            var modules = _brain.Graph.Descendants().Count(aModule => aModule is not SubgraphInputModule and not SubgraphOutputModule);
            var subgraphs = _brain.Graph.Descendants().OfType<CompositeModule>().Count();
            _brainInfo.Text = $"{modules} modułów · {_brain.Graph.Connections.Count} połączeń" + (subgraphs > 0 ? $" · {subgraphs} podgrafy" : string.Empty);
        }

        // Snapshoty: przebudowa przy nowym snapshocie albo co sekundę (czy stan mózgu wciąż któremuś odpowiada).
        _snapshotCheck += aDelta;
        if (_snapshotCount != _brain.Snapshots.Count || _snapshotCheck > 1)
        {
            _snapshotCount = _brain.Snapshots.Count;
            _snapshotCheck = 0;
            RebuildSnapshots();
        }
    }

    public override void OnShown() => _snapshotCount = -1;

    public override bool HandleKey(KeyEventArgs aEvent)
    {
        switch (aEvent.Key)
        {
            case Key.S when (aEvent.KeyModifiers & KeyModifiers.Control) != 0:
                Session.SaveSnapshot(_brain);
                return true;
            case Key.Z:
                Session.StepBack(_brain);
                return true;
            case Key.Space:
                Session.TogglePause();
                return true;
            default:
                return false;
        }
    }
}
