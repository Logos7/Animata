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
using Animata.Core.WorldObjects;
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
    private StackPanel _brainContents = new();
    private TextBlock? _brainInfo;
    private string _brainShape = string.Empty;
    private float _brainCheck;
    private int _snapshotCount = -1;
    private float _snapshotCheck;
    private int _builtWhiskers;
    private int _builtSegments;

    public CreaturePanel(StudioSession aSession, ActiveEntity aCreature) : base(aSession, StudioSession.NameOf(aCreature))
    {
        _creature = aCreature;
        _brain = aCreature.Brain!;
    }

    protected override Control Build()
    {
        _updaters.Clear();
        _builtWhiskers = WorldObjectCatalog.WhiskerCountOf(_creature);
        _builtSegments = (_creature as SnakeCreature)?.Segments ?? 0;
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

        _brainShape = string.Empty;
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
        _brainContents = new StackPanel { Spacing = 2, Margin = new Thickness(10, 0, 10, 10) };
        brainDock.Children.Add(Ui.Scroll(_brainContents));
        var brainCard = Clickable(Ui.Card(brainDock, 0), aCard => EnterBrain(null, aCard));
        Grid.SetRow(brainCard, 1);
        center.Children.Add(brainCard);
        Grid.SetColumn(center, 1);
        body.Children.Add(center);

        // ---------- aktuatory, nauka, snapshoty ----------
        var effects = Ui.VStack(14, Ui.Header("Aktuatory"));
        if (_creature is SnakeCreature snake)
            effects.Children.Add(Ui.Card(Ui.VStack(10, CardHead(Icons.Snake, "Długość", "SnakeCreature"),
                Ui.Row("Segmenty", PanelParts.SegmentPicker(Session, snake), 34),
                new TextBlock
                {
                    Text = "Ciało przebudowuje się w miejscu; CPG zachowuje wyuczony chód (jego parametry nie zależą od długości).",
                    FontSize = 12,
                    TextWrapping = TextWrapping.Wrap
                }.Res(TextBlock.ForegroundProperty, "Studio.Text3"))));
        foreach (var actuator in _creature.Body.Actuators)
            effects.Children.Add(ActuatorCard(actuator));
        if (PanelParts.HasNetwork(_creature))
            effects.Children.Add(PanelParts.TrainingCard(Session, _creature, _updaters));
        var snapshotsHead = Ui.Header("Snapshoty mózgu");
        snapshotsHead.Margin = new Thickness(0, 6, 0, 0);
        effects.Children.Add(snapshotsHead);
        // Lista snapshotów przeżywa przebudowę panelu — najpierw odpina się od starej karty.
        if (_snapshots.Parent is Border oldCard)
            oldCard.Child = null;
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
        var module = _brain.Graph.Descendants().OfType<SensorModule>().FirstOrDefault(aModule => aModule.Slot == aSensor.Slot);
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
                        : "cel: brak (w scenie: PPM → Kula)";
                });
                break;
            }
            case RaySensor rays:
            {
                content.Children.Add(CardHead(Icons.Whiskers, module is not null ? GraphCanvas.TitleOf(module) : "Wąsy",
                    $"RaySensor ×{rays.Angles.Count} · {rays.Range:0.#} m"));
                // Karta przebuduje się sama (Refresh widzi inną liczbę wąsów), więc lista nie potrzebuje własnej reakcji.
                content.Children.Add(Ui.Row("Liczba wąsów", PanelParts.WhiskerPicker(Session, _creature), 34));
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
        var module = _brain.Graph.Descendants().OfType<ActuatorModule>().FirstOrDefault(aModule => aModule.Slot == aActuator.Slot);
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
        SnakeCreature snake => $"SnakeCreature · {snake.Segments} segm. · {snake.JointCount} stawów · fizyka Bepu",
        SpiderCreature spider => $"SpiderCreature · 4 nogi · {spider.JointCount} stawów · fizyka Bepu",
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
        // Liczba wąsów zmieniona (tu, w scenie albo w grafie) — karty, schemat i podgląd grafu od nowa.
        if (WorldObjectCatalog.WhiskerCountOf(_creature) != _builtWhiskers ||
            ((_creature as SnakeCreature)?.Segments ?? 0) != _builtSegments)
            Child = Build();
        foreach (var update in _updaters)
            update();
        _body?.InvalidateVisual();
        _brainCheck += aDelta;
        if (_brainCheck > 1 || BrainShape() != _brainShape)
        {
            _brainCheck = 0;
            RebuildBrainContents();
        }
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

    public override void OnShown()
    {
        _snapshotCount = -1;
        _brainShape = string.Empty;
    }

    // ---------- co jest w mózgu ----------

    /// <summary>Struktura mózgu (moduły, połączenia, kształty sieci) — zmiana oznacza przebudowę listy.</summary>
    private string BrainShape() => string.Join("|", _brain.Graph.Modules.Select(aModule =>
        $"{aModule.Id:N}{aModule.Name}{(aModule as NeuralNetworkModule)?.Network.Layers.Sum()}")) + "#" + _brain.Graph.Connections.Count;

    /// <summary>
    /// Zawartość mózgu jako lista modułów najwyższego poziomu: czym jest każdy moduł (sieć — warstwy i parametry, CPG,
    /// chód, sterownik, podgraf), skąd bierze dane i dokąd je wysyła. Klik w wiersz wjeżdża do grafu z tym modułem.
    /// </summary>
    private void RebuildBrainContents()
    {
        _brainShape = BrainShape();
        _brainContents.Children.Clear();
        var graph = _brain.Graph;
        var trainable = Core.Training.TrainingController.FindTrainable(_creature);
        var progress = Session.Training.ProgressOf(_brain);
        var ordered = graph.Modules.OrderBy(aModule => aModule switch { SensorModule => 0, ActuatorModule => 2, _ => 1 });
        foreach (var module in ordered)
        {
            var sources = graph.Connections.Where(aLink => aLink.TargetId == module.Id).Select(aLink => aLink.SourceId).Distinct()
                .Select(graph.Find).OfType<BrainModule>().Select(GraphCanvas.TitleOf).ToList();
            var targets = graph.Connections.Where(aLink => aLink.SourceId == module.Id).Select(aLink => aLink.TargetId).Distinct()
                .Select(graph.Find).OfType<BrainModule>().Select(GraphCanvas.TitleOf).ToList();
            var flow = module switch
            {
                SensorModule => targets.Count > 0 ? $"→ {string.Join(", ", targets)}" : "nic go nie czyta",
                ActuatorModule => sources.Count > 0 ? $"← {string.Join(", ", sources)}" : "nic nim nie steruje",
                _ => $"{(sources.Count > 0 ? string.Join(", ", sources) : "—")} → {(targets.Count > 0 ? string.Join(", ", targets) : "—")}"
            };
            var detail = Describe(module);
            if (module == trainable)
                detail += progress is { } last
                    ? $" · uczy się: pokolenie {last.Generation}, mistrz z {last.ChampionGeneration} ({last.ChampionScore:F2})"
                    : Session.IsTraining(_creature) ? " · uczy się" : " · uczony moduł (L — nauka)";

            var icon = Ui.Icon(GraphCanvas.IconOf(module), 16);
            icon.VerticalAlignment = VerticalAlignment.Top;
            icon.Margin = new Thickness(0, 2, 0, 0);
            var title = Ui.Text(GraphCanvas.TitleOf(module), 13, "Studio.Text", FontWeight.SemiBold);
            var kind = Ui.MonoText(module.GetType().Name.Replace("Module", string.Empty), 11, "Studio.Text3");
            var text = Ui.VStack(1, Ui.HStack(8, title, kind), Wrapped(detail, 12, "Studio.Text2"), Wrapped(flow, 11.5, "Studio.Text3"));
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 10 };
            row.Children.Add(icon);
            Grid.SetColumn(text, 1);
            row.Children.Add(text);
            var item = new Border
            {
                Child = row,
                Padding = new Thickness(8, 6),
                CornerRadius = new CornerRadius(6),
                Background = Brushes.Transparent,
                Cursor = new Cursor(StandardCursorType.Hand)
            };
            ToolTip.SetTip(item, "Wejdź do mózgu z tym modułem zaznaczonym");
            var focus = module;
            item.PointerEntered += (_, _) => item.Res(Border.BackgroundProperty, "Studio.Card2");
            item.PointerExited += (_, _) => item.Background = Brushes.Transparent;
            item.Tapped += (_, aEvent) =>
            {
                EnterBrain(focus, item);
                aEvent.Handled = true;
            };
            _brainContents.Children.Add(item);
        }
        if (graph.Modules.Count == 0)
            _brainContents.Children.Add(Ui.Text("Mózg jest pusty — wejdź i dodaj moduły z palety.", 12.5, "Studio.Text3"));
    }

    private static TextBlock Wrapped(string aText, double aSize, string aBrush)
    {
        var text = Ui.Text(aText, aSize, aBrush);
        text.TextWrapping = TextWrapping.Wrap;
        text.TextTrimming = TextTrimming.None;
        return text;
    }

    /// <summary>Czym jest moduł, po ludzku.</summary>
    private static string Describe(BrainModule aModule) => aModule switch
    {
        SensorModule sensor => $"zmysł „{sensor.Slot}” · {Ports(sensor.OutputPorts)}",
        ActuatorModule actuator => $"napęd „{actuator.Slot}” · {Ports(actuator.InputPorts)}",
        NeuralNetworkModule network => $"sieć neuronowa {string.Join("-", network.Network.Layers)} · {network.ParameterCount} parametrów · " +
            $"{network.Inputs.Count} wejść, {network.Outputs.Count} wyjść",
        CpgModule cpg => $"generator fali{(cpg.Grip ? " (toczenie, chwyt)" : string.Empty)} · {cpg.Joints} stawów · " +
            $"A {cpg.Amplitude:0.##}, f {cpg.Frequency:0.##} Hz, λ {cpg.PhaseLag:0.##}, skręt {cpg.TurnGain:0.##}",
        GaitModule gait => $"generator kłusa · krok {gait.Stride:0.##}, uniesienie {gait.Lift:0.##}, f {gait.Frequency:0.##} Hz, skręt {gait.TurnGain:0.##}",
        AvoidAndSeekModule avoid => $"sterownik: jazda do celu z omijaniem przeszkód · {avoid.RayAngles.Count} wąsów",
        ApproachTargetModule => "sterownik: jazda prosto do celu i zatrzymanie przy nim",
        CompositeModule composite => $"podgraf · {composite.Children.Count()} modułów · wejścia {Ports(composite.InputPorts)} · wyjścia {Ports(composite.OutputPorts)}",
        RouterModule router => $"przełącznik {router.Channels} kanałów · teraz kanał {router.ActiveChannel}",
        ConstantModule constant => $"stała {constant.Port} = {constant.Value:0.###}",
        _ => GraphCanvas.SubtitleOf(aModule)
    };

    private static string Ports(IReadOnlyList<string> aPorts) => aPorts.Count == 0 ? "bez portów"
        : aPorts.Count <= 4 ? string.Join(", ", aPorts)
        : $"{string.Join(", ", aPorts.Take(3))} … (+{aPorts.Count - 3})";

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
