using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
namespace Island;

/// <summary>Persisted per-day counters and hydration settings.</summary>
public sealed class TimersData
{
    public string Date { get; set; } = "";
    public int Cycles { get; set; }
    public int Water { get; set; }
    public bool HydrationOn { get; set; }
    public int HydrationMin { get; set; } = 30;
}

/// <summary>Pomodoro, countdown, lap stopwatch and hydration nudge. All clocks are timestamp based (UtcNow / Stopwatch), never tick counting.</summary>
internal sealed class TimersPanel : IslandPanel
{
    public override string Id => "timers";
    public override string Title => "Timers";
    public override string Glyph => "";
    public override int Order => 50;
    public override Color Accent => Ui.C("#FF9F0A");

    private const int FocusMin = 25, BreakMin = 5;
    private static readonly int[] QuickMin = { 5, 10, 25 };
    private static readonly int[] WaterMin = { 15, 30, 60 };

    private TimersData data = new();
    private bool shown;

    // pomodoro
    private bool pRun, pBreak; private DateTime pEnd; private TimeSpan pRemain = TimeSpan.FromMinutes(FocusMin);
    // countdown
    private bool cRun; private DateTime cEnd; private int cTotalMin = 10; private TimeSpan cRemain = TimeSpan.FromMinutes(10);
    // stopwatch
    private readonly Stopwatch sw = new(); private bool swRun; private readonly List<TimeSpan> laps = new();
    // hydration
    private DateTime hNext;

    // ui
    private Ring ring = null!; private TextBlock pTime = null!, pPhase = null!, pCycleText = null!, phaseChip = null!, cTime = null!, swTime = null!, hTime = null!, hCount = null!;
    private Border pPlay = null!, cPlay = null!, swPlay = null!, hToggle = null!, hKnob = null!;
    private StackPanel dots = null!, lapList = null!; private TextBox cInput = null!;
    private readonly List<Border> quickChips = new(), waterChips = new();
    private DispatcherTimer? fast;
    private int paintedLaps = -1, paintedCycles = -1;

    public override void Start()
    {
        data = Store.Load<TimersData>("timers"); EnsureToday();
        if (data.HydrationMin < 5) data.HydrationMin = 30;
        if (data.HydrationOn) hNext = DateTime.UtcNow.AddMinutes(data.HydrationMin);
    }

    public override string? RestingText
    {
        get
        {
            if (pRun) return (pBreak ? "Break " : "Focus ") + Clock(Remaining(pRun, pEnd, pRemain));
            if (cRun) return "Timer " + Clock(Remaining(cRun, cEnd, cRemain));
            if (swRun) return "Stopwatch " + Elapsed(sw.Elapsed, false);
            return null;
        }
    }

    public override string? ActiveTaskText => RestingText;

    public override void Tick()
    {
        try
        {
            EnsureToday();
            if (pRun && DateTime.UtcNow >= pEnd) PomoFinished();
            if (cRun && DateTime.UtcNow >= cEnd) CountdownFinished();
            if (data.HydrationOn && DateTime.UtcNow >= hNext)
            {
                hNext = DateTime.UtcNow.AddMinutes(data.HydrationMin);
                if (Settings.Load().HydrationAlerts) Hub.Alert("Time to hydrate", "Have a glass of water", "\U0001F4A7", "#5AC8FA", false);
            }
            if (IsBuilt && shown) UpdateAll();
        }
        catch { }
    }

    public override void OnShown() { shown = true; if (IsBuilt) UpdateAll(); SyncFast(); }
    public override void OnHidden() { shown = false; SyncFast(); }

    // ---------------- logic ----------------
    private static TimeSpan Remaining(bool run, DateTime end, TimeSpan paused) => run ? TimeSpan.FromTicks(Math.Max(0, (end - DateTime.UtcNow).Ticks)) : paused;
    private void EnsureToday()
    {
        string today = DateTime.Today.ToString("yyyy-MM-dd");
        if (data.Date != today) { data.Date = today; data.Cycles = 0; data.Water = 0; Save(); }
    }
    private void Save() => Store.Save("timers", data);
    private static TimeSpan PhaseLen(bool brk) => TimeSpan.FromMinutes(brk ? BreakMin : FocusMin);

    private void PomoToggle()
    {
        if (pRun) { pRemain = Remaining(true, pEnd, pRemain); pRun = false; }
        else { if (pRemain <= TimeSpan.Zero) pRemain = PhaseLen(pBreak); pEnd = DateTime.UtcNow + pRemain; pRun = true; }
        UpdateAll();
    }
    private void PomoReset() { pRun = false; pBreak = false; pRemain = PhaseLen(false); UpdateAll(); }
    private void PomoSkip() { pBreak = !pBreak; pRemain = PhaseLen(pBreak); if (pRun) pEnd = DateTime.UtcNow + pRemain; UpdateAll(); }
    private void PomoFinished()
    {
        if (!pBreak)
        {
            data.Cycles++; Save();
            if (Settings.Load().TimerAlerts) Hub.Alert("Focus complete", "Take a " + BreakMin + " minute break", "", "#FF9F0A");
        }
        else if (Settings.Load().TimerAlerts) Hub.Alert("Break over", "Ready for the next focus session", "", "#30D158");
        pBreak = !pBreak; pRemain = PhaseLen(pBreak); pEnd = DateTime.UtcNow + pRemain; // next phase starts right away
    }

    private void SetCountdown(int minutes)
    {
        cTotalMin = Math.Clamp(minutes, 1, 999); cRun = false; cRemain = TimeSpan.FromMinutes(cTotalMin); UpdateAll();
    }
    private void CountdownToggle()
    {
        if (cRun) { cRemain = Remaining(true, cEnd, cRemain); cRun = false; }
        else
        {
            if (!cRun && cInput.Text.Trim().Length > 0 && int.TryParse(cInput.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int m) && m > 0) { cTotalMin = Math.Clamp(m, 1, 999); cRemain = TimeSpan.FromMinutes(cTotalMin); cInput.Text = ""; }
            if (cRemain <= TimeSpan.Zero) cRemain = TimeSpan.FromMinutes(cTotalMin);
            cEnd = DateTime.UtcNow + cRemain; cRun = true;
        }
        UpdateAll();
    }
    private void CountdownFinished()
    {
        cRun = false; cRemain = TimeSpan.FromMinutes(cTotalMin);
        if (Settings.Load().TimerAlerts) Hub.Alert("Timer done", cTotalMin + (cTotalMin == 1 ? " minute is up" : " minutes are up"), "", "#FF9F0A");
    }

    private void SwToggle() { if (swRun) { sw.Stop(); swRun = false; } else { sw.Start(); swRun = true; } SyncFast(); UpdateAll(); }
    private void SwLapOrReset()
    {
        if (swRun) laps.Add(sw.Elapsed);
        else { sw.Reset(); laps.Clear(); }
        UpdateAll();
    }

    private void HydrationToggle()
    {
        data.HydrationOn = !data.HydrationOn; if (data.HydrationOn) hNext = DateTime.UtcNow.AddMinutes(data.HydrationMin); Save(); UpdateAll();
    }
    private void Drank() { EnsureToday(); data.Water++; if (data.HydrationOn) hNext = DateTime.UtcNow.AddMinutes(data.HydrationMin); Save(); UpdateAll(); }

    /// <summary>100 ms repaint only while the stopwatch runs on a visible panel.</summary>
    private void SyncFast()
    {
        bool want = swRun && shown && IsBuilt;
        if (want)
        {
            fast ??= new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(100) };
            fast.Tick -= FastTick; fast.Tick += FastTick; fast.Start();
        }
        else if (fast != null) { fast.Stop(); fast.Tick -= FastTick; }
    }
    private void FastTick(object? s, EventArgs e) { try { UpdateStopwatch(); } catch { } }

    // ---------------- formatting ----------------
    private static string Clock(TimeSpan t)
    {
        int s = (int)Math.Ceiling(t.TotalSeconds); if (s < 0) s = 0;
        return s >= 3600 ? $"{s / 3600}:{s % 3600 / 60:00}:{s % 60:00}" : $"{s / 60:00}:{s % 60:00}";
    }
    private static string Elapsed(TimeSpan t, bool tenths)
    {
        int h = (int)t.TotalHours, m = t.Minutes, s = t.Seconds, f = t.Milliseconds / 100;
        string main = h > 0 ? $"{h}:{m:00}:{s:00}" : $"{m}:{s:00}";
        return tenths ? main + "." + f : main;
    }

    // ---------------- ui ----------------
    protected override FrameworkElement Build()
    {
        var g = new Grid { Width = Ui.ContentW, Height = Ui.ContentH };
        g.ColumnDefinitions.Add(new ColumnDefinition()); g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) }); g.ColumnDefinitions.Add(new ColumnDefinition());
        g.RowDefinitions.Add(new RowDefinition()); g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(10) }); g.RowDefinitions.Add(new RowDefinition());
        void Place(UIElement e, int col, int row) { Grid.SetColumn(e, col); Grid.SetRow(e, row); g.Children.Add(e); }
        Place(BuildPomodoro(), 0, 0); Place(BuildCountdown(), 2, 0); Place(BuildStopwatch(), 0, 2); Place(BuildHydration(), 2, 2);
        UpdateAll(); return g;
    }

    private Border Round(string glyph, string tip, Action click, double size, bool primary)
    {
        var fill = primary ? Ui.B(Accent) : Ui.Chip; var hover = primary ? Ui.B(Accent, 0xDD) : Ui.ChipHover;
        var b = new Border { Width = size, Height = size, CornerRadius = new CornerRadius(size / 2), Background = fill, Cursor = Cursors.Hand, ToolTip = tip, Child = Ui.Glyph(glyph, size * .42, primary ? Ui.B("#1A1100") : Ui.Ink) };
        b.MouseEnter += (_, _) => b.Background = hover; b.MouseLeave += (_, _) => b.Background = fill;
        b.MouseLeftButtonUp += (_, e) => { click(); e.Handled = true; };
        return b;
    }
    private static void SetGlyph(Border b, string glyph) { if (b.Child is TextBlock t) t.Text = glyph; }
    private Border Chip(string text, Action click)
    {
        var t = Ui.Text(text, 13, Ui.Dim, FontWeights.SemiBold); t.VerticalAlignment = VerticalAlignment.Center;
        var b = new Border { CornerRadius = new CornerRadius(14), Padding = new Thickness(12, 0, 12, 0), Height = 28, Margin = new Thickness(0, 0, 6, 0), Cursor = Cursors.Hand, Background = Ui.Chip, Child = t };
        b.MouseLeftButtonUp += (_, e) => { click(); e.Handled = true; };
        return b;
    }
    private void PaintChip(Border b, bool on)
    {
        b.Background = on ? Ui.B(Accent, 0x33) : Ui.Chip; if (b.Child is TextBlock t) t.Foreground = on ? Ui.B(Accent) : Ui.Dim;
    }

    private Border BuildPomodoro()
    {
        var body = new Grid(); body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) }); body.ColumnDefinitions.Add(new ColumnDefinition());
        ring = new Ring { Width = 86, Height = 86, HorizontalAlignment = HorizontalAlignment.Left };
        var mid = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left, Width = 86 };
        pTime = Ui.Text("25:00", 23, Ui.Ink, FontWeights.SemiBold); pTime.HorizontalAlignment = HorizontalAlignment.Center;
        pPhase = Ui.Text("Focus", 12, Ui.Dim, FontWeights.SemiBold); pPhase.HorizontalAlignment = HorizontalAlignment.Center;
        mid.Children.Add(pTime); mid.Children.Add(pPhase); body.Children.Add(ring); body.Children.Add(mid);
        var right = new StackPanel { VerticalAlignment = VerticalAlignment.Center }; Grid.SetColumn(right, 1);
        right.Children.Add(Ui.Label("Cycles today"));
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 12), Height = 14 };
        dots = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        pCycleText = Ui.Text("", 13, Ui.Soft, FontWeights.SemiBold); pCycleText.Margin = new Thickness(6, 0, 0, 0); pCycleText.VerticalAlignment = VerticalAlignment.Center;
        row.Children.Add(dots); row.Children.Add(pCycleText); right.Children.Add(row);
        var btns = new StackPanel { Orientation = Orientation.Horizontal };
        pPlay = Round("", "Start / pause", PomoToggle, 40, true); btns.Children.Add(pPlay);
        var skip = Round("", "Skip phase", PomoSkip, 36, false); skip.Margin = new Thickness(8, 0, 0, 0); btns.Children.Add(skip);
        var reset = Round("", "Reset", PomoReset, 36, false); reset.Margin = new Thickness(8, 0, 0, 0); btns.Children.Add(reset);
        right.Children.Add(btns); body.Children.Add(right);
        phaseChip = Ui.Text("FOCUS", 11.5, Ui.B(Accent), FontWeights.Bold);
        var chip = new Border { Background = Ui.B(Accent, 0x26), CornerRadius = new CornerRadius(10), Padding = new Thickness(9, 2, 9, 2), Child = phaseChip, HorizontalAlignment = HorizontalAlignment.Right };
        return Ui.Titled("Pomodoro", "", body, Accent, chip, 14);
    }

    private Border BuildCountdown()
    {
        var body = new Grid(); body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) }); body.ColumnDefinitions.Add(new ColumnDefinition());
        var left = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        cTime = Ui.Text("10:00", 38, Ui.Ink, FontWeights.SemiBold); left.Children.Add(cTime); left.Children.Add(Ui.Text("Countdown", 12.5, Ui.Dim)); body.Children.Add(left);
        var right = new StackPanel { VerticalAlignment = VerticalAlignment.Center }; Grid.SetColumn(right, 1);
        var chips = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
        foreach (int m in QuickMin) { int mm = m; var c = Chip(mm + "m", () => SetCountdown(mm)); quickChips.Add(c); chips.Children.Add(c); }
        right.Children.Add(chips);
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        cInput = Ui.Input("min"); cInput.Width = 62; cInput.Height = 34; cInput.Padding = new Thickness(10, 0, 10, 0); cInput.MaxLength = 3; cInput.ToolTip = "Custom minutes, then press Start";
        cInput.KeyDown += (_, e) => { if (e.Key == Key.Enter) { CountdownToggle(); e.Handled = true; } };
        row.Children.Add(cInput);
        cPlay = Round("", "Start / pause", CountdownToggle, 38, true); cPlay.Margin = new Thickness(10, 0, 0, 0); row.Children.Add(cPlay);
        var reset = Round("", "Reset", () => SetCountdown(cTotalMin), 34, false); reset.Margin = new Thickness(8, 0, 0, 0); row.Children.Add(reset);
        right.Children.Add(row); body.Children.Add(right);
        return Ui.Titled("Countdown", "", body, Accent, null, 14);
    }

    private Border BuildStopwatch()
    {
        var body = new Grid(); body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(176) }); body.ColumnDefinitions.Add(new ColumnDefinition());
        var left = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        swTime = Ui.Text("0:00.0", 36, Ui.Ink, FontWeights.SemiBold); left.Children.Add(swTime);
        var btns = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        swPlay = Round("", "Start / stop", SwToggle, 40, true); btns.Children.Add(swPlay);
        var lap = Round("", "Lap (or reset when stopped)", SwLapOrReset, 36, false); lap.Margin = new Thickness(8, 0, 0, 0); btns.Children.Add(lap);
        var reset = Round("", "Reset", () => { if (swRun) { sw.Stop(); swRun = false; SyncFast(); } sw.Reset(); laps.Clear(); UpdateAll(); }, 36, false); reset.Margin = new Thickness(8, 0, 0, 0); btns.Children.Add(reset);
        left.Children.Add(btns); body.Children.Add(left);
        lapList = new StackPanel(); var sc = Ui.Scroll(lapList); Grid.SetColumn(sc, 1); sc.Margin = new Thickness(0, 0, 0, 0); body.Children.Add(sc);
        return Ui.Titled("Stopwatch", "", body, Accent, null, 14);
    }

    private Border BuildHydration()
    {
        var body = new Grid(); body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(190) }); body.ColumnDefinitions.Add(new ColumnDefinition());
        var left = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        left.Children.Add(Ui.Label("Next sip in"));
        hTime = Ui.Text("Off", 34, Ui.Ink, FontWeights.SemiBold); hTime.Margin = new Thickness(0, 0, 0, 8); left.Children.Add(hTime);
        var chips = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (int m in WaterMin) { int mm = m; var c = Chip(mm + "m", () => { data.HydrationMin = mm; if (data.HydrationOn) hNext = DateTime.UtcNow.AddMinutes(mm); Save(); UpdateAll(); }); c.Padding = new Thickness(10, 0, 10, 0); waterChips.Add(c); chips.Children.Add(c); }
        left.Children.Add(chips); body.Children.Add(left);
        var right = new StackPanel { VerticalAlignment = VerticalAlignment.Center }; Grid.SetColumn(right, 1);
        hKnob = new Border { Width = 18, Height = 18, CornerRadius = new CornerRadius(9), Background = Ui.Ink, Margin = new Thickness(3), VerticalAlignment = VerticalAlignment.Center };
        hToggle = new Border { Width = 44, Height = 24, CornerRadius = new CornerRadius(12), Cursor = Cursors.Hand, Child = hKnob, HorizontalAlignment = HorizontalAlignment.Left, ToolTip = "Remind me to drink" };
        hToggle.MouseLeftButtonUp += (_, e) => { HydrationToggle(); e.Handled = true; };
        var trow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
        trow.Children.Add(hToggle); var tl = Ui.Text("Remind me", 13.5, Ui.Soft, FontWeights.Medium); tl.Margin = new Thickness(10, 0, 0, 0); tl.VerticalAlignment = VerticalAlignment.Center; trow.Children.Add(tl);
        right.Children.Add(trow);
        var drank = Ui.Button("I drank", Ui.C("#5AC8FA"), Drank, 34); drank.HorizontalAlignment = HorizontalAlignment.Left; right.Children.Add(drank);
        hCount = Ui.Text("", 13, Ui.Dim, FontWeights.Medium); hCount.Margin = new Thickness(2, 6, 0, 0); right.Children.Add(hCount);
        body.Children.Add(right);
        return Ui.Titled("Hydration", "", body, Ui.C("#5AC8FA"), null, 14);
    }

    // ---------------- ui refresh ----------------
    private void UpdateAll() { if (!IsBuilt) return; UpdatePomodoro(); UpdateCountdown(); UpdateStopwatch(); UpdateHydration(); }

    private void UpdatePomodoro()
    {
        var rem = Remaining(pRun, pEnd, pRemain); var total = PhaseLen(pBreak);
        double frac = 1 - Math.Clamp(rem.TotalSeconds / total.TotalSeconds, 0, 1);
        ring.Set(frac, pBreak ? Ui.Green.Color : Accent, "", "");
        pTime.Text = Clock(rem); pPhase.Text = pBreak ? "Break" : "Focus"; phaseChip.Text = (pBreak ? "BREAK" : "FOCUS") + (pRun ? "" : " · PAUSED");
        SetGlyph(pPlay, pRun ? "" : "");
        if (paintedCycles != data.Cycles)
        {
            paintedCycles = data.Cycles; dots.Children.Clear(); int n = Math.Min(8, Math.Max(4, data.Cycles));
            for (int i = 0; i < n; i++) dots.Children.Add(new Ellipse { Width = 11, Height = 11, Margin = new Thickness(0, 0, 5, 0), Fill = i < data.Cycles ? Ui.B(Accent) : Ui.Chip, Stroke = i < data.Cycles ? null : Ui.B("#3A3A40"), StrokeThickness = 1 });
            pCycleText.Text = data.Cycles == 0 ? "none yet" : data.Cycles + (data.Cycles > 8 ? " (8+)" : "");
        }
    }
    private void UpdateCountdown()
    {
        cTime.Text = Clock(Remaining(cRun, cEnd, cRemain)); cTime.Foreground = cRun ? Ui.Ink : Ui.Soft;
        SetGlyph(cPlay, cRun ? "" : "");
        for (int i = 0; i < quickChips.Count; i++) PaintChip(quickChips[i], QuickMin[i] == cTotalMin);
    }
    private void UpdateStopwatch()
    {
        swTime.Text = Elapsed(sw.Elapsed, true); SetGlyph(swPlay, swRun ? "" : "");
        if (paintedLaps == laps.Count) return;
        paintedLaps = laps.Count; lapList.Children.Clear();
        if (laps.Count == 0) { var h = Ui.Text("Laps show up here", 13, Ui.Dim); h.Margin = new Thickness(6, 8, 0, 0); lapList.Children.Add(h); return; }
        for (int i = laps.Count - 1; i >= 0; i--)
        {
            var split = laps[i] - (i > 0 ? laps[i - 1] : TimeSpan.Zero);
            var row = new Grid { Height = 25, Margin = new Thickness(0, 0, 6, 0) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(38) }); row.ColumnDefinitions.Add(new ColumnDefinition()); row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var n = Ui.Text("#" + (i + 1), 13, Ui.Dim, FontWeights.SemiBold); n.VerticalAlignment = VerticalAlignment.Center;
            var s = Ui.Text("+" + Elapsed(split, true), 13.5, Ui.Ink, FontWeights.SemiBold); s.VerticalAlignment = VerticalAlignment.Center; Grid.SetColumn(s, 1);
            var t = Ui.Text(Elapsed(laps[i], true), 13, Ui.Dim); t.VerticalAlignment = VerticalAlignment.Center; Grid.SetColumn(t, 2);
            row.Children.Add(n); row.Children.Add(s); row.Children.Add(t); lapList.Children.Add(row);
        }
    }
    private void UpdateHydration()
    {
        var tint = Ui.C("#5AC8FA"); bool on = data.HydrationOn;
        hTime.Text = on ? Clock(Remaining(true, hNext, TimeSpan.Zero)) : "Off"; hTime.Foreground = on ? Ui.Ink : Ui.Dim;
        hToggle.Background = on ? Ui.B(tint) : Ui.B("#3A3A40"); hKnob.HorizontalAlignment = on ? HorizontalAlignment.Right : HorizontalAlignment.Left;
        hCount.Text = data.Water == 0 ? "No glasses logged today" : data.Water + (data.Water == 1 ? " glass today" : " glasses today") + " · every " + data.HydrationMin + "m";
        for (int i = 0; i < waterChips.Count; i++) { bool sel = WaterMin[i] == data.HydrationMin; waterChips[i].Background = sel ? Ui.B(tint, 0x33) : Ui.Chip; if (waterChips[i].Child is TextBlock t) t.Foreground = sel ? Ui.B(tint) : Ui.Dim; }
    }
}
