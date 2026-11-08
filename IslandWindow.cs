using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
namespace Island;

// Rendering model (why this does not jitter):
//   * The OS window NEVER changes size or position while animating. It is one fixed, transparent surface
//     pinned flush to the top-centre of the screen (WinW x WinH).
//   * The visible "island" is a black notch-shaped Clip on a fixed-size host Grid. Only the clip geometry
//     changes per frame: a pure render operation, no layout pass, no SetWindowPos, no SetWindowRgn.
//   * Content layers have fixed layout sizes and cross-fade by opacity, so nothing re-measures mid-animation.
internal sealed class IslandWindow : Window
{
    private const double WinW = 980, WinH = 480;
    private const double IdleW = 240, IdleH = 42, IdleR = 21;
    private const double AlertW = 460, AlertH = 78, AlertR = 28;
    private const double FullW = 940, FullH = 480, FullR = 38;
    private static bool classHandlersRegistered;

    private readonly Grid host = new() { Width = WinW, Height = WinH, Background = Brushes.Black };
    private readonly Grid dash = new() { Width = FullW, Height = FullH, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top, Opacity = 0, IsHitTestVisible = false };
    private readonly Grid alertLayer = new() { Width = AlertW, Height = AlertH, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top, Opacity = 0, IsHitTestVisible = false };
    private readonly Grid activityLayer = new() { Height = IdleH, VerticalAlignment = VerticalAlignment.Top };
    private readonly Grid overlayLayer = new() { Height = IdleH, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top, Opacity = 0, IsHitTestVisible = false };
    private readonly TextBlock activity = new() { Text = "Island   •   Ready", Foreground = Brushes.White, FontSize = 13.5, FontWeight = FontWeights.Medium, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, MaxWidth = IdleW - 30, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock activityGlyph = Ui.Glyph("", 16);
    private readonly Border alertIcon = new() { Width = 50, Height = 50, CornerRadius = new CornerRadius(25), BorderThickness = new Thickness(1), VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock alertGlyph = new() { FontSize = 21, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock alertTitle = new() { FontSize = 17.5, FontWeight = FontWeights.Bold, Foreground = Ui.Ink, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock alertSub = new() { FontSize = 13.5, Foreground = Ui.Dim, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 3, 0, 0) };

    // expanded chrome
    private readonly Grid panelHost = new() { Width = Ui.ContentW, Height = Ui.ContentH };
    private readonly TextBlock headerGlyph = new() { FontFamily = Ui.IconFont, FontSize = 19, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock headerTitle = new() { FontSize = 19, FontWeight = FontWeights.SemiBold, Foreground = Ui.Ink, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(11, 0, 0, 0) };
    private readonly Border headerRule = new() { Height = 1, Margin = new Thickness(22, 58, 22, 0), VerticalAlignment = VerticalAlignment.Top };
    private readonly Border pinButton;
    private readonly TextBlock pinGlyph = Ui.Glyph("", 15, Ui.Dim);
    private readonly Border batteryBox = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 14, 0), Visibility = Visibility.Collapsed };
    private readonly TextBlock batteryText = new() { FontSize = 14, FontWeight = FontWeights.SemiBold, Foreground = Ui.Ink, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
    private readonly Border batteryFill = new() { CornerRadius = new CornerRadius(2), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(2), Background = Ui.Ink };
    private readonly StackPanel dockRow = new() { Orientation = Orientation.Horizontal };
    private readonly List<(IslandPanel panel, Border button, TextBlock glyph)> dockItems = new();
    private readonly List<IslandPanel> panels;
    private readonly AppsPanel appsPanel;
    private IslandPanel current;

    private readonly DispatcherTimer hover = new() { Interval = TimeSpan.FromMilliseconds(150) };
    private readonly DispatcherTimer leave = new() { Interval = TimeSpan.FromMilliseconds(400) };
    private readonly DispatcherTimer monitor = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly DispatcherTimer alertTimer = new() { Interval = TimeSpan.FromSeconds(5) };
    private readonly DispatcherTimer rotation = new() { Interval = TimeSpan.FromSeconds(8) };
    private readonly IslandAnimator animator;
    private readonly Dashboard dashboard = new();
    private readonly Queue<AlertItem> alerts = new();
    private bool expanded, previewing;
    private nint handle;
    private AlertItem? currentAlert;
    private int activityIndex, ticks;
    private bool pauseAlerts;
    public bool PauseAlerts
    {
        get => pauseAlerts;
        set
        {
            if (pauseAlerts == value) return;
            pauseAlerts = value;
            if (value) alerts.Clear();
            Hub.SetPaused(value);
        }
    }

    public IslandWindow()
    {
        Title = "Island"; Width = WinW; Height = WinH; WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent;
        ShowInTaskbar = false; ShowActivated = false; Topmost = true; ResizeMode = ResizeMode.NoResize; FontFamily = Ui.TextFont;
        UseLayoutRounding = true; SnapsToDevicePixels = true; TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
        host.Resources.MergedDictionaries.Add(Ui.Resources); System.Windows.Documents.TextElement.SetForeground(host, Brushes.White); // default text colour for any element that doesn't set one (e.g. emoji)

        Hub.Dashboard = dashboard; Hub.OpenSettings = OpenSettings; Hub.RefreshNow = RefreshNow; Hub.Collapse = () => Expand(false); Hub.FocusInput = FocusInput;
        Hub.PauseChanged += on => { if (pauseAlerts != on) PauseAlerts = on; };
        var previousChanged = SettingsWindow.Changed; SettingsWindow.Changed = () => { previousChanged?.Invoke(); ApplyPanels(); };
        Hub.AlertRequested += item => { if (!PauseAlerts && alerts.Count < 50) { alerts.Enqueue(item); ShowNextAlert(); } };
        Hub.NotchOverlayChanged += OnOverlayChanged;

        pinButton = Ui.IconButton("", "Show this on the notch when collapsed", TogglePin, 34); pinButton.Child = pinGlyph;
        panels = DiscoverPanels(); current = panels.First(p => p.Id == "home");
        Hub.ShowPanel = id => { var panel = panels.FirstOrDefault(p => p.Id == id); if (panel != null && Settings.Load().Allows(panel.Id)) { ShowPanel(panel, true); Expand(true); } };
        appsPanel = new AppsPanel(panels.ToArray(), panel => ShowPanel(panel, true));
        panels.Add(appsPanel);

        var activityRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        activityGlyph.Margin = new Thickness(0, 0, 9, 0); activityGlyph.Visibility = Visibility.Collapsed;
        activityRow.Children.Add(activityGlyph); activityRow.Children.Add(activity);
        activityLayer.Children.Add(activityRow); activityLayer.ToolTip = "Island — hover to expand";
        BuildAlertLayer(); BuildDash();
        host.Children.Add(activityLayer); host.Children.Add(overlayLayer); host.Children.Add(alertLayer); host.Children.Add(dash);
        host.ContextMenuOpening += OnContextMenu;
        Content = host;
        Render(IdleW, IdleH, IdleR);
        ShowPanel(current, false); UpdateBattery();

        animator = new IslandAnimator(Render);
        dashboard.Alert += message => { if (!PauseAlerts && alerts.Count < 50) { alerts.Enqueue(FromDashboard(message)); ShowNextAlert(); } };
        alertTimer.Tick += (_, _) => { alertTimer.Stop(); currentAlert = null; SetActivity(); ApplyState(); ShowNextAlert(); };
        rotation.Tick += (_, _) => { activityIndex++; SetActivity(); }; rotation.Start();
        SettingsWindow.ConnectGoogle = async () => { await dashboard.Auth.Connect(); dashboard.Gmail.Reset(); dashboard.Meetings.Clear(); dashboard.Agenda.Clear(); await dashboard.Refresh(true); };
        SettingsWindow.DisconnectGoogle = () => { dashboard.Auth.Disconnect(); dashboard.Meetings.Clear(); dashboard.Agenda.Clear(); dashboard.Gmail.Reset(); dashboard.RaiseChanged(); };
        Loaded += async (_, _) => await dashboard.Refresh();
        AllowDrop = true;
        PreviewDragOver += OnShelfDrag;
        PreviewDrop += OnShelfDrop;
        MouseEnter += (_, _) => { leave.Stop(); hover.Start(); }; MouseLeave += (_, _) => { hover.Stop(); leave.Start(); };
        MouseLeftButtonDown += (_, _) => { leave.Stop(); Expand(true); };
        Deactivated += (_, _) => { if (expanded && !IsMouseOver) leave.Start(); };
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape && expanded) { Keyboard.ClearFocus(); Expand(false); e.Handled = true; } };
        PreviewMouseWheel += OnWheel;
        hover.Tick += (_, _) => { hover.Stop(); Expand(true); };
        leave.Tick += (_, _) => { leave.Stop(); if (TypingInside()) return; Expand(false); };
        SourceInitialized += (_, _) =>
        {
            handle = new WindowInteropHelper(this).Handle; Hub.Handle = handle;
            NativeMethods.SetWindowLong(handle, -20, (nint)((long)NativeMethods.GetWindowLong(handle, -20) | 0x08000080));
            HwndSource.FromHwnd(handle)?.AddHook(Hook); Place();
            var allowed = Settings.Load(); foreach (var p in panels) if (allowed.Allows(p.Id)) StartPanel(p);
            if (!classHandlersRegistered)
            {
                classHandlersRegistered = true;
                EventManager.RegisterClassHandler(typeof(TextBoxBase), PreviewMouseLeftButtonDownEvent, new MouseButtonEventHandler((s, _) => FocusInput((UIElement)s)));
                EventManager.RegisterClassHandler(typeof(PasswordBox), PreviewMouseLeftButtonDownEvent, new MouseButtonEventHandler((s, _) => FocusInput((UIElement)s)));
            }
        };
        monitor.Tick += (_, _) => { CheckFullscreen(); ShowNextAlert(); var allowed = Settings.Load(); foreach (var p in panels) if (allowed.Allows(p.Id)) Safe(p.Tick); if (++ticks % 30 == 0) UpdateBattery(); SetActivity(); }; monitor.Start();
        Closed += (_, _) => { animator.Stop(); dashboard.Stop(); rotation.Stop(); alertTimer.Stop(); monitor.Stop(); hover.Stop(); leave.Stop(); };
    }

    /// <summary>Dev aid (`Island.exe --preview <dir>`): renders every panel to a PNG, then exits.</summary>
    public void RunPreview(string dir)
    {
        System.IO.Directory.CreateDirectory(dir); previewing = true; expanded = true; animator.Stop(); Render(FullW, FullH, FullR);
        dash.BeginAnimation(OpacityProperty, null); dash.Opacity = 1; dash.IsHitTestVisible = true; activityLayer.Opacity = 0;
        int i = 0; var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        void Shoot(IslandPanel p)
        {
            host.UpdateLayout(); var rtb = new RenderTargetBitmap((int)WinW * 2, (int)WinH * 2, 192, 192, PixelFormats.Pbgra32); rtb.Render(host);
            var enc = new PngBitmapEncoder(); enc.Frames.Add(BitmapFrame.Create(rtb)); using var f = System.IO.File.Create(System.IO.Path.Combine(dir, p.Id + ".png")); enc.Save(f);
        }
        t.Tick += (_, _) =>
        {
            if (i > 0) Shoot(panels[i - 1]);
            if (i >= panels.Count) { t.Stop(); Application.Current.Shutdown(); return; }
            ShowPanel(panels[i], false); i++;
        };
        t.Start();
    }

    private readonly HashSet<string> started = new();
    private static void Safe(Action a) { try { a(); } catch { /* one broken panel must never take the island down */ } }
    private void StartPanel(IslandPanel panel) { if (started.Add(panel.Id)) Safe(panel.Start); }
    private void ApplyPanels()
    {
        var allowed = Settings.Load();
        foreach (var (panel, button, _) in dockItems)
        {
            bool on = allowed.Allows(panel.Id);
            button.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
            if (on) StartPanel(panel);
        }
        if (!allowed.Allows(current.Id)) ShowPanel(panels.First(p => p.Id == "home"), false);
    }

    private static List<IslandPanel> DiscoverPanels()
    {
        var list = new List<IslandPanel>();
        foreach (var t in typeof(IslandPanel).Assembly.GetTypes().Where(t => !t.IsAbstract && t.IsSubclassOf(typeof(IslandPanel)) && t.GetConstructor(Type.EmptyTypes) != null))
            try { list.Add((IslandPanel)Activator.CreateInstance(t)!); } catch { }
        return list.OrderBy(p => p.Order).ToList();
    }

    // ---- window plumbing -------------------------------------------------------------------------------------

    private nint Hook(nint hwnd, int msg, nint wp, nint lp, ref bool handled)
    {
        if (msg == 0x21)
        {   // WM_MOUSEACTIVATE: never take focus, except when the click lands in a text box (FocusInput activates explicitly)
            handled = true; return 3;
        }
        if (msg == 0x2e0 || msg == 0x7e) Dispatcher.BeginInvoke(Place); // DPI / display change: re-pin once, never per frame
        try { Hub.RaiseWindowMessage(msg, wp, lp); } catch { }
        return 0;
    }

    // The window is positioned exactly once (and again only on DPI/display changes). It never moves during animation.
    private void Place()
    {
        var screen = System.Windows.Forms.Screen.PrimaryScreen!; double scale = handle == 0 ? 1 : NativeMethods.GetDpiForWindow(handle) / 96d;
        Left = (screen.Bounds.Left + (screen.Bounds.Width - WinW * scale) / 2) / scale; Top = screen.Bounds.Top / scale;
    }

    // Notch silhouette: flush with the top screen edge, concave "ears" where it meets the bezel, rounded bottom.
    private static StreamGeometry Notch(double w, double h, double r)
    {
        r = Math.Min(r, h / 2); double e = Math.Min(r * .55, 14), cx = WinW / 2, bl = cx - w / 2, br = cx + w / 2;
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(new Point(bl - e, 0), true, true);
            c.LineTo(new Point(br + e, 0), true, false);
            c.ArcTo(new Point(br, e), new Size(e, e), 0, false, SweepDirection.Counterclockwise, true, false);
            c.LineTo(new Point(br, h - r), true, false);
            c.ArcTo(new Point(br - r, h), new Size(r, r), 0, false, SweepDirection.Clockwise, true, false);
            c.LineTo(new Point(bl + r, h), true, false);
            c.ArcTo(new Point(bl, h - r), new Size(r, r), 0, false, SweepDirection.Clockwise, true, false);
            c.LineTo(new Point(bl, e), true, false);
            c.ArcTo(new Point(bl - e, 0), new Size(e, e), 0, false, SweepDirection.Counterclockwise, true, false);
        }
        g.Freeze(); return g;
    }

    // Per-frame work: swap one clip geometry. Nothing else.
    private void Render(double w, double h, double r) => host.Clip = Notch(w, h, r);

    private static void Fade(UIElement e, double to, int ms, int delay = 0) =>
        e.BeginAnimation(OpacityProperty, new DoubleAnimation(to, TimeSpan.FromMilliseconds(ms)) { BeginTime = TimeSpan.FromMilliseconds(delay), EasingFunction = new QuadraticEase() });

    private void ApplyState()
    {
        bool alert = !expanded && currentAlert != null, idle = !expanded && currentAlert == null, overlay = idle && Hub.NotchOverlay != null;
        double idleW = Hub.NotchOverlay != null ? Math.Max(IdleW, Hub.NotchOverlayWidth + 34) : IdleW;
        if (expanded) animator.Target(FullW, FullH, FullR); else if (alert) animator.Target(AlertW, AlertH, AlertR); else animator.Target(idleW, IdleH, IdleR);
        dash.IsHitTestVisible = expanded; alertLayer.IsHitTestVisible = alert;
        if (expanded) Fade(dash, 1, 220, 130); else Fade(dash, 0, 90);
        if (alert) Fade(alertLayer, 1, 180, 90); else Fade(alertLayer, 0, 90);
        if (idle && !overlay) Fade(activityLayer, 1, 180, 100); else Fade(activityLayer, 0, 80);
        if (overlay) Fade(overlayLayer, 1, 180, 100); else Fade(overlayLayer, 0, 80);
    }

    private void OnOverlayChanged()
    {
        overlayLayer.Children.Clear(); var el = Hub.NotchOverlay; if (el != null) { overlayLayer.Width = Hub.NotchOverlayWidth; overlayLayer.Children.Add(el); }
        ApplyState();
    }

    private void Expand(bool value)
    {
        if (expanded == value || (previewing && !value)) return;
        expanded = value;
        if (value) { Safe(current.OnShown); UpdateBattery(); } else { Safe(current.OnHidden); if (TypingInside()) { Keyboard.ClearFocus(); } }
        SetActivity();
        ApplyState();
    }

    private bool TypingInside() => IsActive && Keyboard.FocusedElement is TextBoxBase or PasswordBox;

    // Make a text box typeable: the island is a no-activate window, so activate it explicitly on click.
    private void FocusInput(UIElement target)
    {
        if (!expanded || !target.IsDescendantOf(this)) return; leave.Stop();
        if (!IsActive) { try { Activate(); NativeMethods.SetForegroundWindow(handle); } catch { } }
        Dispatcher.BeginInvoke(DispatcherPriority.Input, () => Keyboard.Focus(target));
    }

    private void CheckFullscreen()
    {
        if (previewing) return;
        nint fg = NativeMethods.GetForegroundWindow(); bool full = false;
        if (fg != handle && fg != NativeMethods.GetShellWindow() && NativeMethods.GetWindowRect(fg, out var rect))
        {
            var cls = new System.Text.StringBuilder(128); NativeMethods.GetClassName(fg, cls, 128);
            var b = System.Windows.Forms.Screen.PrimaryScreen!.Bounds;
            full = cls.ToString() is not ("Progman" or "WorkerW") && rect.Left <= b.Left && rect.Top <= b.Top && rect.Right >= b.Right && rect.Bottom >= b.Bottom;
        }
        if (full && IsVisible) Hide(); else if (!full && !IsVisible && Settings.Load().Onboarded) Show();
    }

    public void OpenSettings() { foreach (Window w in Application.Current.Windows) if (w is SettingsWindow) { w.Activate(); return; } new SettingsWindow().Show(); }

    private void OnWheel(object sender, MouseWheelEventArgs e)
    {
        if (host.Clip == null || !host.Clip.FillContains(e.GetPosition(host))) return;
        if (!expanded) { leave.Stop(); Expand(true); e.Handled = true; return; }
        if (current.Id != "nowplaying" || current is not NowPlayingPanel playing) return;
        playing.Skip(e.Delta > 0);
        e.Handled = true;
    }

    private bool OverNotch(DragEventArgs e)
    {
        if (host.Clip == null) return false;
        var p = e.GetPosition(host);
        return host.Clip.FillContains(p);
    }

    private void OnShelfDrag(object sender, DragEventArgs e)
    {
        bool ok = OverNotch(e) && (e.Data.GetDataPresent(DataFormats.FileDrop) || e.Data.GetDataPresent(DataFormats.UnicodeText) || e.Data.GetDataPresent(DataFormats.Text));
        e.Effects = ok ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnShelfDrop(object sender, DragEventArgs e)
    {
        if (!OverNotch(e)) return;
        if (!ShelfBoard.Add(e.Data)) return;
        var shelf = panels.FirstOrDefault(p => p.Id == "shelf");
        if (shelf != null) ShowPanel(shelf, true);
        if (!expanded) Expand(true);
        e.Handled = true;
    }
    public async void RefreshNow() => await dashboard.Refresh(true);

    // ---- expanded chrome: header, panel host, dock ------------------------------------------------------------

    private void BuildDash()
    {
        // header
        var head = new Grid { Height = 40, Margin = new Thickness(24, 12, 24, 0), VerticalAlignment = VerticalAlignment.Top };
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.ColumnDefinitions.Add(new ColumnDefinition());
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var appsContent = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        appsContent.Children.Add(Ui.Glyph(appsPanel.Glyph, 15));
        var appsLabel = Ui.Text("Apps", 12.5, Ui.Ink, FontWeights.SemiBold); appsLabel.Margin = new Thickness(7, 0, 0, 0); appsContent.Children.Add(appsLabel);
        var appsButton = Ui.IconButton(appsPanel.Glyph, "Browse Island apps", () => ShowPanel(appsPanel, true), 34);
        appsButton.Width = double.NaN; appsButton.Padding = new Thickness(12, 0, 12, 0); appsButton.Child = appsContent;
        head.Children.Add(appsButton);
        var title = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(18, 0, 12, 0) }; title.Children.Add(headerGlyph); title.Children.Add(headerTitle); Grid.SetColumn(title, 1); head.Children.Add(title);
        batteryBox.Child = BuildBattery();
        var right = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        pinButton.Margin = new Thickness(0, 0, 8, 0); right.Children.Add(pinButton); right.Children.Add(batteryBox);
        var gear = Ui.IconButton("", "Settings", OpenSettings, 34); gear.Margin = new Thickness(0, 0, 8, 0); right.Children.Add(gear);
        right.Children.Add(Ui.IconButton("", "Close", () => Expand(false), 34));
        Grid.SetColumn(right, 2); head.Children.Add(right);

        panelHost.HorizontalAlignment = HorizontalAlignment.Center; panelHost.VerticalAlignment = VerticalAlignment.Top; panelHost.Margin = new Thickness(0, 68, 0, 0);

        // dock
        var allowed = Settings.Load();
        foreach (var p in panels.Where(p => p != appsPanel))
        {
            var g = Ui.Glyph(p.Glyph, 18, Ui.Dim);
            var b = new Border { Width = 38, Height = 38, CornerRadius = new CornerRadius(19), Margin = new Thickness(1.5, 0, 1.5, 0), Cursor = Cursors.Hand, ToolTip = p.Title, Background = Brushes.Transparent, Child = g };
            var panel = p;
            b.MouseEnter += (_, _) => { if (panel != current) b.Background = Ui.Chip; }; b.MouseLeave += (_, _) => { if (panel != current) b.Background = Brushes.Transparent; };
            b.MouseLeftButtonUp += (_, e) => { ShowPanel(panel, true); e.Handled = true; };
            if (!allowed.Allows(p.Id)) b.Visibility = Visibility.Collapsed;
            dockItems.Add((p, b, g)); dockRow.Children.Add(b);
        }
        var dock = new Border { Background = Ui.B("#0F0F11"), BorderBrush = Ui.Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(27), Padding = new Thickness(7), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 16), Child = dockRow };

        dash.Children.Add(headerRule); dash.Children.Add(head); dash.Children.Add(panelHost); dash.Children.Add(dock);
    }

    private UIElement BuildBattery()
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var body = new Border { Width = 28, Height = 14, CornerRadius = new CornerRadius(4), BorderBrush = Ui.B("#6A6A72"), BorderThickness = new Thickness(1.5), Child = batteryFill };
        var nub = new Border { Width = 2.5, Height = 6, CornerRadius = new CornerRadius(1), Background = Ui.B("#6A6A72"), Margin = new Thickness(1, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        row.Children.Add(batteryText); row.Children.Add(body); row.Children.Add(nub); return row;
    }

    private void UpdateBattery()
    {
        try
        {
            var ps = System.Windows.Forms.SystemInformation.PowerStatus;
            if (ps.BatteryChargeStatus.HasFlag(System.Windows.Forms.BatteryChargeStatus.NoSystemBattery) || ps.BatteryLifePercent > 1) { batteryBox.Visibility = Visibility.Collapsed; return; }
            double pct = ps.BatteryLifePercent; batteryBox.Visibility = Visibility.Visible; batteryText.Text = (int)Math.Round(pct * 100) + "%";
            batteryFill.Width = Math.Max(2, 22 * pct); batteryFill.Background = ps.PowerLineStatus == System.Windows.Forms.PowerLineStatus.Online ? Ui.Green : pct <= .2 ? Ui.Red : Ui.Ink;
        }
        catch { batteryBox.Visibility = Visibility.Collapsed; }
    }

    private void ShowPanel(IslandPanel panel, bool animate)
    {
        if (panel != current) { Safe(current.OnHidden); if (current.IsBuilt) current.View.Visibility = Visibility.Collapsed; }
        current = panel;
        FrameworkElement view;
        try { view = panel.View; }
        catch (Exception ex) { view = new Grid { Width = Ui.ContentW, Height = Ui.ContentH }; ((Grid)view).Children.Add(Ui.Text("This panel failed to load: " + ex.Message, 14, Ui.Dim)); }
        if (view.Parent == null) panelHost.Children.Add(view);
        view.Visibility = Visibility.Visible; if (animate) Ui.FadeIn(view, 170);
        headerGlyph.Text = panel.Glyph; headerGlyph.Foreground = Ui.B(panel.Accent); headerTitle.Text = panel.Title;
        headerRule.Background = new LinearGradientBrush(new GradientStopCollection { new(Colors.Transparent, 0), new(Color.FromArgb(0x70, panel.Accent.R, panel.Accent.G, panel.Accent.B), .5), new(Colors.Transparent, 1) }, 0);
        foreach (var (p, b, g) in dockItems)
        {
            bool on = p == panel; b.Background = on ? Ui.B(p.Accent, 0x33) : Brushes.Transparent; g.Foreground = on ? Ui.B(p.Accent) : Ui.Dim;
        }
        UpdatePin(); if (expanded) Safe(panel.OnShown); SetActivity();
    }

    private void UpdatePin()
    {
        bool can = current.RestingText != null; pinButton.Visibility = can ? Visibility.Visible : Visibility.Collapsed;
        bool pinned = Settings.Load().PinnedPanel == current.Id; pinGlyph.Foreground = pinned ? Ui.B(current.Accent) : Ui.Dim; pinGlyph.Text = pinned ? "" : "";
        pinButton.ToolTip = pinned ? "Pinned to the notch — click to unpin" : "Show this on the notch when collapsed";
    }

    private void TogglePin() { var s = Settings.Load(); s.PinnedPanel = s.PinnedPanel == current.Id ? "" : current.Id; s.Save(); UpdatePin(); SetActivity(); }

    private void OnContextMenu(object sender, ContextMenuEventArgs e)
    {
        if (expanded) { e.Handled = true; return; }
        var menu = new ContextMenu(); var pinned = Settings.Load().PinnedPanel;
        void Add(string header, string id) { var item = new MenuItem { Header = header, IsCheckable = true, IsChecked = pinned == id }; item.Click += (_, _) => { var s = Settings.Load(); s.PinnedPanel = id; s.Save(); UpdatePin(); SetActivity(); }; menu.Items.Add(item); }
        Add("Rotate activity", ""); Add("Clock", "clock");
        foreach (var p in panels.Where(p => p.RestingText != null)) Add(p.Title, p.Id);
        host.ContextMenu = menu;
    }

    // ---- alerts + collapsed-pill activity --------------------------------------------------------------------

    private static AlertItem FromDashboard(string message)
    {
        string glyph = "!", title = message, sub = "Tap to view"; string tint = "#FF9F0A"; bool font = false;
        if (message.StartsWith('+')) { int via = message.LastIndexOf(" via ", StringComparison.Ordinal); glyph = "$"; tint = "#30D158"; title = "New sale  " + (via > 0 ? message[..via] : message); sub = via > 0 ? "Paid via " + message[(via + 5)..] : "Payment received"; }
        else if (message.StartsWith("Important email: ", StringComparison.Ordinal)) { glyph = ""; font = true; title = "Important email"; sub = message["Important email: ".Length..]; }
        else { int at = message.LastIndexOf(" starts in ", StringComparison.Ordinal); if (at > 0) { glyph = ""; font = true; tint = "#0A84FF"; title = message[..at]; sub = "Starts in " + message[(at + 11)..] + " — tap to view"; } }
        return new AlertItem(title, sub, glyph, Ui.C(tint), font);
    }

    private void BuildAlertLayer()
    {
        alertIcon.Child = alertGlyph;
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16, 0, 10, 0) }; text.Children.Add(alertTitle); text.Children.Add(alertSub);
        var chevron = Ui.Glyph("", 14, Ui.Dim); chevron.Margin = new Thickness(0, 0, 4, 0);
        var row = new Grid { Margin = new Thickness(22, 0, 20, 0) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); row.ColumnDefinitions.Add(new ColumnDefinition()); row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.Children.Add(alertIcon); Grid.SetColumn(text, 1); row.Children.Add(text); Grid.SetColumn(chevron, 2); row.Children.Add(chevron);
        alertLayer.Children.Add(row);
    }

    private void ShowAlert(AlertItem a)
    {
        alertGlyph.Text = a.Glyph; alertGlyph.FontFamily = a.IconFont ? Ui.IconFont : FontFamily; alertGlyph.Foreground = Ui.B(a.Tint);
        alertIcon.Background = Ui.B(a.Tint, 0x30); alertIcon.BorderBrush = Ui.B(a.Tint, 0x80); alertTitle.Text = a.Title; alertSub.Text = a.Subtitle;
    }

    private void ShowNextAlert()
    {
        if (PauseAlerts) { alerts.Clear(); return; }
        if (currentAlert != null || alerts.Count == 0 || !IsVisible) return;
        currentAlert = alerts.Dequeue(); ShowAlert(currentAlert); ApplyState(); alertTimer.Start();
    }

    private void SetActivity()
    {
        if (currentAlert != null) return;
        var settings = Settings.Load();
        // Running tasks remain visible even when the user switches to another page.
        var task = panels.FirstOrDefault(p => settings.Allows(p.Id) && p.ActiveTaskText != null);
        if (task != null) { SetActivityContent(task.ActiveTaskText!, task); return; }
        if (current is ClaudePanel && settings.Allows(current.Id)) { SetActivityContent(current.RestingText!, current); return; }
        string pinned = settings.PinnedPanel;
        if (pinned.Length > 0)
        {
            var panel = panels.FirstOrDefault(p => p.Id == pinned);
            string? text = pinned == "clock" ? DateTime.Now.ToString("h:mm tt") : panel?.RestingText;
            if (text != null) { SetActivityContent(text, panel); return; }
        }
        var values = new List<string>();
        if (settings.Revenue) values.Add("$  " + dashboard.RevenueActivity);
        if (settings.Calendar) { var m = dashboard.Meetings.Find(m => !m.AllDay && m.Start > DateTimeOffset.Now); values.Add(m == null ? "▦  No upcoming meetings" : "▦  " + m.Title + " in " + Math.Ceiling((m.Start - DateTimeOffset.Now).TotalMinutes) + "m"); }
        if (settings.Email) values.Add("✉  " + dashboard.Gmail.Unread + " unread");
        if (values.Count == 0) values.Add("Island  •  Ready");
        SetActivityContent(values[activityIndex % values.Count]);
    }

    private void SetActivityContent(string text, IslandPanel? panel = null)
    {
        activity.Text = text;
        activity.MaxWidth = IdleW - (panel != null ? 60 : 30);
        activityGlyph.Text = panel?.Glyph ?? "";
        activityGlyph.Foreground = panel != null ? Ui.B(panel.Accent) : Ui.Ink;
        activityGlyph.Visibility = panel != null ? Visibility.Visible : Visibility.Collapsed;
        activityLayer.ToolTip = text + " — hover to expand";
    }
}

