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
/// Okno Studia: pasek (<see cref="NavigationBar"/>) i pod nim jeden <see cref="PanelNavigator"/> i kilka scen (demo, wąż), każda z własną
/// sesją i własnym panelem. Zegar (~30 Hz) przesuwa tylko otwarte sceny (symulację i naukę) i odświeża aktywny panel.
/// Klawisze idą najpierw do aktywnego panelu; nieobsłużone Esc i Alt+← (poza polami tekstowymi) cofają o poziom.
/// </summary>
public sealed class StudioWindow : Window
{
    private readonly List<(StudioSession Session, Func<SimulationPanel> Panel)> _scenes = [];
    private readonly Dictionary<StudioSession, SimulationPanel> _panels = [];
    private readonly PanelNavigator _navigator = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(33) };
    private readonly Stopwatch _clock = new();
    private int _demoScenes;

    public StudioWindow()
    {
        Title = "Animata Studio";
        Width = 1440;
        Height = 900;
        MinWidth = 1100;
        MinHeight = 700;
        // Pasek stoi nad nawigatorem, poza przejściami — lecą tylko panele.
        var bar = new NavigationBar(_navigator);
        var shell = new DockPanel();
        DockPanel.SetDock(bar, Dock.Top);
        shell.Children.Add(bar);
        shell.Children.Add(_navigator);
        Content = shell;

        AddScene(new StudioSession("Scena demo", WorldObjectCatalog.CreateDemo)
        {
            Description = "Dwa autka omijają cylindry w drodze do kuli, dwa walce jadą po wolnym torze. W każdej parze jeden ma gotowy sterownik, drugi sieć do nauczenia."
        });
        AddScene(new StudioSession("Węże", WorldObjectCatalog.CreateSnakeScene)
        {
            Description = "Wąż z generatorem fali i wąż z siecią pełzną przez niskie klocki do kuli leżącej na najwyższym."
        });
        AddScene(new StudioSession("Pająki", WorldObjectCatalog.CreateSpiderScene)
        {
            Description = "Pająk z generatorem kłusa i pająk z siecią idą do kuli przez niskie klocki — trzeba nie upaść na brzuch."
        });
        AddScene(new StudioSession("Humanoidy", WorldObjectCatalog.CreateHumanoidScene)
        {
            Description = "Dwa humanoidy z mózgiem ze stanami: stoją, gdy kula jest blisko, idą, gdy daleko. Jeden ma ręczne stanie i krok, drugi dwie sieci do nauczenia."
        });
        AddScene(new StudioSession("Wspinaczka", WorldObjectCatalog.CreateClimbScene)
        {
            Description = "Dwa węże owinięte wokół pni: jeden toczy się w górę ręcznym ruchem, drugi ma sieć, która musi się tego nauczyć."
        });
        _demoScenes = _scenes.Count;

        _navigator.Navigated += UpdateTitle;
        KeyDown += OnKeyDown;
        _timer.Tick += (_, _) => Tick();

        Opened += (_, _) =>
        {
            _navigator.Push(new MenuPanel(
                _scenes.Take(_demoScenes).Select(aScene => new MenuScene(aScene.Session, aScene.Panel)).ToList(), OpenFile));
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

    /// <summary>
    /// Otwiera plik świata jako scenę (nazwa = nazwa pliku) i wchodzi do niej. Plik otwarty już wcześniej w tej sesji
    /// programu otwiera tę samą scenę — chyba że plik na dysku się od tamtej pory zmienił: wtedy scena powstaje od nowa
    /// z nowej wersji. Błąd odczytu — komunikat w oknie z pliku, scena nie powstaje.
    /// </summary>
    private void OpenFile(string aPath)
    {
        var path = Path.GetFullPath(aPath);
        var written = File.Exists(path) ? File.GetLastWriteTimeUtc(path) : DateTime.MinValue;
        if (_files.TryGetValue(path, out var opened) && opened.Written != written)
        {
            CloseScene(opened.Session);
            _files.Remove(path);
        }
        StudioSession session;
        if (_files.TryGetValue(path, out opened))
            session = opened.Session;
        else
        {
            try
            {
                var document = Core.Persistence.WorldFile.FromJson(File.ReadAllText(path));
                session = new StudioSession(Kit.WorldFiles.SceneName(path), () => Core.Persistence.WorldFile.Restore(document));
            }
            catch (Exception exception) when (exception is IOException or NotSupportedException or System.Text.Json.JsonException
                or InvalidOperationException or ArgumentException or UnauthorizedAccessException)
            {
                _ = Kit.Dialogs.ShowAsync(this, "Nie udało się otworzyć pliku", $"{path}\n\n{exception.Message}");
                return;
            }
            _files[path] = (session, written);
            AddScene(session);
        }
        RecentFiles.Add(path);
        var panel = _scenes.First(aScene => aScene.Session == session).Panel();
        _navigator.PopTo(0);
        _navigator.Push(panel, null);
    }

    private readonly Dictionary<string, (StudioSession Session, DateTime Written)> _files =
        new(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

    /// <summary>Zamyka scenę z pliku (nieotwartą): jej panel, naukę i świat.</summary>
    private void CloseScene(StudioSession aSession)
    {
        _scenes.RemoveAll(aScene => aScene.Session == aSession);
        if (_panels.Remove(aSession, out var panel))
            panel.Dispose();
        aSession.Dispose();
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
        // Tylko otwarte sceny żyją: w menu (i w innej scenie) świat stoi, a nauka czeka między pokoleniami.
        foreach (var (session, _) in _scenes)
        {
            var open = _panels.TryGetValue(session, out var panel) && _navigator.Stack.Contains(panel);
            if (session.Visible != open)
                session.Visible = open;
            if (open)
                session.Tick(delta);
        }
        _navigator.Active?.Refresh(delta);
    }

    private void UpdateTitle() =>
        Title = _navigator.Stack.Count > 1
            ? "Animata Studio — " + string.Join(" › ", _navigator.Stack.Skip(1).Select(aPanel => aPanel.Title))
            : "Animata Studio";

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
