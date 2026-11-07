using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
namespace Island;

/// <summary>Real screen time: today's total, most-used app, seven-day chart and a per-app breakdown with real icons.</summary>
internal sealed class ScreenTimePanel : IslandPanel
{
    public override string Id => "screentime";
    public override string Title => "Screen Time";
    public override string Glyph => "";
    public override int Order => 70;
    public override Color Accent => Ui.C("#6366F1");

    private readonly ScreenTimeTracker tracker = new();
    private readonly Dictionary<string, ImageSource?> icons = new(StringComparer.OrdinalIgnoreCase);
    private bool shown; private int sinceUi;
    private TextBlock totalV = null!, deltaV = null!, topName = null!, topSub = null!, appsV = null!, avgV = null!;
    private BarChart chart = null!; private StackPanel list = null!; private TextBlock hint = null!;

    public override void Start() { tracker.Load(); tracker.Tick(); }
    public override void Tick()
    {
        tracker.Tick();
        if (shown && IsBuilt && ++sinceUi >= 5) Refresh();
    }
    public override void OnShown() { shown = true; if (IsBuilt) Refresh(); }
    public override void OnHidden() { shown = false; }
    public override string? RestingText => "Today " + Dur(tracker.TotalOn(DateTime.Today));

    protected override FrameworkElement Build()
    {
        var g = new Grid { Width = Ui.ContentW, Height = Ui.ContentH };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(520) }); g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) }); g.ColumnDefinitions.Add(new ColumnDefinition());

        // --- left: today + 7 days ---
        var body = new Grid(); body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(86) }); body.RowDefinitions.Add(new RowDefinition());
        var stats = new Grid(); foreach (double w in new[] { 1.45, 1.35, 0.6 }) stats.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(w, GridUnitType.Star) });
        var s1 = new StackPanel(); s1.Children.Add(Ui.Label("Screen time"));
        totalV = Ui.Text("–", 36, Ui.Ink, FontWeights.SemiBold); s1.Children.Add(totalV); deltaV = Ui.Text(" ", 12.5, Ui.Dim, FontWeights.Medium); s1.Children.Add(deltaV); stats.Children.Add(s1);
        var s2 = new StackPanel { Margin = new Thickness(6, 0, 8, 0) }; Grid.SetColumn(s2, 1); s2.Children.Add(Ui.Label("Most used"));
        topName = Ui.Text("–", 20, Ui.B(Accent), FontWeights.SemiBold); topName.Margin = new Thickness(0, 7, 0, 0); s2.Children.Add(topName);
        topSub = Ui.Text(" ", 12.5, Ui.Dim, FontWeights.Medium); topSub.Margin = new Thickness(0, 2, 0, 0); s2.Children.Add(topSub); stats.Children.Add(s2);
        var s3 = new StackPanel(); Grid.SetColumn(s3, 2); s3.Children.Add(Ui.Label("Apps")); appsV = Ui.Text("0", 36, Ui.Ink, FontWeights.SemiBold); s3.Children.Add(appsV); stats.Children.Add(s3);
        body.Children.Add(stats);
        var chartBox = new Grid(); Grid.SetRow(chartBox, 1);
        chartBox.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); chartBox.RowDefinitions.Add(new RowDefinition());
        var cl = Ui.Label("Last 7 days"); cl.Margin = new Thickness(0, 0, 0, 2); chartBox.Children.Add(cl);
        chart = new BarChart(); Grid.SetRow(chart, 1); chartBox.Children.Add(chart); body.Children.Add(chartBox);
        avgV = Ui.Text("", 12.5, Ui.B(Accent), FontWeights.SemiBold);
        var avgChip = new Border { Background = Ui.B(Accent, 0x26), CornerRadius = new CornerRadius(10), Padding = new Thickness(9, 2, 9, 2), Child = avgV };
        g.Children.Add(Ui.Titled("Today", "", body, Accent, avgChip));

        // --- right: by app ---
        var rb = new Grid(); hint = Ui.Text("", 13.5, Ui.Dim); hint.TextWrapping = TextWrapping.Wrap; hint.VerticalAlignment = VerticalAlignment.Top; hint.Margin = new Thickness(2, 4, 0, 0);
        list = new StackPanel(); rb.Children.Add(Ui.Scroll(list)); rb.Children.Add(hint);
        var right = Ui.Titled("By app", "", rb, Accent); Grid.SetColumn(right, 2); g.Children.Add(right);
        Refresh(); return g;
    }

    private void Refresh()
    {
        sinceUi = 0;
        try
        {
            var today = DateTime.Today; var apps = tracker.AppsOn(today); long total = apps.Values.Sum(), yest = tracker.TotalOn(today.AddDays(-1));
            totalV.Text = Dur(total);
            if (yest > 0) { long d = total - yest; deltaV.Text = (d >= 0 ? "▲ " : "▼ ") + Dur(Math.Abs(d)) + (d >= 0 ? " more" : " less") + " than yesterday"; deltaV.Foreground = d >= 0 ? Ui.Orange : Ui.Green; }
            else { deltaV.Text = total > 0 ? "so far today" : "tracking…"; deltaV.Foreground = Ui.Dim; }
            var top = apps.OrderByDescending(a => a.Value).FirstOrDefault();
            topName.Text = top.Key != null ? tracker.NameOf(top.Key) : "–"; topSub.Text = top.Key != null ? Dur(top.Value) + " today" : "nothing yet";
            appsV.Text = apps.Count.ToString(CultureInfo.CurrentCulture);

            // 7-day chart: M T W T F S S ending today
            var vals = new List<double>(); var labels = new List<string>(); var tops = new List<string?>(); long sum = 0; int peak = 0;
            for (int i = 6; i >= 0; i--) { var day = today.AddDays(-i); long t = tracker.TotalOn(day); vals.Add(t); sum += t; labels.Add(day.DayOfWeek.ToString()[0].ToString()); if (t > vals[peak]) peak = vals.Count - 1; }
            for (int i = 0; i < 7; i++) tops.Add(i == 6 || (i == peak && vals[i] > 0) ? Dur((long)vals[i]) : null);
            chart.Set(vals, labels, Accent, tops, 6);
            avgV.Text = "avg " + Dur(sum / 7) + " / day";

            // per-app list
            list.Children.Clear();
            if (apps.Count == 0) { hint.Text = tracker.HasAnyData() ? "Nothing tracked yet today. Switch to an app and it will show up here." : "Tracking has just started. Use your apps as usual and their time will appear here."; hint.Visibility = Visibility.Visible; return; }
            hint.Visibility = Visibility.Collapsed;
            long max = apps.Values.Max();
            foreach (var kv in apps.OrderByDescending(a => a.Value).Take(40)) list.Children.Add(Row(kv.Key, kv.Value, max));
        }
        catch { }
    }

    private Grid Row(string exe, long secs, long max)
    {
        const double W = 322, H = 42;
        var row = new Grid { Width = W, Height = H, Margin = new Thickness(0, 0, 0, 4), HorizontalAlignment = HorizontalAlignment.Left };
        row.Children.Add(new Border { CornerRadius = new CornerRadius(11), Background = Ui.B(Accent, 0x10) });
        row.Children.Add(new Border { CornerRadius = new CornerRadius(11), Background = Ui.B(Accent, 0x3A), Width = Math.Max(10, W * secs / Math.Max(1, max)), HorizontalAlignment = HorizontalAlignment.Left });
        string name = tracker.NameOf(exe);
        var icon = IconFor(exe); FrameworkElement ic;
        if (icon != null) ic = new Image { Source = icon, Width = 24, Height = 24 };
        else
        {
            var letter = Ui.Text(name.Length > 0 ? name[..1].ToUpperInvariant() : "?", 14, Ui.Ink, FontWeights.Bold); letter.HorizontalAlignment = HorizontalAlignment.Center; letter.VerticalAlignment = VerticalAlignment.Center;
            ic = new Border { Width = 24, Height = 24, CornerRadius = new CornerRadius(7), Background = Ui.B(Accent, 0x66), Child = letter };
        }
        ic.HorizontalAlignment = HorizontalAlignment.Left; ic.VerticalAlignment = VerticalAlignment.Center; ic.Margin = new Thickness(10, 0, 0, 0); row.Children.Add(ic);
        var n = Ui.Text(name, 14, Ui.Ink, FontWeights.Medium); n.VerticalAlignment = VerticalAlignment.Center; n.Margin = new Thickness(44, 0, 82, 0); row.Children.Add(n);
        var d = Ui.Text(Dur(secs), 13.5, Ui.Soft, FontWeights.SemiBold); d.VerticalAlignment = VerticalAlignment.Center; d.HorizontalAlignment = HorizontalAlignment.Right; d.Margin = new Thickness(0, 0, 11, 0); row.Children.Add(d);
        return row;
    }

    private ImageSource? IconFor(string exe)
    {
        if (icons.TryGetValue(exe, out var cached)) return cached;
        ImageSource? src = null;
        try
        {
            var path = tracker.PathOf(exe);
            if (path != null && System.IO.File.Exists(path))
            {
                using var ico = System.Drawing.Icon.ExtractAssociatedIcon(path);
                if (ico != null) { var bs = Imaging.CreateBitmapSourceFromHIcon(ico.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions()); bs.Freeze(); src = bs; }
            }
        }
        catch { }
        icons[exe] = src; return src;
    }

    private static string Dur(long s)
    {
        if (s >= 3600) return $"{s / 3600}h {s % 3600 / 60}m";
        if (s >= 60) return $"{s / 60}m";
        return s + "s";
    }
}
