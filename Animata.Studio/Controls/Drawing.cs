using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Animata.Core.Training;
using Animata.Studio.Kit;
using Animata.Studio.Theme;

namespace Animata.Studio.Controls;

/// <summary>Kontrolka rysowana ręcznie, która przemalowuje się po zmianie motywu.</summary>
public abstract class ThemedControl : Control
{
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs aEvent)
    {
        base.OnAttachedToVisualTree(aEvent);
        StudioTheme.Changed += InvalidateVisual;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs aEvent)
    {
        StudioTheme.Changed -= InvalidateVisual;
        base.OnDetachedFromVisualTree(aEvent);
    }

    protected static StudioPalette P => StudioTheme.Palette;
}

public enum TextAnchor
{
    Left,
    Center,
    Right
}

/// <summary>Pomocniki rysowania: tekst, pióra.</summary>
public static class Draw
{
    private static readonly Typeface Sans = new(FontFamily.Default);
    private static readonly Typeface SansBold = new(FontFamily.Default, FontStyle.Normal, FontWeight.SemiBold);
    private static readonly Typeface MonoFace = new(Ui.Mono);

    public static FormattedText Format(string aText, double aSize, Color aColor, bool aBold = false, bool aMono = false) =>
        new(aText, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, aMono ? MonoFace : aBold ? SansBold : Sans, aSize, Ui.Brush(aColor));

    /// <summary>Tekst wyśrodkowany pionowo na <paramref name="aAt"/>.Y.</summary>
    public static double Text(DrawingContext aContext, string aText, Point aAt, double aSize, Color aColor,
        TextAnchor aAnchor = TextAnchor.Left, bool aBold = false, bool aMono = false)
    {
        var text = Format(aText, aSize, aColor, aBold, aMono);
        var x = aAnchor switch
        {
            TextAnchor.Center => aAt.X - text.Width / 2,
            TextAnchor.Right => aAt.X - text.Width,
            _ => aAt.X
        };
        aContext.DrawText(text, new Point(x, aAt.Y - text.Height / 2));
        return text.Width;
    }

    public static IPen Pen(Color aColor, double aThickness = 1, IDashStyle? aDash = null, PenLineCap aCap = PenLineCap.Round) =>
        new Pen(Ui.Brush(aColor), aThickness, aDash, aCap);
}

/// <summary>Wykres nauki: wynik zwycięzcy pokolenia (szary, szum) i mistrza na stałej walidacji (akcent, schodki w górę).</summary>
public sealed class Sparkline : ThemedControl
{
    private IReadOnlyList<TrainingProgress> _points = [];

    public IReadOnlyList<TrainingProgress> Points
    {
        get => _points;
        set
        {
            _points = value;
            InvalidateVisual();
        }
    }

    public override void Render(DrawingContext aContext)
    {
        var width = Bounds.Width;
        var height = Bounds.Height;
        if (_points.Count < 2)
        {
            Draw.Text(aContext, "czekam na pokolenia…", new Point(0, height / 2), 11.5, P.Text3);
            return;
        }

        var shown = _points.Skip(Math.Max(0, _points.Count - 120)).ToList();
        var values = shown.SelectMany(aPoint => new[] { aPoint.BestFitness, aPoint.ChampionScore }).Where(float.IsFinite).ToList();
        if (values.Count == 0)
            return;
        var min = values.Min();
        var max = values.Max();
        if (max - min < 1e-3f)
            max = min + 1e-3f;

        Point At(int aIndex, float aValue) => new(
            aIndex * width / (shown.Count - 1),
            3 + (1 - (aValue - min) / (max - min)) * (height - 6));

        void Line(Func<TrainingProgress, float> aValue, IPen aPen)
        {
            var geometry = new StreamGeometry();
            using (var context = geometry.Open())
            {
                var started = false;
                for (var index = 0; index < shown.Count; index++)
                {
                    var value = aValue(shown[index]);
                    if (!float.IsFinite(value))
                        continue;
                    if (!started)
                        context.BeginFigure(At(index, value), false);
                    else
                        context.LineTo(At(index, value));
                    started = true;
                }
                if (started)
                    context.EndFigure(false);
            }
            aContext.DrawGeometry(null, aPen, geometry);
        }

        Line(aPoint => aPoint.BestFitness, Draw.Pen(P.Text3, 1.2));
        Line(aPoint => aPoint.ChampionScore, Draw.Pen(P.Accent, 2.2));
    }
}

/// <summary>Pasek wartości: dwubiegunowy [-1, 1] ze środkiem albo jednobiegunowy [0, 1].</summary>
public sealed class Gauge : ThemedControl
{
    private float _value;

    public Gauge(bool aBipolar = true, bool aHitColors = false)
    {
        Bipolar = aBipolar;
        HitColors = aHitColors;
        Height = 8;
    }

    public bool Bipolar { get; }

    /// <summary>Kolor ostrzegawczy przy dużych wartościach (bliskość wąsów).</summary>
    public bool HitColors { get; }

    public float Value
    {
        get => _value;
        set
        {
            if (_value == value)
                return;
            _value = value;
            InvalidateVisual();
        }
    }

    public override void Render(DrawingContext aContext)
    {
        var rect = new Rect(Bounds.Size);
        aContext.DrawRectangle(Ui.Brush(P.Card2), null, rect, 4, 4);
        var value = Math.Clamp(_value, Bipolar ? -1 : 0, 1);
        var color = HitColors && value > 0.15f ? StudioPalette.Warm : P.Accent;
        Rect fill;
        if (Bipolar)
        {
            var mid = rect.Width / 2;
            var extent = value * mid;
            fill = extent >= 0 ? new Rect(mid, 0, extent, rect.Height) : new Rect(mid + extent, 0, -extent, rect.Height);
        }
        else
            fill = new Rect(0, 0, value * rect.Width, rect.Height);
        if (fill.Width > 0.5)
            aContext.DrawRectangle(Ui.Brush(color), null, fill, 4, 4);
        if (Bipolar)
            aContext.DrawLine(Draw.Pen(P.Text3), new Point(rect.Width / 2, -3), new Point(rect.Width / 2, rect.Height + 3));
    }
}

/// <summary>Kompas: strzałka w stronę celu w układzie stwora (przód = w prawo, lewo = w górę).</summary>
public sealed class Compass : ThemedControl
{
    private float _angle;
    private bool _found;

    public Compass()
    {
        Width = 80;
        Height = 80;
    }

    public void Set(float aAngle, bool aFound)
    {
        if (_angle == aAngle && _found == aFound)
            return;
        _angle = aAngle;
        _found = aFound;
        InvalidateVisual();
    }

    public override void Render(DrawingContext aContext)
    {
        var center = new Point(Bounds.Width / 2, Bounds.Height / 2);
        var radius = Math.Min(center.X, center.Y) - 4;
        aContext.DrawEllipse(null, Draw.Pen(P.Stroke2), center, radius, radius);
        foreach (var angle in new[] { 0, Math.PI / 2, Math.PI, 3 * Math.PI / 2 })
        {
            var direction = new Vector(Math.Cos(angle), Math.Sin(angle));
            aContext.DrawLine(Draw.Pen(P.Text3), center + direction * (radius - 6), center + direction * radius);
        }
        if (_found)
        {
            var direction = new Vector(Math.Cos(_angle), -Math.Sin(_angle));
            var tip = center + direction * (radius - 4);
            var pen = Draw.Pen(P.Accent, 2.5);
            aContext.DrawLine(pen, center, tip);
            var side = new Vector(-direction.Y, direction.X);
            aContext.DrawLine(pen, tip, tip - direction * 7 + side * 5);
            aContext.DrawLine(pen, tip, tip - direction * 7 - side * 5);
        }
        aContext.DrawEllipse(Ui.Brush(P.Text), null, center, 4, 4);
    }
}

/// <summary>Mapa wag jednej warstwy sieci: wiersz = neuron, kolumny = wagi od wejść i bias; niebieski +, pomarańczowy −.</summary>
public sealed class WeightHeatmap : ThemedControl
{
    private float[][] _weights = [];
    private float[] _biases = [];

    public void Set(float[][] aWeights, float[] aBiases)
    {
        _weights = aWeights;
        _biases = aBiases;
        var columns = aWeights.Length > 0 ? aWeights[0].Length + 1 : 1;
        Height = Math.Max(1, aWeights.Length) * 14;
        MinWidth = columns * 6;
        InvalidateVisual();
    }

    public override void Render(DrawingContext aContext)
    {
        if (_weights.Length == 0)
            return;
        var columns = _weights[0].Length + 1;
        var cell = (Bounds.Width - (columns - 1) * 2) / columns;
        for (var row = 0; row < _weights.Length; row++)
            for (var column = 0; column < columns; column++)
            {
                var value = column < columns - 1 ? _weights[row][column] : _biases[row];
                var strength = Math.Clamp(Math.Abs(value), 0, 1);
                var color = value >= 0 ? Color.FromRgb(96, 205, 255) : Color.FromRgb(255, 150, 70);
                var rect = new Rect(column * (cell + 2), row * 14, cell, 12);
                aContext.DrawRectangle(Ui.Brush(StudioPalette.WithAlpha(color, 0.12 + 0.85 * strength)), null, rect, 2, 2);
            }
    }
}
