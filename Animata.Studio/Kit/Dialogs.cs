using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Animata.Studio.Kit;

/// <summary>Proste okna komunikatów (błąd otwarcia pliku itp.).</summary>
public static class Dialogs
{
    /// <summary>Okno modalne z tytułem, treścią i przyciskiem OK.</summary>
    public static Task ShowAsync(Window aOwner, string aTitle, string aMessage)
    {
        var text = Ui.Text(aMessage, 13, "Studio.Text2");
        text.TextWrapping = TextWrapping.Wrap;
        text.TextTrimming = TextTrimming.None;
        var dialog = new Window
        {
            Title = aTitle,
            Width = 520,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };
        var ok = Ui.Button("OK", dialog.Close, aAccent: true);
        ok.HorizontalAlignment = HorizontalAlignment.Right;
        var content = new Border
        {
            Padding = new Thickness(24),
            Child = Ui.VStack(16, Ui.Text(aTitle, 16, "Studio.Text", FontWeight.SemiBold), text, ok)
        };
        content.Res(Border.BackgroundProperty, "Studio.Layer");
        dialog.Content = content;
        return dialog.ShowDialog(aOwner);
    }
}
