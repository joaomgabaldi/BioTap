using System.Text.Json.Nodes;
using BioTap.App.Shell;
using BioTap.Core.Actions;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.Windows.Storage.Pickers;

namespace BioTap.App.Settings;

/// <summary>One parameter editor per preset: a panel and a function that reads the params back. No rules here (they live in the presets).</summary>
static class PresetEditors
{
    public static (UIElement Panel, Func<JsonObject> Read) Build(string preset, JsonObject p, WindowId windowId)
    {
        string S(string key) => p[key] is JsonValue v && v.TryGetValue<string>(out var s) ? s : "";
        bool B(string key, bool fallback) => p[key] is JsonValue v && v.TryGetValue<bool>(out var b) ? b : fallback;

        return preset switch
        {
            "runCommand" => RunCommand(S, B),
            "shortcut" => Shortcut(S, B),
            "volumeUp" or "volumeDown" => VolumeStep(p),
            "typeText" => TypeText(S("text")),
            "openTarget" => OpenTarget(S, windowId),
            _ => (new StackPanel(), () => new JsonObject()), // copy, paste, mute, playPause… have no params
        };
    }

    static (UIElement, Func<JsonObject>) RunCommand(Func<string, string> S, Func<string, bool, bool> B)
    {
        var shell = new ComboBox { Header = "Shell", Items = { "powershell", "cmd" }, SelectedItem = S("shell") == "cmd" ? "cmd" : "powershell" };
        var command = new TextBox { Header = "Comando", Text = S("command"), AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 90, FontFamily = new FontFamily("Cascadia Mono, Consolas") };
        var show = new ToggleSwitch { Header = "Mostrar janela", IsOn = B("showWindow", true) };
        var admin = new ToggleSwitch { Header = "Executar como administrador", IsOn = B("runAsAdmin", false) };
        return (new StackPanel { Spacing = 12, Children = { shell, command, show, admin } }, () => new JsonObject
        {
            ["shell"] = (string)shell.SelectedItem, ["command"] = command.Text, ["showWindow"] = show.IsOn, ["runAsAdmin"] = admin.IsOn,
        });
    }

    static (UIElement, Func<JsonObject>) OpenTarget(Func<string, string> S, WindowId windowId)
    {
        var target = new TextBox { Header = "Programa, arquivo ou URL", Text = S("target"), PlaceholderText = @"C:\Program Files\...\app.exe  ou  https://..." };
        var browse = new Button { Content = "Procurar…" };
        browse.Click += (_, _) => Ui.RunAsync(async () =>
        {
            // Windows App SDK picker: unlike the WinRT one, it works in an elevated app
            try
            {
                var picker = new FileOpenPicker(windowId) { ViewMode = PickerViewMode.List };
                picker.FileTypeFilter.Add("*");
                if (await picker.PickSingleFileAsync() is { } file) target.Text = file.Path;
            }
            catch (Exception e)
            {
                Program.Log.Write("ERROR", "File picker: " + e.GetType().Name);
            }
        }, "file picker");
        var args = new TextBox { Header = "Argumentos (opcional)", Text = S("args") };
        return (new StackPanel { Spacing = 12, Children = { new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { target, browse } }, args } }, () =>
        {
            var o = new JsonObject { ["target"] = target.Text.Trim() };
            if (!string.IsNullOrWhiteSpace(args.Text)) o["args"] = args.Text;
            return o;
        });
    }

    static (UIElement, Func<JsonObject>) Shortcut(Func<string, string> S, Func<string, bool, bool> B)
    {
        var ctrl = new CheckBox { Content = "Ctrl", IsChecked = B("ctrl", false) };
        var alt = new CheckBox { Content = "Alt", IsChecked = B("alt", false) };
        var shift = new CheckBox { Content = "Shift", IsChecked = B("shift", false) };
        var win = new CheckBox { Content = "Win", IsChecked = B("win", false) };
        var key = new ComboBox { Header = "Tecla", MinWidth = 160 };
        foreach (var name in ShortcutKeys.All.Keys) key.Items.Add(name);
        key.SelectedItem = ShortcutKeys.All.ContainsKey(S("key")) ? S("key") : null;
        var preview = new TextBlock { Opacity = 0.7 };

        void Refresh()
        {
            var parts = new List<string>();
            if (ctrl.IsChecked == true) parts.Add("Ctrl");
            if (shift.IsChecked == true) parts.Add("Shift");
            if (alt.IsChecked == true) parts.Add("Alt");
            if (win.IsChecked == true) parts.Add("Win");
            if (key.SelectedItem is string k) parts.Add(k);
            preview.Text = string.Join(" + ", parts);
        }
        foreach (var c in new[] { ctrl, alt, shift, win })
        {
            c.Checked += (_, _) => Ui.Run(Refresh, "shortcut preview");
            c.Unchecked += (_, _) => Ui.Run(Refresh, "shortcut preview");
        }
        key.SelectionChanged += (_, _) => Ui.Run(Refresh, "shortcut preview");
        Refresh();

        var mods = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { ctrl, alt, shift, win } };
        return (new StackPanel { Spacing = 12, Children = { mods, key, preview } }, () =>
        {
            var o = new JsonObject { ["ctrl"] = ctrl.IsChecked == true, ["alt"] = alt.IsChecked == true, ["shift"] = shift.IsChecked == true, ["win"] = win.IsChecked == true };
            if (key.SelectedItem is string k) o["key"] = k;
            return o;
        });
    }

    static (UIElement, Func<JsonObject>) VolumeStep(JsonObject p)
    {
        var current = p["step"] is JsonValue v && v.TryGetValue<int>(out var s) ? s : 10;
        var step = new NumberBox { Header = "Passo do volume (%)", Minimum = 2, Maximum = 50, Value = current, SmallChange = 2, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline, MaxWidth = 360, HorizontalAlignment = HorizontalAlignment.Left };
        return (step, () => double.IsNaN(step.Value) ? new JsonObject() : new JsonObject { ["step"] = (int)Math.Round(step.Value) });
    }

    static (UIElement, Func<JsonObject>) TypeText(string text)
    {
        var box = new TextBox { Header = "Texto", Text = text, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 120, MaxLength = 10_000 };
        var counter = new TextBlock { Opacity = 0.7 };
        void Refresh() => counter.Text = $"{box.Text.Length} / 10.000";
        box.TextChanged += (_, _) => Ui.Run(Refresh, "text counter");
        Refresh();
        return (new StackPanel { Spacing = 4, Children = { box, counter } }, () => new JsonObject { ["text"] = box.Text });
    }
}
