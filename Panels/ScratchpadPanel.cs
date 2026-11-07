using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
namespace Island;

/// <summary>Persisted scratchpad text.</summary>
internal sealed class ScratchpadData { public string Text { get; set; } = ""; }

/// <summary>One text box that never closes. Autosaves; its first line can be pinned to the collapsed notch.</summary>
internal sealed class ScratchpadPanel : IslandPanel
{
    public override string Id => "scratchpad";
    public override string Title => "Scratchpad";
    public override string Glyph => "";
    public override int Order => 140;
    public override Color Accent => Ui.C("#A3E635");

    private string text = "";
    private bool loaded;
    private TextBox? box;
    private TextBlock? count, saved;
    private DispatcherTimer? debounce;

    public override string? RestingText
    {
        get
        {
            Load();
            foreach (var raw in text.Split('\n'))
            {
                var line = raw.Trim();
                if (line.Length == 0) continue;
                return line.Length > 26 ? line[..25].TrimEnd() + "…" : line;
            }
            return null;
        }
    }

    public override void Start() => Load();
    private void Load() { if (loaded) return; loaded = true; text = Store.Load<ScratchpadData>("scratchpad").Text ?? ""; }

    protected override FrameworkElement Build()
    {
        Load();
        var g = new Grid { Width = Ui.ContentW, Height = Ui.ContentH };
        box = Ui.Input("Type anything. It stays here, and it is saved as you type.", true);
        box.FontSize = 16; box.Padding = new Thickness(16, 12, 16, 12); box.Text = text; box.AcceptsTab = true; box.SpellCheck.IsEnabled = false;
        box.Background = Ui.B("#141417");
        box.TextChanged += (_, _) => { text = box.Text; UpdateCount(); MarkDirty(); };

        count = Ui.Text("", 12.5, Ui.Dim, FontWeights.Medium); count.VerticalAlignment = VerticalAlignment.Center; count.Margin = new Thickness(0, 0, 14, 0);
        saved = Ui.Text("", 12.5, Ui.B(Accent), FontWeights.SemiBold); saved.VerticalAlignment = VerticalAlignment.Center; saved.Margin = new Thickness(0, 0, 14, 0);
        var copy = Ui.Button("Copy", Accent, () => { if (text.Length > 0) { Hub.Copy(text); Flash("Copied"); } }, 30);
        var clear = Ui.Button("Clear", Ui.C("#FF453A"), () => { if (box != null) { box.Text = ""; Flash("Cleared"); } }, 30); clear.Margin = new Thickness(8, 0, 0, 0);
        var right = new StackPanel { Orientation = Orientation.Horizontal };
        right.Children.Add(saved); right.Children.Add(count); right.Children.Add(copy); right.Children.Add(clear);
        g.Children.Add(Ui.Titled("Scratchpad", "", box, Accent, right));
        UpdateCount();
        return g;
    }

    private void UpdateCount()
    {
        if (count == null) return;
        int words = 0; bool inWord = false;
        foreach (var c in text) { if (char.IsWhiteSpace(c)) inWord = false; else if (!inWord) { inWord = true; words++; } }
        count.Text = text.Length == 0 ? "" : $"{words} words · {text.Length} chars";
    }

    private void Flash(string msg)
    {
        if (saved == null) return;
        saved.Text = msg;
    }

    private void MarkDirty()
    {
        if (saved != null) saved.Text = "";
        debounce ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
        debounce.Tick -= OnDebounce; debounce.Tick += OnDebounce;
        debounce.Stop(); debounce.Start();
    }
    private void OnDebounce(object? s, EventArgs e) { debounce?.Stop(); Save(); if (saved != null) saved.Text = "Saved"; }
    private void Save() { try { Store.Save("scratchpad", new ScratchpadData { Text = text }); } catch { } }

    public override void OnHidden() { if (debounce is { IsEnabled: true }) { debounce.Stop(); Save(); } }
}
