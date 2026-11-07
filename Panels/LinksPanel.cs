using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
namespace Island;

internal sealed class LinksItem { public string Title { get; set; } = ""; public string Url { get; set; } = ""; }
internal sealed class LinksData { public int Engine { get; set; } public List<LinksItem> Items { get; set; } = new(); }

/// <summary>Web search from the notch (5 engines) plus a grid of saved links.</summary>
internal sealed class LinksPanel : IslandPanel
{
    public override string Id => "links";
    public override string Title => "Links";
    public override string Glyph => "";
    public override int Order => 100;
    public override Color Accent => Ui.C("#BF5AF2");

    private static readonly (string Name, string Url)[] Engines =
    {
        ("Google", "https://www.google.com/search?q="), ("DuckDuckGo", "https://duckduckgo.com/?q="), ("Perplexity", "https://www.perplexity.ai/search?q="),
        ("YouTube", "https://www.youtube.com/results?search_query="), ("GitHub", "https://github.com/search?q="),
    };
    private static readonly (string Title, string Url)[] Suggestions =
    {
        ("GitHub", "https://github.com"), ("YouTube", "https://www.youtube.com"), ("Gmail", "https://mail.google.com"),
        ("X", "https://x.com"), ("Reddit", "https://www.reddit.com"), ("Hacker News", "https://news.ycombinator.com"),
    };

    private LinksData data = new();
    private bool loaded;
    private TextBox? query;
    private Grid? host;
    private bool adding;

    public override void Start() => Load();
    private void Load() { if (loaded) return; loaded = true; data = Store.Load<LinksData>("links"); data.Items ??= new(); if (data.Engine < 0 || data.Engine >= Engines.Length) data.Engine = 0; }
    private void Save() => Store.Save("links", data);

    protected override FrameworkElement Build()
    {
        Load();
        var g = new Grid { Width = Ui.ContentW, Height = Ui.ContentH };
        g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(158) }); g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(10) }); g.RowDefinitions.Add(new RowDefinition());

        // ---- search tile ----
        query = Ui.Input("Search " + Engines[data.Engine].Name); query.FontSize = 16; query.Padding = new Thickness(16, 8, 16, 8);
        query.KeyDown += (_, e) => { if (e.Key == Key.Enter) { RunSearch(); e.Handled = true; } };
        var seg = new SegmentedControl(Engines.Select(x => x.Name), data.Engine, Accent, i => { data.Engine = i; if (query != null) query.Tag = "Search " + Engines[i].Name; Save(); });
        var go = Ui.Button("Search", Accent, RunSearch, 40); go.MinWidth = 96;
        var row = new Grid { Margin = new Thickness(0, 10, 0, 0) }; row.ColumnDefinitions.Add(new ColumnDefinition()); row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) }); row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.Children.Add(query); Grid.SetColumn(go, 2); row.Children.Add(go);
        var body = new StackPanel(); body.Children.Add(seg); body.Children.Add(row);
        row.Height = 42;
        g.Children.Add(Ui.Titled("Search the web", "", body, Accent));

        // ---- saved links tile ----
        host = new Grid();
        var add = Ui.Button("+ Add link", Accent, () => { adding = !adding; Render(); }, 28);
        var tile = Ui.Titled("Your links", "", host, Accent, add); Grid.SetRow(tile, 2); g.Children.Add(tile);
        Render();
        return g;
    }

    private void RunSearch()
    {
        var q = query?.Text.Trim() ?? ""; if (q.Length == 0) return;
        Api.Open(Engines[data.Engine].Url + Uri.EscapeDataString(q));
        if (query != null) query.Text = "";
        Hub.Collapse?.Invoke();
    }

    private void Render()
    {
        if (host == null) return;
        host.Children.Clear();
        if (adding) { host.Children.Add(AddForm()); return; }
        if (data.Items.Count == 0)
        {
            var p = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            var t = Ui.Text("The handful of sites you actually live in", 14.5, Ui.Soft, FontWeights.SemiBold); t.HorizontalAlignment = HorizontalAlignment.Center; p.Children.Add(t);
            var h = Ui.Text("Add your own with + Add link, or start from one of these.", 13, Ui.Dim); h.HorizontalAlignment = HorizontalAlignment.Center; h.Margin = new Thickness(0, 3, 0, 10); p.Children.Add(h);
            p.Children.Add(SuggestionRow());
            host.Children.Add(p); return;
        }
        var wrap = new WrapPanel();
        foreach (var it in data.Items.ToList()) wrap.Children.Add(Tile(it));
        host.Children.Add(Ui.Scroll(wrap));
    }

    private WrapPanel SuggestionRow()
    {
        var w = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Center };
        foreach (var (title, url) in Suggestions)
        {
            if (data.Items.Any(i => string.Equals(i.Url.TrimEnd('/'), url, StringComparison.OrdinalIgnoreCase))) continue;
            var t = Ui.Text("+ " + title, 13.5, Ui.Soft, FontWeights.SemiBold); t.VerticalAlignment = VerticalAlignment.Center;
            var chip = new Border { CornerRadius = new CornerRadius(15), Padding = new Thickness(14, 5, 14, 5), Margin = new Thickness(0, 0, 8, 6), Background = Ui.Chip, Cursor = Cursors.Hand, Child = t };
            chip.MouseEnter += (_, _) => chip.Background = Ui.B(Accent, 0x44); chip.MouseLeave += (_, _) => chip.Background = Ui.Chip;
            var tt = title; var uu = url;
            chip.MouseLeftButtonUp += (_, e) => { data.Items.Add(new LinksItem { Title = tt, Url = uu }); Save(); adding = false; Render(); e.Handled = true; };
            w.Children.Add(chip);
        }
        return w;
    }

    private UIElement AddForm()
    {
        var titleBox = Ui.Input("Title (optional)"); var urlBox = Ui.Input("Address, e.g. github.com/you");
        var err = Ui.Text("", 12.5, Ui.Red); err.Margin = new Thickness(0, 6, 0, 0);
        void Commit()
        {
            var url = NormalizeUrl(urlBox.Text);
            if (url == null) { err.Text = "That does not look like a valid web address."; return; }
            var title = titleBox.Text.Trim(); if (title.Length == 0) title = new Uri(url).Host.Replace("www.", "");
            data.Items.Add(new LinksItem { Title = title, Url = url }); Save(); adding = false; Render();
        }
        urlBox.KeyDown += (_, e) => { if (e.Key == Key.Enter) { Commit(); e.Handled = true; } };
        titleBox.KeyDown += (_, e) => { if (e.Key == Key.Enter) { Commit(); e.Handled = true; } };
        var row = new Grid { Height = 40 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) }); row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) }); row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) }); row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(urlBox, 2); row.Children.Add(titleBox); row.Children.Add(urlBox);
        var ok = Ui.Button("Add", Accent, Commit, 40); Grid.SetColumn(ok, 4); var cancel = Ui.Button("Cancel", Ui.C("#A0A0A8"), () => { adding = false; Render(); }, 40); cancel.Margin = new Thickness(6, 0, 0, 0); Grid.SetColumn(cancel, 5);
        row.Children.Add(ok); row.Children.Add(cancel);
        var p = new StackPanel(); p.Children.Add(row); p.Children.Add(err);
        var more = Ui.Label("Quick add"); more.Margin = new Thickness(0, 8, 0, 6); p.Children.Add(more); p.Children.Add(SuggestionRow());
        return Ui.Scroll(p);
    }

    /// <summary>Trim, add https:// if missing, upgrade http to https; null if invalid.</summary>
    private static string? NormalizeUrl(string raw)
    {
        raw = raw.Trim(); if (raw.Length == 0 || raw.Contains(' ')) return null;
        if (raw.StartsWith("http://", StringComparison.OrdinalIgnoreCase)) raw = "https://" + raw[7..];
        else if (!raw.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) raw = "https://" + raw;
        if (!Uri.TryCreate(raw, UriKind.Absolute, out var u) || u.Scheme != "https" || !u.Host.Contains('.')) return null;
        return u.ToString();
    }

    private UIElement Tile(LinksItem it)
    {
        string title = it.Title.Length > 0 ? it.Title : it.Url; string host = it.Url; try { host = new Uri(it.Url).Host.Replace("www.", ""); } catch { }
        var color = Ui.Palette[(Math.Abs(StableHash(host)) % Ui.Palette.Length)];
        var letter = Ui.Text(title.Length > 0 ? title[..1].ToUpperInvariant() : "?", 17, Ui.B(color), FontWeights.Bold); letter.HorizontalAlignment = HorizontalAlignment.Center; letter.VerticalAlignment = VerticalAlignment.Center;
        var sq = new Border { Width = 38, Height = 38, CornerRadius = new CornerRadius(11), Background = Ui.B(color, 0x30), Child = letter, VerticalAlignment = VerticalAlignment.Center };
        var col = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(11, 0, 22, 0) };
        col.Children.Add(Ui.Text(title, 14.5, Ui.Ink, FontWeights.SemiBold)); col.Children.Add(Ui.Text(host, 12.5, Ui.Dim));
        var inner = new Grid(); inner.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); inner.ColumnDefinitions.Add(new ColumnDefinition());
        inner.Children.Add(sq); Grid.SetColumn(col, 1); inner.Children.Add(col);
        var x = Ui.IconButton("", "Remove", () => { data.Items.Remove(it); Save(); Render(); }, 22, false);
        x.HorizontalAlignment = HorizontalAlignment.Right; x.VerticalAlignment = VerticalAlignment.Top; x.Visibility = Visibility.Collapsed;
        var root = new Grid(); root.Children.Add(inner); root.Children.Add(x);
        var tile = new Border { Width = 205, Height = 58, Margin = new Thickness(0, 0, 8, 8), Padding = new Thickness(10, 0, 8, 0), CornerRadius = new CornerRadius(14), Background = Ui.Chip, Cursor = Cursors.Hand, Child = root, ToolTip = it.Url };
        tile.MouseEnter += (_, _) => { tile.Background = Ui.ChipHover; x.Visibility = Visibility.Visible; };
        tile.MouseLeave += (_, _) => { tile.Background = Ui.Chip; x.Visibility = Visibility.Collapsed; };
        tile.MouseLeftButtonUp += (_, e) => { Api.Open(it.Url); e.Handled = true; };
        return tile;
    }

    private static int StableHash(string s) { int h = 17; foreach (var c in s) h = h * 31 + c; return h == int.MinValue ? 0 : h; }
}
