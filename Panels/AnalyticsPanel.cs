using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
namespace Island;

/// <summary>Analytics: DataFast or Google Analytics 4 traffic (visitors, pageviews, bounce, session time, top pages, sources) with inline setup.</summary>
internal sealed class AnalyticsPanel : IslandPanel
{
    public override string Id => "analytics";
    public override string Title => "Analytics";
    public override string Glyph => "";
    public override int Order => 20;
    public override Color Accent => Ui.C("#8B7CFF");

    private static readonly string[] Providers = { "datafast", "ga" };
    private AnalyticsConfig cfg = new();
    private bool cfgLoaded;
    private string dfKey = "";
    private readonly Dictionary<string, AnalyticsSnapshot> cache = new();
    private readonly HashSet<string> inflight = new();
    private string error = "";
    private int? live; private DateTime nextLive = DateTime.MinValue; private bool liveBusy;
    private Grid? body; private Border? liveChip; private TextBlock? liveText; private SegmentedControl? provSeg, rangeSeg;

    private bool Ga => cfg.Provider == "ga";
    /// <summary>Cheap check (no disk access): are there credentials for the selected provider?</summary>
    private bool HasCredentials => Ga ? cfg.GaProperty.Length > 0 : dfKey.Length > 0;
    private string CacheKey => cfg.Provider + ":" + cfg.Range;

    private void LoadCfg()
    {
        if (cfgLoaded) return; cfgLoaded = true;
        cfg = Store.Load<AnalyticsConfig>("analytics"); if (cfg.Provider != "ga") cfg.Provider = "datafast"; cfg.Range = Math.Clamp(cfg.Range, 0, 2);
        try { dfKey = new SecretStore().Get("DataFastKey"); } catch { dfKey = ""; }
    }
    private void SaveCfg() => Store.Save("analytics", cfg);
    public override void Start() => LoadCfg();

    public override string? RestingText => HasCredentials && live is { } n ? n + " live" : null;

    public override void Tick()
    {
        if (liveBusy || DateTime.Now < nextLive) return;
        nextLive = DateTime.Now.AddSeconds(60); LoadCfg(); if (!HasCredentials) return;
        _ = PollLive();
    }
    private async System.Threading.Tasks.Task PollLive()
    {
        liveBusy = true; bool ga = Ga; string prop = cfg.GaProperty, key = dfKey;
        try
        {
            if (ga && !Hub.Dashboard.Auth.Connected) return;
            int n = ga ? await AnalyticsProviders.LiveGa(prop) : await AnalyticsProviders.LiveDataFast(key);
            if (ga == Ga) { live = n; UpdateLive(); }
        }
        catch { /* keep the last value; the main fetch shows errors */ }
        finally { liveBusy = false; }
    }
    private void UpdateLive()
    {
        if (liveChip == null || liveText == null) return;
        liveChip.Visibility = live != null && HasCredentials ? Visibility.Visible : Visibility.Collapsed; liveText.Text = (live ?? 0) + " online";
    }

    protected override FrameworkElement Build()
    {
        LoadCfg();
        var root = new Grid { Width = Ui.ContentW, Height = Ui.ContentH };
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(36) }); root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(10) }); root.RowDefinitions.Add(new RowDefinition());
        var bar = new Grid(); bar.ColumnDefinitions.Add(new ColumnDefinition()); bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var left = new StackPanel { Orientation = Orientation.Horizontal };
        provSeg = new SegmentedControl(new[] { "DataFast", "Google Analytics" }, Ga ? 1 : 0, Accent, i => { cfg.Provider = Providers[i]; SaveCfg(); live = null; error = ""; nextLive = DateTime.MinValue; UpdateLive(); Refresh(false); }) { VerticalAlignment = VerticalAlignment.Center };
        rangeSeg = new SegmentedControl(new[] { "Today", "7 days", "30 days" }, cfg.Range, Accent, i => { cfg.Range = i; SaveCfg(); error = ""; Refresh(false); }) { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
        left.Children.Add(provSeg); left.Children.Add(rangeSeg); bar.Children.Add(left);
        var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center }; Grid.SetColumn(right, 1);
        liveText = Ui.Text("", 13.5, Ui.Ink, FontWeights.SemiBold); var dot = Ui.Dot(Ui.Green, 8); dot.Margin = new Thickness(0, 0, 8, 0);
        var lp = new StackPanel { Orientation = Orientation.Horizontal }; lp.Children.Add(dot); lp.Children.Add(liveText);
        liveChip = new Border { Background = Ui.Chip, CornerRadius = new CornerRadius(16), Padding = new Thickness(13, 7, 13, 7), Child = lp, Visibility = Visibility.Collapsed, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
        right.Children.Add(liveChip);
        right.Children.Add(Ui.IconButton("", "Refresh", () => Refresh(true), 34));
        var gear = Ui.IconButton("", "Change connection", Disconnect, 34); gear.Margin = new Thickness(8, 0, 0, 0); right.Children.Add(gear);
        bar.Children.Add(right); root.Children.Add(bar);
        body = new Grid(); Grid.SetRow(body, 2); root.Children.Add(body);
        UpdateLive(); Refresh(false);
        return root;
    }
    public override void OnShown() { if (IsBuilt) { nextLive = DateTime.MinValue; Refresh(false); } }

    private void Disconnect()
    {
        try { if (Ga) cfg.GaProperty = ""; else { new SecretStore().Remove("DataFastKey"); dfKey = ""; } } catch { }
        SaveCfg(); cache.Clear(); live = null; error = ""; UpdateLive(); Render();
    }

    // ---------- data ----------
    private async void Refresh(bool force)
    {
        try
        {
            Render(); if (!HasCredentials) return;
            string key = CacheKey; bool ga = Ga; int range = cfg.Range;
            if (!force && cache.TryGetValue(key, out var hit) && (DateTime.Now - hit.At).TotalSeconds < 60) return;
            if (!inflight.Add(key)) return;
            Render();
            try
            {
                if (ga && !Hub.Dashboard.Auth.Connected) throw new AnalyticsException("Google is not connected. Open Settings and connect Google.");
                var snap = ga ? await AnalyticsProviders.FetchGa(cfg.GaProperty, range) : await AnalyticsProviders.FetchDataFast(dfKey, range);
                cache[key] = snap; if (key == CacheKey) error = snap.Warning;
            }
            catch (AnalyticsException ex) { if (key == CacheKey) error = ex.Message; }
            catch (Exception) { if (key == CacheKey) error = "Couldn't load analytics. Check your connection."; }
            finally { inflight.Remove(key); }
            Render();
        }
        catch { /* async void must never throw */ }
    }

    // ---------- rendering ----------
    private void Render()
    {
        if (body == null) return;
        try
        {
            body.Children.Clear(); body.RowDefinitions.Clear();
            if (!HasCredentials || (Ga && !Hub.Dashboard.Auth.Connected)) { body.Children.Add(ConnectCard()); return; }
            bool busy = inflight.Contains(CacheKey);
            if (!cache.TryGetValue(CacheKey, out var snap))
            {
                var msg = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
                var t = Ui.Text(error.Length > 0 ? error : "Loading your traffic…", 14.5, error.Length > 0 ? Ui.Orange : Ui.Dim); t.TextWrapping = TextWrapping.Wrap; t.MaxWidth = 560; t.TextAlignment = TextAlignment.Center; msg.Children.Add(t);
                if (error.Length > 0 && !busy) { var b = Ui.Button("Retry", Accent, () => Refresh(true)); b.Margin = new Thickness(0, 12, 0, 0); b.HorizontalAlignment = HorizontalAlignment.Center; msg.Children.Add(b); }
                body.Children.Add(Ui.Card(msg)); return;
            }
            body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(74) }); body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(10) }); body.RowDefinitions.Add(new RowDefinition());
            var kpis = Kpis(snap); body.Children.Add(kpis);
            var lower = new Grid(); Grid.SetRow(lower, 2);
            foreach (double w in new[] { 360.0, 10, 260, 10 }) lower.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(w) }); lower.ColumnDefinitions.Add(new ColumnDefinition());
            lower.Children.Add(ChartCard(snap, busy));
            var pages = ListCard("Top pages", "", snap.Pages, 212, "No page data yet"); Grid.SetColumn(pages, 2); lower.Children.Add(pages);
            var src = ListCard("Sources", "", snap.Sources, 210, "No referrers yet"); Grid.SetColumn(src, 4); lower.Children.Add(src);
            body.Children.Add(lower);
        }
        catch { /* never take the island down */ }
    }

    private static string Count(double v) => v >= 100_000 ? (v / 1000).ToString("0", CultureInfo.InvariantCulture) + "k" : Math.Round(v).ToString("N0", CultureInfo.InvariantCulture);
    private static string Duration(double sec)
    {
        int s = (int)Math.Round(sec); if (s >= 3600) return s / 3600 + "h " + (s % 3600 / 60).ToString("00", CultureInfo.InvariantCulture) + "m";
        return s >= 60 ? s / 60 + "m " + (s % 60).ToString("00", CultureInfo.InvariantCulture) + "s" : s + "s";
    }

    private FrameworkElement Kpis(AnalyticsSnapshot s)
    {
        var tiles = new List<(string label, string value, double? cur, double? prev, bool lowerIsBetter, bool pts)>
        {
            ("Visitors", Count(s.Cur.Visitors), s.Cur.Visitors, s.Prev?.Visitors, false, false),
            ("Pageviews", Count(s.Cur.Pageviews), s.Cur.Pageviews, s.Prev?.Pageviews, false, false),
            ("Bounce", s.Cur.Bounce is { } b ? b.ToString("0.#", CultureInfo.InvariantCulture) + "%" : "—", s.Cur.Bounce, s.Prev?.Bounce, true, true),
            ("Session time", s.Cur.Duration is { } d ? Duration(d) : "—", s.Cur.Duration, s.Prev?.Duration, false, false),
        };
        if ((s.Cur.Revenue ?? 0) > 0 || (s.Prev?.Revenue ?? 0) > 0) tiles.Add(("Revenue", RevenueFmt.Compact((decimal)(s.Cur.Revenue ?? 0), s.Currency.Length > 0 ? s.Currency : "USD"), s.Cur.Revenue, s.Prev?.Revenue, false, false));
        var g = new Grid();
        for (int i = 0; i < tiles.Count; i++)
        {
            if (i > 0) g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) }); g.ColumnDefinitions.Add(new ColumnDefinition());
            var t = tiles[i]; var head = new Grid(); head.ColumnDefinitions.Add(new ColumnDefinition()); head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            head.Children.Add(Ui.Label(t.label));
            if (Delta(t.cur, t.prev, t.lowerIsBetter, t.pts) is { } dl) { Grid.SetColumn(dl, 1); head.Children.Add(dl); }
            var col = new StackPanel(); col.Children.Add(head); var v = Ui.Text(t.value, 26, i == 0 ? Ui.B(Accent) : Ui.Ink, FontWeights.SemiBold); v.Margin = new Thickness(0, 1, 0, 0); col.Children.Add(v);
            var card = Ui.Card(col, 12); card.Padding = new Thickness(14, 9, 12, 6); Grid.SetColumn(card, i * 2); g.Children.Add(card);
        }
        return g;
    }

    /// <summary>"▲ 12%" vs previous period (percentage points for rates); null when there is nothing to compare.</summary>
    private static TextBlock? Delta(double? cur, double? prev, bool lowerIsBetter, bool points)
    {
        if (cur is not { } c || prev is not { } p) return null;
        double diff = c - p; if (!points && p <= 0) return null; if (Math.Abs(diff) < 0.05) return null;
        double shown = points ? diff : diff / p * 100; bool up = diff > 0, good = up != lowerIsBetter;
        var t = Ui.Text((up ? "▲ " : "▼ ") + Math.Abs(shown).ToString(Math.Abs(shown) < 10 ? "0.#" : "0", CultureInfo.InvariantCulture) + (points ? " pt" : "%"), 12.5, good ? Ui.Green : Ui.Red, FontWeights.SemiBold);
        t.ToolTip = "vs previous period"; t.VerticalAlignment = VerticalAlignment.Center; return t;
    }

    private FrameworkElement ChartCard(AnalyticsSnapshot s, bool busy)
    {
        var box = new Grid(); box.RowDefinitions.Add(new RowDefinition()); box.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var chart = new AreaChart(); chart.Set(s.Series, Accent); box.Children.Add(chart);
        if (s.Series.Count < 2) { var n = Ui.Text("Not enough data yet", 13.5, Ui.Dim); n.HorizontalAlignment = HorizontalAlignment.Center; n.VerticalAlignment = VerticalAlignment.Center; box.Children.Add(n); }
        var axis = new Grid { Margin = new Thickness(0, 4, 0, 0) }; var today = DateTime.Today;
        axis.Children.Add(Ui.Text(cfg.Range == 0 ? "12 AM" : today.AddDays(cfg.Range == 1 ? -6 : -29).ToString("MMM d", CultureInfo.CurrentCulture), 12, Ui.Dim));
        var r = Ui.Text(cfg.Range == 0 ? "now" : "Today", 12, Ui.Dim); r.HorizontalAlignment = HorizontalAlignment.Right; axis.Children.Add(r); Grid.SetRow(axis, 1); box.Children.Add(axis);
        TextBlock? status = null;
        if (error.Length > 0) { status = Ui.Text(error, 12.5, Ui.Orange); status.ToolTip = error; status.MaxWidth = 210; }
        else if (busy) status = Ui.Text("Updating…", 12.5, Ui.Dim);
        return Ui.Titled("Visitors", "", box, Accent, status, 14);
    }

    private FrameworkElement ListCard(string title, string glyph, List<AnalyticsItem> items, double rowWidth, string empty)
    {
        var list = new StackPanel(); double max = items.Count > 0 ? Math.Max(items.Max(i => i.Value), 1e-9) : 1;
        if (items.Count == 0) list.Children.Add(Ui.Text(empty, 13.5, Ui.Dim));
        foreach (var it in items)
        {
            var row = new Border { Height = 28, CornerRadius = new CornerRadius(8), Margin = new Thickness(0, 0, 0, 3), Width = rowWidth, HorizontalAlignment = HorizontalAlignment.Left, ToolTip = it.Name + " · " + Count(it.Value) };
            var g = new Grid(); g.Children.Add(new Border { Width = Math.Max(4, rowWidth * it.Value / max), HorizontalAlignment = HorizontalAlignment.Left, CornerRadius = new CornerRadius(8), Background = Ui.B(Accent, 0x30) });
            g.ColumnDefinitions.Add(new ColumnDefinition()); g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var name = Ui.Text(it.Name, 13.5, Ui.Ink); name.Margin = new Thickness(9, 0, 6, 0); name.VerticalAlignment = VerticalAlignment.Center;
            var cnt = Ui.Text(Count(it.Value), 13, Ui.Soft, FontWeights.SemiBold); cnt.Margin = new Thickness(0, 0, 9, 0); cnt.VerticalAlignment = VerticalAlignment.Center; Grid.SetColumn(cnt, 1);
            g.Children.Add(name); g.Children.Add(cnt); row.Child = g; list.Children.Add(row);
        }
        return Ui.Titled(title, glyph, Ui.Scroll(list), Accent, null, 14);
    }

    // ---------- inline setup ----------
    private FrameworkElement ConnectCard()
    {
        var col = new StackPanel { Width = 540 };
        var title = Ui.Text(Ga ? "Connect Google Analytics" : "Connect DataFast", 20, Ui.Ink, FontWeights.SemiBold); col.Children.Add(title);
        var copy = Ui.Text("See your traffic without opening a tab: visitors, pageviews, bounce rate, average time, top pages and where people came from. Keep the live count on the collapsed notch.", 13.5, Ui.Dim);
        copy.TextWrapping = TextWrapping.Wrap; copy.Margin = new Thickness(0, 6, 0, 14); col.Children.Add(copy);
        var status = Ui.Text("", 12.5, Ui.Orange); status.TextWrapping = TextWrapping.Wrap; status.Margin = new Thickness(0, 10, 0, 0);
        var row = new Grid(); row.ColumnDefinitions.Add(new ColumnDefinition()); row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        if (!Ga)
        {
            var pb = new PasswordBox { Height = 40, ToolTip = "DataFast website API key (df_...)" };
            var go = Ui.Button("Connect", Accent, () => { string k = pb.Password.Trim(); if (k.Length == 0) { status.Text = "Paste your DataFast website API key first."; return; } try { new SecretStore().Set("DataFastKey", k); } catch { status.Text = "Couldn't store the key."; return; } dfKey = k; cache.Clear(); error = ""; nextLive = DateTime.MinValue; Refresh(true); }, 40);
            go.Margin = new Thickness(10, 0, 0, 0); Grid.SetColumn(go, 1); row.Children.Add(pb); row.Children.Add(go); col.Children.Add(Ui.Label("Website API key")); row.Margin = new Thickness(0, 6, 0, 0); col.Children.Add(row);
            var hint = Ui.Text("In DataFast open Website settings, then API, and create a key (it starts with df_). It is stored encrypted on this PC.", 12.5, Ui.Dim); hint.TextWrapping = TextWrapping.Wrap; hint.Margin = new Thickness(0, 10, 0, 0); col.Children.Add(hint);
        }
        else
        {
            var tb = Ui.Input("GA4 property ID, e.g. 123456789"); tb.Height = 40; tb.Text = cfg.GaProperty;
            var go = Ui.Button("Connect", Accent, () =>
            {
                string id = new string(tb.Text.Where(char.IsDigit).ToArray()); if (id.Length == 0) { status.Text = "Enter the numeric GA4 property ID (Admin, Property settings)."; return; }
                cfg.GaProperty = id; SaveCfg(); cache.Clear(); error = ""; nextLive = DateTime.MinValue;
                if (!Hub.Dashboard.Auth.Connected) { Render(); return; }
                Refresh(true);
            }, 40);
            go.Margin = new Thickness(10, 0, 0, 0); Grid.SetColumn(go, 1); row.Children.Add(tb); row.Children.Add(go); col.Children.Add(Ui.Label("GA4 property ID")); row.Margin = new Thickness(0, 6, 0, 0); col.Children.Add(row);
            if (cfg.GaProperty.Length > 0 && !Hub.Dashboard.Auth.Connected) { status.Text = "Property saved. Now connect Google in Settings."; var sb = Ui.Button("Open Settings", Accent, () => Hub.OpenSettings?.Invoke()); sb.HorizontalAlignment = HorizontalAlignment.Left; sb.Margin = new Thickness(0, 10, 0, 0); col.Children.Add(status); col.Children.Add(sb); }
            var hint = Ui.Text("Uses your Google connection from Settings. If you connected before this feature existed, disconnect and reconnect Google so the Analytics permission is granted, and enable the \"Google Analytics Data API\" in your Google Cloud project.", 12.5, Ui.Dim);
            hint.TextWrapping = TextWrapping.Wrap; hint.Margin = new Thickness(0, 10, 0, 0); col.Children.Add(hint);
        }
        if (status.Parent == null) col.Children.Add(status);
        var card = Ui.Card(col, 22); card.HorizontalAlignment = HorizontalAlignment.Center; card.VerticalAlignment = VerticalAlignment.Center; return card;
    }
}
