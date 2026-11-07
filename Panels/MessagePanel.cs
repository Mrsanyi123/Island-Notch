using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
namespace Island;

internal sealed class MessageSettings
{
    public string Text { get; set; } = "";
    public int Anim { get; set; }
    public int ColorIndex { get; set; }
    public int Speed { get; set; } = 1;
    public bool On { get; set; }
}

/// <summary>Message: your own text as a dot-matrix scroll across the collapsed notch.</summary>
internal sealed class MessagePanel : IslandPanel
{
    public override string Id => "message";
    public override string Title => "Message";
    public override string Glyph => "";
    public override int Order => 120;
    public override Color Accent => Ui.C("#FF375F");

    private static readonly string[] Swatches = { "#FF375F", "#FF9F0A", "#FFD60A", "#30D158", "#64D2FF", "#0A84FF", "#BF5AF2", "#FFFFFF" };
    private static readonly double[] Speeds = { 22, 40, 80 };
    private const double OverlayW = 300;

    private MessageSettings? cfg;
    private MessageDotMatrix? overlay, preview;
    private TextBox? input; private TextBlock? emptyHint, toggleSub, flashMsg;
    private Border? toggle, toggleKnob, togglePill;
    private readonly List<Border> swatchBoxes = new();
    private bool dirty;

    private MessageSettings Cfg => cfg ??= Store.Load<MessageSettings>("message");
    private Color Tint => Ui.C(Swatches[Math.Clamp(Cfg.ColorIndex, 0, Swatches.Length - 1)]);
    public override string? RestingText => null;

    public override void Start() { if (Cfg.On && Cfg.Text.Trim().Length > 0) Apply(); else Cfg.On = false; }
    public override void OnHidden() { if (dirty) { dirty = false; Store.Save("message", Cfg); } }

    private void Configure(MessageDotMatrix m, double scale)
    {
        m.Scale = scale; m.Color = Tint; m.Anim = (MessageAnim)Math.Clamp(Cfg.Anim, 0, 5); m.Speed = Speeds[Math.Clamp(Cfg.Speed, 0, 2)]; m.Text = Cfg.Text.Trim(); m.Refresh();
    }

    /// <summary>Install / update / remove the notch overlay to match the settings.</summary>
    private void Apply()
    {
        try
        {
            if (Cfg.On && Cfg.Text.Trim().Length > 0)
            {
                bool fresh = overlay == null; overlay ??= new MessageDotMatrix { Width = OverlayW, Height = 36, VerticalAlignment = VerticalAlignment.Center };
                Configure(overlay, 1);
                if (fresh || Hub.NotchOverlay != overlay) Hub.SetNotchOverlay(overlay, OverlayW);
            }
            else
            {
                if (overlay != null && Hub.NotchOverlay == overlay) Hub.SetNotchOverlay(null, 0);
                overlay = null;
            }
            if (preview != null) Configure(preview, 2);
            if (emptyHint != null) emptyHint.Visibility = Cfg.Text.Trim().Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            PaintToggle(); PaintSwatches();
        }
        catch { }
    }

    private void Changed(bool save = true) { dirty = true; if (save) { Store.Save("message", Cfg); dirty = false; } Apply(); }

    protected override FrameworkElement Build()
    {
        var g = new Grid { Width = Ui.ContentW, Height = Ui.ContentH };
        g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(140) }); g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(10) }); g.RowDefinitions.Add(new RowDefinition());

        // live preview at 2x (its own instance: an element can only have one parent)
        preview = new MessageDotMatrix { Height = 72, HorizontalAlignment = HorizontalAlignment.Stretch };
        emptyHint = Ui.Text("Type a message below and watch it light up", 14, Ui.Dim); emptyHint.HorizontalAlignment = HorizontalAlignment.Center; emptyHint.VerticalAlignment = VerticalAlignment.Center;
        var screen = new Border { Background = Ui.B("#050506"), CornerRadius = new CornerRadius(14), Height = 72, ClipToBounds = true, Child = new Grid { Children = { preview, emptyHint } } };
        g.Children.Add(Ui.Titled("Preview", "", screen, Accent, null, 14));

        // controls
        var c = new Grid(); c.ColumnDefinitions.Add(new ColumnDefinition()); c.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) }); c.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(224) });
        var left = new StackPanel();
        input = Ui.Input("Your message, e.g. Back in 5 minutes"); input.MaxLength = 140; input.Height = 40; input.Text = Cfg.Text;
        input.TextChanged += (_, _) => { Cfg.Text = input.Text; Changed(false); }; input.LostFocus += (_, _) => { if (dirty) { dirty = false; Store.Save("message", Cfg); } };
        left.Children.Add(input);
        var anim = new SegmentedControl(new[] { "Scroll", "Bounce", "Wave", "Sparkle", "Pulse", "Rainbow" }, Math.Clamp(Cfg.Anim, 0, 5), Accent, i => { Cfg.Anim = i; Changed(); }) { Margin = new Thickness(0, 12, 0, 0) };
        left.Children.Add(anim);
        var row = new Grid { Margin = new Thickness(0, 12, 0, 0) }; row.ColumnDefinitions.Add(new ColumnDefinition()); row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var sw = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        for (int i = 0; i < Swatches.Length; i++)
        {
            int idx = i; var b = new Border { Width = 26, Height = 26, CornerRadius = new CornerRadius(13), Background = Ui.B(Swatches[i]), Margin = new Thickness(0, 0, 8, 0), Cursor = Cursors.Hand, BorderThickness = new Thickness(2.5), BorderBrush = Brushes.Transparent, ToolTip = i == 0 ? "Colour (ignored by Rainbow)" : null };
            b.MouseLeftButtonUp += (_, e) => { Cfg.ColorIndex = idx; Changed(); e.Handled = true; };
            swatchBoxes.Add(b); sw.Children.Add(b);
        }
        row.Children.Add(sw);
        var speed = new SegmentedControl(new[] { "Slow", "Normal", "Fast" }, Math.Clamp(Cfg.Speed, 0, 2), Accent, i => { Cfg.Speed = i; Changed(); }); Grid.SetColumn(speed, 1); row.Children.Add(speed);
        left.Children.Add(row); c.Children.Add(left);

        // right: big on/off + flash
        var right = new StackPanel { VerticalAlignment = VerticalAlignment.Top }; Grid.SetColumn(right, 2);
        toggleKnob = new Border { Width = 22, Height = 22, CornerRadius = new CornerRadius(11), Background = Brushes.White, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(3) };
        togglePill = new Border { Width = 50, Height = 28, CornerRadius = new CornerRadius(14), Child = toggleKnob, VerticalAlignment = VerticalAlignment.Center };
        var tl = new StackPanel { VerticalAlignment = VerticalAlignment.Center }; tl.Children.Add(Ui.Text("Show on notch", 15, Ui.Ink, FontWeights.SemiBold)); toggleSub = Ui.Text("", 12.5, Ui.Dim); tl.Children.Add(toggleSub);
        var tg = new Grid(); tg.ColumnDefinitions.Add(new ColumnDefinition()); tg.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); tg.Children.Add(tl); Grid.SetColumn(togglePill, 1); tg.Children.Add(togglePill);
        toggle = new Border { Height = 64, CornerRadius = new CornerRadius(16), Padding = new Thickness(14, 0, 14, 0), Cursor = Cursors.Hand, Child = tg };
        toggle.MouseLeftButtonUp += (_, e) => { Toggle(); e.Handled = true; };
        right.Children.Add(toggle);
        var flash = Ui.Button("Flash it", Accent, DoFlash, 40); flash.Margin = new Thickness(0, 10, 0, 0); flash.ToolTip = "Show your message in the closed notch and flash it for 3 seconds"; right.Children.Add(flash);
        flashMsg = Ui.Text("", 12.5, Ui.Dim); flashMsg.Margin = new Thickness(2, 6, 0, 0); right.Children.Add(flashMsg);
        c.Children.Add(right);
        var tile = Ui.Card(c, 14); Grid.SetRow(tile, 2); g.Children.Add(tile);
        Apply();
        return g;
    }

    private void Toggle()
    {
        if (!Cfg.On && Cfg.Text.Trim().Length == 0) { if (flashMsg != null) flashMsg.Text = "Type a message first"; input?.Focus(); return; }
        Cfg.On = !Cfg.On; if (flashMsg != null) flashMsg.Text = ""; Changed();
        if (Cfg.On) Hub.Collapse?.Invoke();
    }

    private void DoFlash()
    {
        var t = Cfg.Text.Trim(); if (t.Length == 0) { if (flashMsg != null) flashMsg.Text = "Type a message first"; return; }
        if (flashMsg != null) flashMsg.Text = "";
        Cfg.On = true; Changed();
        Hub.Collapse?.Invoke();
        try { overlay?.Flash(3); } catch { }
    }

    private void PaintToggle()
    {
        if (toggle == null || togglePill == null || toggleKnob == null) return;
        bool on = Cfg.On; toggle.Background = on ? Ui.B(Accent, 0x26) : Ui.Chip;
        togglePill.Background = on ? Ui.B(Accent) : Ui.B("#3A3A40"); toggleKnob.HorizontalAlignment = on ? HorizontalAlignment.Right : HorizontalAlignment.Left;
        if (toggleSub != null) toggleSub.Text = on ? "Live while collapsed" : "Off";
    }

    private void PaintSwatches()
    {
        for (int i = 0; i < swatchBoxes.Count; i++) swatchBoxes[i].BorderBrush = i == Cfg.ColorIndex ? Ui.B("#FFFFFF") : Brushes.Transparent;
    }
}
