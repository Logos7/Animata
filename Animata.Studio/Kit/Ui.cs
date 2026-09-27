using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Path = Avalonia.Controls.Shapes.Path;
using Shape = Avalonia.Controls.Shapes.Shape;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Animata.Studio.Theme;

namespace Animata.Studio.Kit;

/// <summary>Ikony obrysowe w układzie 16×16 (dane ścieżek).</summary>
public static class Icons
{
    public const string Back = "M10 3L5 8L10 13";
    public const string Chevron = "M6 4L10 8L6 12";
    public const string Enter = "M3 8H12M9 5L12 8L9 11";
    public const string Play = "M5 3.2V12.8L12.5 8Z";
    public const string Pause = "M5.5 3.5V12.5M10.5 3.5V12.5";
    public const string Step = "M4 3.5V12.5L10 8ZM12.5 3.5V12.5";
    public const string Camera = "M2.5 5.5H4.5L5.5 4H10.5L11.5 5.5H13.5V12.5H2.5ZM10 9A2 2 0 1 1 6 9A2 2 0 1 1 10 9";
    public const string Undo = "M4 6H10A3 3 0 0 1 10 12H7M6 4L4 6L6 8";
    public const string Moon = "M13 9.5A5.5 5.5 0 1 1 6.5 3A4.5 4.5 0 0 0 13 9.5Z";
    public const string Learn = "M2 12L5.5 8L8.5 10.5L14 4";
    public const string Shuffle = "M2 4H5L11 12H14M2 12H5L6.5 10M11 4H14M12.5 2.5L14 4L12.5 5.5M12.5 10.5L14 12L12.5 13.5";
    public const string Eye = "M1.5 8C1.5 8 4 3.5 8 3.5C12 3.5 14.5 8 14.5 8C14.5 8 12 12.5 8 12.5C4 12.5 1.5 8 1.5 8ZM10 8A2 2 0 1 1 6 8A2 2 0 1 1 10 8";
    public const string Whiskers = "M3 8H13M4 3.5L13 8M4 12.5L13 8";
    public const string Neural = "M5.3 4.6L10.7 7.2M5.3 11.4L10.7 8.8M5.3 4A1.8 1.8 0 1 1 1.7 4A1.8 1.8 0 1 1 5.3 4M5.3 12A1.8 1.8 0 1 1 1.7 12A1.8 1.8 0 1 1 5.3 12M14.3 8A1.8 1.8 0 1 1 10.7 8A1.8 1.8 0 1 1 14.3 8";
    public const string Wheel = "M13.5 8A5.5 5.5 0 1 1 2.5 8A5.5 5.5 0 1 1 13.5 8M9.5 8A1.5 1.5 0 1 1 6.5 8A1.5 1.5 0 1 1 9.5 8";
    public const string Router = "M2 5H6L10 11H14M2 11H6L7.5 9M10 5H14";
    public const string Constant = "M4 6H12M4 10H12";
    public const string Composite = "M2 2H10V10H2ZM6 6H14V14H6Z";
    public const string BoundaryIn = "M2 8H10M7 5L10 8L7 11M13 2V14";
    public const string BoundaryOut = "M3 2V14M6 8H14M11 5L14 8L11 11";
    public const string Brain = "M6 4.5L10 7.3M6 11.5L10 8.7M6 4A2 2 0 1 1 2 4A2 2 0 1 1 6 4M6 12A2 2 0 1 1 2 12A2 2 0 1 1 6 12M14 8A2 2 0 1 1 10 8A2 2 0 1 1 14 8";
    public const string Check = "M3 8.5L6 11.5L13 4.5";
    public const string Warning = "M8 2L14.5 13.5H1.5ZM8 6.5V9.5M8 11.5V11.6";
    public const string Plus = "M8 3V13M3 8H13";
    public const string Minus = "M3 8H13";
    public const string Spider = "M8 6.5A1.8 1.5 0 1 1 8 9.5A1.8 1.5 0 1 1 8 6.5M6.5 7L4 5L2 7M9.5 7L12 5L14 7M6.5 9L4 11L2 9.5M9.5 9L12 11L14 9.5";
    public const string Slab = "M1.5 10L5 7H14.5L11 10ZM1.5 10V12H11V10M11 12L14.5 9V7";
    public const string Magnet = "M4 2.5V8A4 4 0 0 0 12 8V2.5M4 2.5H6.5V8A1.5 1.5 0 0 0 9.5 8V2.5H12M4 5H6.5M9.5 5H12M8 14V12.5M3 13.5L4.5 12.3M13 13.5L11.5 12.3";
    public const string Trash = "M3 4.5H13M6 4.5V3H10V4.5M4.5 4.5L5.2 13.5H10.8L11.5 4.5";
    public const string Layout = "M2 3H6V7H2ZM10 3H14V7H10ZM6 11H10V15H6ZM6 5H10M4 7V13H6M12 7V13H10";
    public const string Fit = "M2 6V2H6M10 2H14V6M14 10V14H10M6 14H2V10";
    public const string Group = "M2 2H14V14H2ZM5 5H8V8H5ZM9 8H11V11H9Z";
    public const string Ungroup = "M2 2H7V7H2ZM9 9H14V14H9Z";
    public const string Search = "M7 12A5 5 0 1 1 7 2A5 5 0 1 1 7 12M11 11L14 14";
    public const string Target = "M13 8A5 5 0 1 1 3 8A5 5 0 1 1 13 8M10 8A2 2 0 1 1 6 8A2 2 0 1 1 10 8";
    public const string Pillar = "M4 4A4 1.5 0 1 1 12 4A4 1.5 0 1 1 4 4M4 4V12A4 1.5 0 0 0 12 12V4";
    public const string Home = "M2.5 7.5L8 3L13.5 7.5M4 6.5V13H12V6.5";
    public const string Save = "M3 2.5H11L13.5 5V13.5H2.5V2.5ZM5 2.5V6H10V2.5M5 13.5V9.5H11V13.5";
    public const string Open = "M2 4.5V13H13L14.5 7H4.5L2 13M2 4.5H6L7.5 6H12.5V7";
    public const string Snake = "M2 11C4 11 4 7 6 7S8 11 10 11S12 7 14 7M14 7L14.8 6.2";
    public const string Floor = "M1.5 10.5L5 6.5H14.5L11 10.5ZM1.5 10.5V12H11V10.5M11 12L14.5 8V6.5";
    public const string Logo = "M12.5 8A4.5 4.5 0 1 1 3.5 8A4.5 4.5 0 1 1 12.5 8M12.3 6.4L15.5 4.5M13 8H15.8M12.3 9.6L15.5 11.5";
}

/// <summary>Pomocniki do budowania UI w kodzie (bez XAML), spójne z motywem Studia.</summary>
public static class Ui
{
    public static readonly FontFamily Mono = new("Cascadia Mono, Consolas, Courier New");

    private static readonly Dictionary<uint, IBrush> Brushes = [];

    /// <summary>Pędzel z pamięci podręcznej (do rysowania ręcznego co klatkę).</summary>
    public static IBrush Brush(Color aColor)
    {
        var key = aColor.ToUInt32();
        if (!Brushes.TryGetValue(key, out var brush))
        {
            brush = new SolidColorBrush(aColor);
            Brushes[key] = brush;
        }
        return brush;
    }

    /// <summary>Wiąże właściwość z zasobem motywu (np. "Studio.Text3"), dynamicznie — zmiana motywu przemalowuje.</summary>
    public static T Res<T>(this T aControl, AvaloniaProperty aProperty, string aKey) where T : Control
    {
        aControl.Bind(aProperty, aControl.GetResourceObservable(aKey));
        return aControl;
    }

    public static string F(float aValue, int aDigits = 2) =>
        (aValue < 0 ? "−" : string.Empty) + MathF.Abs(aValue).ToString("F" + aDigits, CultureInfo.InvariantCulture);

    public static bool TryParse(string? aText, out float aValue) =>
        float.TryParse((aText ?? string.Empty).Replace('−', '-').Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out aValue)
        && float.IsFinite(aValue);

    public static TextBlock Text(string aText, double aSize = 13, string aBrush = "Studio.Text", FontWeight aWeight = FontWeight.Normal)
    {
        var block = new TextBlock
        {
            Text = aText,
            FontSize = aSize,
            FontWeight = aWeight,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        return block.Res(TextBlock.ForegroundProperty, aBrush);
    }

    public static TextBlock MonoText(string aText, double aSize = 12, string aBrush = "Studio.Text")
    {
        var block = Text(aText, aSize, aBrush);
        block.FontFamily = Mono;
        return block;
    }

    /// <summary>Nagłówek sekcji: małe, pogrubione, wersaliki.</summary>
    public static TextBlock Header(string aText) =>
        Text(aText.ToUpperInvariant(), 11, "Studio.Text3", FontWeight.SemiBold);

    public static Control Icon(string aData, double aSize = 16, string aBrush = "Studio.Text", bool aFill = false, double aThickness = 1.5)
    {
        var path = new Path
        {
            Data = Geometry.Parse(aData),
            StrokeThickness = aThickness,
            StrokeLineCap = PenLineCap.Round,
            StrokeJoin = PenLineJoin.Round
        };
        path.Res(aFill ? Shape.FillProperty : Shape.StrokeProperty, aBrush);
        var canvas = new Canvas { Width = 16, Height = 16 };
        canvas.Children.Add(path);
        return new Viewbox { Width = aSize, Height = aSize, Child = canvas };
    }

    public static Control IconColored(string aData, Color aColor, double aSize = 16, double aThickness = 1.5)
    {
        var path = new Path
        {
            Data = Geometry.Parse(aData),
            Stroke = new SolidColorBrush(aColor),
            StrokeThickness = aThickness,
            StrokeLineCap = PenLineCap.Round,
            StrokeJoin = PenLineJoin.Round
        };
        var canvas = new Canvas { Width = 16, Height = 16 };
        canvas.Children.Add(path);
        return new Viewbox { Width = aSize, Height = aSize, Child = canvas };
    }

    /// <summary>Przycisk bez tła z ikoną (32×32). Nie łapie fokusu, żeby spacja i skróty trafiały do panelu.</summary>
    public static Button IconButton(string aIcon, string aTip, Action aClick, bool aFill = false)
    {
        var button = new Button
        {
            Content = Icon(aIcon, 16, "Studio.Text", aFill),
            Width = 32,
            Height = 32,
            Padding = new Thickness(0),
            Background = Avalonia.Media.Brushes.Transparent,
            BorderThickness = new Thickness(0),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            Focusable = false
        };
        ToolTip.SetTip(button, aTip);
        button.Click += (_, _) => aClick();
        return button;
    }

    public static Button Button(string aText, Action aClick, string? aIcon = null, bool aAccent = false,
        string? aShortcut = null, bool aGhost = false)
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
        if (aIcon is not null)
            content.Children.Add(Icon(aIcon, 16, aAccent ? "Studio.OnAccent" : "Studio.Text"));
        var label = new TextBlock { Text = aText, FontSize = 13, VerticalAlignment = VerticalAlignment.Center };
        if (aAccent)
        {
            label.FontWeight = FontWeight.SemiBold;
            label.Res(TextBlock.ForegroundProperty, "Studio.OnAccent");
        }
        content.Children.Add(label);
        if (aShortcut is not null)
            content.Children.Add(Kbd(aShortcut));

        var button = new Button
        {
            Content = content,
            Height = 32,
            Padding = new Thickness(12, 0),
            VerticalContentAlignment = VerticalAlignment.Center,
            Focusable = false
        };
        if (aAccent)
            button.Classes.Add("accent");
        if (aGhost)
        {
            button.Background = Avalonia.Media.Brushes.Transparent;
            button.BorderThickness = new Thickness(0);
        }
        button.Click += (_, _) => aClick();
        return button;
    }

    public static Border Kbd(string aText)
    {
        var text = Text(aText, 11, "Studio.Text3");
        var border = new Border
        {
            Child = text,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(5, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        return border.Res(Border.BorderBrushProperty, "Studio.Stroke2");
    }

    public static Border Card(Control aChild, double aPadding = 16)
    {
        var border = new Border
        {
            Child = aChild,
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(aPadding)
        };
        border.Res(Border.BackgroundProperty, "Studio.Card");
        return border.Res(Border.BorderBrushProperty, "Studio.Stroke");
    }

    public static Border Chip(string aText)
    {
        var border = new Border
        {
            Child = Text(aText, 12),
            Height = 26,
            Padding = new Thickness(10, 0),
            CornerRadius = new CornerRadius(13),
            BorderThickness = new Thickness(1),
            VerticalAlignment = VerticalAlignment.Center
        };
        border.Res(Border.BackgroundProperty, "Studio.Card2");
        return border.Res(Border.BorderBrushProperty, "Studio.Stroke2");
    }

    public static Border Dot(Color aColor, double aSize = 10) => new()
    {
        Width = aSize,
        Height = aSize,
        CornerRadius = new CornerRadius(aSize / 2),
        Background = new SolidColorBrush(aColor),
        VerticalAlignment = VerticalAlignment.Center
    };

    /// <summary>Wiersz klucz–wartość; wartość to podana kontrolka (np. TextBlock aktualizowany na żywo).</summary>
    public static Grid Row(string aKey, Control aValue, double aHeight = 28)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), MinHeight = aHeight };
        var key = Text(aKey, 13, "Studio.Text3");
        grid.Children.Add(key);
        aValue.HorizontalAlignment = HorizontalAlignment.Right;
        aValue.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(aValue, 1);
        grid.Children.Add(aValue);
        return grid;
    }

    public static Grid Row(string aKey, string aValue, bool aMono = false) =>
        Row(aKey, aMono ? MonoText(aValue, 12.5) : Text(aValue, 13));

    public static Border Separator() => new Border { Width = 1, Height = 24, Margin = new Thickness(6, 0) }
        .Res(Border.BackgroundProperty, "Studio.Stroke2");

    public static Border HLine() => new Border { Height = 1 }.Res(Border.BackgroundProperty, "Studio.Stroke");

    public static StackPanel HStack(double aSpacing, params Control[] aChildren)
    {
        var stack = new StackPanel { Orientation = Orientation.Horizontal, Spacing = aSpacing, VerticalAlignment = VerticalAlignment.Center };
        foreach (var child in aChildren)
            stack.Children.Add(child);
        return stack;
    }

    public static StackPanel VStack(double aSpacing, params Control[] aChildren)
    {
        var stack = new StackPanel { Orientation = Orientation.Vertical, Spacing = aSpacing };
        foreach (var child in aChildren)
            stack.Children.Add(child);
        return stack;
    }

    /// <summary>Pole tekstowe, które zatwierdza wartość Enterem albo przy utracie fokusu (Esc przywraca).</summary>
    public static TextBox Field(string aText, Func<string, bool> aCommit, double aWidth = double.NaN, bool aMono = true)
    {
        var box = new TextBox { Text = aText, Width = aWidth, MinHeight = 28, FontSize = 12.5, Padding = new Thickness(8, 4) };
        if (aMono)
            box.FontFamily = Mono;
        var committed = aText;
        void Commit()
        {
            var text = box.Text ?? string.Empty;
            if (text == committed)
                return;
            if (aCommit(text))
                committed = text;
            else
                box.Text = committed;
        }
        box.KeyDown += (_, aEvent) =>
        {
            if (aEvent.Key == Key.Enter)
            {
                Commit();
                aEvent.Handled = true;
            }
            else if (aEvent.Key == Key.Escape)
            {
                box.Text = committed;
                aEvent.Handled = true;
            }
        };
        box.LostFocus += (_, _) => Commit();
        return box;
    }

    /// <summary>Ustawia tekst pola, chyba że użytkownik właśnie w nim pisze.</summary>
    public static void SetIfIdle(TextBox aBox, string aText)
    {
        if (!aBox.IsFocused && aBox.Text != aText)
            aBox.Text = aText;
    }

    public static ScrollViewer Scroll(Control aContent) => new()
    {
        Content = aContent,
        HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
        VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto
    };

    /// <summary>Kolor encji do list i miniatur.</summary>
    public static Color ColorOf(Core.Entities.Entity aEntity) => aEntity switch
    {
        Core.WorldObjects.CarCreature car => Color.FromRgb((byte)(car.Color.X * 255), (byte)(car.Color.Y * 255), (byte)(car.Color.Z * 255)),
        Core.WorldObjects.CylinderCreature cylinder => Color.FromRgb((byte)(cylinder.Color.X * 255), (byte)(cylinder.Color.Y * 255), (byte)(cylinder.Color.Z * 255)),
        Core.Entities.ArticulatedCreature body => Color.FromRgb((byte)(body.Color.X * 255), (byte)(body.Color.Y * 255), (byte)(body.Color.Z * 255)),
        Core.WorldObjects.Floor => Color.FromRgb(62, 82, 98),
        Core.WorldObjects.TargetBall => StudioPalette.Target,
        Core.WorldObjects.Obstacle => StudioPalette.Obstacle,
        Core.WorldObjects.Slab => Color.FromRgb(104, 122, 138),
        _ => StudioTheme.Palette.Text3
    };
}
