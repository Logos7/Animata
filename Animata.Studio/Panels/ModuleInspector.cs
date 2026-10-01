using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Animata.Core.Actuators;
using Animata.Core.Brains;
using Animata.Core.Brains.Modules;
using Animata.Core.Entities;
using Animata.Core.Sensors;
using Animata.Core.WorldObjects;
using Animata.Studio.Controls;
using Animata.Studio.Kit;
using Animata.Studio.Session;

namespace Animata.Studio.Panels;

/// <summary>
/// Inspektor zaznaczonego węzła grafu: nazwa, porty z wartościami na żywo i edycja tego, co moduł ma do edycji —
/// parametry (dowolny rekord stanu z polami float), wyrażenia i wagi sieci, porty podgrafu.
/// Po każdej zmianie woła <see cref="Changed"/>, a błędy zgłasza przez <see cref="Message"/>.
/// </summary>
public sealed class ModuleInspector
{
    private readonly StudioSession _session;
    private readonly ActiveEntity _creature;
    private readonly BrainGraph _graph;
    private readonly CompositeModule? _owner;
    private readonly BrainGraph? _ownerParent;
    private readonly List<Action> _updaters = [];
    private float _slowClock;

    public ModuleInspector(StudioSession aSession, ActiveEntity aCreature, BrainGraph aGraph, CompositeModule? aOwner, BrainGraph? aOwnerParent)
    {
        _session = aSession;
        _creature = aCreature;
        _graph = aGraph;
        _owner = aOwner;
        _ownerParent = aOwnerParent;
    }

    public event Action? Changed;
    public event Action<string>? Message;
    public event Action? DeleteRequested;
    public event Action<BrainModule>? EnterRequested;
    public event Action? UngroupRequested;

    public void Refresh(float aDelta)
    {
        _slowClock += aDelta;
        foreach (var update in _updaters)
            update();
    }

    /// <summary>Zawartość inspektora dla węzła, połączenia albo — bez zaznaczenia — całego grafu.</summary>
    public Control Build(BrainModule? aModule, BrainConnection? aLink, int aSelected)
    {
        _updaters.Clear();
        var panel = new StackPanel { Spacing = 18, Margin = new Thickness(18) };
        if (aModule is not null)
            BuildModule(panel, aModule, aSelected);
        else if (aLink is not null)
            BuildLink(panel, aLink);
        else
            BuildGraph(panel);
        return Ui.Scroll(panel);
    }

    // ---------- bez zaznaczenia ----------

    private void BuildGraph(StackPanel aPanel)
    {
        var title = _owner is not null ? GraphCanvas.TitleOf(_owner) : "Mózg";
        aPanel.Children.Add(Ui.VStack(4, Ui.Header("Inspektor"), Ui.Text(title, 18, "Studio.Text", FontWeight.SemiBold),
            Ui.MonoText(_owner is not null ? "CompositeModule · wnętrze" : "BrainGraph", 11.5, "Studio.Text3")));
        var modules = Ui.MonoText(string.Empty, 12.5);
        var links = Ui.MonoText(string.Empty, 12.5);
        _updaters.Add(() =>
        {
            modules.Text = _graph.Modules.Count.ToString();
            links.Text = _graph.Connections.Count.ToString();
        });
        aPanel.Children.Add(Ui.VStack(2, Ui.Row("Moduły", modules), Ui.Row("Połączenia", links)));
        aPanel.Children.Add(Tips(
            "Przeciągnij moduł z palety na płótno (albo kliknij, żeby dodać na środku).",
            "Połącz: przeciągnij z kółka wyjścia na kółko wejścia. Chwycenie zajętego wejścia odpina drut.",
            "Ramka albo Ctrl+klik zaznacza kilka; Ctrl+G grupuje je w podgraf, dwuklik podgrafu wjeżdża do środka.",
            "Del usuwa, F dopasowuje widok, kółko myszy przybliża, PPM przesuwa."));
    }

    private static Control Tips(params string[] aLines)
    {
        var stack = Ui.VStack(8, Ui.Header("Jak edytować"));
        foreach (var line in aLines)
        {
            var text = Ui.Text(line, 12.5, "Studio.Text2");
            text.TextWrapping = TextWrapping.Wrap;
            text.TextTrimming = TextTrimming.None;
            stack.Children.Add(text);
        }
        return Ui.Card(stack, 14);
    }

    // ---------- połączenie ----------

    private void BuildLink(StackPanel aPanel, BrainConnection aLink)
    {
        var source = _graph.Find(aLink.SourceId);
        var target = _graph.Find(aLink.TargetId);
        aPanel.Children.Add(Ui.VStack(4, Ui.Header("Inspektor"), Ui.Text("Połączenie", 18, "Studio.Text", FontWeight.SemiBold)));
        var value = Ui.MonoText("—", 12.5);
        _updaters.Add(() => value.Text = source is not null && _graph.LastOutputs(source) is { } outputs && outputs.TryGetValue(aLink.SourcePort, out var v)
            ? Ui.F(v, 3) : "—");
        aPanel.Children.Add(Ui.VStack(2,
            Ui.Row("Z", $"{(source is null ? "?" : GraphCanvas.TitleOf(source))}.{aLink.SourcePort}", true),
            Ui.Row("Do", $"{(target is null ? "?" : GraphCanvas.TitleOf(target))}.{aLink.TargetPort}", true),
            Ui.Row("Wartość", value)));
        aPanel.Children.Add(Ui.Button("Rozłącz", () => DeleteRequested?.Invoke(), Icons.Trash, aShortcut: "Del"));
    }

    // ---------- moduł ----------

    private void BuildModule(StackPanel aPanel, BrainModule aModule, int aSelected)
    {
        var boundary = aModule is SubgraphInputModule or SubgraphOutputModule;
        var name = Ui.Field(aModule.Name, aText =>
        {
            aModule.Name = aText.Trim();
            Changed?.Invoke();
            return true;
        }, aMono: false);
        name.FontSize = 16;
        // Węzły zmysłów i napędów to widok ciała — ich nazwą jest slot, nie zmienia się jej tutaj.
        name.IsReadOnly = aModule is SensorModule or ActuatorModule;
        aPanel.Children.Add(Ui.VStack(6,
            Ui.HStack(8, Ui.IconColored(GraphCanvas.IconOf(aModule), GraphCanvas.ColorOf(aModule), 16), Ui.Header("Inspektor")),
            name,
            Ui.MonoText(aModule.GetType().Name + (aSelected > 1 ? $" · zaznaczono {aSelected}" : string.Empty), 11.5, "Studio.Text3")));

        if (aModule is CompositeModule or NeuralNetworkModule)
        {
            var enter = Ui.Button(aModule is CompositeModule ? "Wejdź do podgrafu" : "Wejdź do sieci · neurony i warstwy",
                () => EnterRequested?.Invoke(aModule), Icons.Enter, aAccent: true);
            enter.HorizontalAlignment = HorizontalAlignment.Stretch;
            enter.HorizontalContentAlignment = HorizontalAlignment.Center;
            enter.Height = 36;
            aPanel.Children.Add(enter);
        }

        switch (aModule)
        {
            case SensorModule sensor:
                SensorSection(aPanel, sensor);
                break;
            case ActuatorModule actuator:
                ActuatorSection(aPanel, actuator);
                break;
            case NeuralNetworkModule network:
                NetworkSection(aPanel, network);
                break;
            case CompositeModule composite:
                PortsSection(aPanel, composite, _graph);
                break;
            case SubgraphInputModule or SubgraphOutputModule when _owner is not null && _ownerParent is not null:
                PortsSection(aPanel, _owner, _ownerParent, aModule is SubgraphInputModule, aModule is SubgraphOutputModule);
                break;
            case StateMachineModule machine:
                StateMachineSection(aPanel, machine);
                break;
            case RouterModule router:
                var active = Ui.MonoText("0", 12.5);
                _updaters.Add(() => active.Text = router.ActiveChannel.ToString());
                aPanel.Children.Add(Ui.VStack(2, Ui.Header("Router"), Ui.Row("Kanały", router.Channels.ToString(), true),
                    Ui.Row("Porty", string.Join(", ", router.OutputPorts), true), Ui.Row("Aktywny kanał", active)));
                break;
        }

        StateSection(aPanel, aModule);
        if (!boundary)
            PortValues(aPanel, aModule);

        var actions = Ui.HStack(8);
        if (!boundary)
            actions.Children.Add(Ui.Button("Usuń", () => DeleteRequested?.Invoke(), Icons.Trash, aShortcut: "Del"));
        if (aModule is CompositeModule)
            actions.Children.Add(Ui.Button("Rozgrupuj", () => UngroupRequested?.Invoke(), Icons.Ungroup));
        if (actions.Children.Count > 0)
            aPanel.Children.Add(actions);
    }

    private void PortValues(StackPanel aPanel, BrainModule aModule)
    {
        if (aModule.InputPorts.Count > 0)
        {
            var inputs = Ui.VStack(0, Ui.Header("Wejścia"));
            foreach (var port in aModule.InputPorts)
            {
                var value = Ui.MonoText("—", 12);
                var link = port;
                _updaters.Add(() => value.Text = InputValue(aModule, link) is { } v ? Ui.F(v, 3) : "niepodłączone");
                inputs.Children.Add(Ui.Row(port, value, 24));
            }
            aPanel.Children.Add(inputs);
        }
        if (aModule.OutputPorts.Count > 0)
        {
            var outputs = Ui.VStack(0, Ui.Header("Wyjścia"));
            foreach (var port in aModule.OutputPorts)
            {
                var value = Ui.MonoText("—", 12);
                var name = port;
                _updaters.Add(() => value.Text = _graph.LastOutputs(aModule) is { } values && values.TryGetValue(name, out var v) ? Ui.F(v, 3) : "—");
                outputs.Children.Add(Ui.Row(port, value, 24));
            }
            aPanel.Children.Add(outputs);
        }
    }

    private float? InputValue(BrainModule aModule, string aPort) => _graph.LastInput(aModule, aPort);

    /// <summary>Węzeł zmysłu: typ, slot, porty i ustawienia (zmysłu i stwora, które go dotyczą) z edytora ustawień.</summary>
    private void SensorSection(StackPanel aPanel, SensorModule aModule)
    {
        var sensor = _creature.Body.FindSensor(aModule.Slot);
        var section = Ui.VStack(2, Ui.Header("Sensor"));
        section.Children.Add(Ui.Row("Typ", sensor?.GetType().Name ?? "brak w ciele!", true));
        section.Children.Add(Ui.Row("Slot", aModule.Slot, true));
        section.Children.Add(Ui.Row("Porty", aModule.OutputPorts.Count.ToString(), true));
        foreach (var row in SettingsEditor.SlotRows(_session, _creature, aModule.Slot, _ => Changed?.Invoke()))
            section.Children.Add(row);
        aPanel.Children.Add(section);
    }

    /// <summary>Węzeł napędu: typ, slot, porty i ustawienia (napędu i stwora, które go dotyczą) z edytora ustawień.</summary>
    private void ActuatorSection(StackPanel aPanel, ActuatorModule aModule)
    {
        var actuator = _creature.Body.FindActuator(aModule.Slot);
        var section = Ui.VStack(2, Ui.Header("Aktuator"));
        section.Children.Add(Ui.Row("Typ", actuator?.GetType().Name ?? "brak w ciele!", true));
        section.Children.Add(Ui.Row("Slot", aModule.Slot, true));
        section.Children.Add(Ui.Row("Porty", aModule.InputPorts.Count.ToString(), true));
        foreach (var row in SettingsEditor.SlotRows(_session, _creature, aModule.Slot, _ => Changed?.Invoke()))
            section.Children.Add(row);
        aPanel.Children.Add(section);
    }

    /// <summary>Automat stanów: bieżący stan i wagi mieszanki (na żywo), przejścia, czasy przejścia i pobytu.</summary>
    private void StateMachineSection(StackPanel aPanel, StateMachineModule aMachine)
    {
        var current = Ui.MonoText(aMachine.CurrentName, 12.5);
        var weights = Ui.MonoText(string.Empty, 12);
        _updaters.Add(() =>
        {
            current.Text = aMachine.CurrentName;
            weights.Text = string.Join("  ", aMachine.States.Select((aState, aIndex) => $"{aState} {aMachine.Weights[aIndex]:0.00}"));
        });
        var section = Ui.VStack(2, Ui.Header("Automat stanów"), Ui.Row("Stan", current), Ui.Row("Mieszanka", weights),
            Ui.Row("Warunki", aMachine.Conditions.Count > 0 ? string.Join(", ", aMachine.Conditions) : "—", true),
            Ui.Row("Przejście", $"{aMachine.BlendSeconds:0.##} s", true), Ui.Row("Min. pobyt", $"{aMachine.MinDwellSeconds:0.##} s", true));
        foreach (var transition in aMachine.Transitions)
            section.Children.Add(Ui.MonoText(transition.ToString(), 11.5, "Studio.Text2"));
        section.Children.Add(Ui.Text("Wejścia „stan.port” podłącz do modułów, które sterują w danym stanie.", 11.5, "Studio.Text3"));
        aPanel.Children.Add(section);
    }

    private void NetworkSection(StackPanel aPanel, NeuralNetworkModule aModule)
    {
        var network = aModule.Network;
        var section = Ui.VStack(2, Ui.Header("Sieć neuronowa"),
            Ui.Row("Warstwy", string.Join(" → ", network.Layers), true),
            Ui.Row("Parametry", network.ParameterCount.ToString(), true),
            Ui.Row("Aktywacja", "tanh", true));
        if (_creature.Brain is { } brain && _session.Training.IsTraining(brain) && Core.Training.TrainingController.FindTrainable(_creature) == aModule)
        {
            var note = Ui.Text("Uczy się — wagi mistrza podmieniają się same. Zmiana portów albo wyrażeń zaczyna naukę od nowa.", 12, "Studio.Accent");
            note.TextWrapping = TextWrapping.Wrap;
            section.Children.Add(note);
        }
        aPanel.Children.Add(section);

        // Porty modułu = zmienne dostępne w wyrażeniach.
        var ports = Ui.Field(string.Join(", ", aModule.Ports), aText =>
        {
            var names = aText.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
            if (names.Count == 0 || names.Distinct().Count() != names.Count)
                return Fail("Porty muszą być niepuste i unikalne.");
            aModule.Ports.Clear();
            aModule.Ports.AddRange(names);
            _graph.Connections.RemoveAll(aLink => aLink.TargetId == aModule.Id && !names.Contains(aLink.TargetPort));
            _graph.Invalidate();
            Changed?.Invoke();
            return true;
        });
        ports.TextWrapping = TextWrapping.Wrap;
        var portsHint = Ui.Text("Nazwy wejść modułu, oddzielone przecinkami — zmienne w wyrażeniach.", 11.5, "Studio.Text3");
        portsHint.TextWrapping = TextWrapping.Wrap;
        aPanel.Children.Add(Ui.VStack(6, Ui.Header("Porty wejściowe"), ports, portsHint));

        var expressions = Ui.VStack(4, Ui.Header("Wejścia sieci · wyrażenia"));
        for (var index = 0; index < aModule.Inputs.Count; index++)
        {
            var input = aModule.Inputs[index];
            var field = Ui.Field(input.Expression, aText =>
            {
                try
                {
                    input.Expression = aText;
                }
                catch (FormatException exception)
                {
                    return Fail($"Błąd w wyrażeniu: {exception.Message}");
                }
                _graph.Invalidate();
                try
                {
                    _graph.Validate();
                }
                catch (BrainException exception)
                {
                    Message?.Invoke(exception.Message);
                }
                Changed?.Invoke();
                return true;
            });
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("22,*") };
            row.Children.Add(Ui.MonoText(index.ToString(), 11, "Studio.Text3"));
            Grid.SetColumn(field, 1);
            row.Children.Add(field);
            expressions.Children.Add(row);
        }
        aPanel.Children.Add(expressions);

        var outputs = Ui.VStack(2, Ui.Header("Wyjścia sieci"));
        foreach (var output in aModule.Outputs)
            outputs.Children.Add(Ui.Row(output.Port, $"tanh · {output.Scale:0.##} + {output.Offset:0.##}", true));
        aPanel.Children.Add(outputs);

        var maps = Ui.VStack(8);
        var heatmaps = new List<WeightHeatmap>();
        for (var layer = 0; layer < network.Weights.Length; layer++)
        {
            var map = new WeightHeatmap();
            map.Set(network.Weights[layer], network.Biases[layer]);
            heatmaps.Add(map);
            maps.Children.Add(Ui.Header($"Wagi · warstwa {layer + 1} ({network.Layers[layer]} → {network.Layers[layer + 1]})"));
            maps.Children.Add(map);
        }
        var legend = Ui.Text("wiersz = neuron, ostatnia kolumna = bias · niebieski +, pomarańczowy −", 11, "Studio.Text3");
        legend.TextWrapping = TextWrapping.Wrap;
        maps.Children.Add(legend);
        aPanel.Children.Add(maps);
        _updaters.Add(() =>
        {
            if (_slowClock < 0.5f)
                return;
            _slowClock = 0;
            for (var layer = 0; layer < heatmaps.Count && layer < network.Weights.Length; layer++)
                heatmaps[layer].Set(network.Weights[layer], network.Biases[layer]);
        });
    }

    /// <summary>Porty podgrafu: zmiana nazwy, usuwanie, dodawanie — z poprawą połączeń w rodzicu i we wnętrzu.</summary>
    private void PortsSection(StackPanel aPanel, CompositeModule aComposite, BrainGraph aParent, bool aInputs = true, bool aOutputs = true)
    {
        void Side(bool aInput)
        {
            var ports = aInput ? aComposite.InputPorts : aComposite.OutputPorts;
            var stack = Ui.VStack(4, Ui.Header(aInput ? "Wejścia podgrafu" : "Wyjścia podgrafu"));
            foreach (var port in ports.ToList())
            {
                var old = port;
                var field = Ui.Field(port, aText =>
                {
                    try
                    {
                        BrainGraphEditing.RenamePort(aParent, aComposite, aInput, old, aText.Trim());
                    }
                    catch (ArgumentException exception)
                    {
                        return Fail(exception.Message);
                    }
                    Changed?.Invoke();
                    return true;
                });
                var remove = Ui.IconButton(Icons.Trash, "Usuń port (i jego połączenia)", () =>
                {
                    BrainGraphEditing.RemovePort(aParent, aComposite, aInput, old);
                    Changed?.Invoke();
                });
                var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 4 };
                row.Children.Add(field);
                Grid.SetColumn(remove, 1);
                row.Children.Add(remove);
                stack.Children.Add(row);
            }
            stack.Children.Add(Ui.Button(aInput ? "Dodaj wejście" : "Dodaj wyjście", () =>
            {
                BrainGraphEditing.AddPort(aParent, aComposite, aInput, aInput ? "In" : "Out");
                Changed?.Invoke();
            }, Icons.Plus, aGhost: true));
            aPanel.Children.Add(stack);
        }

        if (aInputs)
            Side(true);
        if (aOutputs)
            Side(false);
    }

    /// <summary>
    /// Ogólny edytor parametrów: każde pole float rekordu stanu (ApproachTarget, AvoidAndSeek, Stała…) jako pole liczby.
    /// Zmiana = kopia rekordu z nową wartością i RestoreState — ta sama droga co snapshot.
    /// </summary>
    private void StateSection(StackPanel aPanel, BrainModule aModule)
    {
        if (aModule is NeuralNetworkModule or CompositeModule || aModule.CaptureState() is not { } state)
            return;
        var properties = state.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(aProperty => aProperty.PropertyType == typeof(float) && aProperty.CanWrite)
            .ToList();
        if (properties.Count == 0)
            return;

        var section = Ui.VStack(2, Ui.Header("Parametry"));
        foreach (var property in properties)
        {
            var target = property;
            var field = Ui.Field(Ui.F((float)target.GetValue(state)!, 3), aText =>
            {
                if (!Ui.TryParse(aText, out var value))
                    return Fail("To nie jest liczba.");
                if (aModule.CaptureState() is not { } current)
                    return false;
                var clone = (ModuleState)current.GetType().GetMethod("<Clone>$")!.Invoke(current, null)!;
                target.SetValue(clone, value);
                aModule.RestoreState(clone);
                _graph.Invalidate();
                Changed?.Invoke();
                return true;
            }, 110);
            section.Children.Add(Ui.Row(Spaced(target.Name), field));
        }
        aPanel.Children.Add(section);
    }

    /// <summary>"SteerGain" → "Steer gain".</summary>
    private static string Spaced(string aName)
    {
        var builder = new System.Text.StringBuilder();
        for (var index = 0; index < aName.Length; index++)
        {
            var character = aName[index];
            if (index > 0 && char.IsUpper(character))
                builder.Append(' ').Append(char.ToLowerInvariant(character));
            else
                builder.Append(character);
        }
        return builder.ToString();
    }

    private bool Fail(string aMessage)
    {
        Message?.Invoke(aMessage);
        return false;
    }

    /// <summary>Nowe moduły z palety dla danego stwora.</summary>
    public static IEnumerable<(string Group, string Name, string Icon, Func<BrainModule> Create, string? Disabled)> Palette(ActiveEntity aCreature, Brain aBrain)
    {
        var actuatorPorts = aCreature.Body.Actuators.FirstOrDefault()?.InputPorts.ToArray() ?? ["V"];
        yield return ("Logika", "Sieć neuronowa", Icons.Neural, NewNetwork, null);
        yield return ("Logika", "Router", Icons.Router, () => new RouterModule(2, actuatorPorts) { Name = "Router" }, null);
        yield return ("Logika", "Automat stanów", Icons.States, () => NewStateMachine(aCreature, actuatorPorts), null);
        yield return ("Logika", "Stała", Icons.Constant, () => new ConstantModule("Value", 0) { Name = "Stała" }, null);
        // Sterowniki dopasowane do tego ciała (te same co w menu „Mózg”, ale wstawiane obok istniejących modułów).
        // Gotowce budujące kilka modułów naraz (np. automat stanów z dwiema sieciami) są tylko w menu „Mózg”.
        foreach (var preset in aCreature.BrainPresets.Where(aPreset => aPreset.Build is null))
            yield return ("Dla tego ciała", preset.Name, Icons.Brain, preset.Create, null);

        yield return ("Struktura", "Podgraf", Icons.Composite, () => new CompositeModule { Name = "Podgraf" }, null);
    }

    /// <summary>
    /// Automat dwóch stanów „A”/„B” na portach pierwszego napędu; warunki — Found i Gap z oka, jeśli ciało je ma
    /// (A → B, gdy cel daleko; B → A, gdy blisko). Wejścia stanów „A.port”, „B.port” podpina się do dwóch modułów.
    /// </summary>
    private static StateMachineModule NewStateMachine(ActiveEntity aCreature, string[] aPorts)
    {
        var eye = aCreature.Body.Sensors.Any(aSensor => aSensor.OutputPorts.Contains(TargetSensor.GapPort));
        return eye
            ? HumanoidBrains.StateMachine(aPorts, "A", "B")
            : new StateMachineModule(["A", "B"], [], aPorts) { Name = "Automat" };
    }

    /// <summary>Mała sieć 2-4-2 z losowymi wagami; porty i wyrażenia zmienia się w inspektorze.</summary>
    private static NeuralNetworkModule NewNetwork()
    {
        var module = NeuralNetworkModule.Build("Sieć", ["A", "B"], ["A", "B"], ["Out0", "Out1"], [4]);
        module.Network.Randomize();
        return module;
    }
}
