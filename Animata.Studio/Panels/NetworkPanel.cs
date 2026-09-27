using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Animata.Core.Brains;
using Animata.Core.Brains.Modules;
using Animata.Core.Brains.Neural;
using Animata.Core.Entities;
using Animata.Core.Training;
using Animata.Studio.Controls;
using Animata.Studio.Kit;
using Animata.Studio.Navigation;
using Animata.Studio.Session;
using Animata.Studio.Theme;

namespace Animata.Studio.Panels;

/// <summary>
/// Wnętrze modułu sieci neuronowej: rysunek warstw z aktywacjami na żywo i edycja kształtu z prawej — liczba neuronów
/// w każdej warstwie ukrytej, dodawanie i usuwanie warstw. Wejścia i wyjścia wynikają z wyrażeń i portów modułu
/// (edytowanych w inspektorze grafu), więc tu są tylko do podglądu.
/// Zmiana kształtu: snapshot „przed zmianą sieci”, zostające wagi zostają, nowe połączenia są losowe, trwająca nauka
/// jest wznawiana dla nowego kształtu (<see cref="StudioSession.ReshapeNetwork"/>).
/// </summary>
public sealed class NetworkPanel : StudioPanel
{
    private readonly ActiveEntity _creature;
    private readonly Brain _brain;
    private readonly NeuralNetworkModule _module;
    private readonly Border _sideHost = new();
    private NetworkView? _view;
    private TextBlock? _shape;
    private TextBlock? _neuronValue;
    private string _lastShape = string.Empty;
    private bool _lastTraining;

    public NetworkPanel(StudioSession aSession, ActiveEntity aCreature, NeuralNetworkModule aModule)
        : base(aSession, GraphCanvas.TitleOf(aModule))
    {
        _creature = aCreature;
        _brain = aCreature.Brain!;
        _module = aModule;
    }

    private NeuralNetwork Network => _module.Network;
    private int HiddenCount => Network.Layers.Count - 2;

    protected override Control Build()
    {
        _view = new NetworkView(_module);
        _view.SelectionChanged += BuildSide;

        _shape = Ui.MonoText(string.Empty, 12.5, "Studio.Text2");
        var right = Ui.HStack(8, _shape, Ui.Separator(),
            Ui.Button("Snapshot", () => Session.SaveSnapshot(_brain), Icons.Camera, aShortcut: "Ctrl+S"));

        var hint = Ui.Text("Klik w neuron — jego wagi · klik w kolumnę — wybór warstwy · + / − neurony · Ins warstwa · Del usuń warstwę",
            12, "Studio.Text3");
        hint.Margin = new Thickness(16, 0);
        hint.VerticalAlignment = VerticalAlignment.Center;
        var hintBar = new Border { Child = hint, Height = 40, BorderThickness = new Thickness(0, 0, 0, 1) };
        hintBar.Res(Border.BackgroundProperty, "Studio.Layer");
        hintBar.Res(Border.BorderBrushProperty, "Studio.Stroke");

        var center = new DockPanel();
        DockPanel.SetDock(hintBar, Dock.Top);
        center.Children.Add(hintBar);
        center.Children.Add(_view);

        var side = PanelFrame.Side(_sideHost, false, 330);
        var body = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        body.Children.Add(center);
        Grid.SetColumn(side, 1);
        body.Children.Add(side);

        if (HiddenCount > 0)
            _view.Select(1);
        else
            BuildSide();
        return PanelFrame.Create(this, body, Ui.Chip("sieć · NeuralNetworkModule"), aRight: right);
    }

    // ---------- zmiany kształtu ----------

    private void Reshape(Action<NeuralNetwork> aChange, int? aSelectLayer = null)
    {
        Session.ReshapeNetwork(_creature, _module, aChange);
        if (_view is null)
            return;
        _view.Clamp();
        if (aSelectLayer is { } layer && layer >= 1 && layer <= HiddenCount)
            _view.Select(layer);
        else
            BuildSide();
    }

    private void ResizeLayer(int aLayer, int aSize)
    {
        var size = Math.Clamp(aSize, NeuralNetwork.MinLayerSize, NeuralNetwork.MaxLayerSize);
        if (size != Network.Layers[aLayer])
            Reshape(aNetwork => aNetwork.ResizeLayer(aLayer, size), aLayer);
    }

    /// <summary>Nowa warstwa za wybraną (albo przed wyjściami), tak szeroka jak warstwa przed nią.</summary>
    private void InsertLayer()
    {
        if (HiddenCount >= NeuralNetwork.MaxHiddenLayers)
            return;
        var after = _view?.SelectedLayer is { } layer && layer < Network.Layers.Count - 1 ? layer : Network.Layers.Count - 2;
        var index = after + 1;
        var size = Math.Clamp(Network.Layers[after], NeuralNetwork.MinLayerSize, Math.Min(16, NeuralNetwork.MaxLayerSize));
        Reshape(aNetwork => aNetwork.InsertLayer(index, size), index);
    }

    private void RemoveLayer(int aLayer) =>
        Reshape(aNetwork => aNetwork.RemoveLayer(aLayer), Math.Min(aLayer, HiddenCount - 1));

    // ---------- panel boczny ----------

    private void BuildSide()
    {
        var layers = Network.Layers;
        var selectedLayer = _view?.SelectedLayer;
        var panel = Ui.VStack(18);
        panel.Margin = new Thickness(16);

        // Kształt: wejścia, warstwy ukryte z edycją, wyjścia.
        var shape = Ui.VStack(6, Ui.HStack(8, Ui.IconColored(Icons.Neural, StudioPalette.Neural, 16), Ui.Header("Kształt sieci")));
        shape.Children.Add(LayerRow("Wejścia", layers[0], "z wyrażeń wejść", null, false));
        for (var layer = 1; layer < layers.Count - 1; layer++)
            shape.Children.Add(LayerRow($"Ukryta {layer}", layers[layer], null, layer, selectedLayer == layer));
        shape.Children.Add(LayerRow("Wyjścia", layers[^1], "z portów wyjść", null, false));

        var add = Ui.Button(selectedLayer is { } chosen && chosen < layers.Count - 1
                ? $"Dodaj warstwę za „{(chosen == 0 ? "wejścia" : $"ukryta {chosen}")}”"
                : "Dodaj warstwę ukrytą",
            InsertLayer, Icons.Plus, aShortcut: "Ins");
        add.HorizontalAlignment = HorizontalAlignment.Stretch;
        add.HorizontalContentAlignment = HorizontalAlignment.Center;
        add.IsEnabled = HiddenCount < NeuralNetwork.MaxHiddenLayers;
        shape.Children.Add(add);
        panel.Children.Add(shape);

        var stats = Ui.VStack(2,
            Ui.Row("Parametry", _module.ParameterCount.ToString(), true),
            Ui.Row("Aktywacja", "tanh", true),
            Ui.Row("Limity", $"{NeuralNetwork.MinLayerSize}–{NeuralNetwork.MaxLayerSize} neuronów, ≤ {NeuralNetwork.MaxHiddenLayers} warstw", true));
        panel.Children.Add(stats);

        var training = Session.IsTraining(_creature) && TrainingController.FindTrainable(_creature) == _module;
        var note = Ui.Text(training
                ? "Uczy się. Zmiana kształtu wznawia naukę od nowych wag — zostające wagi zostają, nowe połączenia są losowe."
                : "Zostające wagi zostają, nowe połączenia są losowe — po zmianie warto douczyć. Stary kształt wraca z snapshotu „przed zmianą sieci”.",
            12, training ? "Studio.Accent" : "Studio.Text3");
        note.TextWrapping = TextWrapping.Wrap;
        panel.Children.Add(note);

        // Zaznaczony neuron: wartość, bias i wagi wejściowe.
        if (_view?.Selected is { } selected && selected.Layer < layers.Count && selected.Neuron < layers[selected.Layer])
            panel.Children.Add(NeuronSection(selected.Layer, selected.Neuron));

        _sideHost.Child = Ui.Scroll(panel);
    }

    private Control LayerRow(string aName, int aSize, string? aFixedNote, int? aLayer, bool aSelected)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), MinHeight = 36 };
        var name = Ui.Text(aName, 13, aSelected ? "Studio.Accent" : "Studio.Text", aSelected ? FontWeight.SemiBold : FontWeight.Normal);
        name.VerticalAlignment = VerticalAlignment.Center;
        grid.Children.Add(name);

        Control value;
        if (aLayer is not { } layer)
        {
            var text = Ui.MonoText(aSize.ToString(), 12.5);
            var sub = Ui.Text(aFixedNote ?? string.Empty, 11, "Studio.Text3");
            value = Ui.HStack(8, sub, text);
            value.VerticalAlignment = VerticalAlignment.Center;
        }
        else
        {
            var field = Ui.Field(aSize.ToString(), aText =>
            {
                if (!int.TryParse(aText.Trim(), out var size) || size < NeuralNetwork.MinLayerSize || size > NeuralNetwork.MaxLayerSize)
                    return false;
                Dispatcher.UIThread.Post(() => ResizeLayer(layer, size));
                return true;
            }, 52);
            field.HorizontalContentAlignment = HorizontalAlignment.Center;
            var minus = Ui.IconButton(Icons.Minus, "Jeden neuron mniej (−)", () => ResizeLayer(layer, Network.Layers[layer] - 1));
            minus.IsEnabled = aSize > NeuralNetwork.MinLayerSize;
            var plus = Ui.IconButton(Icons.Plus, "Jeden neuron więcej (+)", () => ResizeLayer(layer, Network.Layers[layer] + 1));
            plus.IsEnabled = aSize < NeuralNetwork.MaxLayerSize;
            var remove = Ui.IconButton(Icons.Trash, "Usuń warstwę (Del)", () => RemoveLayer(layer));
            value = Ui.HStack(2, minus, field, plus, remove);
        }
        Grid.SetColumn(value, 1);
        grid.Children.Add(value);

        var row = new Border
        {
            Child = grid,
            Padding = new Thickness(10, 2, 4, 2),
            CornerRadius = new CornerRadius(6),
            Background = aSelected ? Ui.Brush(StudioTheme.Palette.AccentSoft) : Brushes.Transparent
        };
        if (aLayer is { } clickable)
        {
            row.Cursor = new Cursor(StandardCursorType.Hand);
            row.PointerPressed += (_, aEvent) =>
            {
                if (aEvent.Source is Visual source && source.FindAncestorOfType<TextBox>(true) is null &&
                    source.FindAncestorOfType<Button>(true) is null)
                    _view?.Select(clickable);
            };
        }
        return row;
    }

    private Control NeuronSection(int aLayer, int aNeuron)
    {
        var layers = Network.Layers;
        var title = aLayer == 0 ? $"Wejście {aNeuron}" : aLayer == layers.Count - 1 ? $"Wyjście {aNeuron}" : $"Neuron {aNeuron} · ukryta {aLayer}";
        _neuronValue = Ui.MonoText(string.Empty, 12.5);
        var section = Ui.VStack(2, Ui.Header(title), Ui.Row("Aktywacja", _neuronValue));
        if (aLayer == 0 && aNeuron < _module.Inputs.Count)
            section.Children.Add(Ui.Row("Wyrażenie", _module.Inputs[aNeuron].Expression, true));
        if (aLayer == layers.Count - 1 && aNeuron < _module.Outputs.Count)
        {
            var output = _module.Outputs[aNeuron];
            section.Children.Add(Ui.Row("Port", $"{output.Port} = tanh · {output.Scale:0.##} + {output.Offset:0.##}", true));
        }
        if (aLayer > 0)
        {
            section.Children.Add(Ui.Row("Bias", Ui.F(Network.Biases[aLayer - 1][aNeuron], 3), true));
            var weights = Network.Weights[aLayer - 1][aNeuron];
            var list = Ui.VStack(0);
            for (var source = 0; source < weights.Length; source++)
            {
                var from = aLayer == 1 && source < _module.Inputs.Count ? _module.Inputs[source].Expression : $"#{source}";
                var weight = weights[source];
                var value = Ui.MonoText(Ui.F(weight, 3), 12);
                value.Foreground = Ui.Brush(weight >= 0 ? Color.FromRgb(96, 205, 255) : Color.FromRgb(255, 150, 70));
                var row = Ui.Row(from.Length > 26 ? from[..25] + "…" : from, value, 22);
                list.Children.Add(row);
            }
            section.Children.Add(Ui.Header($"Wagi wejściowe · {weights.Length}"));
            section.Children.Add(list);
        }
        return section;
    }

    // ---------- odświeżanie i klawisze ----------

    public override void Refresh(float aDelta)
    {
        _view?.InvalidateVisual();
        var shape = string.Join(" → ", Network.Layers);
        if (_shape is not null)
            _shape.Text = shape;
        if (_neuronValue is not null && _view?.Selected is { } selected && selected.Layer < Network.Layers.Count &&
            selected.Neuron < Network.Layers[selected.Layer])
            _neuronValue.Text = Ui.F(Network.Activations(selected.Layer)[selected.Neuron], 3);

        // Kształt mógł się zmienić z zewnątrz (liczba wąsów, snapshot) albo nauka ruszyła/stanęła: przebuduj bok.
        var training = Session.IsTraining(_creature);
        if (shape != _lastShape || training != _lastTraining)
        {
            _lastShape = shape;
            _lastTraining = training;
            _view?.Clamp();
            BuildSide();
        }
    }

    public override bool HandleKey(KeyEventArgs aEvent)
    {
        if (aEvent.Source is TextBox)
            return false;
        var layer = _view?.SelectedLayer is { } chosen && chosen >= 1 && chosen <= HiddenCount ? chosen : (int?)null;
        switch (aEvent.Key)
        {
            case Key.OemPlus or Key.Add when layer is { } grow:
                ResizeLayer(grow, Network.Layers[grow] + 1);
                return true;
            case Key.OemMinus or Key.Subtract when layer is { } shrink:
                ResizeLayer(shrink, Network.Layers[shrink] - 1);
                return true;
            case Key.Insert:
                InsertLayer();
                return true;
            case Key.Delete when layer is { } removed:
                RemoveLayer(removed);
                return true;
            case Key.S when (aEvent.KeyModifiers & KeyModifiers.Control) != 0:
                Session.SaveSnapshot(_brain);
                return true;
            case Key.Space:
                Session.TogglePause();
                return true;
            case Key.Escape when _view?.Selected is not null:
                _view.Select(_view.SelectedLayer);
                return true;
            default:
                return false;
        }
    }
}
