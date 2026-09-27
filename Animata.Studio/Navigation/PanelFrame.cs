using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Animata.Studio.Kit;

namespace Animata.Studio.Navigation;

/// <summary>
/// Rama panelu: pasek u góry (logo → menu, wstecz, breadcrumb, narzędzia panelu) i treść.
/// Pasek jest częścią panelu, więc przy przejściu leci razem z nim — całe okno „zamienia się” w następny ekran.
/// </summary>
public static class PanelFrame
{
    public const double BarHeight = 56;

    public static Control Create(StudioPanel aPanel, Control aBody, Control? aTitleExtra = null,
        Control? aCenter = null, Control? aRight = null)
    {
        var bar = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,*,Auto"), Height = BarHeight };
        var left = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };

        var navigator = aPanel.Navigator!;
        var home = Ui.IconButton(Icons.Logo, "Menu główne", () => navigator.PopTo(0));
        home.Content = Ui.Icon(Icons.Logo, 20, "Studio.Accent", aThickness: 1.8);
        left.Children.Add(home);
        var back = Ui.IconButton(Icons.Back, "Wstecz (Esc, Alt+←)", navigator.Back);
        back.IsEnabled = aPanel.Depth > 0;
        left.Children.Add(back);

        // Panel buduje się, zanim trafi na stos: poziomy niżej bierzemy ze stosu, ostatni to on sam.
        for (var index = 0; index <= aPanel.Depth; index++)
        {
            var panel = index < aPanel.Depth && index < navigator.Stack.Count ? navigator.Stack[index] : aPanel;
            if (index < aPanel.Depth)
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
                crumb.Click += (_, _) => navigator.PopTo(target);
                left.Children.Add(crumb);
                left.Children.Add(Ui.Icon(Icons.Chevron, 12, "Studio.Text3"));
            }
            else
            {
                var current = Ui.Text(panel.Title, 14, "Studio.Text", FontWeight.SemiBold);
                current.Margin = new Thickness(8, 0);
                left.Children.Add(current);
            }
        }
        if (aTitleExtra is not null)
            left.Children.Add(aTitleExtra);
        bar.Children.Add(left);

        if (aCenter is not null)
        {
            aCenter.HorizontalAlignment = HorizontalAlignment.Center;
            aCenter.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(aCenter, 2);
            bar.Children.Add(aCenter);
        }
        if (aRight is not null)
        {
            aRight.VerticalAlignment = VerticalAlignment.Center;
            aRight.Margin = new Thickness(0, 0, 12, 0);
            Grid.SetColumn(aRight, 4);
            bar.Children.Add(aRight);
        }

        var barBorder = new Border { Child = bar, BorderThickness = new Thickness(0, 0, 0, 1) };
        barBorder.Res(Border.BackgroundProperty, "Studio.Layer");
        barBorder.Res(Border.BorderBrushProperty, "Studio.Stroke");

        var dock = new DockPanel();
        DockPanel.SetDock(barBorder, Dock.Top);
        dock.Children.Add(barBorder);
        dock.Children.Add(aBody);
        return dock;
    }

    /// <summary>Boczny panel z tłem warstwy i linią od strony treści.</summary>
    public static Border Side(Control aContent, bool aLeft, double aWidth)
    {
        var border = new Border
        {
            Child = aContent,
            Width = aWidth,
            BorderThickness = aLeft ? new Thickness(0, 0, 1, 0) : new Thickness(1, 0, 0, 0)
        };
        border.Res(Border.BackgroundProperty, "Studio.Layer");
        return border.Res(Border.BorderBrushProperty, "Studio.Stroke");
    }

    /// <summary>Grupa przycisków na wspólnym tle (np. pauza / krok / prędkość).</summary>
    public static Border Group(params Control[] aChildren)
    {
        var border = new Border
        {
            Child = Ui.HStack(2, aChildren),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(3)
        };
        return border.Res(Border.BackgroundProperty, "Studio.Card2");
    }
}
