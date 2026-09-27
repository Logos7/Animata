using Avalonia;
using Avalonia.Input;
using Avalonia.Media;
using Animata.Core.Brains.Modules;
using Animata.Studio.Kit;
using Animata.Studio.Theme;

namespace Animata.Studio.Controls;

/// <summary>
/// Sieć neuronowa narysowana warstwami: kolumny neuronów (kolor = aktywacja na żywo, niebieski +, pomarańczowy −),
/// linie = wagi (kolor znaku, grubość i krycie z wartości). Z lewej wyrażenia wejść, z prawej porty wyjść.
/// Klik w neuron zaznacza go (i jego warstwę) — jego połączenia są wtedy wyróżnione, reszta przygaszona.
/// </summary>
public sealed class NetworkView : ThemedControl
{
    private static readonly Color Positive = Color.FromRgb(96, 205, 255);
    private static readonly Color Negative = Color.FromRgb(255, 150, 70);
    private const double LabelLeft = 180;
    private const double LabelRight = 130;
    private const double Top = 44;
    private const double Bottom = 24;

    private readonly NeuralNetworkModule _module;

    public NetworkView(NeuralNetworkModule aModule)
    {
        _module = aModule;
        ClipToBounds = true;
        Cursor = new Cursor(StandardCursorType.Arrow);
    }

    /// <summary>Zaznaczony neuron (warstwa, indeks) albo null.</summary>
    public (int Layer, int Neuron)? Selected { get; private set; }

    /// <summary>Zaznaczona warstwa: warstwa zaznaczonego neuronu albo kliknięty nagłówek kolumny.</summary>
    public int? SelectedLayer { get; private set; }

    public event Action? SelectionChanged;

    public void Select(int? aLayer, int? aNeuron = null)
    {
        SelectedLayer = aLayer;
        Selected = aLayer is { } layer && aNeuron is { } neuron ? (layer, neuron) : null;
        SelectionChanged?.Invoke();
        InvalidateVisual();
    }

    /// <summary>Po zmianie kształtu: zaznaczenie poza siecią jest zdejmowane.</summary>
    public void Clamp()
    {
        var layers = _module.Network.Layers;
        if (SelectedLayer is { } layer && layer >= layers.Count)
            SelectedLayer = null;
        if (Selected is { } selected && (selected.Layer >= layers.Count || selected.Neuron >= layers[selected.Layer]))
            Selected = null;
        InvalidateVisual();
    }

    // ---------- geometria ----------

    private double ColumnX(int aLayer, int aCount)
    {
        var left = LabelLeft;
        var right = Math.Max(left + 1, Bounds.Width - LabelRight);
        return aCount < 2 ? (left + right) / 2 : left + (right - left) * aLayer / (aCount - 1);
    }

    private double Radius()
    {
        var most = _module.Network.Layers.Max();
        var height = Math.Max(1, Bounds.Height - Top - Bottom);
        return Math.Clamp(height / most * 0.36, 2.5, 13);
    }

    private double NeuronY(int aNeuron, int aSize)
    {
        var height = Math.Max(1, Bounds.Height - Top - Bottom);
        var step = Math.Min(height / aSize, Radius() * 3.2);
        return Top + height / 2 + (aNeuron - (aSize - 1) / 2.0) * step;
    }

    private Point At(int aLayer, int aNeuron)
    {
        var layers = _module.Network.Layers;
        return new Point(ColumnX(aLayer, layers.Count), NeuronY(aNeuron, layers[aLayer]));
    }

    private static Color Signed(float aValue, double aBase, double aGain) =>
        StudioPalette.WithAlpha(aValue >= 0 ? Positive : Negative, Math.Clamp(aBase + aGain * Math.Abs(aValue), 0, 1));

    // ---------- rysowanie ----------

    public override void Render(DrawingContext aContext)
    {
        var palette = P;
        aContext.FillRectangle(Ui.Brush(palette.Canvas), new Rect(Bounds.Size));
        var network = _module.Network;
        var layers = network.Layers;
        if (layers.Count < 2)
            return;
        var radius = Radius();
        var selected = Selected;

        // Połączenia: przy zaznaczonym neuronie jego wagi mocno, reszta ledwo.
        var weights = network.Weights;
        var total = 0;
        for (var matrix = 0; matrix < weights.Length; matrix++)
            total += layers[matrix] * layers[matrix + 1];
        var dense = Math.Clamp(600.0 / Math.Max(1, total), 0.15, 1);
        for (var matrix = 0; matrix < weights.Length; matrix++)
            for (var neuron = 0; neuron < weights[matrix].Length; neuron++)
                for (var source = 0; source < weights[matrix][neuron].Length; source++)
                {
                    var weight = weights[matrix][neuron][source];
                    var touches = selected is { } s &&
                        ((s.Layer == matrix + 1 && s.Neuron == neuron) || (s.Layer == matrix && s.Neuron == source));
                    var alpha = selected is null ? dense : touches ? 1 : 0.12 * dense;
                    var color = Signed(weight, 0.05 * alpha, 0.8 * alpha);
                    var thickness = (touches ? 1.2 : 0.6) + Math.Min(2.5, Math.Abs(weight)) * (touches ? 1.1 : 0.6);
                    aContext.DrawLine(Draw.Pen(color, thickness), At(matrix, source), At(matrix + 1, neuron));
                }

        // Nagłówki kolumn.
        for (var layer = 0; layer < layers.Count; layer++)
        {
            var x = ColumnX(layer, layers.Count);
            var title = layer == 0 ? "wejścia" : layer == layers.Count - 1 ? "wyjścia" : $"ukryta {layer}";
            var active = SelectedLayer == layer;
            if (active && layer > 0 && layer < layers.Count - 1)
                aContext.DrawRectangle(Ui.Brush(palette.AccentSoft), null,
                    new Rect(x - Math.Max(radius + 10, 36), 8, 2 * Math.Max(radius + 10, 36), Bounds.Height - 16), 8, 8);
            Draw.Text(aContext, $"{title} · {layers[layer]}", new Point(x, 22), 12, active ? palette.Accent : palette.Text2,
                TextAnchor.Center, active);
        }

        // Neurony.
        for (var layer = 0; layer < layers.Count; layer++)
        {
            var values = network.Activations(layer);
            for (var neuron = 0; neuron < layers[layer]; neuron++)
            {
                var center = At(layer, neuron);
                var value = neuron < values.Length ? values[neuron] : 0;
                var fill = StudioPalette.Mix(palette.Card, value >= 0 ? Positive : Negative, Math.Clamp(Math.Abs(value), 0, 1) * 0.85);
                var isSelected = selected is { } s && s.Layer == layer && s.Neuron == neuron;
                aContext.DrawEllipse(Ui.Brush(fill), Draw.Pen(isSelected ? palette.Accent : palette.Stroke2, isSelected ? 2.5 : 1),
                    center, radius, radius);
            }
        }

        // Etykiety wejść i wyjść (tylko gdy się mieszczą).
        var labelSize = Math.Clamp(radius * 1.1, 9, 12);
        if (radius >= 4)
        {
            var inputs = _module.Inputs;
            for (var neuron = 0; neuron < layers[0] && neuron < inputs.Count; neuron++)
            {
                var point = At(0, neuron);
                Draw.Text(aContext, Shorten(inputs[neuron].Expression, 22), new Point(point.X - radius - 8, point.Y), labelSize,
                    palette.Text2, TextAnchor.Right, aMono: true);
            }
            var outputs = _module.Outputs;
            for (var neuron = 0; neuron < layers[^1] && neuron < outputs.Count; neuron++)
            {
                var point = At(layers.Count - 1, neuron);
                var value = network.Activations(layers.Count - 1)[neuron] * outputs[neuron].Scale + outputs[neuron].Offset;
                Draw.Text(aContext, $"{outputs[neuron].Port} {value:+0.00;-0.00}", new Point(point.X + radius + 8, point.Y), labelSize,
                    palette.Text, TextAnchor.Left, aMono: true);
            }
        }
    }

    private static string Shorten(string aText, int aLength) =>
        aText.Length <= aLength ? aText : aText[..(aLength - 1)] + "…";

    // ---------- mysz ----------

    protected override void OnPointerPressed(PointerPressedEventArgs aEvent)
    {
        base.OnPointerPressed(aEvent);
        if (!aEvent.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;
        var point = aEvent.GetPosition(this);
        var layers = _module.Network.Layers;
        var radius = Radius();
        for (var layer = 0; layer < layers.Count; layer++)
            for (var neuron = 0; neuron < layers[layer]; neuron++)
            {
                var center = At(layer, neuron);
                if (Math.Abs(center.X - point.X) <= radius + 3 && Math.Abs(center.Y - point.Y) <= radius + 3)
                {
                    Select(layer, neuron);
                    aEvent.Handled = true;
                    return;
                }
            }
        // Poza neuronem: kolumna najbliższa w poziomie (w jej pasie) albo nic.
        for (var layer = 0; layer < layers.Count; layer++)
            if (Math.Abs(ColumnX(layer, layers.Count) - point.X) <= Math.Max(radius + 10, 36))
            {
                Select(layer);
                return;
            }
        Select(null);
    }
}
