using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Themes.Fluent;
using Animata.Rendering.HelixToolkit;
using Animata.Studio.Theme;

namespace Animata.Studio;

internal static class Program
{
    [STAThread]
    private static void Main(string[] aArgs) => AppBuilder.Configure<StudioApp>()
        .UsePlatformDetect()
        .StartWithClassicDesktopLifetime(aArgs);
}

public sealed class StudioApp : Application
{
    public override void Initialize()
    {
        Styles.Add(new FluentTheme());
        HelixRendererStyles.Register(this);
        StudioTheme.Apply(true, 0);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.MainWindow = new StudioWindow();

        base.OnFrameworkInitializationCompleted();
    }
}
