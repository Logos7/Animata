using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Animata.Studio.Kit;

namespace Animata.Studio.Navigation;

/// <summary>
/// Pasek okna nad nawigatorem: logo (→ menu), wstecz, breadcrumb i narzędzia aktywnego panelu.
/// Nie bierze udziału w przejściach — przy wejściu i powrocie breadcrumb zmienia się od razu, a narzędzia
/// nowego panelu krótko się pojawiają (przenikanie), zamiast lecieć razem z panelem.
/// </summary>
public sealed class NavigationBar : Border
{
    private static readonly TimeSpan FadeDuration = TimeSpan.FromMilliseconds(180);

    private readonly PanelNavigator _navigator;
    private readonly StackPanel _crumbs = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
    private readonly Button _back;
    private readonly Border _titleExtra = Tools();
    private readonly Border _center = Tools();
    private readonly Border _right = Tools();
    private StudioPanel? _panel;

    public NavigationBar(PanelNavigator aNavigator)
    {
        _navigator = aNavigator;

        var home = Ui.IconButton(Icons.Logo, "Menu główne", () => _navigator.PopTo(0));
        home.Content = Ui.Icon(Icons.Logo, 20, "Studio.Accent", aThickness: 1.8);
        _back = Ui.IconButton(Icons.Back, "Wstecz (Esc, Alt+←)", _navigator.Back);
        _titleExtra.Margin = new Thickness(6, 0, 0, 0);
        var left = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 2,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(12, 0, 0, 0)
        };
        left.Children.Add(home);
        left.Children.Add(_back);
        left.Children.Add(_crumbs);
        left.Children.Add(_titleExtra);

        var bar = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,*,Auto"), Height = PanelFrame.BarHeight };
        bar.Children.Add(left);
        _center.HorizontalAlignment = HorizontalAlignment.Center;
        Grid.SetColumn(_center, 2);
        bar.Children.Add(_center);
        _right.Margin = new Thickness(0, 0, 12, 0);
        Grid.SetColumn(_right, 4);
        bar.Children.Add(_right);

        Child = bar;
        BorderThickness = new Thickness(0, 0, 0, 1);
        this.Res(BackgroundProperty, "Studio.Layer");
        this.Res(BorderBrushProperty, "Studio.Stroke");

        _navigator.Navigated += Update;
        Update();
    }

    /// <summary>Nowa ścieżka: breadcrumb od razu, narzędzia aktywnego panelu z przenikaniem.</summary>
    private void Update()
    {
        var active = _navigator.Active;
        if (_panel != active)
        {
            if (_panel is not null)
                _panel.BarChanged -= Refresh;
            _panel = active;
            if (_panel is not null)
                _panel.BarChanged += Refresh;
            Show(true);
        }
        else
        {
            Show(false);
        }
    }

    /// <summary>Aktywny panel zmienił tytuł albo narzędzia (np. przebudował się) — bez przenikania.</summary>
    private void Refresh() => Show(false);

    private void Show(bool aFade)
    {
        var stack = _navigator.Stack;
        _back.IsEnabled = stack.Count > 1;

        // Menu (poziom 0) nie ma okruszka — prowadzi do niego logo.
        _crumbs.Children.Clear();
        for (var index = 1; index < stack.Count; index++)
        {
            var panel = stack[index];
            if (index < stack.Count - 1)
            {
                var target = index;
                var crumb = new Button
                {
                    Content = Ui.Text(panel.Title, 14, "Studio.Text3"),
                    Background = Brushes.Transparent,
                    BorderThickness = new Thickness(0),
                    Padding = new Thickness(8, 6),
                    Focusable = false
                };
                crumb.Click += (_, _) => _navigator.PopTo(target);
                _crumbs.Children.Add(crumb);
                _crumbs.Children.Add(Ui.Icon(Icons.Chevron, 12, "Studio.Text3"));
            }
            else
            {
                var current = Ui.Text(panel.Title, 14, "Studio.Text", FontWeight.SemiBold);
                current.Margin = new Thickness(8, 0);
                current.VerticalAlignment = VerticalAlignment.Center;
                _crumbs.Children.Add(current);
            }
        }

        Place(_titleExtra, _panel?.BarTitleExtra, aFade);
        Place(_center, _panel?.BarCenter, aFade);
        Place(_right, _panel?.BarRight, aFade);
    }

    /// <summary>Wstawia narzędzie panelu do miejsca w pasku (najpierw odpina je od poprzedniego rodzica).</summary>
    private static void Place(Border aHost, Control? aTools, bool aFade)
    {
        if (aHost.Child == aTools)
            return;
        aHost.Child = null;
        if (aTools?.Parent is Border previous)
            previous.Child = null;
        aHost.Child = aTools;
        if (!aFade || aTools is null)
            return;
        // Przenikanie: bez przejścia skok do 0, potem z przejściem do 1.
        var transitions = aHost.Transitions;
        aHost.Transitions = null;
        aHost.Opacity = 0;
        aHost.Transitions = transitions;
        Dispatcher.UIThread.Post(() => aHost.Opacity = 1, DispatcherPriority.Render);
    }

    private static Border Tools() => new()
    {
        VerticalAlignment = VerticalAlignment.Center,
        Transitions = new Transitions { new DoubleTransition { Property = OpacityProperty, Duration = FadeDuration } }
    };
}
