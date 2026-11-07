using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Island.Providers;
namespace Island;

/// <summary>Money formatting shared by the revenue panel (amounts are already in major units, see Money.Convert).</summary>
internal static class RevenueFmt
{
    private static readonly HashSet<string> ZeroDecimal = new() { "BIF", "CLP", "DJF", "GNF", "JPY", "KMF", "KRW", "MGA", "PYG", "RWF", "UGX", "VND", "VUV", "XAF", "XOF", "XPF" };
    private static string Symbol(string cur) => cur.ToUpperInvariant() switch
    {
        "USD" => "$", "EUR" => "€", "GBP" => "£", "INR" => "₹", "JPY" => "¥", "CNY" => "CN¥", "KRW" => "₩", "CAD" => "CA$", "AUD" => "A$", "BRL" => "R$", "MXN" => "MX$", "NZD" => "NZ$",
        var c => c + " "
    };
    public static string Full(decimal amount, string cur)
    {
        int dec = ZeroDecimal.Contains(cur.ToUpperInvariant()) ? 0 : 2;
        string body = Math.Abs(amount).ToString("N" + dec, CultureInfo.InvariantCulture);
        return (amount < 0 ? "-" : "") + Symbol(cur) + body;
    }
    public static string Compact(decimal amount, string cur)
    {
        decimal a = Math.Abs(amount); string s = amount < 0 ? "-" : "";
        if (a >= 1_000_000m) return s + Symbol(cur) + (a / 1_000_000m).ToString("0.#", CultureInfo.InvariantCulture) + "m";
        if (a >= 1000m) return s + Symbol(cur) + (a / 1000m).ToString("0.#", CultureInfo.InvariantCulture) + "k";
        return s + Symbol(cur) + a.ToString("0.##", CultureInfo.InvariantCulture);
    }
    public static string Age(DateTimeOffset t)
    {
        var span = DateTimeOffset.Now - t; if (span.TotalMinutes < 1) return "now";
        if (span.TotalMinutes < 60) return (int)span.TotalMinutes + "m";
        if (span.TotalHours < 24) return (int)span.TotalHours + "h";
        return (int)span.TotalDays + "d";
    }
}

/// <summary>Revenue: today's takings, 7/30-day totals with trend chart and the latest sales, from the connected Stripe / Polar accounts.</summary>
internal sealed class RevenuePanel : IslandPanel
{
    public override string Id => "revenue";
    public override string Title => "Revenue";
    public override string Glyph => "";
    public override int Order => 10;
    public override Color Accent => Ui.C("#30D158");

    private Grid? root;
    private int range; // 0 today, 1 7 days, 2 30 days
    private ScrollViewer? recentScroll;

    protected override FrameworkElement Build()
    {
        root = new Grid { Width = Ui.ContentW, Height = Ui.ContentH };
        Hub.Dashboard.Changed += Render; Render();
        return root;
    }
    public override void OnShown() { if (IsBuilt) Render(); }

    public override string? RestingText
    {
        get
        {
            try
            {
                var sales = Hub.Dashboard.Sales; if (sales.Count == 0) return Hub.Dashboard.RevenueConnected ? "0 today" : null;
                var today = sales.Where(s => InRange(s, 0)).GroupBy(s => s.Currency).Select(g => (Cur: g.Key, Sum: g.Sum(x => x.Amount))).OrderByDescending(x => Math.Abs(x.Sum)).ToList();
                if (today.Count == 0) return RevenueFmt.Compact(0, sales[0].Currency) + " today";
                return RevenueFmt.Compact(today[0].Sum, today[0].Cur) + " today";
            }
            catch { return null; }
        }
    }

    private static bool InRange(Sale s, int r)
    {
        var d = s.Time.LocalDateTime.Date; var today = DateTime.Today;
        return r == 0 ? d == today : r == 1 ? d >= today.AddDays(-6) : d >= today.AddDays(-29);
    }
    /// <summary>Equal-length previous window (yesterday-so-far / prior 7 days); null for 30 days (no data before that).</summary>
    private static bool InPrev(Sale s, int r)
    {
        var t = s.Time.LocalDateTime; var today = DateTime.Today;
        if (r == 0) return t >= today.AddDays(-1) && t <= DateTime.Now.AddDays(-1);
        if (r == 1) return t.Date >= today.AddDays(-13) && t.Date <= today.AddDays(-7);
        return false;
    }

    private void Render()
    {
        if (root == null) return;
        try
        {
            double scroll = recentScroll?.VerticalOffset ?? 0;
            root.Children.Clear(); root.ColumnDefinitions.Clear(); recentScroll = null;
            var d = Hub.Dashboard;
            if (!d.RevenueConnected && d.RevenueErrorText.Length == 0) { root.Children.Add(EmptyState()); return; }
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(520) }); root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) }); root.ColumnDefinitions.Add(new ColumnDefinition());
            var sales = d.Sales;
            root.Children.Add(Summary(sales, d.RevenueErrorText));
            var right = Recent(sales); Grid.SetColumn(right, 2); root.Children.Add(right);
            if (scroll > 0 && recentScroll != null) { var sv = recentScroll; sv.Dispatcher.BeginInvoke(new Action(() => sv.ScrollToVerticalOffset(scroll)), System.Windows.Threading.DispatcherPriority.Loaded); }
        }
        catch { /* never take the island down */ }
    }

    private FrameworkElement EmptyState()
    {
        var col = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, Width = 460 };
        var glyph = Ui.Glyph(Glyph, 34, Ui.B(Accent)); glyph.Margin = new Thickness(0, 0, 0, 10); col.Children.Add(glyph);
        var t = Ui.Text("Your sales, without opening a dashboard", 20, Ui.Ink, FontWeights.SemiBold); t.HorizontalAlignment = HorizontalAlignment.Center; col.Children.Add(t);
        var s = Ui.Text("Connect Stripe or Polar with a read-only key and today's takings live in the notch. Every sale lands as a banner with the amount, and the running total stays on the glass after it fades.", 14, Ui.Dim);
        s.TextWrapping = TextWrapping.Wrap; s.TextAlignment = TextAlignment.Center; s.Margin = new Thickness(0, 8, 0, 18); col.Children.Add(s);
        var b = Ui.Button("Open Settings", Accent, () => Hub.OpenSettings?.Invoke(), 38); b.HorizontalAlignment = HorizontalAlignment.Center; col.Children.Add(b);
        var host = Ui.Card(col, 24); return host;
    }

    private FrameworkElement Summary(List<Sale> sales, string error)
    {
        var g = new Grid();
        g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); g.RowDefinitions.Add(new RowDefinition());
        // header: range switch + (error) + refresh
        var head = new Grid { Height = 36 }; head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); head.ColumnDefinitions.Add(new ColumnDefinition()); head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var seg = new SegmentedControl(new[] { "Today", "7 days", "30 days" }, range, Accent, i => { range = i; Render(); }) { VerticalAlignment = VerticalAlignment.Center }; head.Children.Add(seg);
        if (error.Length > 0) { var e = Ui.Text(error.Replace("\n", "  "), 12.5, Ui.Orange); e.Margin = new Thickness(12, 0, 8, 0); e.VerticalAlignment = VerticalAlignment.Center; e.ToolTip = error; Grid.SetColumn(e, 1); head.Children.Add(e); }
        var refresh = Ui.IconButton("", "Refresh now", () => Hub.RefreshNow?.Invoke(), 34); Grid.SetColumn(refresh, 2); head.Children.Add(refresh);
        g.Children.Add(head);

        var cur = sales.Where(s => InRange(s, range)).ToList(); var prev = sales.Where(s => InPrev(s, range)).ToList();
        var totals = cur.GroupBy(s => s.Currency).Select(x => (Cur: x.Key, Sum: x.Sum(y => y.Amount))).OrderByDescending(x => Math.Abs(x.Sum)).ToList();
        string heroCur = totals.Count > 0 ? totals[0].Cur : (sales.Count > 0 ? sales[0].Currency : "");
        decimal hero = totals.Count > 0 ? totals[0].Sum : 0;

        var heroRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0) };
        heroRow.Children.Add(Ui.Text(heroCur.Length > 0 ? RevenueFmt.Full(hero, heroCur) : "0", 42, Ui.Ink, FontWeights.SemiBold));
        if (range < 2 && heroCur.Length > 0)
        {
            decimal before = prev.Where(s => s.Currency == heroCur).Sum(s => s.Amount);
            if (before > 0)
            {
                double pct = (double)((hero - before) / before) * 100; bool up = pct >= 0;
                var chip = Ui.Chipped((up ? "▲ " : "▼ ") + Math.Abs(pct).ToString("0", CultureInfo.InvariantCulture) + "% vs " + (range == 0 ? "yesterday" : "prior 7 days"), up ? Ui.C("#30D158") : Ui.C("#FF453A"), 12.5);
                chip.Margin = new Thickness(14, 0, 0, 7); chip.VerticalAlignment = VerticalAlignment.Bottom; heroRow.Children.Add(chip);
            }
        }
        Grid.SetRow(heroRow, 1); g.Children.Add(heroRow);

        var sub = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(2, 2, 0, 0) };
        int orders = cur.Count(s => s.Amount > 0);
        sub.Children.Add(Ui.Text("net · " + orders + (orders == 1 ? " order" : " orders"), 14, Ui.Dim));
        foreach (var o in totals.Skip(1).Take(3)) { var c = Ui.Chipped(RevenueFmt.Full(o.Sum, o.Cur), Ui.C("#64D2FF"), 12.5); c.Margin = new Thickness(10, 0, 0, 0); sub.Children.Add(c); }
        Grid.SetRow(sub, 2); g.Children.Add(sub);

        // chart (hero currency only): cumulative per hour for today, per day otherwise
        var chartBox = new Grid { Margin = new Thickness(0, 12, 0, 0) }; chartBox.RowDefinitions.Add(new RowDefinition()); chartBox.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var series = Series(cur.Where(s => s.Currency == heroCur).ToList());
        var chart = new AreaChart(); chart.Set(series, Accent); chartBox.Children.Add(chart);
        if (series.Count < 2 || series.All(v => v == 0)) { var none = Ui.Text("No sales in this period yet", 13.5, Ui.Dim); none.HorizontalAlignment = HorizontalAlignment.Center; none.VerticalAlignment = VerticalAlignment.Center; chartBox.Children.Add(none); }
        var axis = new Grid { Margin = new Thickness(0, 5, 0, 0) };
        var l = Ui.Text(range == 0 ? "12 AM" : DateTime.Today.AddDays(range == 1 ? -6 : -29).ToString("MMM d", CultureInfo.CurrentCulture), 12, Ui.Dim); var r = Ui.Text(range == 0 ? "now" : "Today", 12, Ui.Dim); r.HorizontalAlignment = HorizontalAlignment.Right;
        axis.Children.Add(l); axis.Children.Add(r); Grid.SetRow(axis, 1); chartBox.Children.Add(axis);
        Grid.SetRow(chartBox, 3); g.Children.Add(chartBox);
        return Ui.Card(g, 16);
    }

    private List<double> Series(List<Sale> cur)
    {
        var res = new List<double>();
        if (range == 0)
        {
            int hours = DateTime.Now.Hour + 1; var per = new double[hours];
            foreach (var s in cur) { int h = s.Time.LocalDateTime.Hour; if (h < hours) per[h] += (double)s.Amount; }
            double run = 0; if (hours < 2) res.Add(0);
            foreach (var v in per) { run += v; res.Add(run); }
        }
        else
        {
            int days = range == 1 ? 7 : 30; var per = new double[days]; var start = DateTime.Today.AddDays(-(days - 1));
            foreach (var s in cur) { int i = (s.Time.LocalDateTime.Date - start).Days; if (i >= 0 && i < days) per[i] += (double)s.Amount; }
            res.AddRange(per);
        }
        return res;
    }

    private FrameworkElement Recent(List<Sale> sales)
    {
        var list = new StackPanel();
        if (sales.Count == 0) list.Children.Add(Ui.Text("No sales in the last 30 days", 14, Ui.Dim));
        foreach (var s in sales.Take(40))
        {
            var row = new Border { CornerRadius = new CornerRadius(12), Padding = new Thickness(10, 7, 10, 7), Background = Brushes.Transparent, Margin = new Thickness(0, 0, 0, 2) };
            row.MouseEnter += (_, _) => row.Background = Ui.Chip; row.MouseLeave += (_, _) => row.Background = Brushes.Transparent;
            var g = new Grid(); g.ColumnDefinitions.Add(new ColumnDefinition()); g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var col = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            string desc = string.IsNullOrWhiteSpace(s.Description) ? "Sale" : s.Description; row.ToolTip = desc + "  ·  " + s.Time.LocalDateTime.ToString("g", CultureInfo.CurrentCulture);
            col.Children.Add(Ui.Text(desc, 14.5, Ui.Ink, FontWeights.Medium)); col.Children.Add(Ui.Text(RevenueFmt.Age(s.Time), 12.5, Ui.Dim));
            var amount = Ui.Text(RevenueFmt.Full(s.Amount, s.Currency), 15, s.Amount < 0 ? Ui.Red : Ui.Ink, FontWeights.Bold); amount.Margin = new Thickness(10, 0, 10, 0); amount.VerticalAlignment = VerticalAlignment.Center; Grid.SetColumn(amount, 1);
            var chip = Ui.Chipped(s.Provider, s.Provider.Equals("Stripe", StringComparison.OrdinalIgnoreCase) ? Ui.C("#8B7CFF") : Ui.C("#64D2FF"), 12); Grid.SetColumn(chip, 2);
            g.Children.Add(col); g.Children.Add(amount); g.Children.Add(chip); row.Child = g; list.Children.Add(row);
        }
        recentScroll = Ui.Scroll(list);
        return Ui.Titled("Recent", "", recentScroll, Accent, null, 14);
    }
}
