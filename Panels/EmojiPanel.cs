using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
namespace Island;

internal sealed class EmojiState { public List<string> Recent { get; set; } = new(); }

/// <summary>Emoji picker: search, category tabs, recent strip. Clicking copies and pastes into the app the user is typing in.</summary>
internal sealed class EmojiPanel : IslandPanel
{
    public override string Id => "emoji";
    public override string Title => "Emoji";
    public override string Glyph => "";
    public override int Order => 110;
    public override Color Accent => Ui.C("#FFD60A");

    private static readonly FontFamily EmojiFont = new("Segoe UI Emoji");
    private const int Cap = 250, RecentMax = 18;

    private EmojiState state = new();
    private bool loaded;
    private int cat;
    private string search = "";
    private TextBox? box;
    private WrapPanel? grid;
    private ScrollViewer? scroll;
    private StackPanel? recentRow;
    private TextBlock? status, caption;
    private readonly List<Border> tabs = new();
    private DispatcherTimer? statusTimer;

    public override void Start() => Load();
    private void Load() { if (loaded) return; loaded = true; state = Store.Load<EmojiState>("emoji"); state.Recent ??= new(); }

    protected override FrameworkElement Build()
    {
        Load();
        var g = new Grid { Width = Ui.ContentW, Height = Ui.ContentH };
        var inner = new Grid();
        inner.RowDefinitions.Add(new RowDefinition { Height = new GridLength(38) }); inner.RowDefinitions.Add(new RowDefinition { Height = new GridLength(8) });
        inner.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); inner.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); inner.RowDefinitions.Add(new RowDefinition());

        // row 0: search + tabs + status
        box = Ui.Input("Search emoji, e.g. heart, fire, cat"); box.Padding = new Thickness(12, 5, 12, 5); box.Width = 270; box.HorizontalAlignment = HorizontalAlignment.Left;
        box.TextChanged += (_, _) => { search = box.Text.Trim(); scroll?.ScrollToTop(); RenderGrid(); };
        var top = new Grid(); top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); top.ColumnDefinitions.Add(new ColumnDefinition()); top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        top.Children.Add(box);
        var tabRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(14, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        for (int i = 0; i < EmojiData.Categories.Length; i++)
        {
            int idx = i; var (name, tab, _) = EmojiData.Categories[i];
            var t = new TextBlock { Text = tab, FontFamily = EmojiFont, FontSize = 18, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            var b = new Border { Width = 38, Height = 34, CornerRadius = new CornerRadius(10), Margin = new Thickness(0, 0, 3, 0), Cursor = Cursors.Hand, Child = t, ToolTip = name, Background = Brushes.Transparent };
            b.MouseEnter += (_, _) => { if (cat != idx || search.Length > 0) b.Background = Ui.Chip; };
            b.MouseLeave += (_, _) => PaintTabs();
            b.MouseLeftButtonUp += (_, e) => { cat = idx; if (box != null && box.Text.Length > 0) box.Text = ""; else RenderGrid(); PaintTabs(); scroll?.ScrollToTop(); e.Handled = true; };
            tabs.Add(b); tabRow.Children.Add(b);
        }
        Grid.SetColumn(tabRow, 1); top.Children.Add(tabRow);
        status = Ui.Text("", 13, Ui.B(Accent), FontWeights.SemiBold); status.VerticalAlignment = VerticalAlignment.Center; Grid.SetColumn(status, 2); top.Children.Add(status);
        inner.Children.Add(top);

        // row 2: recent strip
        recentRow = new StackPanel { Orientation = Orientation.Horizontal, Height = 42, Margin = new Thickness(0, 0, 0, 4) };
        Grid.SetRow(recentRow, 2); inner.Children.Add(recentRow);

        // row 3: caption (category name / results)
        caption = Ui.Label(""); caption.Margin = new Thickness(2, 0, 0, 6); Grid.SetRow(caption, 3); inner.Children.Add(caption);

        // row 4: grid
        grid = new WrapPanel(); scroll = Ui.Scroll(grid); Grid.SetRow(scroll, 4); inner.Children.Add(scroll);

        g.Children.Add(Ui.Card(inner, 16));
        PaintTabs(); RenderRecent(); RenderGrid();
        return g;
    }

    private void PaintTabs()
    {
        for (int i = 0; i < tabs.Count; i++) tabs[i].Background = search.Length == 0 && i == cat ? Ui.B(Accent, 0x30) : Brushes.Transparent;
    }

    private Border Tile(string e, string tip, double size, double font)
    {
        var tb = new TextBlock { Text = e, FontFamily = EmojiFont, FontSize = font, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, RenderTransformOrigin = new Point(.5, .5), RenderTransform = new ScaleTransform(1, 1) };
        var b = new Border { Width = size, Height = size, Margin = new Thickness(2), CornerRadius = new CornerRadius(10), Background = Brushes.Transparent, Cursor = Cursors.Hand, Child = tb, ToolTip = tip };
        b.MouseEnter += (_, _) => { b.Background = Ui.Chip; tb.RenderTransform = new ScaleTransform(1.28, 1.28); };
        b.MouseLeave += (_, _) => { b.Background = Brushes.Transparent; tb.RenderTransform = new ScaleTransform(1, 1); };
        b.MouseLeftButtonUp += (_, ev) => { Pick(e); ev.Handled = true; };
        return b;
    }

    private void RenderGrid()
    {
        if (grid == null || caption == null) return; grid.Children.Clear();
        if (recentRow != null) recentRow.Visibility = search.Length == 0 && state.Recent.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        List<(string E, string N)> items;
        if (search.Length > 0) { items = EmojiData.Search(search, Cap); caption.Text = items.Count == 0 ? "NO RESULTS" : (items.Count >= Cap ? "TOP " + Cap + " RESULTS" : items.Count + " RESULTS"); }
        else { items = EmojiData.Entries(cat); caption.Text = EmojiData.Categories[cat].Name.ToUpperInvariant(); }
        if (items.Count == 0) { var t = Ui.Text("No emoji match \"" + search + "\". Try another word.", 13.5, Ui.Dim); t.Margin = new Thickness(4, 4, 0, 0); grid.Children.Add(t); return; }
        foreach (var (e, n) in items.Take(Cap)) grid.Children.Add(Tile(e, n, 40, 24));
    }

    private void RenderRecent()
    {
        if (recentRow == null) return; recentRow.Children.Clear();
        recentRow.Visibility = search.Length == 0 && state.Recent.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (state.Recent.Count == 0) return;
        var l = Ui.Label("Recent"); l.VerticalAlignment = VerticalAlignment.Center; l.Margin = new Thickness(2, 0, 12, 0); recentRow.Children.Add(l);
        foreach (var e in state.Recent.Take(RecentMax)) recentRow.Children.Add(Tile(e, "Recently used", 38, 22));
    }

    private void Pick(string e)
    {
        Hub.Copy(e);
        bool typingHere = box != null && box.IsKeyboardFocused;   // if the island owns the keyboard, pasting would go into our own search box
        if (!typingHere) Hub.PasteIntoForeground();
        state.Recent.Remove(e); state.Recent.Insert(0, e); if (state.Recent.Count > 40) state.Recent.RemoveRange(40, state.Recent.Count - 40);
        Store.Save("emoji", state);
        RenderRecent();
        Flash(typingHere ? "Copied" : "Pasted");
    }

    private void Flash(string msg)
    {
        if (status == null) return; status.Text = msg;
        statusTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1400) };
        statusTimer.Tick -= ClearStatus; statusTimer.Tick += ClearStatus; statusTimer.Stop(); statusTimer.Start();
    }
    private void ClearStatus(object? s, EventArgs e) { statusTimer?.Stop(); if (status != null) status.Text = ""; }

    public override void OnHidden() { statusTimer?.Stop(); if (status != null) status.Text = ""; }
}
