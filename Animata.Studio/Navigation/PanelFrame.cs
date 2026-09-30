using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Animata.Studio.Kit;

namespace Animata.Studio.Navigation;

/// <summary>
/// Rama panelu. Pasek u góry (logo, wstecz, breadcrumb, narzędzia) należy do okna (<see cref="NavigationBar"/>)
/// i stoi w miejscu przy przejściach — panel oddaje mu tylko swoje narzędzia, a sam zwraca treść.
/// </summary>
public static class PanelFrame
{
    public const double BarHeight = 56;

    /// <summary>
    /// Rejestruje narzędzia panelu w pasku okna (obok tytułu, na środku, z prawej) i zwraca treść bez zmian.
    /// </summary>
    public static Control Create(StudioPanel aPanel, Control aBody, Control? aTitleExtra = null,
        Control? aCenter = null, Control? aRight = null)
    {
        aPanel.SetBar(aTitleExtra, aCenter, aRight);
        return aBody;
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
