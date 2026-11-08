using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
namespace Island;

internal sealed class SettingsWindow : Window
{
    public static Action? Changed { get; set; }
    public static Func<Task>? ConnectGoogle { get; set; }
    public static Action? DisconnectGoogle { get; set; }
    public static Func<string, string, Task>? TestProvider { get; set; }

    private readonly Settings settings = Settings.Load();
    private SecretStore secrets = new();
    private readonly Grid page = new();
    private readonly Dictionary<string, Border> nav = new();
    private string current = "general";
    private TextBlock? banner;

    public SettingsWindow()
    {
        Title = "Island"; Width = 920; Height = 640; WindowStartupLocation = WindowStartupLocation.CenterScreen;
        WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent;
        ShowInTaskbar = true; ResizeMode = ResizeMode.CanMinimize; FontFamily = Ui.TextFont;
        var shell = new Border { Background = Brushes.Black, CornerRadius = new CornerRadius(16), BorderBrush = Ui.Line, BorderThickness = new Thickness(1) };
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(48) });
        root.RowDefinitions.Add(new RowDefinition());
        root.Children.Add(TitleBar());
        var body = new Grid();
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });
        body.ColumnDefinitions.Add(new ColumnDefinition());
        body.Children.Add(Sidebar());
        page.Margin = new Thickness(28, 8, 28, 22); Grid.SetColumn(page, 1); body.Children.Add(page);
        Grid.SetRow(body, 1); root.Children.Add(body);
        shell.Child = root; Content = shell;
        shell.Resources.MergedDictionaries.Add(Ui.Resources);
        SourceInitialized += (_, _) => { try { var hwnd = new WindowInteropHelper(this).Handle; int pref = 2; NativeMethods.DwmSetWindowAttribute(hwnd, 33, ref pref, 4); } catch { } };
        ShowPage("general");
    }

    private UIElement TitleBar()
    {
        var bar = new Grid { Height = 48 };
        var left = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(18, 0, 0, 0) };
        left.Children.Add(new Border { Width = 22, Height = 8, CornerRadius = new CornerRadius(0, 0, 4, 4), Background = Brushes.Black, BorderBrush = Ui.Line, BorderThickness = new Thickness(1), VerticalAlignment = VerticalAlignment.Center });
        var word = Ui.Text("Island", 15, Ui.Ink, FontWeights.SemiBold); word.Margin = new Thickness(10, 0, 0, 0); left.Children.Add(word);
        bar.Children.Add(left);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(Caption("—", () => WindowState = WindowState.Minimized));
        buttons.Children.Add(Caption("✕", Close));
        bar.Children.Add(buttons);
        bar.MouseLeftButtonDown += (_, e) => { try { DragMove(); } catch { } e.Handled = true; };
        return bar;
    }

    private static Border Caption(string glyph, Action click)
    {
        var b = new Border { Width = 46, Height = 48, Background = Brushes.Transparent, Cursor = Cursors.Hand, Child = Ui.Text(glyph, 14, Ui.Dim, FontWeights.SemiBold) };
        ((TextBlock)b.Child).HorizontalAlignment = HorizontalAlignment.Center; ((TextBlock)b.Child).VerticalAlignment = VerticalAlignment.Center;
        b.MouseLeftButtonDown += (_, e) => e.Handled = true;
        b.MouseEnter += (_, _) => b.Background = Ui.Chip; b.MouseLeave += (_, _) => b.Background = Brushes.Transparent;
        b.MouseLeftButtonUp += (_, e) => { click(); e.Handled = true; };
        return b;
    }

    private UIElement Sidebar()
    {
        var side = new StackPanel { Background = Ui.B("#0F0F11"), Margin = new Thickness(0, 0, 0, 12) };
        side.Children.Add(Item("general", "General", "\uE713"));
        side.Children.Add(Item("connections", "Connections", "\uE8A5"));
        side.Children.Add(Item("apps", "Apps", "\uE80A"));
        side.Children.Add(Item("alerts", "Alerts", "\uEA8F"));
        side.Children.Add(Item("permissions", "Permissions", "\uE72E"));
        side.Children.Add(Item("about", "About", "\uE946"));
        return side;
    }

    private Border Item(string id, string title, string glyph)
    {
        var row = new Border { Height = 42, Margin = new Thickness(12, 4, 12, 0), CornerRadius = new CornerRadius(12), Background = Brushes.Transparent, Cursor = Cursors.Hand };
        var line = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
        var icon = Ui.Glyph(glyph, 16, Ui.Dim); var label = Ui.Text(title, 14, Ui.Dim, FontWeights.SemiBold); label.Margin = new Thickness(10, 0, 0, 0);
        line.Children.Add(icon); line.Children.Add(label); row.Child = line; row.Tag = (icon, label);
        row.MouseLeftButtonUp += (_, e) => { ShowPage(id); e.Handled = true; };
        nav[id] = row; return row;
    }

    private void ShowPage(string id)
    {
        current = id;
        foreach (var pair in nav)
        {
            bool on = pair.Key == id; pair.Value.Background = on ? Ui.B(Ui.C("#0A84FF"), 0x33) : Brushes.Transparent;
            if (pair.Value.Tag is (TextBlock icon, TextBlock label)) { icon.Foreground = on ? Ui.Blue : Ui.Dim; label.Foreground = on ? Ui.Blue : Ui.Dim; }
        }
        page.Children.Clear();
        banner = Ui.Text(secrets.LoadError ?? "", 12.5, Ui.Orange); banner.TextWrapping = TextWrapping.Wrap; banner.Margin = new Thickness(0, 0, 0, 8);
        var body = id switch
        {
            "connections" => Connections(),
            "apps" => Apps(),
            "alerts" => Alerts(),
            "permissions" => Permissions(),
            "about" => About(),
            _ => General(),
        };
        var stack = new StackPanel(); if ((secrets.LoadError ?? "").Length > 0) stack.Children.Add(banner); stack.Children.Add(body);
        page.Children.Add(Ui.Scroll(stack));
    }

    private void Persist() { try { settings.Save(); Changed?.Invoke(); } catch { Note("Could not save settings."); } }
    private void Note(string text) { if (banner != null) banner.Text = text; }

    private UIElement General()
    {
        var stack = new StackPanel();
        stack.Children.Add(Heading("General"));
        stack.Children.Add(ToggleRow("Start with Windows", "Opens Island when you sign in. Publish first if you are running from dotnet.", Startup.Enabled, on =>
        {
            try { Startup.Set(on); } catch (Exception ex) { Note(ex is InvalidOperationException ? ex.Message : "Could not update your Windows startup setting."); }
        }));
        stack.Children.Add(ToggleRow("Pause alerts", "Stops new banners. The tray checkbox stays in step.", Hub.Paused, on => Hub.SetPaused(on)));
        stack.Children.Add(Ui.Text("Collapsed notch", 13, Ui.Dim));
        var pin = new SegmentedControl(new[] { "Rotate", "Clock" }, settings.PinnedPanel == "clock" ? 1 : 0, Ui.C("#0A84FF"), i => { settings.PinnedPanel = i == 1 ? "clock" : ""; Persist(); });
        pin.Margin = new Thickness(0, 8, 0, 8); stack.Children.Add(pin);
        var pins = new WrapPanel { Margin = new Thickness(0, 0, 0, 14) };
        foreach (var panel in Panels())
        {
            if (panel.Id is "home" or "apps") continue;
            var id = panel.Id;
            var chip = Ui.Button(panel.Title, settings.PinnedPanel == id ? Ui.C("#30D158") : Ui.C("#0A84FF"), () => { settings.PinnedPanel = id; Persist(); ShowPage("general"); }, 28);
            chip.Margin = new Thickness(0, 0, 6, 6); pins.Children.Add(chip);
        }
        stack.Children.Add(pins);
        stack.Children.Add(Interval("Revenue", settings.RevenueMinutes, v => settings.RevenueMinutes = v));
        stack.Children.Add(Interval("Calendar", settings.CalendarMinutes, v => settings.CalendarMinutes = v));
        stack.Children.Add(Interval("Email", settings.EmailMinutes, v => settings.EmailMinutes = v));
        return stack;
    }

    private UIElement Interval(string name, int value, Action<int> set)
    {
        var box = Ui.Input("1–60"); box.Text = value.ToString(); box.Width = 80; box.HorizontalAlignment = HorizontalAlignment.Left; box.Margin = new Thickness(0, 4, 0, 12);
        var label = Ui.Text(name + " refresh, minutes", 14, Ui.Ink, FontWeights.SemiBold);
        box.LostFocus += (_, _) => { if (!int.TryParse(box.Text, out int n) || n < 1 || n > 60) { Note("Enter intervals from 1 to 60."); box.Text = value.ToString(); return; } set(n); Persist(); };
        var stack = new StackPanel(); stack.Children.Add(label); stack.Children.Add(box); return stack;
    }

    private UIElement Connections()
    {
        var stack = new StackPanel();
        stack.Children.Add(Heading("Connections"));
        stack.Children.Add(SecretCard("Stripe", "Stripe", "Paste a Stripe restricted key with read access to Charges.", "Stripe: connected"));
        stack.Children.Add(SecretCard("Polar", "Polar", "Paste a Polar organization token with orders:read.", "Polar: connected"));
        stack.Children.Add(GoogleCard());
        var open = Ui.Button("Open Analytics in the island", Ui.C("#8B7CFF"), () => Hub.ShowPanel?.Invoke("analytics"), 32);
        open.HorizontalAlignment = HorizontalAlignment.Left; open.Margin = new Thickness(0, 8, 0, 0); stack.Children.Add(open);
        var hint = Ui.Text("Analytics keys stay in the encrypted store. Google Analytics uses the same sign-in plus a property id, which the Analytics panel asks for. Scopes stay calendar.readonly and gmail.readonly.", 12.5, Ui.Dim);
        hint.TextWrapping = TextWrapping.Wrap; hint.Margin = new Thickness(0, 8, 0, 0); stack.Children.Add(hint);
        return stack;
    }

    private UIElement SecretCard(string title, string key, string why, string ok)
    {
        var box = new PasswordBox { Height = 40, Password = secrets.Get(key), Margin = new Thickness(0, 8, 0, 8) };
        var status = Ui.Text("", 12.5, Ui.Dim); status.TextWrapping = TextWrapping.Wrap;
        var test = Ui.Button("Test", Ui.C("#30D158"), async () =>
        {
            try
            {
                secrets.Set(key, box.Password.Trim());
                if (TestProvider == null) { status.Text = "Provider available after the island is running."; return; }
                await TestProvider(title, box.Password.Trim());
                status.Text = ok; status.Foreground = Ui.Green;
            }
            catch (Exception ex) { status.Text = ex is System.Net.Http.HttpRequestException ? "Connection failed. Check key, permissions, and network." : "Invalid key — check permissions and try again."; status.Foreground = Ui.Orange; }
        }, 32);
        test.HorizontalAlignment = HorizontalAlignment.Left;
        var whyText = Ui.Text(why, 12.5, Ui.Dim); whyText.TextWrapping = TextWrapping.Wrap;
        var stack = new StackPanel(); stack.Children.Add(Ui.Text(title, 16, Ui.Ink, FontWeights.SemiBold)); stack.Children.Add(whyText); stack.Children.Add(box); stack.Children.Add(test); stack.Children.Add(status);
        var card = Ui.Card(stack, 14, 16); card.Margin = new Thickness(0, 0, 0, 10); return card;
    }

    private UIElement GoogleCard()
    {
        string email = secrets.Get("GoogleEmail");
        var id = new PasswordBox { Height = 40, Password = secrets.Get("GoogleClientId"), Margin = new Thickness(0, 6, 0, 6) };
        var secret = new PasswordBox { Height = 40, Password = secrets.Get("GoogleClientSecret"), Margin = new Thickness(0, 0, 0, 8) };
        var status = Ui.Text(email.Length > 0 ? "Connected as " + email : "Not connected", 12.5, email.Length > 0 ? Ui.Green : Ui.Dim); status.TextWrapping = TextWrapping.Wrap;
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(Ui.Button("Connect", Ui.C("#0A84FF"), async () =>
        {
            try
            {
                secrets.Set("GoogleClientId", id.Password.Trim()); secrets.Set("GoogleClientSecret", secret.Password.Trim());
                if (ConnectGoogle == null) { status.Text = "Google support is not ready yet."; return; }
                await ConnectGoogle();
                email = new SecretStore().Get("GoogleEmail");
                status.Text = email.Length > 0 ? "Connected as " + email : "Google connected"; status.Foreground = Ui.Green;
            }
            catch { status.Text = "Google sign-in failed or was cancelled. Check your desktop OAuth client."; status.Foreground = Ui.Orange; }
        }, 32));
        var disconnect = Ui.Button("Disconnect", Ui.C("#FF453A"), () => { DisconnectGoogle?.Invoke(); status.Text = "Google disconnected"; status.Foreground = Ui.Dim; }, 32);
        disconnect.Margin = new Thickness(8, 0, 0, 0); row.Children.Add(disconnect);
        var why = Ui.Text("Google sign-in opens your browser. Island only asks to read calendar and mail headers.", 12.5, Ui.Dim); why.TextWrapping = TextWrapping.Wrap; why.Margin = new Thickness(0, 0, 0, 4);
        var stack = new StackPanel();
        stack.Children.Add(Ui.Text("Google", 16, Ui.Ink, FontWeights.SemiBold)); stack.Children.Add(why);
        stack.Children.Add(Ui.Text("Desktop client ID", 12, Ui.Faint)); stack.Children.Add(id);
        stack.Children.Add(Ui.Text("Client secret", 12, Ui.Faint)); stack.Children.Add(secret);
        stack.Children.Add(row); stack.Children.Add(status);
        var card = Ui.Card(stack, 14, 16); card.Margin = new Thickness(0, 0, 0, 10); return card;
    }

    private UIElement Apps()
    {
        var stack = new StackPanel();
        stack.Children.Add(Heading("Apps"));
        stack.Children.Add(ToggleRow("Email", "Unread mail on Home and in the notch.", settings.Email, on => { settings.SetPanel("email", on); Persist(); }));
        foreach (var panel in Panels())
        {
            if (panel.Id is "home" or "apps") continue;
            string blurb = Blurb(panel.Id);
            bool on = settings.Allows(panel.Id);
            stack.Children.Add(ToggleRow(panel.Title, blurb, on, value => { settings.SetPanel(panel.Id, value); Persist(); }));
        }
        return stack;
    }

    private UIElement Alerts()
    {
        var stack = new StackPanel();
        stack.Children.Add(Heading("Alerts"));
        stack.Children.Add(ToggleRow("Sale alerts", "A banner when a new paid sale arrives.", settings.SaleAlerts, on => { settings.SaleAlerts = on; Persist(); }));
        stack.Children.Add(ToggleRow("Meeting alerts", "A banner about five minutes before a meeting.", settings.MeetingAlerts, on => { settings.MeetingAlerts = on; Persist(); }));
        stack.Children.Add(ToggleRow("Important email", "A banner for mail Gmail marks important.", settings.EmailAlerts, on => { settings.EmailAlerts = on; Persist(); }));
        stack.Children.Add(ToggleRow("Track changes", "A banner when the playing song changes.", settings.NowPlayingAlerts, on => { settings.NowPlayingAlerts = on; Persist(); }));
        stack.Children.Add(ToggleRow("Timer done", "Focus, break, and countdown banners.", settings.TimerAlerts, on => { settings.TimerAlerts = on; Persist(); }));
        stack.Children.Add(ToggleRow("Hydration", "The water reminder.", settings.HydrationAlerts, on => { settings.HydrationAlerts = on; Persist(); }));
        var note = Ui.Text("Pause all alerts from General or the tray. That clears the queue.", 12.5, Ui.Dim); note.Margin = new Thickness(0, 8, 0, 0); note.TextWrapping = TextWrapping.Wrap; stack.Children.Add(note);
        return stack;
    }

    private UIElement Permissions()
    {
        var stack = new StackPanel();
        stack.Children.Add(Heading("Permissions"));
        stack.Children.Add(Permit("Stripe key", "Revenue reads charges.", secrets.Get("Stripe").Length > 0 ? "Stored" : "Not stored", secrets.Get("Stripe").Length > 0, () => { secrets.Remove("Stripe"); Persist(); ShowPage("permissions"); }));
        stack.Children.Add(Permit("Polar token", "Revenue reads orders.", secrets.Get("Polar").Length > 0 ? "Stored" : "Not stored", secrets.Get("Polar").Length > 0, () => { secrets.Remove("Polar"); Persist(); ShowPage("permissions"); }));
        bool google = secrets.Get("GoogleRefresh").Length > 0;
        stack.Children.Add(Permit("Google Calendar and Gmail", "Calendar events, and mail headers only. Never message bodies.", google ? "Connected" : "Not connected", google, () => { DisconnectGoogle?.Invoke(); ShowPage("permissions"); }));
        bool datafast = secrets.Get("DataFastKey").Length > 0;
        stack.Children.Add(Permit("DataFast", "Analytics traffic. The key stays encrypted.", datafast ? "Stored" : "Not stored", datafast, () => { secrets.Remove("DataFastKey"); Persist(); ShowPage("permissions"); }));
        stack.Children.Add(ToggleRow("Clipboard history", "Off stops new captures. History already saved stays until you delete it.", settings.Allows("clipboard"), on => { settings.SetPanel("clipboard", on); Persist(); }));
        stack.Children.Add(ToggleRow("Clean copied links", "Auto mode strips tracking parameters from a single http(s) URL.", settings.CleanLinks, on => { settings.CleanLinks = on; Persist(); }));
        stack.Children.Add(ToggleRow("Screen time", "Off stops new tracking.", settings.Allows("screentime"), on => { settings.SetPanel("screentime", on); Persist(); }));
        stack.Children.Add(InfoRow("Claude logs", "Reads %USERPROFILE%\\.claude\\projects if it is already there. Nothing is uploaded."));
        stack.Children.Add(InfoRow("Codex and Copilot logs", "Read only when their folders exist. Island does not create them or call their APIs."));
        stack.Children.Add(InfoRow("Network", "Used for revenue, Google, weather, and analytics. There is no Island account."));
        if (!settings.Revenue && secrets.Get("Stripe").Length > 0) stack.Children.Add(Hint("You still have a Stripe key stored.", () => { secrets.Remove("Stripe"); Persist(); ShowPage("permissions"); }));
        if (!settings.Revenue && secrets.Get("Polar").Length > 0) stack.Children.Add(Hint("You still have a Polar token stored.", () => { secrets.Remove("Polar"); Persist(); ShowPage("permissions"); }));
        return stack;
    }

    private UIElement About()
    {
        var stack = new StackPanel();
        stack.Children.Add(Heading("About"));
        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "1.0.0.0";
        stack.Children.Add(Ui.Text("Island  " + version, 22, Ui.Ink, FontWeights.SemiBold));
        var local = Ui.Text("Runs locally. No Island account.", 14, Ui.Dim); local.Margin = new Thickness(0, 6, 0, 14); stack.Children.Add(local);
        stack.Children.Add(Link("Open Island folder", () => { try { Directory.CreateDirectory(Settings.Folder); Process.Start(new ProcessStartInfo("explorer.exe", Settings.Folder) { UseShellExecute = true }); } catch { } }));
        stack.Children.Add(Link("Google Cloud setup in the README", OpenReadme));
        stack.Children.Add(Ui.Button("Reset local data", Ui.C("#FF453A"), ConfirmReset, 34));
        return stack;
    }

    private void ConfirmReset()
    {
        var ask = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };
        ask.Children.Add(Ui.Text("Delete encrypted keys? Notes and clipboard stay unless you confirm that separately.", 13, Ui.Soft));
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        row.Children.Add(Ui.Button("Delete keys", Ui.C("#FF453A"), () =>
        {
            try { File.Delete(Path.Combine(Settings.Folder, "secrets.dat")); secrets = new SecretStore(); Note("Keys removed."); ShowPage("about"); } catch { Note("Could not delete secrets."); }
        }, 30));
        var more = Ui.Button("Also delete notes and clipboard", Ui.C("#A0A0A8"), () =>
        {
            try
            {
                string data = Path.Combine(Settings.Folder, "data");
                if (Directory.Exists(data)) Directory.Delete(data, true);
                File.Delete(Path.Combine(Settings.Folder, "secrets.dat"));
                secrets = new SecretStore(); Note("Local data removed."); ShowPage("about");
            }
            catch { Note("Could not delete local data."); }
        }, 30);
        more.Margin = new Thickness(8, 0, 0, 0); row.Children.Add(more); ask.Children.Add(row);
        if (page.Children[0] is ScrollViewer scroll && scroll.Content is StackPanel host) host.Children.Add(ask);
    }

    private static void OpenReadme()
    {
        foreach (var path in new[] { Path.Combine(AppContext.BaseDirectory, "README.md"), Path.Combine(Environment.CurrentDirectory, "README.md") })
            if (File.Exists(path)) { try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); } catch { } return; }
    }

    private UIElement Heading(string text) { var t = Ui.Text(text, 26, Ui.Ink, FontWeights.SemiBold); t.Margin = new Thickness(0, 4, 0, 14); return t; }

    private UIElement ToggleRow(string title, string sub, bool on, Action<bool> change)
    {
        bool state = on;
        var label = Ui.Text(title, 14.5, Ui.Ink, FontWeights.SemiBold);
        var detail = Ui.Text(sub, 12.5, Ui.Dim); detail.TextWrapping = TextWrapping.Wrap;
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center }; text.Children.Add(label); text.Children.Add(detail);
        var knob = new Border { Width = 16, Height = 16, CornerRadius = new CornerRadius(8), Background = Ui.Ink, Margin = new Thickness(3), HorizontalAlignment = state ? HorizontalAlignment.Right : HorizontalAlignment.Left };
        var track = new Border { Width = 44, Height = 26, CornerRadius = new CornerRadius(13), Background = state ? Ui.B("#0A84FF") : Ui.Chip, Child = knob, Cursor = Cursors.Hand, VerticalAlignment = VerticalAlignment.Center };
        track.MouseLeftButtonUp += (_, e) =>
        {
            state = !state; knob.HorizontalAlignment = state ? HorizontalAlignment.Right : HorizontalAlignment.Left; track.Background = state ? Ui.B("#0A84FF") : Ui.Chip;
            change(state); e.Handled = true;
        };
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 12) };
        grid.ColumnDefinitions.Add(new ColumnDefinition()); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(text); Grid.SetColumn(track, 1); grid.Children.Add(track);
        return Ui.Card(grid, 14, 14);
    }

    private UIElement Permit(string title, string why, string state, bool stored, Action remove)
    {
        var stack = new StackPanel();
        stack.Children.Add(Ui.Text(title, 15, Ui.Ink, FontWeights.SemiBold));
        var line = Ui.Text(why + "  " + state, 12.5, stored ? Ui.Green : Ui.Dim); line.TextWrapping = TextWrapping.Wrap; line.Margin = new Thickness(0, 3, 0, 0); stack.Children.Add(line);
        if (stored) { var button = Ui.Button("Remove", Ui.C("#FF453A"), remove, 28); button.HorizontalAlignment = HorizontalAlignment.Left; button.Margin = new Thickness(0, 8, 0, 0); stack.Children.Add(button); }
        var card = Ui.Card(stack, 14, 14); card.Margin = new Thickness(0, 0, 0, 8); return card;
    }

    private static UIElement InfoRow(string title, string why)
    {
        var stack = new StackPanel();
        stack.Children.Add(Ui.Text(title, 15, Ui.Ink, FontWeights.SemiBold));
        var line = Ui.Text(why, 12.5, Ui.Dim); line.TextWrapping = TextWrapping.Wrap; line.Margin = new Thickness(0, 3, 0, 0); stack.Children.Add(line);
        var card = Ui.Card(stack, 14, 14); card.Margin = new Thickness(0, 0, 0, 8); return card;
    }

    private static UIElement Hint(string text, Action remove)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        var label = Ui.Text(text, 13, Ui.Orange); label.VerticalAlignment = VerticalAlignment.Center; label.TextWrapping = TextWrapping.Wrap; label.MaxWidth = 420; row.Children.Add(label);
        var button = Ui.Button("Remove it", Ui.C("#FF453A"), remove, 28); button.Margin = new Thickness(12, 0, 0, 0); row.Children.Add(button);
        var card = Ui.Card(row, 12, 14); card.Margin = new Thickness(0, 4, 0, 0); return card;
    }

    private static UIElement Link(string text, Action click)
    {
        var button = Ui.Button(text, Ui.C("#0A84FF"), click, 34); button.HorizontalAlignment = HorizontalAlignment.Left; button.Margin = new Thickness(0, 0, 0, 8); return button;
    }

    private static IEnumerable<IslandPanel> Panels()
    {
        var list = new List<IslandPanel>();
        foreach (var type in typeof(IslandPanel).Assembly.GetTypes().Where(t => !t.IsAbstract && t.IsSubclassOf(typeof(IslandPanel)) && t.GetConstructor(Type.EmptyTypes) != null))
        {
            try { list.Add((IslandPanel)Activator.CreateInstance(type)!); } catch { }
        }
        return list.OrderBy(p => p.Order);
    }

    private static string Blurb(string id) => id switch
    {
        "revenue" => "Today, this month, and recent sales.",
        "calendar" => "Today and tomorrow from Google Calendar.",
        "analytics" => "DataFast or Google Analytics.",
        "clipboard" => "Recent copies, with paste and clean link.",
        "nowplaying" => "The current Windows media session.",
        "downloads" => "Files landing in Downloads.",
        "controls" => "Awake, dark mode, lock, Recycle Bin.",
        "volume" => "Per-app volume and the default microphone.",
        "shelf" => "Files and text you drop on the island.",
        "snippets" => "Short triggers that expand to saved text.",
        "color" => "A magnifier that copies a color.",
        "claude" => "Today's tokens from local Claude logs.",
        "codex" => "Today's Codex activity from local logs.",
        "copilot" => "Today's Copilot activity from local logs.",
        "timers" => "Pomodoro, countdown, and stopwatch.",
        "screentime" => "Which apps were in front today.",
        "weather" => "The forecast from Open-Meteo.",
        "message" => "A ticker on the collapsed notch.",
        "sounds" => "Layered ambience.",
        "notes" => "Short notes kept on this PC.",
        "links" => "Search and saved links.",
        "emoji" => "An emoji picker.",
        "stats" => "CPU, memory, disk, and battery.",
        "scratchpad" => "A scratch box that stays local.",
        _ => "Shows in the island dock.",
    };
}
