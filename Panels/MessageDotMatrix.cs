using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
namespace Island;

internal enum MessageAnim { Scroll, Bounce, Wave, Sparkle, Pulse, Rainbow }

/// <summary>
/// LED dot-matrix text: a built-in 5x7 font drawn as round dots on a 4 px grid (7 rows = 28 px + 4 px margins = 36 px at Scale 1).
/// Scrolls as a marquee when the text is wider than the element, is centred otherwise. Animates only while visible
/// (subscribes to CompositionTarget.Rendering when shown, detaches when hidden/unloaded/faded out) and at ~30 fps.
/// </summary>
internal sealed class MessageDotMatrix : FrameworkElement
{
    private const int Rows = 7;
    private static readonly Dictionary<char, byte[]> Font = BuildFont();
    private static readonly Dictionary<uint, Brush> Brushes_ = new();
    private static readonly Brush OffBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x23, 0x23, 0x28)));

    private byte[] cols = Array.Empty<byte>();
    private string text = "";
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private DateTime flashUntil, flashStart;
    private bool attached; private int frame;
    private Geometry? offGeom; private double offW, offH, offScale;

    public MessageAnim Anim { get; set; } = MessageAnim.Scroll;
    public Color Color { get; set; } = Color.FromRgb(0xFF, 0x37, 0x5F);
    /// <summary>Marquee speed in unscaled px per second.</summary>
    public double Speed { get; set; } = 40;
    /// <summary>Size multiplier (1 = notch size, 2 = panel preview).</summary>
    public double Scale { get; set; } = 1;
    public string Text { get => text; set { text = value ?? ""; cols = Layout(text); InvalidateVisual(); } }

    public MessageDotMatrix()
    {
        ClipToBounds = true; IsHitTestVisible = false;
        IsVisibleChanged += (_, _) => Sync(); Loaded += (_, _) => Sync(); Unloaded += (_, _) => Sync();
    }

    public void Flash(double seconds = 3) { flashStart = DateTime.Now; flashUntil = flashStart.AddSeconds(seconds); Sync(); InvalidateVisual(); }
    public void Refresh() => InvalidateVisual();

    private void Sync()
    {
        bool want = IsVisible && IsLoaded;
        if (want && !attached) { CompositionTarget.Rendering += OnFrame; attached = true; }
        else if (!want && attached) { CompositionTarget.Rendering -= OnFrame; attached = false; }
    }

    private bool Faded()
    {
        // the notch fades the overlay layer / dashboard with Opacity; skip all work while it is invisible
        double o = 1; DependencyObject? d = this;
        for (int i = 0; i < 6 && d != null; i++) { if (d is UIElement u) o *= u.Opacity; d = VisualTreeHelper.GetParent(d); }
        return o < 0.02;
    }

    private void OnFrame(object? s, EventArgs e)
    {
        try
        {
            if ((++frame & 1) == 0) return;                               // ~30 fps
            bool flashing = DateTime.Now < flashUntil;
            double w = ActualWidth;
            bool scrolls = cols.Length > (int)(w / (4 * Scale));
            if (cols.Length == 0 && !flashing) return;
            if (!scrolls && Anim == MessageAnim.Scroll && !flashing) return; // static, nothing moves
            if (Faded()) return;
            InvalidateVisual();
        }
        catch { }
    }

    protected override void OnRender(DrawingContext dc)
    {
        try { Draw(dc); } catch { }
    }

    private void Draw(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight; if (w < 8 || h < 8) return;
        double p = 4 * Scale, rad = 1.5 * Scale, top = (h - Rows * p) / 2, t = clock.Elapsed.TotalSeconds;
        int viewCols = (int)(w / p); double ox = (w - viewCols * p) / 2;
        bool flashing = DateTime.Now < flashUntil, flashOn = false;
        if (flashing) { flashOn = (int)((DateTime.Now - flashStart).TotalSeconds * 6) % 2 == 0; if (flashOn) dc.DrawRoundedRectangle(Shade(Color, 0.55), null, new Rect(0, 0, w, h), 10 * Scale, 10 * Scale); }

        if (offGeom == null || offW != w || offH != h || offScale != Scale)
        {
            var g = new GeometryGroup();
            for (int c = 0; c < viewCols; c++) for (int r = 0; r < Rows; r++) g.Children.Add(new EllipseGeometry(new Point(ox + c * p + p / 2, top + r * p + p / 2), rad * .8, rad * .8));
            g.Freeze(); offGeom = g; offW = w; offH = h; offScale = Scale;
        }
        dc.DrawGeometry(OffBrush, null, offGeom);
        if (cols.Length == 0) return;

        bool scroll = cols.Length > viewCols; int gap = Math.Max(10, viewCols / 3), period = cols.Length + gap;
        double shift = scroll ? t * Speed * Scale / p : 0; int whole = (int)Math.Floor(shift); double frac = shift - whole;
        int lead = scroll ? 0 : (viewCols - cols.Length) / 2;
        int tick = (int)(t * 10);

        for (int c = 0; c <= viewCols; c++)
        {
            int src = scroll ? (c + whole) % period : c - lead;
            if (src < 0 || src >= cols.Length) continue; byte bits = cols[src]; if (bits == 0) continue;
            double x = ox + c * p + p / 2 - (scroll ? frac * p : 0);
            double yoff = Anim switch { MessageAnim.Wave => Math.Sin(src * .55 + t * 5) * p * 1.1, MessageAnim.Bounce => Math.Sin(t * 4.5) * p * .9, _ => 0 };
            Color baseC = Anim == MessageAnim.Rainbow ? Hsv((src * 9 + t * 140) % 360) : Color;
            if (flashing && flashOn) baseC = Colors.White;
            for (int r = 0; r < Rows; r++)
            {
                if ((bits & (1 << r)) == 0) continue;
                double b = 1;
                if (Anim == MessageAnim.Pulse) b = .45 + .55 * (.5 + .5 * Math.Sin(t * 3.2));
                else if (Anim == MessageAnim.Sparkle)
                {
                    uint hsh = unchecked((uint)((src + 7) * 73856093 ^ (r + 3) * 19349663 ^ tick * 83492791)); double v = ((hsh >> 7) & 0xFF) / 255.0;
                    b = v < .3 ? .22 : v > .93 ? 1.3 : .95;
                }
                var pt = new Point(x, top + r * p + p / 2 + yoff);
                if (b > .6) dc.DrawEllipse(Shade(baseC, .16), null, pt, rad * 1.9, rad * 1.9);          // soft glow
                dc.DrawEllipse(Shade(b > 1 ? Colors.White : baseC, Math.Min(b, 1)), null, pt, rad, rad);
            }
        }
    }

    // ---------- brushes ----------
    private static Brush Freeze(SolidColorBrush b) { b.Freeze(); return b; }
    /// <summary>Colour scaled toward black by f (0..1), cached by quantised value.</summary>
    private static Brush Shade(Color c, double f)
    {
        int q = (int)Math.Round(Math.Clamp(f, 0, 1) * 20); uint key = (uint)(c.R << 24 | c.G << 16 | c.B << 8 | q);
        if (Brushes_.TryGetValue(key, out var br)) return br;
        if (Brushes_.Count > 5000) Brushes_.Clear();
        double k = q / 20.0; br = Freeze(new SolidColorBrush(Color.FromRgb((byte)(c.R * k), (byte)(c.G * k), (byte)(c.B * k)))); Brushes_[key] = br; return br;
    }
    private static Color Hsv(double h)
    {
        h = (int)h % 360; double s = .85, v = 1, c = v * s, x = c * (1 - Math.Abs(h / 60 % 2 - 1)), m = v - c; double r, g, b;
        if (h < 60) (r, g, b) = (c, x, 0); else if (h < 120) (r, g, b) = (x, c, 0); else if (h < 180) (r, g, b) = (0, c, x); else if (h < 240) (r, g, b) = (0, x, c); else if (h < 300) (r, g, b) = (x, 0, c); else (r, g, b) = (c, 0, x);
        return Color.FromRgb((byte)((r + m) * 255), (byte)((g + m) * 255), (byte)((b + m) * 255));
    }

    // ---------- font ----------
    /// <summary>Text to 7-bit columns (bit r = row r from the top). Lowercase renders as capitals, unknown chars as blank.</summary>
    private static byte[] Layout(string s)
    {
        var l = new List<byte>();
        foreach (char raw in s)
        {
            char ch = raw is >= 'a' and <= 'z' ? char.ToUpperInvariant(raw) : raw;
            if (l.Count > 0) l.Add(0);
            if (ch == ' ') { l.Add(0); l.Add(0); l.Add(0); continue; }
            Font.TryGetValue(ch, out var g); g ??= new byte[Rows];
            for (int c = 0; c < 5; c++) { int bits = 0; for (int r = 0; r < Rows; r++) if ((g[r] >> (4 - c) & 1) != 0) bits |= 1 << r; l.Add((byte)bits); }
        }
        return l.ToArray();
    }

    private static Dictionary<char, byte[]> BuildFont()
    {
        // each glyph: 7 hex rows, top to bottom, 5 bits wide
        var src = new (char, string)[] {
            ('A',"0E 11 11 1F 11 11 11"),('B',"1E 11 11 1E 11 11 1E"),('C',"0E 11 10 10 10 11 0E"),('D',"1E 11 11 11 11 11 1E"),('E',"1F 10 10 1E 10 10 1F"),
            ('F',"1F 10 10 1E 10 10 10"),('G',"0E 11 10 17 11 11 0F"),('H',"11 11 11 1F 11 11 11"),('I',"0E 04 04 04 04 04 0E"),('J',"07 02 02 02 02 12 0C"),
            ('K',"11 12 14 18 14 12 11"),('L',"10 10 10 10 10 10 1F"),('M',"11 1B 15 15 11 11 11"),('N',"11 11 19 15 13 11 11"),('O',"0E 11 11 11 11 11 0E"),
            ('P',"1E 11 11 1E 10 10 10"),('Q',"0E 11 11 11 15 12 0D"),('R',"1E 11 11 1E 14 12 11"),('S',"0F 10 10 0E 01 01 1E"),('T',"1F 04 04 04 04 04 04"),
            ('U',"11 11 11 11 11 11 0E"),('V',"11 11 11 11 11 0A 04"),('W',"11 11 11 15 15 15 0A"),('X',"11 11 0A 04 0A 11 11"),('Y',"11 11 0A 04 04 04 04"),('Z',"1F 01 02 04 08 10 1F"),
            ('0',"0E 11 13 15 19 11 0E"),('1',"04 0C 04 04 04 04 0E"),('2',"0E 11 01 02 04 08 1F"),('3',"1F 02 04 02 01 11 0E"),('4',"02 06 0A 12 1F 02 02"),
            ('5',"1F 10 1E 01 01 11 0E"),('6',"06 08 10 1E 11 11 0E"),('7',"1F 01 02 04 08 08 08"),('8',"0E 11 11 0E 11 11 0E"),('9',"0E 11 11 0F 01 02 0C"),
            ('!',"04 04 04 04 04 00 04"),('?',"0E 11 01 02 04 00 04"),('.',"00 00 00 00 00 0C 0C"),(',',"00 00 00 00 0C 04 08"),(':',"00 0C 0C 00 0C 0C 00"),
            (';',"00 0C 0C 00 0C 04 08"),('-',"00 00 00 1F 00 00 00"),('+',"00 04 04 1F 04 04 00"),('=',"00 00 1F 00 1F 00 00"),('/',"01 01 02 04 08 10 10"),
            ('\\',"10 10 08 04 02 01 01"),('\'',"04 04 08 00 00 00 00"),('"',"0A 0A 14 00 00 00 00"),('(',"02 04 08 08 08 04 02"),(')',"08 04 02 02 02 04 08"),
            ('*',"00 04 15 0E 15 04 00"),('#',"0A 0A 1F 0A 1F 0A 0A"),('@',"0E 11 17 15 17 10 0E"),('&',"0C 12 14 08 15 12 0D"),('%',"18 19 02 04 08 13 03"),
            ('$',"04 0F 14 0E 05 1E 04"),('_',"00 00 00 00 00 00 1F"),('<',"02 04 08 10 08 04 02"),('>',"08 04 02 01 02 04 08"),('~',"00 00 08 15 02 00 00"),
            ('[',"0E 08 08 08 08 08 0E"),(']',"0E 02 02 02 02 02 0E"),('♥',"00 0A 1F 1F 0E 04 00"),('★',"04 04 1F 0E 0E 0A 11") };
        var d = new Dictionary<char, byte[]>();
        foreach (var (c, rows) in src) { var parts = rows.Split(' '); var b = new byte[Rows]; for (int i = 0; i < Rows; i++) b[i] = Convert.ToByte(parts[i], 16); d[c] = b; }
        return d;
    }
}
