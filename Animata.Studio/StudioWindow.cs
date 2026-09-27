using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Animata.Core.WorldObjects;
using Animata.Studio.Navigation;
using Animata.Studio.Panels;
using Animata.Studio.Session;

namespace Animata.Studio;

/// <summary>
/// Okno Studia: jeden <see cref="PanelNavigator"/> na całą powierzchnię i kilka scen (demo, wąż), każda z własną
/// sesją i własnym panelem. Zegar (~30 Hz) przesuwa wszystkie sesje (symulację i naukę) i odświeża aktywny panel.
/// Klawisze idą najpierw do aktywnego panelu; nieobsłużone Esc i Alt+← (poza polami tekstowymi) cofają o poziom.
/// </summary>
public sealed class StudioWindow : Window
{
    private readonly List<(StudioSession Session, Func<SimulationPanel> Panel)> _scenes = [];
    private readonly Dictionary<StudioSession, SimulationPanel> _panels = [];
    private readonly PanelNavigator _navigator = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(33) };
    private readonly Stopwatch _clock = new();

    public StudioWindow()
    {
        Title = "Animata Studio";
        Width = 1440;
        Height = 900;
        MinWidth = 1100;
        MinHeight = 700;
        Content = _navigator;

        AddScene(new StudioSession("Scena demo", WorldObjectCatalog.CreateDemo));
        AddScene(new StudioSession("Węże", WorldObjectCatalog.CreateSnakeScene));

        _navigator.Navigated += UpdateTitle;
        KeyDown += OnKeyDown;
        _timer.Tick += (_, _) => Tick();

        Opened += (_, _) =>
        {
            _navigator.Push(new MenuPanel(_scenes.Select(aScene => new MenuScene(aScene.Session, aScene.Panel)).ToList()));
            // Nauka nie startuje sama: stwory z uczonym modułem (fioletowe) mają losowe parametry, uczą się po L / „Ucz”.
            _clock.Start();
            _timer.Start();
        };
        Closed += (_, _) =>
        {
            _timer.Stop();
            foreach (var (session, _) in _scenes)
                session.Dispose();
            foreach (var panel in _panels.Values)
                panel.Dispose();
        };
    }

    private void AddScene(StudioSession aSession)
    {
        _scenes.Add((aSession, () =>
        {
            if (!_panels.TryGetValue(aSession, out var panel))
                _panels[aSession] = panel = new SimulationPanel(aSession);
            return panel;
        }));
        // Nowa albo wczytana scena: panele głębiej (stwór, mózg) trzymają stare encje — wracamy do panelu sceny.
        aSession.SceneReset += () =>
        {
            var index = _panels.TryGetValue(aSession, out var panel) ? _navigator.Stack.ToList().IndexOf(panel) : -1;
            if (index >= 0)
                _navigator.PopTo(index);
        };
    }

    private void Tick()
    {
        var delta = (float)_clock.Elapsed.TotalSeconds;
        _clock.Restart();
        foreach (var (session, _) in _scenes)
            session.Tick();
        _navigator.Active?.Refresh(delta);
    }

    private void UpdateTitle() =>
        Title = "Animata Studio — " + string.Join(" › ", _navigator.Stack.Select(aPanel => aPanel.Title));

    private void OnKeyDown(object? aSender, KeyEventArgs aEvent)
    {
        if (aEvent.Handled || aEvent.Source is TextBox)
            return;
        if (_navigator.Active?.HandleKey(aEvent) == true)
        {
            aEvent.Handled = true;
            return;
        }
        var back = aEvent.Key == Key.Escape
            || aEvent.Key == Key.Left && (aEvent.KeyModifiers & KeyModifiers.Alt) != 0
            || aEvent.Key == Key.BrowserBack;
        if (back && _navigator.Stack.Count > 1)
        {
            _navigator.Back();
            aEvent.Handled = true;
        }
    }
}
