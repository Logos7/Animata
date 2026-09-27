using Avalonia;
using Avalonia.Markup.Xaml.Styling;

namespace Animata.Rendering.HelixToolkit;

public static class HelixRendererStyles
{
    public static void Register(Application aApplication)
    {
        var uri = new Uri("avares://HelixToolkit.Avalonia.SharpDX/Styles/Generic.axaml");
        aApplication.Resources.MergedDictionaries.Add(new ResourceInclude(uri) { Source = uri });
    }
}
