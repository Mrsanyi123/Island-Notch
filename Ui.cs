using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
namespace Island;

/// <summary>Island design system: palette, text, cards, pills, charts. Every panel builds its UI from these so the whole island looks like one product.</summary>
internal static class Ui
{
    /// <summary>Exact layout size of every panel's View.</summary>
    public const double ContentW = 896, ContentH = 332;

    public static Color C(string hex) => (Color)ColorConverter.ConvertFromString(hex)!;
    public static SolidColorBrush B(string hex) => Freeze(new SolidColorBrush(C(hex)));
    public static SolidColorBrush B(Color c) => Freeze(new SolidColorBrush(c));
    public static SolidColorBrush B(Color c, byte alpha) => Freeze(new SolidColorBrush(Color.FromArgb(alpha, c.R, c.G, c.B)));
    private static SolidColorBrush Freeze(SolidColorBrush b) { b.Freeze(); return b; }

    // palette (black glass, brighter text than the first version for legibility)
    public static readonly SolidColorBrush Ink = B("#FFFFFF"), Soft = B("#D8D8DE"), Dim = B("#A0A0A8"), Faint = B("#5E5E66"),
        Chip = B("#1F1F23"), ChipHover = B("#2B2B31"), Line = B("#26262A"), Red = B("#FF453A"), Green = B("#30D158"), Orange = B("#FF9F0A"), Blue = B("#0A84FF");
    public static readonly FontFamily IconFont = new("Segoe Fluent Icons, Segoe MDL2 Assets");
    public static readonly FontFamily TextFont = new("Segoe UI Variable Display, Segoe UI");
    public static readonly Color[] Palette = { C("#BF5AF2"), C("#30D158"), C("#0A84FF"), C("#FF9F0A"), C("#FF453A"), C("#64D2FF") };

    // ---------- text ----------
    public static TextBlock Text(string text, double size = 14, Brush? fg = null, FontWeight? weight = null) =>
        new() { Text = text, FontSize = size, Foreground = fg ?? Ink, FontWeight = weight ?? FontWeights.Normal, TextTrimming = TextTrimming.CharacterEllipsis };
    public static TextBlock Glyph(string glyph, double size = 16, Brush? fg = null) =>
        new() { Text = glyph, FontFamily = IconFont, FontSize = size, Foreground = fg ?? Ink, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    /// <summary>Small uppercase section label.</summary>
    public static TextBlock Label(string text, Brush? fg = null) => Text(text.ToUpperInvariant(), 11.5, fg ?? Dim, FontWeights.Bold);
    public static FormattedText Format(string text, double size, Brush fg, FontWeight? weight = null, double ppd = 1) =>
        new(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, new Typeface(TextFont, FontStyles.Normal, weight ?? FontWeights.Normal, FontStretches.Normal), size, fg, ppd);

    // ---------- surfaces ----------
    private static readonly LinearGradientBrush CardFill = Freeze(new LinearGradientBrush(C("#19191C"), C("#0F0F11"), 90));
    private static LinearGradientBrush Freeze(LinearGradientBrush b) { b.Freeze(); return b; }

    /// <summary>Dark rounded tile.</summary>
    public static Border Card(UIElement? child = null, double pad = 16, double radius = 20) =>
        new() { Background = CardFill, BorderBrush = Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(radius), Padding = new Thickness(pad), Child = child };

    /// <summary>Tile with an uppercase title row (accent glyph + label + optional right-aligned element) above the body. Body fills the rest.</summary>
    public static Border Titled(string title, string glyph, UIElement body, Color accent, UIElement? right = null, double pad = 16)
    {
        var g = new Grid(); g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); g.RowDefinitions.Add(new RowDefinition());
        var head = new Grid { Height = 28, Margin = new Thickness(0, 0, 0, 8) };
        var left = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        left.Children.Add(Glyph(glyph, 14, B(accent))); var l = Label(title); l.Margin = new Thickness(8, 0, 0, 0); l.VerticalAlignment = VerticalAlignment.Center; left.Children.Add(l);
        head.Children.Add(left);
        if (right != null) { if (right is FrameworkElement fe) { fe.HorizontalAlignment = HorizontalAlignment.Right; fe.VerticalAlignment = VerticalAlignment.Center; } head.Children.Add(right); }
        g.Children.Add(head); Grid.SetRow(body, 1); g.Children.Add(body);
        return Card(g, pad);
    }

    public static Border Chipped(string text, Color tint, double size = 12)
    {
        var t = Text(text, size, B(tint), FontWeights.SemiBold);
        return new Border { Background = B(tint, 0x26), CornerRadius = new CornerRadius(10), Padding = new Thickness(9, 3, 9, 3), Child = t, VerticalAlignment = VerticalAlignment.Center };
    }
    public static Ellipse Dot(Brush fill, double size = 8) => new() { Width = size, Height = size, Fill = fill, VerticalAlignment = VerticalAlignment.Center };
    public static Border Divider() => new() { Height = 1, Background = Line, Margin = new Thickness(0, 6, 0, 6) };

    /// <summary>Label / big value / optional sub line.</summary>
    public static StackPanel Stat(string label, string value, string? sub = null, Brush? subBrush = null, double valueSize = 28)
    {
        var p = new StackPanel(); p.Children.Add(Label(label)); var v = Text(value, valueSize, Ink, FontWeights.SemiBold); v.Margin = new Thickness(0, 2, 0, 0); p.Children.Add(v);
        if (sub != null) { var s = Text(sub, 12.5, subBrush ?? Dim, FontWeights.Medium); s.Margin = new Thickness(0, 1, 0, 0); p.Children.Add(s); }
        return p;
    }

    public static Border IconButton(string glyph, string tip, Action click, double size = 36, bool filled = true)
    {
        var b = new Border { Width = size, Height = size, CornerRadius = new CornerRadius(size / 2), Background = filled ? Chip : Brushes.Transparent, Cursor = Cursors.Hand, ToolTip = tip, Child = Glyph(glyph, size * .42, Ink) };
        b.MouseEnter += (_, _) => b.Background = ChipHover; b.MouseLeave += (_, _) => b.Background = filled ? Chip : Brushes.Transparent;
        b.MouseLeftButtonUp += (_, e) => { click(); e.Handled = true; };
        return b;
    }

    /// <summary>Text button with a tinted fill, e.g. "Add", "Connect".</summary>
    public static Border Button(string text, Color tint, Action click, double height = 34)
    {
        var t = Text(text, 13.5, B(tint), FontWeights.SemiBold); t.HorizontalAlignment = HorizontalAlignment.Center; t.VerticalAlignment = VerticalAlignment.Center;
        var b = new Border { Height = height, MinWidth = 64, Padding = new Thickness(16, 0, 16, 0), CornerRadius = new CornerRadius(height / 2), Background = B(tint, 0x2A), Cursor = Cursors.Hand, Child = t };
        b.MouseEnter += (_, _) => b.Background = B(tint, 0x44); b.MouseLeave += (_, _) => b.Background = B(tint, 0x2A);
        b.MouseLeftButtonUp += (_, e) => { click(); e.Handled = true; };
        return b;
    }

    public static ScrollViewer Scroll(UIElement content) => new() { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Focusable = false };

    /// <summary>Styled single-line (or multiline) text box. Keyboard focus is handled by the window automatically.</summary>
    public static TextBox Input(string placeholder, bool multiline = false)
    {
        var t = new TextBox { Tag = placeholder };
        if (multiline) { t.AcceptsReturn = true; t.TextWrapping = TextWrapping.Wrap; t.VerticalScrollBarVisibility = ScrollBarVisibility.Auto; t.VerticalContentAlignment = VerticalAlignment.Top; }
        return t;
    }

    public static void FadeIn(UIElement e, int ms = 160) => e.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(ms)) { EasingFunction = new QuadraticEase() });

    // ---------- shared XAML styles (applied on the island's root; panels need not reference them) ----------
    private static ResourceDictionary? resources;
    public static ResourceDictionary Resources => resources ??= (ResourceDictionary)XamlReader.Parse(@"
<ResourceDictionary xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'>
  <Style x:Key='ScrollBtn' TargetType='RepeatButton'>
    <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='RepeatButton'><Border Background='Transparent'/></ControlTemplate></Setter.Value></Setter>
  </Style>
  <Style TargetType='ScrollBar'>
    <Setter Property='Width' Value='8'/><Setter Property='MinWidth' Value='8'/><Setter Property='Background' Value='Transparent'/>
    <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='ScrollBar'>
      <Grid Background='Transparent'><Track x:Name='PART_Track' IsDirectionReversed='True'>
        <Track.DecreaseRepeatButton><RepeatButton Style='{StaticResource ScrollBtn}' Command='ScrollBar.PageUpCommand'/></Track.DecreaseRepeatButton>
        <Track.IncreaseRepeatButton><RepeatButton Style='{StaticResource ScrollBtn}' Command='ScrollBar.PageDownCommand'/></Track.IncreaseRepeatButton>
        <Track.Thumb><Thumb><Thumb.Template><ControlTemplate TargetType='Thumb'><Border Background='#55FFFFFF' CornerRadius='3' Margin='1,0'/></ControlTemplate></Thumb.Template></Thumb></Track.Thumb>
      </Track></Grid></ControlTemplate></Setter.Value></Setter>
  </Style>
  <Style TargetType='TextBox'>
    <Setter Property='Foreground' Value='White'/><Setter Property='CaretBrush' Value='White'/><Setter Property='SelectionBrush' Value='#770A84FF'/>
    <Setter Property='Background' Value='#1B1B1F'/><Setter Property='FontSize' Value='14'/><Setter Property='Padding' Value='12,8,12,8'/><Setter Property='VerticalContentAlignment' Value='Center'/>
    <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='TextBox'>
      <Border x:Name='bd' Background='{TemplateBinding Background}' CornerRadius='12' BorderBrush='#2C2C32' BorderThickness='1'>
        <Grid>
          <ScrollViewer x:Name='PART_ContentHost' Margin='{TemplateBinding Padding}' VerticalAlignment='{TemplateBinding VerticalContentAlignment}'/>
          <TextBlock x:Name='ph' Text='{TemplateBinding Tag}' Foreground='#70707A' IsHitTestVisible='False' Margin='{TemplateBinding Padding}' Padding='2,0,0,0' Visibility='Collapsed' VerticalAlignment='{TemplateBinding VerticalContentAlignment}'/>
        </Grid></Border>
      <ControlTemplate.Triggers>
        <Trigger Property='Text' Value=''><Setter TargetName='ph' Property='Visibility' Value='Visible'/></Trigger>
        <Trigger Property='IsKeyboardFocused' Value='True'><Setter TargetName='bd' Property='BorderBrush' Value='#66FFFFFF'/></Trigger>
      </ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter>
  </Style>
  <Style TargetType='PasswordBox'>
    <Setter Property='Foreground' Value='White'/><Setter Property='CaretBrush' Value='White'/><Setter Property='Background' Value='#1B1B1F'/><Setter Property='FontSize' Value='14'/><Setter Property='Padding' Value='12,8,12,8'/>
    <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='PasswordBox'>
      <Border x:Name='bd' Background='{TemplateBinding Background}' CornerRadius='12' BorderBrush='#2C2C32' BorderThickness='1'>
        <ScrollViewer x:Name='PART_ContentHost' Margin='{TemplateBinding Padding}' VerticalAlignment='Center'/></Border>
      <ControlTemplate.Triggers><Trigger Property='IsKeyboardFocused' Value='True'><Setter TargetName='bd' Property='BorderBrush' Value='#66FFFFFF'/></Trigger></ControlTemplate.Triggers>
    </ControlTemplate></Setter.Value></Setter>
  </Style>
  <Style TargetType='ToolTip'>
    <Setter Property='Background' Value='#242428'/><Setter Property='Foreground' Value='White'/><Setter Property='BorderBrush' Value='#3A3A40'/><Setter Property='Padding' Value='9,5,9,5'/><Setter Property='FontSize' Value='12.5'/>
  </Style>
</ResourceDictionary>");
}

/// <summary>Segmented pill toggle, e.g. Today | 7 days | 30 days. The selected item is tinted with the accent.</summary>
internal sealed class SegmentedControl : Border
{
    private readonly List<(Border box, TextBlock text)> items = new();
    private readonly Color accent; private readonly Action<int> changed;
    public int Selected { get; private set; }
    public SegmentedControl(IEnumerable<string> labels, int selected, Color accent, Action<int> changed)
    {
        this.accent = accent; this.changed = changed; Selected = selected;
        Background = Ui.Chip; CornerRadius = new CornerRadius(17); Padding = new Thickness(3); HorizontalAlignment = HorizontalAlignment.Left;
        var row = new StackPanel { Orientation = Orientation.Horizontal }; Child = row; int i = 0;
        foreach (var label in labels)
        {
            int index = i++; var t = Ui.Text(label, 13, Ui.Dim, FontWeights.SemiBold); t.VerticalAlignment = VerticalAlignment.Center;
            var box = new Border { CornerRadius = new CornerRadius(14), Padding = new Thickness(14, 5, 14, 5), Cursor = Cursors.Hand, Child = t, Background = Brushes.Transparent };
            box.MouseLeftButtonUp += (_, e) => { Select(index, true); e.Handled = true; };
            items.Add((box, t)); row.Children.Add(box);
        }
        Paint();
    }
    public void Select(int index, bool notify) { if (index < 0 || index >= items.Count) return; Selected = index; Paint(); if (notify) changed(index); }
    private void Paint()
    {
        for (int i = 0; i < items.Count; i++)
        {
            bool on = i == Selected; items[i].box.Background = on ? Ui.B(accent, 0x30) : Brushes.Transparent; items[i].text.Foreground = on ? Ui.B(accent) : Ui.Dim;
        }
    }
}

/// <summary>Smooth filled line chart (accent line, soft gradient fill). Call Set(values, accent).</summary>
internal sealed class AreaChart : FrameworkElement
{
    private double[] values = Array.Empty<double>(); private Color accent = Colors.White;
    public bool ShowBaseline { get; set; } = true;
    public void Set(IReadOnlyList<double> v, Color accentColor) { values = v.ToArray(); accent = accentColor; InvalidateVisual(); }
    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight; if (w < 4 || h < 4) return;
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, w, h));
        if (ShowBaseline) dc.DrawLine(new Pen(Ui.B("#2A2A2F"), 1), new Point(0, h - .5), new Point(w, h - .5));
        if (values.Length < 2) return;
        double max = Math.Max(values.Max(), 1e-9), top = 8, bottom = 2, usable = h - top - bottom;
        var pts = values.Select((v, i) => new Point(i * w / (values.Length - 1), top + usable * (1 - v / max))).ToArray();
        double lo = pts.Min(p => p.Y), hi = pts.Max(p => p.Y);
        var line = new StreamGeometry(); var fill = new StreamGeometry();
        using (var c = line.Open()) { c.BeginFigure(pts[0], false, false); Curve(c, pts, lo, hi); }
        using (var c = fill.Open()) { c.BeginFigure(new Point(0, h), true, true); c.LineTo(pts[0], false, false); Curve(c, pts, lo, hi); c.LineTo(new Point(w, h), false, false); }
        line.Freeze(); fill.Freeze();
        var grad = new LinearGradientBrush(Color.FromArgb(0x70, accent.R, accent.G, accent.B), Color.FromArgb(0x00, accent.R, accent.G, accent.B), 90); grad.Freeze();
        dc.DrawGeometry(grad, null, fill);
        dc.DrawGeometry(null, new Pen(Ui.B(accent), 2.6) { LineJoin = PenLineJoin.Round, StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }, line);
    }
    private static void Curve(StreamGeometryContext c, Point[] p, double lo, double hi)
    {
        for (int i = 0; i < p.Length - 1; i++)
        {
            Point p0 = p[Math.Max(i - 1, 0)], p1 = p[i], p2 = p[i + 1], p3 = p[Math.Min(i + 2, p.Length - 1)];
            double y1 = Math.Clamp(p1.Y + (p2.Y - p0.Y) / 6, lo, hi), y2 = Math.Clamp(p2.Y - (p3.Y - p1.Y) / 6, lo, hi);
            c.BezierTo(new Point(p1.X + (p2.X - p0.X) / 6, y1), new Point(p2.X - (p3.X - p1.X) / 6, y2), p2, true, false);
        }
    }
}

/// <summary>Rounded vertical bars with bottom labels. The last bar is "today" (solid accent); others are dimmed.</summary>
internal sealed class BarChart : FrameworkElement
{
    private double[] values = Array.Empty<double>(); private string[] labels = Array.Empty<string>(); private string?[] top = Array.Empty<string?>(); private Color accent = Colors.White; private int highlight = -1;
    /// <summary>values: bar heights; labels: x-axis labels; topLabels: optional text above each bar (null = none); highlight: index drawn solid (-1 = last).</summary>
    public void Set(IReadOnlyList<double> v, IReadOnlyList<string> xLabels, Color accentColor, IReadOnlyList<string?>? topLabels = null, int highlightIndex = -1)
    { values = v.ToArray(); labels = xLabels.ToArray(); top = topLabels?.ToArray() ?? new string?[v.Count]; accent = accentColor; highlight = highlightIndex; InvalidateVisual(); }
    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight; if (w < 10 || h < 30 || values.Length == 0) return;
        double ppd = VisualTreeHelper.GetDpi(this).PixelsPerDip, slot = w / values.Length, bw = Math.Min(slot * .62, 60), labelH = 20, topH = 18, usable = h - labelH - topH, max = Math.Max(values.Max(), 1e-9);
        int hi = highlight < 0 ? values.Length - 1 : highlight;
        for (int i = 0; i < values.Length; i++)
        {
            double bh = Math.Max(4, usable * values[i] / max), x = i * slot + (slot - bw) / 2, y = topH + usable - bh;
            dc.DrawRoundedRectangle(i == hi ? Ui.B(accent) : Ui.B(accent, 0x45), null, new Rect(x, y, bw, bh), 6, 6);
            if (i < labels.Length) { var ft = Ui.Format(labels[i], 12, i == hi ? Ui.Ink : Ui.Dim, i == hi ? FontWeights.Bold : FontWeights.Medium, ppd); dc.DrawText(ft, new Point(i * slot + (slot - ft.Width) / 2, h - labelH + 3)); }
            if (i < top.Length && top[i] is { } s) { var ft = Ui.Format(s, 12, i == hi ? Ui.B(accent) : Ui.Dim, FontWeights.SemiBold, ppd); dc.DrawText(ft, new Point(i * slot + (slot - ft.Width) / 2, Math.Max(0, y - 18))); }
        }
    }
}

/// <summary>Circular progress ring with a centre value and a caption below it.</summary>
internal sealed class Ring : FrameworkElement
{
    private double value; private Color accent = Colors.White; private string center = "", caption = "";
    public void Set(double fraction, Color accentColor, string centerText, string captionText = "") { value = Math.Clamp(fraction, 0, 1); accent = accentColor; center = centerText; caption = captionText; InvalidateVisual(); }
    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight, s = Math.Min(w, h); if (s < 20) return; double ppd = VisualTreeHelper.GetDpi(this).PixelsPerDip, th = Math.Max(7, s * .085), r = s / 2 - th / 2 - 1; var c = new Point(w / 2, h / 2);
        dc.DrawEllipse(null, new Pen(Ui.B("#26262B"), th), c, r, r);
        if (value > .001)
        {
            if (value > .999) dc.DrawEllipse(null, new Pen(Ui.B(accent), th), c, r, r);
            else
            {
                double a = value * 2 * Math.PI - Math.PI / 2; var end = new Point(c.X + r * Math.Cos(a), c.Y + r * Math.Sin(a)); var g = new StreamGeometry();
                using (var ctx = g.Open()) { ctx.BeginFigure(new Point(c.X, c.Y - r), false, false); ctx.ArcTo(end, new Size(r, r), 0, value > .5, SweepDirection.Clockwise, true, false); }
                g.Freeze(); dc.DrawGeometry(null, new Pen(Ui.B(accent), th) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }, g);
            }
        }
        var big = Ui.Format(center, s * .22, Ui.Ink, FontWeights.SemiBold, ppd); var small = Ui.Format(caption, s * .105, Ui.Dim, FontWeights.Medium, ppd);
        double total = big.Height + (caption.Length > 0 ? small.Height : 0), y = c.Y - total / 2;
        dc.DrawText(big, new Point(c.X - big.Width / 2, y)); if (caption.Length > 0) dc.DrawText(small, new Point(c.X - small.Width / 2, y + big.Height));
    }
}
