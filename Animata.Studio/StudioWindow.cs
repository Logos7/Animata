using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Animata.Studio.Navigation;
using Animata.Studio.Panels;
using Animata.Studio.Session;

namespace Animata.Studio;

/// <summary>
/// Okno Studia: jeden <see cref="PanelNavigator"/> na całą powierzchnię. Zegar (~30 Hz) przesuwa sesję
/// (symulację i naukę) i odświeża aktywny panel. Klawisze idą najpierw do aktywnego panelu; nieobsłużone Esc
/// i Alt+← (poza polami tekstowymi) cofają o poziom.
/// </summary>
public sealed class StudioWindow : Window
{
    private readonly StudioSession _session = new();
    private readonly PanelNavigator _navigator = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(33) };
    private readonly Stopwatch _clock = new();
    private SimulationPanel? _scene;

    public StudioWindow()
    {
        Title = "Animata Studio";
        Width = 1440;
        Height = 900;
        MinWidth = 1100;
        MinHeight = 700;
        Content = _navigator;

        _navigator.Navigated += UpdateTitle;
        _session.SceneReset += () => _navigator.PopTo(0);
        KeyDown += OnKeyDown;
        _timer.Tick += (_, _) => Tick();

        Opened += (_, _) =>
        {
            _navigator.Push(new MenuPanel(_session, () => _scene ??= new SimulationPanel(_session)));
            // Fioletowe startują z losowych wag i od razu się uczą — każdy w swoim wątku.
            _session.StartTrainingAll();
            _clock.Start();
            _timer.Start();
        };
        Closed += (_, _) =>
        {
            _timer.Stop();
            _session.Dispose();
            _scene?.Dispose();
        };
    }

    private void Tick()
    {
        var delta = (float)_clock.Elapsed.TotalSeconds;
        _clock.Restart();
        _session.Tick();
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
