using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
namespace Island;

/// <summary>Claude Code usage from the local transcripts: tokens, prompts, tool calls, sessions, streak, projects and a consistency grid.</summary>
internal sealed class ClaudePanel : IslandPanel
{
    public override string Id => "claude";
    public override string Title => "Claude";
    public override string Glyph => "";
    public override int Order => 80;
    public override Color Accent => Ui.C("#D97757");

    private ClaudeSnapshot? snap;
    private bool busy; private DateTime lastRefresh = DateTime.MinValue;
    private int range; // 0 = today, 1 = 7 days
    private TextBlock tokV = null!, tokSub = null!, msgV = null!, toolV = null!, sessV = null!, streakV = null!, status = null!, bestV = null!, bestSub = null!, activeV = null!, projName = null!, projSub = null!;
    private ClaudeGridView grid = null!; private StackPanel projList = null!; private Border empty = null!;

    public override string? RestingText => snap is { Exists: true } s ? "Claude " + Compact(s.TokToday) + " tok" : busy ? "Claude usage loading…" : "Claude · no usage history";

    public override void Start() => Refresh(true);
    public override void Tick() { if ((DateTime.UtcNow - lastRefresh).TotalMinutes >= 5) Refresh(false); }
    public override void OnShown() => Refresh(false);
    public override void OnHidden() => Refresh(false);

    protected override FrameworkElement Build()
    {
        var root = new Grid { Width = Ui.ContentW, Height = Ui.ContentH };
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(540) }); root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) }); root.ColumnDefinitions.Add(new ColumnDefinition());
        var left = new Grid(); left.RowDefinitions.Add(new RowDefinition { Height = new GridLength(112) }); left.RowDefinitions.Add(new RowDefinition { Height = new GridLength(10) }); left.RowDefinitions.Add(new RowDefinition());

        // --- stats strip ---
        var strip = new Grid(); foreach (double w in new[] { 1.5, 1, 1, 1, 1 }) strip.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(w, GridUnitType.Star) });
        strip.Children.Add(StatBox(0, "Tokens today", 34, out tokV, out tokSub, true));
        strip.Children.Add(StatBox(1, "Messages", 28, out msgV, out _, false));
        strip.Children.Add(StatBox(2, "Tool calls", 28, out toolV, out _, false));
        strip.Children.Add(StatBox(3, "Sessions", 28, out sessV, out _, false));
        strip.Children.Add(StatBox(4, "Streak", 28, out streakV, out _, false));
        left.Children.Add(Ui.Card(strip, 14));

        // --- consistency grid ---
        grid = new ClaudeGridView { Width = 16 * 20 - 3, Height = 7 * 20 - 3, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        var gb = new Grid(); gb.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(334) }); gb.ColumnDefinitions.Add(new ColumnDefinition());
        gb.Children.Add(grid);
        var info = new StackPanel { Margin = new Thickness(8, 0, 0, 0) }; Grid.SetColumn(info, 1);
        bestV = Ui.Text("–", 22, Ui.Ink, FontWeights.SemiBold); bestSub = Ui.Text(" ", 12.5, Ui.Dim);
        activeV = Ui.Text("–", 22, Ui.Ink, FontWeights.SemiBold);
        info.Children.Add(Ui.Label("Busiest day")); info.Children.Add(bestV); info.Children.Add(bestSub);
        var al = Ui.Label("Active days"); al.Margin = new Thickness(0, 8, 0, 0); info.Children.Add(al); info.Children.Add(activeV);
        var legend = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 9, 0, 0) };
        var less = Ui.Text("Less", 12, Ui.Dim); less.VerticalAlignment = VerticalAlignment.Center; legend.Children.Add(less);
        for (int i = 0; i < 5; i++) legend.Children.Add(new Border { Width = 13, Height = 13, CornerRadius = new CornerRadius(4), Margin = new Thickness(5, 0, 0, 0), Background = ClaudeGridView.Shade(Accent, i), VerticalAlignment = VerticalAlignment.Center });
        var more = Ui.Text("More", 12, Ui.Dim); more.Margin = new Thickness(6, 0, 0, 0); more.VerticalAlignment = VerticalAlignment.Center; legend.Children.Add(more);
        info.Children.Add(legend); gb.Children.Add(info);
        status = Ui.Text("", 12, Ui.Dim); status.Margin = new Thickness(0, 0, 8, 0); status.VerticalAlignment = VerticalAlignment.Center;
        var refresh = Ui.IconButton("", "Refresh", () => Refresh(true), 28);
        var right = new StackPanel { Orientation = Orientation.Horizontal }; right.Children.Add(status); right.Children.Add(refresh);
        var gridCard = Ui.Titled("Consistency · last 16 weeks", "", gb, Accent, right, 14); Grid.SetRow(gridCard, 2); left.Children.Add(gridCard);
        root.Children.Add(left);

        // --- projects ---
        var pb = new Grid(); pb.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); pb.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); pb.RowDefinitions.Add(new RowDefinition());
        var cur = new StackPanel();
        cur.Children.Add(Ui.Label("Current project"));
        projName = Ui.Text("–", 21, Ui.B(Accent), FontWeights.SemiBold); projName.Margin = new Thickness(0, 2, 0, 0); cur.Children.Add(projName);
        projSub = Ui.Text(" ", 12.5, Ui.Dim); cur.Children.Add(projSub);
        pb.Children.Add(cur);
        var div = Ui.Divider(); div.Margin = new Thickness(0, 10, 0, 8); Grid.SetRow(div, 1); pb.Children.Add(div);
        projList = new StackPanel(); var sc = Ui.Scroll(projList); Grid.SetRow(sc, 2); pb.Children.Add(sc);
        var seg = new SegmentedControl(new[] { "Today", "7 days" }, range, Accent, i => { range = i; ApplyProjects(); });
        var projCard = Ui.Titled("Projects", "", pb, Accent, seg, 16); Grid.SetColumn(projCard, 2); root.Children.Add(projCard);

        // --- empty state (no ~/.claude/projects) ---
        var es = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        var eg = Ui.Glyph("", 34, Ui.B(Accent)); eg.Margin = new Thickness(0, 0, 0, 8); es.Children.Add(eg);
        var e1 = Ui.Text("No Claude Code history found", 18, Ui.Ink, FontWeights.SemiBold); e1.HorizontalAlignment = HorizontalAlignment.Center; es.Children.Add(e1);
        var e2 = Ui.Text("Usage appears here once Claude Code has run on this PC  (~\\.claude\\projects).", 13, Ui.Dim); e2.HorizontalAlignment = HorizontalAlignment.Center; e2.Margin = new Thickness(0, 4, 0, 0); es.Children.Add(e2);
        empty = Ui.Card(es); empty.Visibility = Visibility.Collapsed; Grid.SetColumnSpan(empty, 3); root.Children.Add(empty);

        if (snap != null) Apply(); else if (busy) status.Text = "Reading…";
        return root;
    }

    private static Grid StatBox(int col, string label, double size, out TextBlock value, out TextBlock sub, bool first)
    {
        var g = new Grid { Margin = new Thickness(col == 0 ? 0 : 14, 0, 0, 0) }; Grid.SetColumn(g, col);
        if (col > 0) g.Children.Add(new Border { Width = 1, Background = Ui.Line, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(-8, 4, 0, 4) });
        var p = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        p.Children.Add(Ui.Label(label)); value = Ui.Text("–", size, Ui.Ink, FontWeights.SemiBold); value.Margin = new Thickness(0, 2, 0, 0); p.Children.Add(value);
        sub = Ui.Text(first ? "cache reads –" : "", 12.5, Ui.Dim, FontWeights.Medium); if (first) p.Children.Add(sub);
        g.Children.Add(p); return g;
    }

    private void Refresh(bool force)
    {
        if (busy) return;
        if (!force && (DateTime.UtcNow - lastRefresh).TotalSeconds < 20) return;
        busy = true; lastRefresh = DateTime.UtcNow;
        if (IsBuilt) status.Text = "Reading…";
        Task.Run(() =>
        {
            ClaudeSnapshot? result = null;
            try { result = ClaudeUsage.Compute(); } catch { }
            var disp = Application.Current?.Dispatcher; if (disp == null) { busy = false; return; }
            disp.BeginInvoke(new Action(() => { busy = false; if (result != null) snap = result; if (IsBuilt) Apply(); }));
        });
    }

    private void Apply()
    {
        var s = snap; if (s == null) return;
        try
        {
            empty.Visibility = s.Exists ? Visibility.Collapsed : Visibility.Visible;
            tokV.Text = Compact(s.TokToday); tokSub.Text = "+ " + Compact(s.CacheToday) + " cache reads";
            msgV.Text = s.MsgToday.ToString("N0", CultureInfo.CurrentCulture); toolV.Text = s.ToolToday.ToString("N0", CultureInfo.CurrentCulture); sessV.Text = s.SessToday.ToString(CultureInfo.CurrentCulture);
            streakV.Text = s.Streak + "d";
            status.Text = "Updated " + s.At.ToString("t", CultureInfo.CurrentCulture);
            grid.Set(s, Accent);
            bestV.Text = s.BestDay > 0 ? Compact(s.BestDay) + " tok" : "–"; bestSub.Text = s.BestDay > 0 ? s.BestDate.ToString("ddd, MMM d", CultureInfo.CurrentCulture) : "no activity yet";
            activeV.Text = s.ActiveDays + " of " + s.DayTokens.Count;
            projName.Text = s.CurrentProject.Length > 0 ? s.CurrentProject : "–";
            projSub.Text = s.CurrentProject.Length > 0 ? "active " + Ago(s.LastActive) : "no sessions yet";
            ApplyProjects();
        }
        catch { }
    }

    private void ApplyProjects()
    {
        if (projList == null) return; projList.Children.Clear();
        var s = snap; if (s == null) return;
        var list = range == 0 ? s.ProjToday : s.Proj7;
        if (list.Count == 0) { var t = Ui.Text(s.Exists ? (range == 0 ? "No tokens burned today yet" : "No activity in the last 7 days") : "", 13.5, Ui.Dim); t.Margin = new Thickness(2, 4, 0, 0); projList.Children.Add(t); return; }
        long max = Math.Max(1, list.Max(p => p.Tok)); long total = Math.Max(1, list.Sum(p => p.Tok));
        foreach (var (name, tok) in list.Take(30))
        {
            double frac = (double)tok / max;
            var row = new Grid { Height = 34, Width = 292, Margin = new Thickness(0, 0, 0, 4), HorizontalAlignment = HorizontalAlignment.Left };
            var track = new Border { CornerRadius = new CornerRadius(9), Background = Ui.B(Accent, 0x10) }; row.Children.Add(track);
            row.Children.Add(new Border { CornerRadius = new CornerRadius(9), Background = Ui.B(Accent, 0x38), Width = Math.Max(8, 292 * frac), HorizontalAlignment = HorizontalAlignment.Left });
            var n = Ui.Text(name, 14, Ui.Ink, FontWeights.Medium); n.VerticalAlignment = VerticalAlignment.Center; n.Margin = new Thickness(11, 0, 92, 0); row.Children.Add(n);
            var v = Ui.Text(Compact(tok) + "  ·  " + (100 * tok / total) + "%", 12.5, Ui.Soft, FontWeights.SemiBold); v.VerticalAlignment = VerticalAlignment.Center; v.HorizontalAlignment = HorizontalAlignment.Right; v.Margin = new Thickness(0, 0, 10, 0); row.Children.Add(v);
            projList.Children.Add(row);
        }
    }

    internal static string Compact(long n)
    {
        double v = n;
        if (v >= 1e9) return (v / 1e9).ToString(v >= 1e10 ? "0" : "0.#", CultureInfo.InvariantCulture) + "B";
        if (v >= 1e6) return (v / 1e6).ToString(v >= 1e7 ? "0" : "0.#", CultureInfo.InvariantCulture) + "M";
        if (v >= 1e3) return (v / 1e3).ToString(v >= 1e4 ? "0" : "0.#", CultureInfo.InvariantCulture) + "K";
        return n.ToString(CultureInfo.InvariantCulture);
    }
    private static string Ago(DateTime t)
    {
        var d = DateTime.Now - t; if (d.TotalMinutes < 1) return "just now"; if (d.TotalHours < 1) return (int)d.TotalMinutes + " min ago";
        if (d.TotalDays < 1) return (int)d.TotalHours + " h ago"; return (int)d.TotalDays + (d.TotalDays < 2 ? " day ago" : " days ago");
    }
}

/// <summary>GitHub-style consistency grid (7 rows x N week columns), drawn in one OnRender.</summary>
internal sealed class ClaudeGridView : FrameworkElement
{
    private ClaudeSnapshot? s; private Color accent = Colors.White;
    private const double Cell = 17, Gap = 3;
    private static readonly Brush Empty = Ui.B("#1E1E22");
    public void Set(ClaudeSnapshot snap, Color a) { s = snap; accent = a; InvalidateVisual(); }
    /// <summary>Level 0 (empty) .. 4 (strongest) shade of the accent.</summary>
    public static Brush Shade(Color a, int level) => level <= 0 ? Empty : Ui.B(a, (byte)(level switch { 1 => 0x50, 2 => 0x80, 3 => 0xB4, _ => 0xFF }));
    protected override void OnRender(DrawingContext dc)
    {
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, ActualWidth, ActualHeight));
        if (s == null) return;
        var brushes = Enumerable.Range(0, 5).Select(i => Shade(accent, i)).ToArray(); var pen = new Pen(Ui.Ink, 1.6);
        for (int c = 0; c < s.GridCols; c++)
            for (int r = 0; r < 7; r++)
            {
                var d = s.GridStart.AddDays(c * 7 + r); if (d > DateTime.Today) continue;
                long v = s.DayTokens.GetValueOrDefault(d); double f = s.BestDay > 0 ? (double)v / s.BestDay : 0;
                int lvl = v <= 0 ? 0 : f > .75 ? 4 : f > .5 ? 3 : f > .25 ? 2 : 1;
                var rect = new Rect(c * (Cell + Gap), r * (Cell + Gap), Cell, Cell);
                dc.DrawRoundedRectangle(brushes[lvl], null, rect, 4.5, 4.5);
                if (d == DateTime.Today) dc.DrawRoundedRectangle(null, pen, new Rect(rect.X + .8, rect.Y + .8, Cell - 1.6, Cell - 1.6), 4, 4);
            }
    }
}
