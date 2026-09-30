using System.Numerics;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;
using Animata.Core.Entities;
using Animata.Studio.Kit;
using Animata.Studio.Session;

namespace Animata.Studio.Panels;

/// <summary>
/// Edytor ustawień zbudowany z ich opisu (<see cref="SettingAttribute"/>): ten sam we właściwościach sceny, w kartach
/// panelu stwora i w inspektorze węzła ciała. Nowy stwór, zmysł czy napęd dostaje edytor bez zmian w Studiu — wystarczy,
/// że jego ustawienia są opisane. Rodzaj pola wynika z typu: liczba (z zakresem i jednostką; lista, gdy ma krok), przełącznik,
/// kolor, wektor, wskazanie obiektu sceny (Guid?), flagi, lista wartości wyliczenia; tablica — tylko do odczytu.
/// Każda zmiana idzie przez <see cref="StudioSession.ChangeSetting"/> (zmiana kształtu ciała — przy zatrzymanej nauce).
/// </summary>
public static class SettingsEditor
{
    /// <summary>
    /// Wiersze ustawień <paramref name="aOwner"/> (encja, zmysł albo napęd należący do <paramref name="aEntity"/>).
    /// <paramref name="aChanged"/> — po udanej zmianie, już poza obsługą zdarzenia kontrolki (np. przebudowa panelu).
    /// </summary>
    public static List<Control> Rows(StudioSession aSession, Entity aEntity, object aOwner, Func<SettingInfo, bool>? aWhich = null,
        Action<SettingInfo>? aChanged = null)
    {
        var rows = new List<Control>();
        foreach (var setting in Settings.Of(aOwner))
            if (aWhich?.Invoke(setting) ?? true)
                rows.Add(Row(aSession, aEntity, aOwner, setting, aChanged));
        return rows;
    }

    /// <summary>
    /// Wiersze slotu stwora: najpierw ustawienia stwora, które dotyczą slotu (np. liczba wąsów przy wąsach, segmenty przy
    /// kręgosłupie), potem ustawienia samego zmysłu albo napędu.
    /// </summary>
    public static List<Control> SlotRows(StudioSession aSession, ActiveEntity aCreature, string aSlot, Action<SettingInfo>? aChanged = null)
    {
        var rows = Rows(aSession, aCreature, aCreature, aSetting => aSetting.Concerns(aSlot), aChanged);
        if (((object?)aCreature.Body.FindSensor(aSlot) ?? aCreature.Body.FindActuator(aSlot)) is { } slot)
            rows.AddRange(Rows(aSession, aCreature, slot, null, aChanged));
        return rows;
    }

    /// <summary>Nagłówek i wiersze albo null, gdy nie ma czego edytować.</summary>
    public static Control? Section(string aHeader, List<Control> aRows)
    {
        if (aRows.Count == 0)
            return null;
        var section = Ui.VStack(2, Ui.Header(aHeader));
        foreach (var row in aRows)
            section.Children.Add(row);
        return section;
    }

    private static Control Row(StudioSession aSession, Entity aEntity, object aOwner, SettingInfo aSetting, Action<SettingInfo>? aChanged)
    {
        bool Change(object? aValue)
        {
            if (!aSession.ChangeSetting(aEntity, aOwner, aSetting, aValue))
                return false;
            if (aChanged is not null)
                Dispatcher.UIThread.Post(() => aChanged(aSetting));
            return true;
        }

        var unit = aSetting.Attribute.Unit;
        var label = unit.Length > 0 ? $"{aSetting.Label} [{unit}]" : aSetting.Label;
        var control = Editor(aSession, aEntity, aOwner, aSetting, Change);
        if (aSetting.Attribute.Tip.Length > 0)
            ToolTip.SetTip(control, aSetting.Attribute.Tip);
        // Klik w pole nie może przejść do karty pod nim (karty w panelu stwora po kliknięciu wjeżdżają do grafu).
        control.Tapped += (_, aEvent) => aEvent.Handled = true;
        return Ui.Row(label, control, 34);
    }

    private static Control Editor(StudioSession aSession, Entity aEntity, object aOwner, SettingInfo aSetting, Func<object?, bool> aChange)
    {
        var type = aSetting.Type;
        if (aSetting.Choices() is { } choices)
            return Choice(choices.Cast<object>().ToList(), aSetting.Get(aOwner), aChange);
        if (type == typeof(float) || type == typeof(int) || type == typeof(double))
        {
            var digits = type == typeof(int) ? 0 : Math.Abs(aSetting.GetNumber(aOwner)) is > 0 and < 0.1 ? 3 : 2;
            return Ui.Field(Ui.F((float)aSetting.GetNumber(aOwner), digits),
                aText => Ui.TryParse(aText, out var value) && aChange((double)value), 96);
        }
        if (type == typeof(bool))
        {
            var check = new CheckBox { IsChecked = (bool)aSetting.Get(aOwner)! };
            check.IsCheckedChanged += (_, _) =>
            {
                if (!aChange(check.IsChecked == true))
                    check.IsChecked = (bool)aSetting.Get(aOwner)!;
            };
            return check;
        }
        if (type == typeof(Vector3) && aSetting.Attribute.Color)
            return PanelParts.ColorPicker(() => (Vector3)aSetting.Get(aOwner)!, aColor => aChange(aColor));
        if (type == typeof(Vector3))
            return Vector(aOwner, aSetting, aChange);
        if (type == typeof(Guid?))
            return PanelParts.EntityPicker(aSession, aEntity, () => (Guid?)aSetting.Get(aOwner), aId => aChange(aId));
        if (type.IsEnum && type.IsDefined(typeof(FlagsAttribute), false))
            return Flags(aOwner, aSetting, aChange);
        if (type.IsEnum)
            return Choice(Enum.GetValues(type).Cast<object>().ToList(), aSetting.Get(aOwner), aChange);
        if (type == typeof(float[]))
            return Ui.MonoText(string.Join(" ", ((float[])aSetting.Get(aOwner)!).Select(aValue =>
                (aValue * aSetting.Attribute.Scale).ToString("+0.#;−0.#;0", System.Globalization.CultureInfo.InvariantCulture))), 12);
        return Ui.MonoText(aSetting.Get(aOwner)?.ToString() ?? "—", 12);
    }

    /// <summary>Lista wartości; zmiana po zamknięciu listy (reakcja może przebudować panel razem z tą listą).</summary>
    private static ComboBox Choice(List<object> aValues, object? aCurrent, Func<object?, bool> aChange)
    {
        var picker = new ComboBox { ItemsSource = aValues, SelectedItem = aValues.FirstOrDefault(aValue => Equals(aValue, aCurrent)), MinWidth = 96 };
        var current = aCurrent;
        picker.SelectionChanged += (_, _) =>
        {
            var chosen = picker.SelectedItem;
            if (chosen is null || Equals(chosen, current))
                return;
            Dispatcher.UIThread.Post(() =>
            {
                if (aChange(chosen))
                    current = chosen;
                else
                    picker.SelectedItem = aValues.FirstOrDefault(aValue => Equals(aValue, current));
            });
        };
        return picker;
    }

    /// <summary>Wektor jako trzy liczby (np. wymiary klocka: szerokość, głębokość, grubość).</summary>
    private static Control Vector(object aOwner, SettingInfo aSetting, Func<object?, bool> aChange)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        for (var axis = 0; axis < 3; axis++)
        {
            var index = axis;
            var value = (Vector3)aSetting.Get(aOwner)!;
            var component = index switch { 0 => value.X, 1 => value.Y, _ => value.Z };
            row.Children.Add(Ui.Field(Ui.F(component, component is > 0 and < 0.1f ? 3 : 2), aText =>
            {
                if (!Ui.TryParse(aText, out var number))
                    return false;
                var current = (Vector3)aSetting.Get(aOwner)!;
                return aChange(index switch
                {
                    0 => current with { X = number },
                    1 => current with { Y = number },
                    _ => current with { Z = number }
                });
            }, 60));
        }
        return row;
    }

    /// <summary>Flagi jako przełączniki (np. co wykrywają wąsy).</summary>
    private static Control Flags(object aOwner, SettingInfo aSetting, Func<object?, bool> aChange)
    {
        var type = aSetting.Type;
        var row = new WrapPanel { Orientation = Orientation.Horizontal, MaxWidth = 220 };
        foreach (var flag in Enum.GetValues(type).Cast<Enum>().Where(aFlag => Convert.ToInt64(aFlag) is var mask && mask != 0 && (mask & (mask - 1)) == 0))
        {
            var bits = Convert.ToInt64(flag);
            var check = new CheckBox
            {
                IsChecked = (Convert.ToInt64(aSetting.Get(aOwner)) & bits) != 0,
                Content = Ui.Text(FlagName(flag), 12.5),
                Margin = new Avalonia.Thickness(0, 0, 8, 0)
            };
            check.IsCheckedChanged += (_, _) =>
            {
                var current = Convert.ToInt64(aSetting.Get(aOwner));
                var next = check.IsChecked == true ? current | bits : current & ~bits;
                if (!aChange(Enum.ToObject(type, next)))
                    check.IsChecked = (Convert.ToInt64(aSetting.Get(aOwner)) & bits) != 0;
            };
            row.Children.Add(check);
        }
        return row;
    }

    private static string FlagName(Enum aFlag) => aFlag switch
    {
        EntityCategory.Creature => "stwory",
        EntityCategory.Obstacle => "przeszkody",
        EntityCategory.Target => "cele",
        EntityCategory.Ground => "podłoże",
        _ => aFlag.ToString()
    };
}
