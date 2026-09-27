using Vector2 = System.Numerics.Vector2;
using Avalonia;
using Avalonia.Input;
using Avalonia.Media;
using Animata.Core.Brains;
using Animata.Core.Brains.Modules;
using Animata.Core.Sensors;
using Animata.Studio.Kit;
using Animata.Studio.Theme;

namespace Animata.Studio.Controls;

/// <summary>Port węzła trafiony myszą.</summary>
public readonly record struct PinRef(BrainModule Module, string Port, bool IsOutput);

/// <summary>
/// Edytor grafu mózgu rysowany w całości ręcznie: węzły z portami, druty z płynącymi wartościami, pan i zoom.
/// Mysz: LPM na węźle — zaznacz i przesuń (Ctrl/Shift dokłada), LPM z wyjścia na wejście — połącz (przeciągnięcie
/// z zajętego wejścia odpina drut i pozwala go przełożyć), LPM na pustym — zaznaczanie ramką, klik na drucie — zaznacz
/// drut, PPM/ŚPM — przesuwanie widoku, kółko — zoom, dwuklik węzła — <see cref="NodeActivated"/>.
/// Polecenia (usuń, grupuj, …) woła panel, który obsługuje klawisze.
/// W trybie tylko do odczytu to podgląd: bez interakcji, zawsze dopasowany do rozmiaru.
/// </summary>
public sealed class GraphCanvas : ThemedControl
{
    public const double NodeWidth = 170;
    private const double HeaderHeight = 32;
    private const double RowHeight = 22;
    private const double FootHeight = 8;
    private const double PinRadius = 5;
    private const double GridStep = 22;

    private static readonly Dictionary<string, Geometry> IconCache = [];

    private readonly HashSet<BrainModule> _selection = [];
    private readonly Dictionary<Guid, Vector2> _dragStart = [];
    private double _zoom = 1;
    private Vector _pan = new(40, 40);
    private double _time;
    private bool _fitPending = true;
    private Size _fittedSize;

    private enum Mode { None, Drag, Connect, Rubber, Pan }
    private Mode _mode;
    private Point _lastScreen;
    private Point _pressGraph;
    private Point _cursorGraph;
    private bool _moved;
    private PinRef? _connectFrom;
    private PinRef? _hoverPin;

    public GraphCanvas(BrainGraph aGraph, bool aReadOnly = false)
    {
        Graph = aGraph;
        ReadOnly = aReadOnly;
        Focusable = !aReadOnly;
        ClipToBounds = true;
    }

    public BrainGraph Graph { get; }
    public bool ReadOnly { get; }

    public IReadOnlyCollection<BrainModule> Selection => _selection;

    /// <summary>Pierwszy (albo jedyny) zaznaczony moduł — ten pokazuje inspektor.</summary>
    public BrainModule? Primary { get; private set; }

    public BrainConnection? SelectedLink { get; private set; }

    /// <summary>Moduł, który rzucił błąd w symulacji — rysowany na czerwono.</summary>
    public Guid? ErrorModuleId { get; set; }

    public event Action? SelectionChanged;

    /// <summary>Dwuklik węzła: moduł i środek węzła w układzie tej kontrolki.</summary>
    public event Action<BrainModule, Point>? NodeActivated;

    /// <summary>Graf zmienił strukturę (połączenia, moduły).</summary>
    public event Action? Changed;

    /// <summary>Moduły zaraz zostaną usunięte (np. żeby zatrzymać ich naukę).</summary>
    public event Action<IReadOnlyCollection<BrainModule>>? Removing;

    public event Action<string>? Message;

    /// <summary>Animacja płynięcia sygnału; woła ją panel co klatkę.</summary>
    public void Tick(float aDelta)
    {
        _time += aDelta;
        InvalidateVisual();
    }

    // ---------- geometria ----------

    public static double NodeHeight(BrainModule aModule) =>
        HeaderHeight + Math.Max(1, Math.Max(aModule.InputPorts.Count, aModule.OutputPorts.Count)) * RowHeight + FootHeight;

    private static Vector2 NodeSize(BrainModule aModule) => new((float)NodeWidth, (float)NodeHeight(aModule));

    private void EnsureLayout()
    {
        if (Graph.Modules.Count == 0)
            return;
        if (!Graph.Modules.Any(aModule => Graph.Positions.ContainsKey(aModule.Id)))
        {
            BrainGraphEditing.AutoLayout(Graph, NodeSize);
            return;
        }
        var placed = Graph.Modules.Where(aModule => Graph.Positions.ContainsKey(aModule.Id)).ToList();
        var x = placed.Max(aModule => Graph.Positions[aModule.Id].X) + (float)NodeWidth + 60;
        var y = placed.Min(aModule => Graph.Positions[aModule.Id].Y);
        foreach (var module in Graph.Modules.Where(aModule => !Graph.Positions.ContainsKey(aModule.Id)))
        {
            Graph.Positions[module.Id] = new Vector2(x, y);
            y += (float)NodeHeight(module) + 30;
        }
    }

    private Rect NodeRect(BrainModule aModule)
    {
        var position = Graph.Positions.GetValueOrDefault(aModule.Id);
        return new Rect(position.X, position.Y, NodeWidth, NodeHeight(aModule));
    }

    private Point PinPoint(BrainModule aModule, string aPort, bool aOutput)
    {
        var rect = NodeRect(aModule);
        var ports = aOutput ? aModule.OutputPorts : aModule.InputPorts;
        var index = Math.Max(0, IndexOf(ports, aPort));
        return new Point(aOutput ? rect.Right : rect.X, rect.Y + HeaderHeight + index * RowHeight + RowHeight / 2);
    }

    private static int IndexOf(IReadOnlyList<string> aPorts, string aPort)
    {
        for (var index = 0; index < aPorts.Count; index++)
            if (aPorts[index] == aPort)
                return index;
        return -1;
    }

    private Point ToGraph(Point aScreen) => new((aScreen.X - _pan.X) / _zoom, (aScreen.Y - _pan.Y) / _zoom);
    private Point ToScreen(Point aGraph) => new(aGraph.X * _zoom + _pan.X, aGraph.Y * _zoom + _pan.Y);

    /// <summary>Środek węzła w układzie tej kontrolki (np. punkt startu przejścia do podgrafu).</summary>
    public Point ScreenCenterOf(BrainModule aModule) => ToScreen(NodeRect(aModule).Center);

    private static (Point, Point, Point, Point) Curve(Point aFrom, Point aTo)
    {
        var dx = Math.Max(40, Math.Abs(aTo.X - aFrom.X) * 0.5);
        return (aFrom, new Point(aFrom.X + dx, aFrom.Y), new Point(aTo.X - dx, aTo.Y), aTo);
    }

    private static Geometry CurveGeometry(Point aFrom, Point aTo)
    {
        var (p0, p1, p2, p3) = Curve(aFrom, aTo);
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(p0, false);
            context.CubicBezierTo(p1, p2, p3);
            context.EndFigure(false);
        }
        return geometry;
    }

    private static Point Bezier((Point, Point, Point, Point) aCurve, double aT)
    {
        var (p0, p1, p2, p3) = aCurve;
        var u = 1 - aT;
        return new Point(
            u * u * u * p0.X + 3 * u * u * aT * p1.X + 3 * u * aT * aT * p2.X + aT * aT * aT * p3.X,
            u * u * u * p0.Y + 3 * u * u * aT * p1.Y + 3 * u * aT * aT * p2.Y + aT * aT * aT * p3.Y);
    }

    // ---------- wygląd modułów ----------

    public static string TitleOf(BrainModule aModule) =>
        !string.IsNullOrWhiteSpace(aModule.Name) ? aModule.Name : aModule.GetType().Name.Replace("Module", string.Empty);

    public static string SubtitleOf(BrainModule aModule) => aModule switch
    {
        SensorModule => "Sensor",
        ActuatorModule => "Aktuator",
        NeuralNetworkModule network => string.Join("-", network.Network.Layers),
        CompositeModule => "podgraf",
        SubgraphInputModule or SubgraphOutputModule => "granica",
        RouterModule router => $"kanał {router.ActiveChannel}",
        ConstantModule constant => Ui.F(constant.Value),
        AvoidAndSeekModule or ApproachTargetModule => "sterownik",
        CpgModule cpg => $"CPG · {cpg.Joints} stawów",
        _ => string.Empty
    };

    public static string IconOf(BrainModule aModule) => aModule switch
    {
        SensorModule sensor when sensor.OutputPorts.Contains(TargetSensor.FoundPort) => Icons.Eye,
        SensorModule => Icons.Whiskers,
        ActuatorModule => Icons.Wheel,
        NeuralNetworkModule => Icons.Neural,
        CompositeModule => Icons.Composite,
        SubgraphInputModule => Icons.BoundaryIn,
        SubgraphOutputModule => Icons.BoundaryOut,
        RouterModule => Icons.Router,
        ConstantModule => Icons.Constant,
        CpgModule => Icons.Snake,
        _ => Icons.Brain
    };

    public static Color ColorOf(BrainModule aModule) => aModule switch
    {
        SensorModule or CompositeModule or SubgraphInputModule or SubgraphOutputModule => StudioTheme.Palette.Accent,
        ActuatorModule => StudioTheme.Palette.WireCommand,
        NeuralNetworkModule or CpgModule => StudioPalette.Neural,
        AvoidAndSeekModule or ApproachTargetModule => StudioPalette.Controller,
        _ => StudioTheme.Palette.Text3
    };

    private static bool IsData(BrainModule aSource) => aSource is SensorModule or SubgraphInputModule;
    private static bool IsBoundary(BrainModule aModule) => aModule is SubgraphInputModule or SubgraphOutputModule;

    private float? OutputValue(BrainModule aModule, string aPort) =>
        Graph.LastOutputs(aModule) is { } outputs && outputs.TryGetValue(aPort, out var value) ? value : null;

    private float? InputValue(BrainModule aModule, string aPort)
    {
        foreach (var link in Graph.Connections)
            if (link.TargetId == aModule.Id && link.TargetPort == aPort)
                return Graph.Find(link.SourceId) is { } source ? OutputValue(source, link.SourcePort) : null;
        return null;
    }

    // ---------- rysowanie ----------

    public override void Render(DrawingContext aContext)
    {
        EnsureLayout();
        if (ReadOnly && _fittedSize != Bounds.Size || _fitPending && Bounds.Width > 0)
        {
            ComputeFit();
            _fitPending = false;
            _fittedSize = Bounds.Size;
        }

        var bounds = new Rect(Bounds.Size);
        aContext.DrawRectangle(Ui.Brush(P.Canvas), null, bounds);
        if (!ReadOnly && _zoom >= 0.45)
        {
            var step = GridStep * _zoom;
            var dot = Ui.Brush(P.Dot);
            for (var x = _pan.X % step; x < bounds.Width; x += step)
                for (var y = _pan.Y % step; y < bounds.Height; y += step)
                    aContext.DrawRectangle(dot, null, new Rect(x - 0.75, y - 0.75, 1.5, 1.5));
        }

        if (Graph.Modules.Count == 0)
        {
            Draw.Text(aContext, "Pusty graf — przeciągnij moduł z palety", bounds.Center, 14, P.Text3, TextAnchor.Center);
            return;
        }

        using (aContext.PushTransform(Matrix.CreateScale(_zoom, _zoom) * Matrix.CreateTranslation(_pan.X, _pan.Y)))
        {
            DrawLinks(aContext);
            if (_mode == Mode.Connect && _connectFrom is { } from)
            {
                var start = PinPoint(from.Module, from.Port, from.IsOutput);
                var geometry = from.IsOutput ? CurveGeometry(start, _cursorGraph) : CurveGeometry(_cursorGraph, start);
                aContext.DrawGeometry(null, Draw.Pen(P.Accent, 1.8, new DashStyle([5, 5], 0)), geometry);
            }
            foreach (var module in Graph.Modules)
                DrawNode(aContext, module);
            if (_mode == Mode.Rubber)
            {
                var rect = new Rect(_pressGraph, _cursorGraph).Normalize();
                aContext.DrawRectangle(Ui.Brush(StudioPalette.WithAlpha(P.Accent, 0.08)), Draw.Pen(P.Accent, 1 / _zoom), rect);
            }
            if (_hoverPin is { } pin && _mode != Mode.Drag)
                DrawPinTip(aContext, pin);
        }
    }

    private void DrawLinks(DrawingContext aContext)
    {
        var flowOffset = -_time * 24;
        foreach (var link in Graph.Connections)
        {
            if (Graph.Find(link.SourceId) is not { } source || Graph.Find(link.TargetId) is not { } target)
                continue;
            var from = PinPoint(source, link.SourcePort, true);
            var to = PinPoint(target, link.TargetPort, false);
            var geometry = CurveGeometry(from, to);
            var selected = link == SelectedLink;
            var color = IsData(source) ? P.Accent : P.WireCommand;
            var alpha = IsData(source) ? 0.55 : 0.85;
            aContext.DrawGeometry(null, Draw.Pen(StudioPalette.WithAlpha(color, selected ? 1 : alpha), selected ? 3.2 : 1.6), geometry);
            if (target is ActuatorModule or SubgraphOutputModule)
                aContext.DrawGeometry(null, Draw.Pen(color, 2.6, new DashStyle([0.5, 4.5], flowOffset / 2.6)), geometry);
        }
    }

    private void DrawNode(DrawingContext aContext, BrainModule aModule)
    {
        var rect = NodeRect(aModule);
        var selected = _selection.Contains(aModule);
        var failed = ErrorModuleId == aModule.Id || aModule is CompositeModule composite && ErrorModuleId is { } error && composite.Inner.FindDeep(error) is not null;
        var color = ColorOf(aModule);
        var card = Ui.Brush(P.Card);

        // Cień i „stos kart” podgrafu.
        aContext.DrawRectangle(Ui.Brush(P.Shadow), null, rect.Translate(new Vector(0, 4)).Inflate(2), 10, 10);
        if (aModule is CompositeModule)
            foreach (var offset in new[] { 12.0, 6.0 })
                aContext.DrawRectangle(card, Draw.Pen(selected ? P.Accent : P.Stroke2), rect.Translate(new Vector(offset, offset)), 8, 8);

        IBrush fill = IsBoundary(aModule) ? Ui.Brush(StudioPalette.Mix(P.Card, P.Accent, 0.08)) : card;
        IPen border = failed ? Draw.Pen(StudioPalette.Bad, 2)
            : selected ? Draw.Pen(P.Accent, 2)
            : IsBoundary(aModule) ? Draw.Pen(P.Accent, 1.2, new DashStyle([4, 3], 0))
            : Draw.Pen(P.Stroke2, 1);
        if (selected)
            aContext.DrawRectangle(Ui.Brush(StudioPalette.WithAlpha(P.Accent, 0.18)), null, rect.Inflate(5), 12, 12);
        aContext.DrawRectangle(fill, border, rect, 8, 8);

        // Nagłówek.
        aContext.DrawLine(Draw.Pen(P.Stroke), new Point(rect.X, rect.Y + HeaderHeight), new Point(rect.Right, rect.Y + HeaderHeight));
        DrawIcon(aContext, IconOf(aModule), new Point(rect.X + 10, rect.Y + 9), color);
        Draw.Text(aContext, Clip(TitleOf(aModule), 17), new Point(rect.X + 30, rect.Y + HeaderHeight / 2), 12, P.Text, aBold: true);
        Draw.Text(aContext, Clip(SubtitleOf(aModule), 10), new Point(rect.Right - 10, rect.Y + HeaderHeight / 2), 10, P.Text3, TextAnchor.Right);

        // Porty.
        var inputs = aModule.InputPorts;
        var outputs = aModule.OutputPorts;
        var oneSided = inputs.Count == 0 || outputs.Count == 0;
        for (var row = 0; row < Math.Max(inputs.Count, outputs.Count); row++)
        {
            var y = rect.Y + HeaderHeight + row * RowHeight + RowHeight / 2;
            if (row < inputs.Count)
            {
                var port = inputs[row];
                var connected = Graph.Connections.Any(aLink => aLink.TargetId == aModule.Id && aLink.TargetPort == port);
                DrawPin(aContext, new Point(rect.X, y), connected, aModule is ActuatorModule or SubgraphOutputModule ? P.WireCommand : P.Accent,
                    new PinRef(aModule, port, false));
                Draw.Text(aContext, Clip(port, oneSided ? 14 : 11), new Point(rect.X + 12, y), 11.5, P.Text2);
                if (oneSided && InputValue(aModule, port) is { } value)
                    Draw.Text(aContext, Ui.F(value), new Point(rect.Right - 12, y), 11, P.Text, TextAnchor.Right, aMono: true);
            }
            if (row < outputs.Count)
            {
                var port = outputs[row];
                var connected = Graph.Connections.Any(aLink => aLink.SourceId == aModule.Id && aLink.SourcePort == port);
                DrawPin(aContext, new Point(rect.Right, y), connected, IsData(aModule) ? P.Accent : P.WireCommand, new PinRef(aModule, port, true));
                if (oneSided)
                {
                    Draw.Text(aContext, Clip(port, 14), new Point(rect.X + 12, y), 11.5, P.Text2);
                    if (OutputValue(aModule, port) is { } value)
                        Draw.Text(aContext, Ui.F(value), new Point(rect.Right - 12, y), 11, P.Text, TextAnchor.Right, aMono: true);
                }
                else
                    Draw.Text(aContext, Clip(port, 11), new Point(rect.Right - 12, y), 11.5, P.Text, TextAnchor.Right);
            }
        }
    }

    private void DrawPin(DrawingContext aContext, Point aCenter, bool aConnected, Color aColor, PinRef aPin)
    {
        var hovered = _hoverPin == aPin;
        var candidate = _mode == Mode.Connect && IsCandidate(aPin);
        var radius = hovered || candidate ? PinRadius + 1.5 : PinRadius;
        if (candidate)
            aContext.DrawEllipse(Ui.Brush(StudioPalette.WithAlpha(P.Accent, 0.25)), null, aCenter, radius + 5, radius + 5);
        aContext.DrawEllipse(aConnected ? Ui.Brush(aColor) : Ui.Brush(P.Card), Draw.Pen(aColor, 2), aCenter, radius, radius);
    }

    private void DrawPinTip(DrawingContext aContext, PinRef aPin)
    {
        var value = aPin.IsOutput ? OutputValue(aPin.Module, aPin.Port) : InputValue(aPin.Module, aPin.Port);
        var text = value is { } v ? $"{aPin.Port} = {Ui.F(v, 3)}" : $"{aPin.Port} · niepodłączony";
        var formatted = Draw.Format(text, 11.5, P.Text, aMono: true);
        var at = PinPoint(aPin.Module, aPin.Port, aPin.IsOutput);
        var box = new Rect(at.X + (aPin.IsOutput ? 12 : -formatted.Width - 28), at.Y - 26, formatted.Width + 16, 22);
        aContext.DrawRectangle(Ui.Brush(P.Card2), Draw.Pen(P.Stroke2), box, 6, 6);
        aContext.DrawText(formatted, new Point(box.X + 8, box.Y + (box.Height - formatted.Height) / 2));
    }

    private static void DrawIcon(DrawingContext aContext, string aData, Point aAt, Color aColor)
    {
        if (!IconCache.TryGetValue(aData, out var geometry))
            IconCache[aData] = geometry = Geometry.Parse(aData);
        using (aContext.PushTransform(Matrix.CreateScale(14 / 16.0, 14 / 16.0) * Matrix.CreateTranslation(aAt.X, aAt.Y)))
            aContext.DrawGeometry(null, Draw.Pen(aColor, 1.6), geometry);
    }

    private static string Clip(string aText, int aMax) => aText.Length <= aMax ? aText : aText[..(aMax - 1)] + "…";

    // ---------- trafianie ----------

    private PinRef? HitPin(Point aGraph)
    {
        var tolerance = Math.Max(8, 9 / _zoom);
        for (var index = Graph.Modules.Count - 1; index >= 0; index--)
        {
            var module = Graph.Modules[index];
            foreach (var port in module.OutputPorts)
                if (Distance(PinPoint(module, port, true), aGraph) <= tolerance)
                    return new PinRef(module, port, true);
            foreach (var port in module.InputPorts)
                if (Distance(PinPoint(module, port, false), aGraph) <= tolerance)
                    return new PinRef(module, port, false);
        }
        return null;
    }

    private BrainModule? HitNode(Point aGraph)
    {
        for (var index = Graph.Modules.Count - 1; index >= 0; index--)
            if (NodeRect(Graph.Modules[index]).Contains(aGraph))
                return Graph.Modules[index];
        return null;
    }

    private BrainConnection? HitLink(Point aGraph)
    {
        var tolerance = 6 / _zoom;
        foreach (var link in Graph.Connections)
        {
            if (Graph.Find(link.SourceId) is not { } source || Graph.Find(link.TargetId) is not { } target)
                continue;
            var curve = Curve(PinPoint(source, link.SourcePort, true), PinPoint(target, link.TargetPort, false));
            var previous = Bezier(curve, 0);
            for (var step = 1; step <= 24; step++)
            {
                var next = Bezier(curve, step / 24.0);
                if (SegmentDistance(aGraph, previous, next) <= tolerance)
                    return link;
                previous = next;
            }
        }
        return null;
    }

    private bool IsCandidate(PinRef aPin)
    {
        if (_connectFrom is not { } from || aPin.IsOutput == from.IsOutput)
            return false;
        return from.IsOutput
            ? BrainGraphEditing.CanConnect(Graph, from.Module, from.Port, aPin.Module, aPin.Port) is null
            : BrainGraphEditing.CanConnect(Graph, aPin.Module, aPin.Port, from.Module, from.Port) is null;
    }

    private static double Distance(Point aA, Point aB) => Math.Sqrt((aA.X - aB.X) * (aA.X - aB.X) + (aA.Y - aB.Y) * (aA.Y - aB.Y));

    private static double SegmentDistance(Point aPoint, Point aA, Point aB)
    {
        var ab = aB - aA;
        var length = ab.X * ab.X + ab.Y * ab.Y;
        var t = length < 1e-9 ? 0 : Math.Clamp(((aPoint.X - aA.X) * ab.X + (aPoint.Y - aA.Y) * ab.Y) / length, 0, 1);
        return Distance(aPoint, aA + ab * t);
    }

    // ---------- mysz ----------

    protected override void OnPointerPressed(PointerPressedEventArgs aEvent)
    {
        base.OnPointerPressed(aEvent);
        if (ReadOnly)
            return;
        Focus();
        var point = aEvent.GetCurrentPoint(this);
        var screen = aEvent.GetPosition(this);
        var graph = ToGraph(screen);
        _lastScreen = screen;
        _pressGraph = graph;
        _cursorGraph = graph;
        _moved = false;

        if (point.Properties.IsMiddleButtonPressed || point.Properties.IsRightButtonPressed)
        {
            _mode = Mode.Pan;
        }
        else if (point.Properties.IsLeftButtonPressed)
        {
            var additive = (aEvent.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Shift)) != 0;
            if (HitPin(graph) is { } pin)
            {
                if (!pin.IsOutput && Graph.Connections.Find(aLink => aLink.TargetId == pin.Module.Id && aLink.TargetPort == pin.Port) is { } existing
                    && Graph.Find(existing.SourceId) is { } source)
                {
                    // Chwycenie zajętego wejścia odpina drut i pozwala przełożyć go gdzie indziej.
                    Graph.Disconnect(existing);
                    Changed?.Invoke();
                    _connectFrom = new PinRef(source, existing.SourcePort, true);
                }
                else
                    _connectFrom = pin;
                _mode = Mode.Connect;
            }
            else if (HitNode(graph) is { } node)
            {
                if (aEvent.ClickCount >= 2)
                {
                    NodeActivated?.Invoke(node, ScreenCenterOf(node));
                    aEvent.Handled = true;
                    return;
                }
                if (additive)
                {
                    if (!_selection.Remove(node))
                        _selection.Add(node);
                }
                else if (!_selection.Contains(node))
                {
                    _selection.Clear();
                    _selection.Add(node);
                }
                Primary = _selection.Contains(node) ? node : _selection.FirstOrDefault();
                SelectedLink = null;
                SelectionChanged?.Invoke();
                _dragStart.Clear();
                foreach (var module in _selection)
                    _dragStart[module.Id] = Graph.Positions.GetValueOrDefault(module.Id);
                _mode = Mode.Drag;
            }
            else if (HitLink(graph) is { } link)
            {
                _selection.Clear();
                Primary = null;
                SelectedLink = link;
                SelectionChanged?.Invoke();
            }
            else
            {
                if (!additive)
                {
                    _selection.Clear();
                    Primary = null;
                    SelectedLink = null;
                    SelectionChanged?.Invoke();
                }
                _mode = Mode.Rubber;
            }
        }
        else
            return;

        aEvent.Pointer.Capture(this);
        aEvent.Handled = true;
        InvalidateVisual();
    }

    protected override void OnPointerMoved(PointerEventArgs aEvent)
    {
        base.OnPointerMoved(aEvent);
        if (ReadOnly)
            return;
        var screen = aEvent.GetPosition(this);
        var graph = ToGraph(screen);
        _cursorGraph = graph;
        var hover = HitPin(graph);
        if (hover != _hoverPin)
        {
            _hoverPin = hover;
            InvalidateVisual();
        }

        switch (_mode)
        {
            case Mode.Pan:
                _pan += screen - _lastScreen;
                break;
            case Mode.Drag:
                var delta = graph - _pressGraph;
                if (Math.Abs(delta.X) + Math.Abs(delta.Y) > 2 / _zoom)
                    _moved = true;
                if (_moved)
                    foreach (var (id, start) in _dragStart)
                        Graph.Positions[id] = new Vector2(
                            MathF.Round(start.X + (float)delta.X),
                            MathF.Round(start.Y + (float)delta.Y));
                break;
            case Mode.None:
                return;
        }
        _lastScreen = screen;
        InvalidateVisual();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs aEvent)
    {
        base.OnPointerReleased(aEvent);
        if (ReadOnly || _mode == Mode.None)
            return;
        var graph = ToGraph(aEvent.GetPosition(this));

        switch (_mode)
        {
            case Mode.Connect when _connectFrom is { } from:
                if (HitPin(graph) is { } pin && pin.IsOutput != from.IsOutput)
                {
                    var error = from.IsOutput
                        ? BrainGraphEditing.TryConnect(Graph, from.Module, from.Port, pin.Module, pin.Port)
                        : BrainGraphEditing.TryConnect(Graph, pin.Module, pin.Port, from.Module, from.Port);
                    if (error is not null)
                        Message?.Invoke(error);
                    else
                        Changed?.Invoke();
                }
                break;
            case Mode.Rubber:
                var rect = new Rect(_pressGraph, graph).Normalize();
                if (rect.Width > 3 || rect.Height > 3)
                {
                    foreach (var module in Graph.Modules)
                        if (rect.Intersects(NodeRect(module)))
                            _selection.Add(module);
                    Primary = _selection.FirstOrDefault();
                    SelectionChanged?.Invoke();
                }
                break;
        }

        _mode = Mode.None;
        _connectFrom = null;
        aEvent.Pointer.Capture(null);
        aEvent.Handled = true;
        InvalidateVisual();
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs aEvent)
    {
        base.OnPointerWheelChanged(aEvent);
        if (ReadOnly)
            return;
        var screen = aEvent.GetPosition(this);
        var anchor = ToGraph(screen);
        _zoom = Math.Clamp(_zoom * Math.Pow(1.12, aEvent.Delta.Y), 0.3, 2.5);
        _pan = new Vector(screen.X - anchor.X * _zoom, screen.Y - anchor.Y * _zoom);
        aEvent.Handled = true;
        InvalidateVisual();
    }

    // ---------- polecenia ----------

    public void Select(BrainModule? aModule)
    {
        _selection.Clear();
        if (aModule is not null)
            _selection.Add(aModule);
        Primary = aModule;
        SelectedLink = null;
        SelectionChanged?.Invoke();
        InvalidateVisual();
    }

    public void SelectAll()
    {
        _selection.Clear();
        foreach (var module in Graph.Modules)
            _selection.Add(module);
        Primary = _selection.FirstOrDefault();
        SelectedLink = null;
        SelectionChanged?.Invoke();
        InvalidateVisual();
    }

    /// <summary>Esc: przerywa przeciąganie albo czyści zaznaczenie. False, gdy nie było czego anulować.</summary>
    public bool Cancel()
    {
        if (_mode != Mode.None)
        {
            _mode = Mode.None;
            _connectFrom = null;
            InvalidateVisual();
            return true;
        }
        if (_selection.Count == 0 && SelectedLink is null)
            return false;
        Select(null);
        return true;
    }

    /// <summary>Dodaje moduł w punkcie ekranu (albo na środku widoku) i go zaznacza.</summary>
    public void AddModule(BrainModule aModule, Point? aScreen = null)
    {
        var at = ToGraph(aScreen ?? new Rect(Bounds.Size).Center);
        Graph.Add(aModule);
        Graph.Positions[aModule.Id] = new Vector2((float)(at.X - NodeWidth / 2), (float)(at.Y - NodeHeight(aModule) / 2));
        Select(aModule);
        Changed?.Invoke();
    }

    public void DeleteSelection()
    {
        if (SelectedLink is { } link)
        {
            Graph.Disconnect(link);
            SelectedLink = null;
            SelectionChanged?.Invoke();
            Changed?.Invoke();
            InvalidateVisual();
            return;
        }
        var doomed = _selection.Where(aModule => !IsBoundary(aModule)).ToList();
        if (doomed.Count == 0)
        {
            if (_selection.Count > 0)
                Message?.Invoke("Granic podgrafu nie da się usunąć — zmieniaj jego porty w inspektorze.");
            return;
        }
        Removing?.Invoke(doomed);
        foreach (var module in doomed)
            Graph.Remove(module);
        Select(null);
        Changed?.Invoke();
    }

    public CompositeModule? GroupSelection()
    {
        var modules = _selection.Where(aModule => !IsBoundary(aModule)).ToList();
        if (modules.Count == 0)
        {
            Message?.Invoke("Zaznacz moduły do zgrupowania (Ctrl+klik albo ramka).");
            return null;
        }
        try
        {
            var composite = BrainGraphEditing.Group(Graph, modules, "Podgraf");
            Select(composite);
            Changed?.Invoke();
            return composite;
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException)
        {
            Message?.Invoke(exception.Message);
            return null;
        }
    }

    public void UngroupSelection()
    {
        var composites = _selection.OfType<CompositeModule>().ToList();
        if (composites.Count == 0)
        {
            Message?.Invoke("Zaznacz podgraf do rozgrupowania.");
            return;
        }
        var moved = new List<BrainModule>();
        foreach (var composite in composites)
        {
            try
            {
                moved.AddRange(BrainGraphEditing.Ungroup(Graph, composite));
            }
            catch (BrainException exception)
            {
                Message?.Invoke(exception.Message);
            }
        }
        _selection.Clear();
        foreach (var module in moved)
            _selection.Add(module);
        Primary = moved.FirstOrDefault();
        SelectionChanged?.Invoke();
        Changed?.Invoke();
        InvalidateVisual();
    }

    public void AutoLayout()
    {
        BrainGraphEditing.AutoLayout(Graph, NodeSize);
        FitToView();
        InvalidateVisual();
    }

    public void FitToView()
    {
        ComputeFit();
        InvalidateVisual();
    }

    /// <summary>
    /// Liczy zoom i przesunięcie tak, żeby cały graf był widoczny. Nie prosi o przerysowanie, więc wolno ją wołać
    /// z <see cref="Render"/> (Avalonia rzuca, gdy kontrolkę unieważnia się w trakcie renderowania).
    /// </summary>
    private void ComputeFit()
    {
        EnsureLayout();
        if (Graph.Modules.Count == 0 || Bounds.Width <= 0 || Bounds.Height <= 0)
            return;
        var rects = Graph.Modules.Select(NodeRect).ToList();
        var union = rects.Aggregate((aA, aB) => aA.Union(aB)).Inflate(ReadOnly ? 16 : 40);
        _zoom = Math.Clamp(Math.Min(Bounds.Width / union.Width, Bounds.Height / union.Height), 0.25, ReadOnly ? 1 : 1.2);
        _pan = new Vector(
            Bounds.Width / 2 - union.Center.X * _zoom,
            Bounds.Height / 2 - union.Center.Y * _zoom);
    }

    /// <summary>Czy punkt ekranu (tej kontrolki) leży nad widokiem grafu — do upuszczania z palety.</summary>
    public bool ContainsScreen(Point aScreen) => new Rect(Bounds.Size).Contains(aScreen);
}
