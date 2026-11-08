using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
namespace Island;

/// <summary>A small magnifier. Click copies HEX, RGB, and HSL, then the picker closes. Escape cancels. It is never full-screen.</summary>
internal sealed class ColorPanel : IslandPanel
{
    public override string Id => "color";
    public override string Title => "Color";
    public override string Glyph => "\uE790";
    public override int Order => 115;
    public override Color Accent => Ui.C("#BF5AF2");

    private static string last = "No color copied yet.";
    private TextBlock? result;
    private Border? swatch;

    protected override FrameworkElement Build()
    {
        var root = new Grid { Width = Ui.ContentW, Height = Ui.ContentH };
        var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        swatch = new Border { Width = 72, Height = 72, CornerRadius = new CornerRadius(18), Background = Ui.Chip, HorizontalAlignment = HorizontalAlignment.Center };
        result = Ui.Text(last, 14, Ui.Soft); result.TextAlignment = TextAlignment.Center; result.TextWrapping = TextWrapping.Wrap; result.MaxWidth = 420; result.Margin = new Thickness(0, 14, 0, 16); result.HorizontalAlignment = HorizontalAlignment.Center;
        var button = Ui.Button("Pick a color", Accent, Open, 36); button.HorizontalAlignment = HorizontalAlignment.Center;
        var note = Ui.Text("A magnifier follows the pointer. Click to copy. Escape cancels.", 13, Ui.Dim); note.Margin = new Thickness(0, 12, 0, 0); note.HorizontalAlignment = HorizontalAlignment.Center;
        stack.Children.Add(swatch); stack.Children.Add(result); stack.Children.Add(button); stack.Children.Add(note);
        root.Children.Add(Ui.Card(stack, 24, 20));
        return root;
    }

    private void Open()
    {
        var picker = new ColorPickerWindow();
        picker.Picked += color =>
        {
            last = color;
            if (result != null) result.Text = color;
            if (swatch != null && color.StartsWith('#')) swatch.Background = Ui.B(color.Split('\n')[0]);
        };
        picker.Show();
    }
}

internal sealed class ColorPickerWindow : Window
{
    public event Action<string>? Picked;
    private readonly Image image = new() { Width = 150, Height = 150, Stretch = Stretch.Fill, IsHitTestVisible = false };
    private readonly TextBlock label;
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(30) };
    private readonly WriteableBitmap bitmap = new(15, 15, 96, 96, PixelFormats.Bgra32, null);
    private string current = "#000000";

    public ColorPickerWindow()
    {
        Width = 180; Height = 214; WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent;
        ShowInTaskbar = false; Topmost = true; ResizeMode = ResizeMode.NoResize; ShowActivated = true; WindowStartupLocation = WindowStartupLocation.Manual;
        var card = new Border { Background = Ui.B("#F0121214"), BorderBrush = Ui.Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(16), Padding = new Thickness(12) };
        var stack = new StackPanel();
        var frame = new Border { CornerRadius = new CornerRadius(10), ClipToBounds = true, Child = image };
        label = Ui.Text("#000000", 13, Ui.Ink, FontWeights.SemiBold); label.HorizontalAlignment = HorizontalAlignment.Center; label.Margin = new Thickness(0, 8, 0, 0);
        stack.Children.Add(frame); stack.Children.Add(label); card.Child = stack; Content = card;
        image.Source = bitmap;
        KeyDown += (_, e) => { if (e.Key == Key.Escape) { Close(); e.Handled = true; } };
        MouseLeftButtonDown += (_, e) => { Hub.Copy(current); Picked?.Invoke(current); Close(); e.Handled = true; };
        timer.Tick += (_, _) => Sample();
        Loaded += (_, _) => { Sample(); timer.Start(); };
        Closed += (_, _) => timer.Stop();
    }

    private void Sample()
    {
        if (!NativeMethods.GetCursorPos(out var point)) return;
        Left = point.X + 22; Top = point.Y + 22;
        var screen = System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point(point.X, point.Y)).Bounds;
        if (Left + Width > screen.Right) Left = point.X - Width - 16;
        if (Top + Height > screen.Bottom) Top = point.Y - Height - 16;
        nint dc = GetDC(0);
        try
        {
            var pixels = new byte[15 * 15 * 4];
            uint center = 0;
            for (int y = 0; y < 15; y++)
                for (int x = 0; x < 15; x++)
                {
                    uint pixel = GetPixel(dc, point.X + x - 7, point.Y + y - 7);
                    int i = (y * 15 + x) * 4;
                    pixels[i] = (byte)(pixel & 0xFF);
                    pixels[i + 1] = (byte)((pixel >> 8) & 0xFF);
                    pixels[i + 2] = (byte)((pixel >> 16) & 0xFF);
                    pixels[i + 3] = 255;
                    if (x == 7 && y == 7) center = pixel;
                }
            bitmap.WritePixels(new Int32Rect(0, 0, 15, 15), pixels, 15 * 4, 0);
            byte r = (byte)(center & 0xFF), g = (byte)((center >> 8) & 0xFF), b = (byte)((center >> 16) & 0xFF);
            current = Format(r, g, b);
            label.Text = "#" + current[1..7];
        }
        finally { ReleaseDC(0, dc); }
    }

    private static string Format(byte r, byte g, byte b)
    {
        double rf = r / 255d, gf = g / 255d, bf = b / 255d;
        double max = Math.Max(rf, Math.Max(gf, bf)), min = Math.Min(rf, Math.Min(gf, bf));
        double l = (max + min) / 2, h = 0, s = 0;
        if (max != min)
        {
            double d = max - min;
            s = l > 0.5 ? d / (2 - max - min) : d / (max + min);
            if (max == rf) h = (gf - bf) / d + (gf < bf ? 6 : 0);
            else if (max == gf) h = (bf - rf) / d + 2;
            else h = (rf - gf) / d + 4;
            h *= 60;
        }
        return "#" + r.ToString("X2") + g.ToString("X2") + b.ToString("X2")
            + "\nrgb(" + r + ", " + g + ", " + b + ")"
            + "\nhsl(" + Math.Round(h) + ", " + Math.Round(s * 100) + "%, " + Math.Round(l * 100) + "%)";
    }

    [DllImport("user32.dll")] private static extern nint GetDC(nint hwnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(nint hwnd, nint dc);
    [DllImport("gdi32.dll")] private static extern uint GetPixel(nint dc, int x, int y);
}
