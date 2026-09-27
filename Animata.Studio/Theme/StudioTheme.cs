using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;

namespace Animata.Studio.Theme;

/// <summary>Kolory Studia dla jednego wariantu motywu (jasny/ciemny) i wybranego akcentu.</summary>
public sealed class StudioPalette
{
    public StudioPalette(bool aDark, Color aAccent)
    {
        IsDark = aDark;
        Accent = aAccent;
        if (aDark)
        {
            Bg = Color.Parse("#1c1c1c");
            Layer = Color.Parse("#232323");
            Card = Color.Parse("#2b2b2b");
            Card2 = Color.Parse("#353535");
            Stroke = Color.Parse("#14ffffff");
            Stroke2 = Color.Parse("#29ffffff");
            Text = Color.Parse("#ffffff");
            Text2 = Color.Parse("#d4d4d4");
            Text3 = Color.Parse("#a3a3a3");
            OnAccent = Colors.Black;
            Canvas = Color.Parse("#181818");
            Dot = Color.Parse("#16ffffff");
            Hover = Color.Parse("#10ffffff");
            WireCommand = Color.Parse("#c58af5");
            Shadow = Color.Parse("#59000000");
        }
        else
        {
            Bg = Color.Parse("#f3f3f3");
            Layer = Color.Parse("#fafafa");
            Card = Colors.White;
            Card2 = Color.Parse("#f0f0f0");
            Stroke = Color.Parse("#17000000");
            Stroke2 = Color.Parse("#2e000000");
            Text = Color.Parse("#1b1b1b");
            Text2 = Color.Parse("#404040");
            Text3 = Color.Parse("#616161");
            OnAccent = Colors.White;
            Canvas = Color.Parse("#f7f7f7");
            Dot = Color.Parse("#1c000000");
            Hover = Color.Parse("#0c000000");
            WireCommand = Color.Parse("#7d3cc0");
            Shadow = Color.Parse("#1a000000");
        }
        AccentSoft = WithAlpha(aAccent, 0.16);
    }

    public bool IsDark { get; }
    public Color Bg { get; }
    public Color Layer { get; }
    public Color Card { get; }
    public Color Card2 { get; }
    public Color Stroke { get; }
    public Color Stroke2 { get; }
    public Color Text { get; }
    public Color Text2 { get; }
    public Color Text3 { get; }
    public Color Accent { get; }
    public Color AccentSoft { get; }
    public Color OnAccent { get; }
    public Color Canvas { get; }
    public Color Dot { get; }
    public Color Hover { get; }
    public Color WireCommand { get; }
    public Color Shadow { get; }

    public static readonly Color Good = Color.Parse("#4fc38a");
    public static readonly Color Bad = Color.Parse("#ff6b5e");
    public static readonly Color Warm = Color.Parse("#ff9646");
    public static readonly Color Target = Color.Parse("#ffab33");
    public static readonly Color Obstacle = Color.Parse("#80808c");
    public static readonly Color Neural = Color.Parse("#b85ce6");
    public static readonly Color Controller = Color.Parse("#3dade6");

    public static Color WithAlpha(Color aColor, double aAlpha) =>
        Color.FromArgb((byte)Math.Clamp(aAlpha * 255, 0, 255), aColor.R, aColor.G, aColor.B);

    public static Color Mix(Color aFrom, Color aTo, double aAmount) => Color.FromArgb(
        (byte)(aFrom.A + (aTo.A - aFrom.A) * aAmount),
        (byte)(aFrom.R + (aTo.R - aFrom.R) * aAmount),
        (byte)(aFrom.G + (aTo.G - aFrom.G) * aAmount),
        (byte)(aFrom.B + (aTo.B - aFrom.B) * aAmount));
}

/// <summary>
/// Motyw Studia: wariant FluentTheme (jasny/ciemny) + kolor akcentu. Kolory trafiają do zasobów aplikacji jako pędzle
/// „Studio.*” (kontrolki wiążą się z nimi dynamicznie) i jako SystemAccentColor* (przyciski Fluent).
/// Kontrolki rysowane ręcznie czytają <see cref="Palette"/> i odświeżają się na <see cref="Changed"/>.
/// </summary>
public static class StudioTheme
{
    public static readonly Color[] DarkAccents = [Color.Parse("#60cdff"), Color.Parse("#4cc2a0"), Color.Parse("#ff9f5a"), Color.Parse("#c58af5")];
    public static readonly Color[] LightAccents = [Color.Parse("#005fb8"), Color.Parse("#0f7b5f"), Color.Parse("#b35a00"), Color.Parse("#7d3cc0")];

    public static StudioPalette Palette { get; private set; } = new(true, DarkAccents[0]);
    public static int AccentIndex { get; private set; }
    public static bool IsDark => Palette.IsDark;

    public static event Action? Changed;

    public static void Apply(bool aDark, int aAccentIndex)
    {
        var app = Application.Current;
        if (app is null)
            return;

        AccentIndex = Math.Clamp(aAccentIndex, 0, DarkAccents.Length - 1);
        var accent = (aDark ? DarkAccents : LightAccents)[AccentIndex];
        Palette = new StudioPalette(aDark, accent);
        app.RequestedThemeVariant = aDark ? ThemeVariant.Dark : ThemeVariant.Light;

        var resources = app.Resources;
        void Brush(string aKey, Color aColor) => resources[aKey] = new SolidColorBrush(aColor);
        Brush("Studio.Bg", Palette.Bg);
        Brush("Studio.Layer", Palette.Layer);
        Brush("Studio.Card", Palette.Card);
        Brush("Studio.Card2", Palette.Card2);
        Brush("Studio.Stroke", Palette.Stroke);
        Brush("Studio.Stroke2", Palette.Stroke2);
        Brush("Studio.Text", Palette.Text);
        Brush("Studio.Text2", Palette.Text2);
        Brush("Studio.Text3", Palette.Text3);
        Brush("Studio.Accent", Palette.Accent);
        Brush("Studio.AccentSoft", Palette.AccentSoft);
        Brush("Studio.OnAccent", Palette.OnAccent);
        Brush("Studio.Canvas", Palette.Canvas);
        Brush("Studio.Hover", Palette.Hover);
        Brush("Studio.Bad", StudioPalette.Bad);
        Brush("Studio.Good", StudioPalette.Good);

        // Akcent kontrolek Fluent. W ciemnym wariancie Fluent maluje akcent odcieniem Light2, w jasnym — Dark1.
        var black = Colors.Black;
        var white = Colors.White;
        var baseColor = aDark ? StudioPalette.Mix(accent, black, 0.3) : StudioPalette.Mix(accent, white, 0.15);
        resources["SystemAccentColor"] = baseColor;
        resources["SystemAccentColorLight1"] = aDark ? StudioPalette.Mix(accent, black, 0.15) : StudioPalette.Mix(accent, white, 0.3);
        resources["SystemAccentColorLight2"] = aDark ? accent : StudioPalette.Mix(accent, white, 0.45);
        resources["SystemAccentColorLight3"] = aDark ? StudioPalette.Mix(accent, white, 0.25) : StudioPalette.Mix(accent, white, 0.6);
        resources["SystemAccentColorDark1"] = aDark ? StudioPalette.Mix(accent, black, 0.4) : accent;
        resources["SystemAccentColorDark2"] = StudioPalette.Mix(accent, black, aDark ? 0.5 : 0.15);
        resources["SystemAccentColorDark3"] = StudioPalette.Mix(accent, black, aDark ? 0.6 : 0.3);

        Changed?.Invoke();
    }

    public static void Toggle() => Apply(!IsDark, AccentIndex);
}
