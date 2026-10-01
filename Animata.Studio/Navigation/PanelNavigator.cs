using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Animata.Studio.Kit;
using Animata.Studio.Session;

namespace Animata.Studio.Navigation;

/// <summary>
/// Jeden ekran Studia (menu, scena, stwór, mózg, podgraf). Zawartość buduje się leniwie przy pierwszym pokazaniu.
/// Panel żyje na stosie <see cref="PanelNavigator"/>; swoje narzędzia oddaje paskowi okna (<see cref="SetBar"/>).
/// </summary>
public abstract class StudioPanel : Border
{
    protected StudioPanel(StudioSession aSession, string aTitle)
    {
        Session = aSession;
        Title = aTitle;
        ClipToBounds = true;
        this.Res(BackgroundProperty, "Studio.Bg");
    }

    public StudioSession Session { get; }

    /// <summary>
    /// Panel zostaje w drzewie wizualnym (ukryty) po zdjęciu ze stosu — np. scena, żeby widok 3D nie tworzył się od nowa.
    /// </summary>
    public virtual bool KeepAlive => false;

    /// <summary>Nazwa w breadcrumbie.</summary>
    public string Title
    {
        get;
        protected set
        {
            field = value;
            BarChanged?.Invoke();
        }
    }

    /// <summary>Narzędzia panelu w pasku okna: obok tytułu, na środku i z prawej (null = brak).</summary>
    public Control? BarTitleExtra { get; private set; }
    public Control? BarCenter { get; private set; }
    public Control? BarRight { get; private set; }

    /// <summary>Zmienił się tytuł albo narzędzia panelu (np. po przebudowie).</summary>
    public event Action? BarChanged;

    /// <summary>Ustawia narzędzia panelu w pasku okna (woła to <see cref="PanelFrame.Create"/> w <see cref="Build"/>).</summary>
    public void SetBar(Control? aTitleExtra, Control? aCenter, Control? aRight)
    {
        BarTitleExtra = aTitleExtra;
        BarCenter = aCenter;
        BarRight = aRight;
        BarChanged?.Invoke();
    }

    public PanelNavigator? Navigator { get; private set; }

    /// <summary>Pozycja na stosie (0 = menu).</summary>
    public int Depth { get; private set; }

    internal void Attach(PanelNavigator aNavigator, int aDepth)
    {
        Navigator = aNavigator;
        Depth = aDepth;
        Child ??= Build();
    }

    protected abstract Control Build();

    /// <summary>Odświeżenie na żywo (~30 Hz), tylko dla aktywnego panelu.</summary>
    public virtual void Refresh(float aDelta)
    {
    }

    /// <summary>
    /// Skróty panelu. True = obsłużone (nawigator nie potraktuje np. Esc jako „wstecz”). Domyślnie Spacja — pauza sceny
    /// (panele sceny wołają to na końcu swoich skrótów).
    /// </summary>
    public virtual bool HandleKey(KeyEventArgs aEvent)
    {
        if (aEvent.Key != Key.Space)
            return false;
        Session.TogglePause();
        return true;
    }

    public virtual void OnShown()
    {
    }

    public virtual void OnHidden()
    {
    }

    /// <summary>Środek kontrolki w układzie nawigatora — stąd wyrasta następny panel.</summary>
    protected Point? OriginOf(Control? aControl)
    {
        if (aControl is null || Navigator is null)
            return null;
        return aControl.TranslatePoint(new Point(aControl.Bounds.Width / 2, aControl.Bounds.Height / 2), Navigator);
    }

    protected Point? OriginOf(Control aControl, Point aLocal) =>
        Navigator is null ? null : aControl.TranslatePoint(aLocal, Navigator);

    /// <summary>Wejście wgłąb: następny panel wyrasta z <paramref name="aFrom"/> (albo ze środka).</summary>
    protected void Enter(StudioPanel aNext, Control? aFrom = null) => Navigator?.Push(aNext, OriginOf(aFrom));

    protected void Enter(StudioPanel aNext, Point? aOrigin) => Navigator?.Push(aNext, aOrigin);
}

/// <summary>
/// Stos paneli z przejściami „wgłąb”: nowy panel rośnie z punktu, w który kliknięto, a stary powiększa się
/// i rozmywa, jakby kamera w niego wjeżdżała. Powrót działa odwrotnie i trafia w ten sam punkt.
/// Panele pod spodem są ukryte, nie niszczone — scena 3D i stan paneli przeżywają wejście wgłąb.
/// </summary>
public sealed class PanelNavigator : Panel
{
    private const double InScale = 0.1;
    private const double OutScale = 3.6;

    private readonly List<StudioPanel> _stack = [];
    private readonly List<Point> _origins = [];
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(15) };
    private readonly System.Diagnostics.Stopwatch _clock = new();
    private Transition? _transition;

    private sealed record Transition(StudioPanel Entering, StudioPanel? Leaving, bool Inward, Point Origin, IReadOnlyList<StudioPanel> Removed);

    public PanelNavigator()
    {
        ClipToBounds = true;
        _timer.Tick += (_, _) => Animate();
        AddHandler(PointerPressedEvent, OnPointerPressed, Avalonia.Interactivity.RoutingStrategies.Tunnel, true);
    }

    public TimeSpan Duration { get; set; } = TimeSpan.FromMilliseconds(620);

    public IReadOnlyList<StudioPanel> Stack => _stack;
    public StudioPanel? Active => _stack.Count > 0 ? _stack[^1] : null;

    /// <summary>Zmieniła się ścieżka (wejście albo powrót).</summary>
    public event Action? Navigated;

    public void Push(StudioPanel aPanel, Point? aOrigin = null)
    {
        Finish();
        var leaving = Active;
        aPanel.Attach(this, _stack.Count);
        _stack.Add(aPanel);
        var origin = aOrigin ?? Center();
        _origins.Add(origin);
        if (!Children.Contains(aPanel))
            Children.Add(aPanel);
        aPanel.IsVisible = true;
        leaving?.OnHidden();
        aPanel.OnShown();
        Begin(new Transition(aPanel, leaving, true, origin, []));
    }

    /// <summary>Wraca do panelu o indeksie <paramref name="aIndex"/> jednym przejściem, nawet przez kilka poziomów.</summary>
    public void PopTo(int aIndex)
    {
        if (aIndex < 0 || aIndex >= _stack.Count - 1)
            return;
        Finish();
        var leaving = _stack[^1];
        var origin = _origins[aIndex + 1];
        var removed = _stack.Skip(aIndex + 1).ToList();
        _stack.RemoveRange(aIndex + 1, _stack.Count - aIndex - 1);
        _origins.RemoveRange(aIndex + 1, _origins.Count - aIndex - 1);
        var entering = _stack[aIndex];
        entering.IsVisible = true;
        leaving.OnHidden();
        entering.OnShown();
        Begin(new Transition(entering, leaving, false, origin, removed));
    }

    public void Back() => PopTo(_stack.Count - 2);

    private Point Center() => new(Bounds.Width / 2, Bounds.Height / 2);

    private void Begin(Transition aTransition)
    {
        _transition = aTransition;
        var origin = new RelativePoint(
            Bounds.Width > 0 ? aTransition.Origin.X / Bounds.Width : 0.5,
            Bounds.Height > 0 ? aTransition.Origin.Y / Bounds.Height : 0.5,
            RelativeUnit.Relative);
        foreach (var panel in new[] { aTransition.Entering, aTransition.Leaving })
            if (panel is not null)
                panel.RenderTransformOrigin = origin;

        // Na wierzchu to, co rośnie do rozmiaru okna: przy wejściu nowy panel, przy powrocie znikający.
        aTransition.Entering.ZIndex = aTransition.Inward ? 2 : 1;
        if (aTransition.Leaving is not null)
        {
            aTransition.Leaving.ZIndex = aTransition.Inward ? 1 : 2;
            aTransition.Leaving.IsHitTestVisible = false;
        }

        _clock.Restart();
        Animate();
        _timer.Start();
        Navigated?.Invoke();
    }

    private void Animate()
    {
        if (_transition is not { } transition)
            return;
        var progress = Math.Clamp(_clock.Elapsed.TotalMilliseconds / Duration.TotalMilliseconds, 0, 1);
        var enter = EaseOut(progress);
        var leave = EaseInOut(progress);

        if (transition.Inward)
        {
            Apply(transition.Entering, Lerp(InScale, 1, enter), Math.Clamp(progress / 0.55, 0, 1), 10 * (1 - enter));
            if (transition.Leaving is { } leaving)
                Apply(leaving, Lerp(1, OutScale, leave), 1 - leave, 14 * leave);
        }
        else
        {
            Apply(transition.Entering, Lerp(OutScale, 1, enter), enter, 14 * (1 - enter));
            if (transition.Leaving is { } leaving)
                Apply(leaving, Lerp(1, InScale, leave), 1 - Math.Clamp(progress / 0.8, 0, 1), 10 * leave);
        }

        if (progress >= 1)
            Finish();
    }

    /// <summary>Kończy bieżące przejście od razu (np. gdy użytkownik klika dalej w trakcie animacji).</summary>
    private void Finish()
    {
        if (_transition is not { } transition)
            return;
        _transition = null;
        _timer.Stop();

        Reset(transition.Entering);
        if (transition.Leaving is { } leaving)
        {
            Reset(leaving);
            leaving.IsHitTestVisible = true;
            if (transition.Inward)
                leaving.IsVisible = false;
        }
        foreach (var panel in transition.Removed)
        {
            if (panel.KeepAlive)
                panel.IsVisible = false;
            else
                Children.Remove(panel);
        }
    }

    private static void Apply(Control aPanel, double aScale, double aOpacity, double aBlur)
    {
        aPanel.RenderTransform = new ScaleTransform(aScale, aScale);
        aPanel.Opacity = aOpacity;
        aPanel.Effect = aBlur > 0.5 ? new BlurEffect { Radius = aBlur } : null;
    }

    private static void Reset(Control aPanel)
    {
        aPanel.RenderTransform = null;
        aPanel.Opacity = 1;
        aPanel.Effect = null;
        aPanel.ZIndex = 0;
    }

    private static double Lerp(double aFrom, double aTo, double aT) => aFrom + (aTo - aFrom) * aT;
    private static double EaseOut(double aT) => 1 - Math.Pow(1 - aT, 4);
    private static double EaseInOut(double aT) => aT < 0.5 ? 4 * aT * aT * aT : 1 - Math.Pow(-2 * aT + 2, 3) / 2;

    /// <summary>Przycisk „wstecz” myszy cofa o poziom.</summary>
    private void OnPointerPressed(object? aSender, PointerPressedEventArgs aEvent)
    {
        if (aEvent.GetCurrentPoint(this).Properties.PointerUpdateKind != PointerUpdateKind.XButton1Pressed)
            return;
        Back();
        aEvent.Handled = true;
    }
}
