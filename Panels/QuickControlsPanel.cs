using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
namespace Island;

/// <summary>Keep the display awake, flip light/dark theme, lock the workstation, and empty the Recycle Bin.</summary>
internal sealed class QuickControlsPanel : IslandPanel
{
    public override string Id => "controls";
    public override string Title => "Quick controls";
    public override string Glyph => "\uE713";
    public override int Order => 44;
    public override Color Accent => Ui.C("#FF9F0A");

    private const uint EsContinuous = 0x80000000, EsSystem = 0x00000001, EsDisplay = 0x00000002;
    private const uint SherbNoConfirmation = 0x00000001;
    private const uint WmSettingChange = 0x001A, SmtoAbortIfHung = 0x0002;

    private static uint awakePrevious;
    private static bool awakeOn;
    private static DispatcherTimer? awakeTimer;
    private static int awakeChoice; // 0 = 15m, 1 = 1h, 2 = until off
    private static event Action? AwakeChanged;

    private Border? awakeKnob, awakeTrack;
    private StackPanel? confirm;
    private TextBlock? awakeState, darkState;
    public override string? ActiveTaskText => awakeOn ? "Awake" : null;

    public override void Start()
    {
        var window = Application.Current?.MainWindow;
        if (window != null) window.Closed += (_, _) => SetAwake(false);
    }

    protected override FrameworkElement Build()
    {
        var stack = new StackPanel();
        stack.Children.Add(AwakeRow());
        stack.Children.Add(Gap());
        stack.Children.Add(ActionRow("\uE793", "Dark mode", "Apps and the taskbar, for this Windows user.", out darkState, ToggleDark));
        stack.Children.Add(Gap());
        stack.Children.Add(ActionRow("\uE72E", "Lock", "Lock this PC.", out _, Lock));
        stack.Children.Add(Gap());
        stack.Children.Add(RecycleRow());
        var root = new Grid { Width = Ui.ContentW, Height = Ui.ContentH };
        root.Children.Add(Ui.Titled("This PC", "\uE713", stack, Accent, null, 16));
        AwakeChanged += PaintAwake;
        PaintAwake(); PaintDark();
        return root;
    }

    private UIElement AwakeRow()
    {
        var grid = RowGrid();
        grid.Children.Add(Icon("\uE708"));
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center }; Grid.SetColumn(text, 1);
        text.Children.Add(Ui.Text("Keep awake", 15.5, Ui.Ink, FontWeights.SemiBold));
        awakeState = Ui.Text("Display and system stay on.", 12.5, Ui.Dim); awakeState.Margin = new Thickness(0, 2, 0, 0); text.Children.Add(awakeState);
        var seg = new SegmentedControl(new[] { "15 min", "1 hour", "Until off" }, awakeChoice, Accent, i => { awakeChoice = i; if (awakeOn) SetAwake(true); });
        seg.Margin = new Thickness(0, 8, 0, 0); text.Children.Add(seg);
        grid.Children.Add(text);
        var toggle = Toggle(out awakeTrack, out awakeKnob, () => SetAwake(!awakeOn));
        Grid.SetColumn(toggle, 2); grid.Children.Add(toggle);
        return Ui.Card(grid, 14, 16);
    }

    private UIElement ActionRow(string glyph, string title, string sub, out TextBlock state, Action click)
    {
        var grid = RowGrid();
        grid.Children.Add(Icon(glyph));
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center }; Grid.SetColumn(text, 1);
        text.Children.Add(Ui.Text(title, 15.5, Ui.Ink, FontWeights.SemiBold));
        state = Ui.Text(sub, 12.5, Ui.Dim); state.Margin = new Thickness(0, 2, 0, 0); text.Children.Add(state);
        grid.Children.Add(text);
        var button = Ui.Button(title == "Lock" ? "Lock" : title == "Dark mode" ? "Switch" : "Run", Accent, click, 32);
        Grid.SetColumn(button, 2); grid.Children.Add(button);
        return Ui.Card(grid, 14, 16);
    }

    private UIElement RecycleRow()
    {
        var grid = RowGrid();
        grid.Children.Add(Icon("\uE74D"));
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center }; Grid.SetColumn(text, 1);
        text.Children.Add(Ui.Text("Empty Recycle Bin", 15.5, Ui.Ink, FontWeights.SemiBold));
        text.Children.Add(Ui.Text("Permanently deletes everything in it.", 12.5, Ui.Dim));
        confirm = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0), Visibility = Visibility.Collapsed };
        confirm.Children.Add(Ui.Button("Empty it", Ui.C("#FF453A"), EmptyBin, 30));
        var cancel = Ui.Button("Cancel", Ui.C("#A0A0A8"), () => Arm(false), 30); cancel.Margin = new Thickness(8, 0, 0, 0); confirm.Children.Add(cancel);
        text.Children.Add(confirm);
        grid.Children.Add(text);
        var button = Ui.Button("Empty", Ui.C("#FF453A"), () => Arm(true), 32);
        Grid.SetColumn(button, 2); grid.Children.Add(button);
        return Ui.Card(grid, 14, 16);
    }

    private void Arm(bool on) { confirmArmed = on; if (confirm != null) confirm.Visibility = on ? Visibility.Visible : Visibility.Collapsed; }

    private void EmptyBin()
    {
        Arm(false);
        try { SHEmptyRecycleBin(0, null, SherbNoConfirmation); Hub.Alert("Recycle Bin emptied", "", "\uE74D", "#FF9F0A"); }
        catch { Hub.Alert("Could not empty Recycle Bin", "Try again from the desktop if a file is in use.", "\uE74D", "#FF453A"); }
    }

    private void Lock()
    {
        try { LockWorkStation(); Hub.Collapse?.Invoke(); }
        catch { }
    }

    private void ToggleDark()
    {
        try
        {
            bool dark = !IsDark();
            using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            key?.SetValue("AppsUseLightTheme", dark ? 0 : 1, RegistryValueKind.DWord);
            key?.SetValue("SystemUsesLightTheme", dark ? 0 : 1, RegistryValueKind.DWord);
            SendMessageTimeout((nint)0xffff, WmSettingChange, 0, "ImmersiveColorSet", SmtoAbortIfHung, 200, out _);
        }
        catch { }
        PaintDark();
    }

    private void PaintDark()
    {
        if (darkState == null) return;
        bool dark = IsDark();
        darkState.Text = dark ? "Dark mode is on for apps and Windows." : "Light mode is on for apps and Windows.";
    }

    private static bool IsDark()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            int apps = key?.GetValue("AppsUseLightTheme") is int a ? a : 1;
            int system = key?.GetValue("SystemUsesLightTheme") is int s ? s : 1;
            return apps == 0 && system == 0;
        }
        catch { return false; }
    }

    private void SetAwake(bool on)
    {
        if (on)
        {
            uint flags = EsContinuous | EsSystem | EsDisplay;
            uint previous = SetThreadExecutionState(flags);
            if (!awakeOn) awakePrevious = previous == 0 ? EsContinuous : previous;
            awakeOn = true;
            awakeTimer?.Stop();
            TimeSpan? span = awakeChoice == 0 ? TimeSpan.FromMinutes(15) : awakeChoice == 1 ? TimeSpan.FromHours(1) : null;
            if (span is TimeSpan due)
            {
                awakeTimer = new DispatcherTimer { Interval = due };
                awakeTimer.Tick += (_, _) => SetAwake(false);
                awakeTimer.Start();
            }
        }
        else if (awakeOn)
        {
            awakeTimer?.Stop(); awakeTimer = null; awakeOn = false;
            SetThreadExecutionState(awakePrevious == 0 ? EsContinuous : awakePrevious);
        }
        AwakeChanged?.Invoke();
    }

    private void PaintAwake()
    {
        if (awakeState != null) awakeState.Text = awakeOn ? (awakeChoice == 2 ? "On until you turn it off." : awakeChoice == 1 ? "On for 1 hour." : "On for 15 minutes.") : "Display and system stay on.";
        if (awakeTrack != null) awakeTrack.Background = awakeOn ? Ui.B(Accent) : Ui.Chip;
        if (awakeKnob != null) awakeKnob.HorizontalAlignment = awakeOn ? HorizontalAlignment.Right : HorizontalAlignment.Left;
    }

    private static Grid RowGrid()
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        return grid;
    }

    private Border Icon(string glyph)
    {
        var box = new Border { Width = 36, Height = 36, CornerRadius = new CornerRadius(12), Background = Ui.B(Accent, 0x24), Margin = new Thickness(0, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center, Child = Ui.Glyph(glyph, 16, Ui.B(Accent)) };
        return box;
    }

    private static Border Toggle(out Border track, out Border knob, Action click)
    {
        knob = new Border { Width = 16, Height = 16, CornerRadius = new CornerRadius(8), Background = Ui.Ink, Margin = new Thickness(3), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
        track = new Border { Width = 44, Height = 26, CornerRadius = new CornerRadius(13), Background = Ui.Chip, Child = knob, Cursor = Cursors.Hand, VerticalAlignment = VerticalAlignment.Center };
        var captured = track;
        captured.MouseLeftButtonUp += (_, e) => { click(); e.Handled = true; };
        return track;
    }

    private static Border Gap() => new() { Height = 8 };

    [DllImport("kernel32.dll")]
    private static extern uint SetThreadExecutionState(uint flags);
    [DllImport("user32.dll")]
    private static extern bool LockWorkStation();
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHEmptyRecycleBin(nint hwnd, string? root, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint SendMessageTimeout(nint hwnd, uint msg, nint wParam, string lParam, uint flags, uint timeout, out nint result);
}
