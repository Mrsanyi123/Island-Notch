using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using Ellipse = System.Windows.Shapes.Ellipse;
using System.Windows.Threading;
namespace Island;

internal sealed class SoundsSettings { public double Master { get; set; } = 0.7; }

/// <summary>One looping sound: two MediaPlayers on the same file that overlap-crossfade at the end, so the loop has no gap.</summary>
internal sealed class SoundsVoice
{
    private const double Overlap = 0.6;
    private MediaPlayer cur, nxt; private bool crossing; private DateTime crossStart;
    public bool Failed { get; private set; }

    public SoundsVoice(string path)
    {
        cur = Make(path); nxt = Make(path); cur.Play();
    }
    private MediaPlayer Make(string path)
    {
        var p = new MediaPlayer { Volume = 0 };
        p.MediaEnded += (s, _) => { try { if (ReferenceEquals(s, cur) && !crossing) { cur.Position = TimeSpan.Zero; cur.Play(); } } catch { } };
        p.MediaFailed += (_, _) => Failed = true;
        p.Open(new Uri(path)); return p;
    }

    public void Update(double vol)
    {
        try
        {
            vol = Math.Clamp(vol, 0, 1);
            if (!crossing)
            {
                cur.Volume = vol;
                if (cur.NaturalDuration.HasTimeSpan)
                {
                    var d = cur.NaturalDuration.TimeSpan;
                    if (d.TotalSeconds > 3 && (d - cur.Position).TotalSeconds < Overlap) { nxt.Position = TimeSpan.Zero; nxt.Volume = 0; nxt.Play(); crossing = true; crossStart = DateTime.Now; }
                }
            }
            if (crossing)
            {
                double f = Math.Clamp((DateTime.Now - crossStart).TotalSeconds / Overlap, 0, 1);
                cur.Volume = vol * Math.Cos(f * Math.PI / 2); nxt.Volume = vol * Math.Sin(f * Math.PI / 2);
                if (f >= 1) { cur.Stop(); (cur, nxt) = (nxt, cur); crossing = false; }
            }
        }
        catch { }
    }

    public void Stop() { try { cur.Close(); } catch { } try { nxt.Close(); } catch { } }
}

/// <summary>Simple draggable horizontal slider (accent fill + round knob).</summary>
internal sealed class SoundsSlider : Border
{
    private readonly Border fill = new() { Height = 6, CornerRadius = new CornerRadius(3), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
    private readonly Ellipse knob = new() { Width = 18, Height = 18, Fill = Brushes.White, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
    private readonly Action<double, bool> changed;
    public double Value { get; private set; }

    public SoundsSlider(double value, Color accent, Action<double, bool> changed)
    {
        this.changed = changed; Value = Math.Clamp(value, 0, 1); Height = 28; Background = Brushes.Transparent; Cursor = Cursors.Hand;
        fill.Background = Ui.B(accent);
        var g = new Grid(); g.Children.Add(new Border { Height = 6, CornerRadius = new CornerRadius(3), Background = Ui.B("#2A2A30"), VerticalAlignment = VerticalAlignment.Center });
        g.Children.Add(fill); g.Children.Add(knob); Child = g; SizeChanged += (_, _) => Place();
        MouseLeftButtonDown += (_, e) => { CaptureMouse(); Set(e); e.Handled = true; };
        MouseMove += (_, e) => { if (IsMouseCaptured) Set(e); };
        MouseLeftButtonUp += (_, e) => { if (IsMouseCaptured) { Set(e); ReleaseMouseCapture(); this.changed(Value, true); } e.Handled = true; };
    }
    private void Set(MouseEventArgs e) { double w = ActualWidth - knob.Width; if (w <= 0) return; Value = Math.Clamp((e.GetPosition(this).X - knob.Width / 2) / w, 0, 1); Place(); changed(Value, false); }
    private void Place() { double w = Math.Max(0, ActualWidth - knob.Width), x = Value * w; knob.Margin = new Thickness(x, 0, 0, 0); fill.Width = x + knob.Width / 2; }
}

internal sealed class SoundsTile
{
    public string Name = "", Key = "", Emoji = "", Blurb = "";
    public Border? Box; public TextBlock? Status; public Ellipse? Dot;
    public bool Wanted, Preparing, Custom, Failed; public SoundsVoice? Voice; public double Gain;
    public bool Active => Wanted && !Preparing && Voice != null;
}

/// <summary>Sounds: eight layerable ambiences (synthesised on first use, or your own files), a master volume and a fade-out timer.</summary>
internal sealed class SoundsPanel : IslandPanel
{
    public override string Id => "sounds";
    public override string Title => "Sounds";
    public override string Glyph => "";
    public override int Order => 130;
    public override Color Accent => Ui.C("#30D5C8");

    private readonly List<SoundsTile> tiles = new()
    {
        new() { Name = "Rain", Key = "rain", Emoji = "🌧️", Blurb = "Steady rainfall" }, new() { Name = "Forest", Key = "forest", Emoji = "🌲", Blurb = "Wind and birdsong" },
        new() { Name = "Coffee shop", Key = "coffeeshop", Emoji = "☕", Blurb = "Murmur and cups" }, new() { Name = "Office", Key = "office", Emoji = "🏢", Blurb = "Hum and keyboards" },
        new() { Name = "Lo-fi", Key = "lofi", Emoji = "🎧", Blurb = "Chords and crackle" }, new() { Name = "Fireworks", Key = "fireworks", Emoji = "🎆", Blurb = "Distant booms" },
        new() { Name = "Whispers", Key = "whispers", Emoji = "🌬️", Blurb = "Airy murmur" }, new() { Name = "Waves", Key = "waves", Emoji = "🌊", Blurb = "Slow ocean swell" },
    };
    private SoundsSettings? cfg;
    private DispatcherTimer? timer; private DateTime last;
    private int fadeMinutes; private DateTime? fadeEnd;
    private TextBlock? countdown, pct;

    private SoundsSettings Cfg => cfg ??= Store.Load<SoundsSettings>("sounds");
    private static string UserDir => Path.Combine(Settings.Folder, "sounds");

    public override string? RestingText
    {
        get
        {
            var on = tiles.Where(t => t.Active).Select(t => t.Name).ToList(); if (on.Count == 0) return null;
            string s = string.Join(" + ", on); if (s.Length <= 28) return s;
            string two = string.Join(" + ", on.Take(2)) + " +" + (on.Count - 2); return two.Length <= 28 && on.Count > 2 ? two : on[0] + " +" + (on.Count - 1);
        }
    }

    // ---------- UI ----------
    protected override FrameworkElement Build()
    {
        var g = new Grid { Width = Ui.ContentW, Height = Ui.ContentH };
        g.RowDefinitions.Add(new RowDefinition()); g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(10) }); g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(92) });
        var grid = new Grid(); for (int c = 0; c < 7; c++) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = c % 2 == 1 ? new GridLength(10) : new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition()); grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(10) }); grid.RowDefinitions.Add(new RowDefinition());
        for (int i = 0; i < tiles.Count; i++) { var el = BuildTile(tiles[i]); Grid.SetColumn(el, (i % 4) * 2); Grid.SetRow(el, i / 4 * 2); grid.Children.Add(el); }
        g.Children.Add(grid);

        // bottom bar: volume + fade-out + hint
        var bar = new Grid(); bar.RowDefinitions.Add(new RowDefinition { Height = new GridLength(36) }); bar.RowDefinitions.Add(new RowDefinition());
        var top = new Grid(); foreach (var w in new[] { GridLength.Auto, new GridLength(1, GridUnitType.Star), GridLength.Auto, new GridLength(28), GridLength.Auto, GridLength.Auto }) top.ColumnDefinitions.Add(new ColumnDefinition { Width = w });
        var vi = Ui.Glyph("", 18, B(Accent)); vi.Margin = new Thickness(0, 0, 10, 0); top.Children.Add(vi);
        var slider = new SoundsSlider(Cfg.Master, Accent, (v, final) => { Cfg.Master = v; if (pct != null) pct.Text = Math.Round(v * 100) + "%"; if (final) Store.Save("sounds", Cfg); }) { VerticalAlignment = VerticalAlignment.Center }; Grid.SetColumn(slider, 1); top.Children.Add(slider);
        pct = Ui.Text(Math.Round(Cfg.Master * 100) + "%", 14, Ui.Soft, FontWeights.SemiBold); pct.Width = 48; pct.TextAlignment = TextAlignment.Right; pct.VerticalAlignment = VerticalAlignment.Center; Grid.SetColumn(pct, 2); top.Children.Add(pct);
        var fl = Ui.Label("Fade out"); fl.VerticalAlignment = VerticalAlignment.Center; fl.Margin = new Thickness(0, 0, 10, 0); Grid.SetColumn(fl, 4); top.Children.Add(fl);
        var seg = new SegmentedControl(new[] { "Off", "15 min", "30 min", "60 min" }, 0, Accent, i => { fadeMinutes = new[] { 0, 15, 30, 60 }[i]; fadeEnd = fadeMinutes > 0 && tiles.Any(t => t.Voice != null) ? DateTime.Now.AddMinutes(fadeMinutes) : null; UpdateCountdown(); }) { VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(seg, 5); top.Children.Add(seg); bar.Children.Add(top);

        var hint = new Grid { VerticalAlignment = VerticalAlignment.Center }; Grid.SetRow(hint, 1); hint.ColumnDefinitions.Add(new ColumnDefinition()); hint.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var ht = Ui.Text("Synthesised, not recorded. To use your own, drop rain.mp3, lo-fi.wav, coffeeshop.m4a… into %AppData%\\Island\\sounds", 12.5, Ui.Dim);
        ht.ToolTip = "Names: rain, forest, coffeeshop, office, lo-fi, fireworks, whispers, waves (mp3, wav or m4a)"; hint.Children.Add(ht);
        countdown = Ui.Text("", 12.5, B(Accent), FontWeights.SemiBold); countdown.Margin = new Thickness(12, 0, 0, 0); Grid.SetColumn(countdown, 1); hint.Children.Add(countdown);
        var open = Ui.Text("Open folder", 12.5, Ui.Blue, FontWeights.SemiBold); open.Cursor = Cursors.Hand; open.Margin = new Thickness(12, 0, 0, 0); Grid.SetColumn(open, 1); open.HorizontalAlignment = HorizontalAlignment.Right; open.VerticalAlignment = VerticalAlignment.Center;
        open.MouseLeftButtonUp += (_, e) => { try { Directory.CreateDirectory(UserDir); System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(UserDir) { UseShellExecute = true }); } catch { } e.Handled = true; };
        countdown.HorizontalAlignment = HorizontalAlignment.Right; countdown.Margin = new Thickness(0, 0, 100, 0); hint.Children.Add(open);
        bar.Children.Add(hint);
        var card = Ui.Card(bar, 14); Grid.SetRow(card, 2); g.Children.Add(card);
        foreach (var t in tiles) Paint(t);
        return g;
    }

    private static SolidColorBrush B(Color c) => Ui.B(c);

    private Border BuildTile(SoundsTile t)
    {
        var grid = new Grid(); grid.RowDefinitions.Add(new RowDefinition()); grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.Children.Add(new TextBlock { Text = t.Emoji, FontFamily = new FontFamily("Segoe UI Emoji"), FontSize = 32, Foreground = Ui.Ink, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top });
        t.Dot = Ui.Dot(B(Accent), 10); t.Dot.HorizontalAlignment = HorizontalAlignment.Right; t.Dot.VerticalAlignment = VerticalAlignment.Top; t.Dot.Margin = new Thickness(0, 6, 2, 0); grid.Children.Add(t.Dot);
        var name = Ui.Text(t.Name, 16, Ui.Ink, FontWeights.SemiBold); Grid.SetRow(name, 1); grid.Children.Add(name);
        t.Status = Ui.Text(t.Blurb, 12.5, Ui.Dim); Grid.SetRow(t.Status, 2); grid.Children.Add(t.Status);
        var box = new Border { CornerRadius = new CornerRadius(20), BorderThickness = new Thickness(1), Padding = new Thickness(16, 12, 16, 12), Cursor = Cursors.Hand, Child = grid }; t.Box = box;
        box.MouseEnter += (_, _) => { if (!t.Active) box.Background = Ui.ChipHover; }; box.MouseLeave += (_, _) => Paint(t);
        box.MouseLeftButtonUp += (_, e) => { _ = Toggle(t); e.Handled = true; };
        return box;
    }

    private void Paint(SoundsTile t)
    {
        if (t.Box == null || t.Status == null || t.Dot == null) return;
        bool on = t.Active;
        t.Box.Background = on ? Ui.B(Accent, 0x26) : Ui.Chip; t.Box.BorderBrush = on ? Ui.B(Accent, 0xAA) : Ui.Line;
        t.Box.Effect = on ? new DropShadowEffect { Color = Accent, BlurRadius = 24, ShadowDepth = 0, Opacity = .45 } : null;
        t.Dot.Visibility = on || t.Preparing ? Visibility.Visible : Visibility.Hidden; t.Dot.Fill = t.Preparing ? Ui.Orange : B(Accent);
        if (t.Failed) { t.Status.Text = "Couldn't play this one"; t.Status.Foreground = Ui.Red; }
        else if (t.Preparing) { t.Status.Text = "Preparing sound…"; t.Status.Foreground = Ui.Orange; }
        else if (on) { t.Status.Text = t.Custom ? "Playing · your file" : "Playing"; t.Status.Foreground = B(Accent); }
        else { t.Status.Text = t.Blurb; t.Status.Foreground = Ui.Dim; }
    }

    // ---------- playback ----------
    private static string? UserFile(string key)
    {
        try
        {
            if (!Directory.Exists(UserDir)) return null;
            var names = key == "lofi" ? new[] { "lo-fi", "lofi" } : new[] { key };
            foreach (var n in names) foreach (var ext in new[] { ".mp3", ".wav", ".m4a" }) { var p = Path.Combine(UserDir, n + ext); if (File.Exists(p)) return p; }
        }
        catch { }
        return null;
    }

    private async Task Toggle(SoundsTile t)
    {
        try
        {
            if (t.Preparing) { t.Wanted = !t.Wanted; Paint(t); return; }       // the pending generation decides what to do when it finishes
            if (t.Wanted) { t.Wanted = false; Paint(t); RefreshChrome(); return; }
            t.Wanted = true; t.Failed = false; Paint(t);
            var path = UserFile(t.Key); t.Custom = path != null;
            if (path == null)
            {
                var dir = Path.Combine(Store.Folder, "sounds"); path = Path.Combine(dir, t.Key + ".wav");
                if (!File.Exists(path) || new FileInfo(path).Length < 100000)
                {
                    t.Preparing = true; Paint(t); string key = t.Key, p = path;
                    try { await Task.Run(() => { Directory.CreateDirectory(dir); SoundsSynth.Generate(key, p); }); }
                    finally { t.Preparing = false; }
                }
            }
            if (!t.Wanted) { Paint(t); return; }
            t.Voice?.Stop(); t.Voice = new SoundsVoice(path); t.Gain = 0; EnsureTimer(); Paint(t); RefreshChrome();
        }
        catch { t.Preparing = false; t.Wanted = false; t.Failed = true; Paint(t); }
    }

    private void EnsureTimer()
    {
        if (timer == null) { timer = new DispatcherTimer(DispatcherPriority.Normal) { Interval = TimeSpan.FromMilliseconds(100) }; timer.Tick += (_, _) => Step(); }
        if (!timer.IsEnabled)
        {
            last = DateTime.Now; timer.Start();
            if (fadeMinutes > 0 && fadeEnd == null) fadeEnd = DateTime.Now.AddMinutes(fadeMinutes);
        }
    }

    private void Step()
    {
        try
        {
            var now = DateTime.Now; double dt = Math.Min((now - last).TotalSeconds, .5); last = now; double fade = 1;
            if (fadeEnd is { } end)
            {
                double rem = (end - now).TotalSeconds;
                if (rem <= 0) { StopAll(); return; }
                fade = Math.Min(1, rem / 20);                         // lower the volume over the final ~20 s
            }
            bool any = false;
            foreach (var t in tiles)
            {
                var v = t.Voice; if (v == null) continue;
                if (v.Failed) { v.Stop(); t.Voice = null; t.Wanted = false; t.Failed = true; Paint(t); continue; }
                t.Gain = t.Wanted ? Math.Min(1, t.Gain + dt / 1.5) : Math.Max(0, t.Gain - dt / 0.5);
                if (!t.Wanted && t.Gain <= 0) { v.Stop(); t.Voice = null; Paint(t); continue; }
                v.Update(t.Gain * Cfg.Master * fade); any = true;
            }
            UpdateCountdown();
            if (!any && !tiles.Any(t => t.Preparing)) { timer?.Stop(); fadeEnd = null; UpdateCountdown(); }
        }
        catch { }
    }

    private void StopAll()
    {
        foreach (var t in tiles) { t.Wanted = false; t.Voice?.Stop(); t.Voice = null; t.Gain = 0; Paint(t); }
        timer?.Stop(); fadeEnd = null; UpdateCountdown();
    }

    private void RefreshChrome() { if (tiles.Any(t => t.Voice != null)) EnsureTimer(); UpdateCountdown(); }

    private void UpdateCountdown()
    {
        if (countdown == null) return;
        if (fadeEnd is { } e && tiles.Any(t => t.Voice != null)) { var r = e - DateTime.Now; if (r < TimeSpan.Zero) r = TimeSpan.Zero; countdown.Text = "Fading out in " + (int)r.TotalMinutes + ":" + r.Seconds.ToString("00"); }
        else countdown.Text = "";
    }
}
