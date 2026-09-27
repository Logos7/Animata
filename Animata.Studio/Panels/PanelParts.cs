using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Animata.Core.Entities;
using Animata.Core.Training;
using Animata.Studio.Controls;
using Animata.Studio.Kit;
using Animata.Studio.Session;

namespace Animata.Studio.Panels;

/// <summary>Kawałki UI wspólne dla kilku paneli.</summary>
public static class PanelParts
{
    /// <summary>
    /// Karta nauki stwora: pokolenie, mistrz, wykres, stop/wznów, losowanie. Odświeżanie dopisuje do <paramref name="aUpdaters"/>.
    /// </summary>
    public static Control TrainingCard(StudioSession aSession, ActiveEntity aCreature, List<Action> aUpdaters)
    {
        var brain = aCreature.Brain!;
        var generation = Ui.MonoText(string.Empty, 11.5);
        var champion = Ui.MonoText(string.Empty, 12);
        var sparkline = new Sparkline { Height = 56 };
        var toggleLabel = Ui.Text("Zatrzymaj", 13);
        var toggle = new Button
        {
            Content = toggleLabel,
            Height = 32,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            Focusable = false
        };
        toggle.Click += (_, _) => aSession.ToggleTraining([aCreature]);
        var randomize = Ui.Button("Losuj od zera", () => aSession.Randomize([aCreature]));
        randomize.HorizontalAlignment = HorizontalAlignment.Stretch;
        randomize.HorizontalContentAlignment = HorizontalAlignment.Center;
        var buttons = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), ColumnSpacing = 8 };
        buttons.Children.Add(toggle);
        Grid.SetColumn(randomize, 1);
        buttons.Children.Add(randomize);

        var head = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        head.Children.Add(Ui.Header("Nauka"));
        Grid.SetColumn(generation, 1);
        head.Children.Add(generation);

        var legend = Ui.Text("szary: najlepszy w pokoleniu · akcent: mistrz na stałych trasach", 11, "Studio.Text3");
        legend.TextWrapping = Avalonia.Media.TextWrapping.Wrap;
        legend.TextTrimming = Avalonia.Media.TextTrimming.None;

        aUpdaters.Add(() =>
        {
            var progress = aSession.Training.ProgressOf(brain);
            generation.Text = progress is not null ? $"gen {progress.Generation}" : "stoi";
            champion.Text = progress is not null ? $"gen {progress.ChampionGeneration} · {Ui.F(progress.ChampionScore, 3)}" : "—";
            toggleLabel.Text = aSession.IsTraining(aCreature) ? "Zatrzymaj" : "Wznów";
            sparkline.Points = aSession.ProgressHistory(brain);
        });
        return Ui.Card(Ui.VStack(10, head, sparkline, legend, Ui.Row("Mistrz", champion), buttons), 14);
    }

    public static bool HasNetwork(ActiveEntity aCreature) => TrainingController.FindNetwork(aCreature) is not null;
}
