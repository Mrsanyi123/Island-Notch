using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
namespace Island;

/// <summary>First-run walkthrough. Skip and Finish both mark onboarding done and let the island show.</summary>
internal sealed class OnboardingWindow : Window
{
    private readonly Settings settings = Settings.Load();
    private readonly Grid body = new();
    private int step;
    private TextBlock? stepLabel;

    public OnboardingWindow()
    {
        base.Title = "Island"; Width = 920; Height = 640; WindowStartupLocation = WindowStartupLocation.CenterScreen;
        WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent;
        ShowInTaskbar = true; ResizeMode = ResizeMode.CanMinimize; FontFamily = Ui.TextFont;
        var shell = new Border { Background = Brushes.Black, CornerRadius = new CornerRadius(16), BorderBrush = Ui.Line, BorderThickness = new Thickness(1) };
        shell.Resources.MergedDictionaries.Add(Ui.Resources);
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(48) });
        root.RowDefinitions.Add(new RowDefinition());
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(72) });
        root.Children.Add(Wordmark());
        body.Margin = new Thickness(48, 12, 48, 0); Grid.SetRow(body, 1); root.Children.Add(body);
        Grid.SetRow(Footer(), 2); root.Children.Add(Footer());
        shell.Child = root; Content = shell;
        SourceInitialized += (_, _) => { try { var hwnd = new WindowInteropHelper(this).Handle; int pref = 2; NativeMethods.DwmSetWindowAttribute(hwnd, 33, ref pref, 4); } catch { } };
        Closed += (_, _) => Finish(false);
        ShowStep();
    }

    private UIElement Wordmark()
    {
        var bar = new Grid();
        var word = Ui.Text("Island", 15, Ui.Ink, FontWeights.SemiBold); word.Margin = new Thickness(22, 0, 0, 0); word.VerticalAlignment = VerticalAlignment.Center;
        bar.Children.Add(word);
        stepLabel = Ui.Text("1 of 5", 12.5, Ui.Faint); stepLabel.HorizontalAlignment = HorizontalAlignment.Right; stepLabel.VerticalAlignment = VerticalAlignment.Center; stepLabel.Margin = new Thickness(0, 0, 22, 0);
        bar.Children.Add(stepLabel);
        bar.MouseLeftButtonDown += (_, _) => { try { DragMove(); } catch { } };
        return bar;
    }

    private UIElement Footer()
    {
        var bar = new Grid { Margin = new Thickness(36, 0, 36, 18) };
        var skip = Ui.Button("Skip", Ui.C("#A0A0A8"), () => Finish(true), 36); skip.HorizontalAlignment = HorizontalAlignment.Left;
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var back = Ui.Button("Back", Ui.C("#A0A0A8"), () => { if (step > 0) { step--; ShowStep(); } }, 36);
        var next = Ui.Button("Next", Ui.C("#0A84FF"), () => { if (step < 4) { step++; ShowStep(); } else Finish(true); }, 36);
        next.Margin = new Thickness(8, 0, 0, 0); row.Children.Add(back); row.Children.Add(next);
        bar.Children.Add(skip); bar.Children.Add(row);
        return bar;
    }

    private void ShowStep()
    {
        if (stepLabel != null) stepLabel.Text = (step + 1) + " of 5";
        body.Children.Clear();
        body.Children.Add(step switch
        {
            0 => Welcome(),
            1 => Find(),
            2 => Accounts(),
            3 => Alerts(),
            _ => DoneStep(),
        });
    }

    private static UIElement Welcome()
    {
        var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        stack.Children.Add(Pill());
        stack.Children.Add(Heading("Island lives at the top of your screen."));
        stack.Children.Add(Copy("A black pill shows what's happening. Hover to open it."));
        return stack;
    }

    private static UIElement Find()
    {
        var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        stack.Children.Add(Heading("Where to find it"));
        stack.Children.Add(Copy("The pill sits at the top center of your main screen. The gear inside the expanded island opens settings. The tray icon is there too, sometimes under the hidden-icons arrow."));
        stack.Children.Add(Copy("It never appears in the taskbar or Alt+Tab."));
        return stack;
    }

    private UIElement Accounts()
    {
        var stack = new StackPanel();
        stack.Children.Add(Heading("Connect accounts"));
        stack.Children.Add(Copy("Optional. You can skip all of these."));
        stack.Children.Add(Mini("Stripe", "Restricted read-only Charges key.", "Stripe"));
        stack.Children.Add(Mini("Polar", "Organization token with orders:read.", "Polar"));
        stack.Children.Add(Google());
        return Ui.Scroll(stack);
    }

    private UIElement Alerts()
    {
        var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        stack.Children.Add(Heading("Alerts and startup"));
        stack.Children.Add(Check("Sale alerts", settings.SaleAlerts, on => settings.SaleAlerts = on));
        stack.Children.Add(Check("Meeting alerts", settings.MeetingAlerts, on => settings.MeetingAlerts = on));
        stack.Children.Add(Check("Important-email alerts", settings.EmailAlerts, on => settings.EmailAlerts = on));
        stack.Children.Add(Check("Start with Windows", Startup.Enabled, on => { try { Startup.Set(on); } catch (Exception ex) { MessageBox.Show(ex is InvalidOperationException ? ex.Message : "Could not update your Windows startup setting.", "Island"); } }));
        return stack;
    }

    private UIElement DoneStep()
    {
        var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        stack.Children.Add(Heading("Island is running."));
        stack.Children.Add(Copy("Hover the pill, or click the tray icon."));
        var open = Ui.Button("Open Island", Ui.C("#30D158"), () => Finish(true), 40);
        open.HorizontalAlignment = HorizontalAlignment.Left;
        stack.Children.Add(open);
        return stack;
    }

    private void Finish(bool close)
    {
        if (settings.Onboarded && !close) return;
        settings.Onboarded = true;
        try { settings.Save(); } catch { }
        if (close) Close();
    }

    private UIElement Mini(string title, string why, string key)
    {
        var secrets = new SecretStore();
        var box = new PasswordBox { Height = 36, Password = secrets.Get(key), Margin = new Thickness(0, 6, 0, 6) };
        var status = Ui.Text("", 12, Ui.Dim);
        var button = Ui.Button("Test", Ui.C("#30D158"), async () =>
        {
            try
            {
                secrets.Set(key, box.Password.Trim());
                if (SettingsWindow.TestProvider == null) { status.Text = "Available once Island is open."; return; }
                await SettingsWindow.TestProvider(title, box.Password.Trim());
                status.Text = "Connected"; status.Foreground = Ui.Green;
            }
            catch { status.Text = "Invalid key — check permissions and try again."; status.Foreground = Ui.Orange; }
        }, 30);
        button.HorizontalAlignment = HorizontalAlignment.Left;
        var stack = new StackPanel();
        stack.Children.Add(Ui.Text(title, 15, Ui.Ink, FontWeights.SemiBold));
        var copy = Ui.Text(why, 12.5, Ui.Dim); stack.Children.Add(copy);
        stack.Children.Add(box); stack.Children.Add(button); stack.Children.Add(status);
        var card = Ui.Card(stack, 12, 14); card.Margin = new Thickness(0, 0, 0, 8); return card;
    }

    private static UIElement Google()
    {
        var secrets = new SecretStore();
        var id = new PasswordBox { Height = 36, Password = secrets.Get("GoogleClientId"), Margin = new Thickness(0, 6, 0, 4) };
        var secret = new PasswordBox { Height = 36, Password = secrets.Get("GoogleClientSecret"), Margin = new Thickness(0, 0, 0, 6) };
        var status = Ui.Text("Uses your own Google Cloud desktop client.", 12.5, Ui.Dim); status.TextWrapping = TextWrapping.Wrap;
        var button = Ui.Button("Connect", Ui.C("#0A84FF"), async () =>
        {
            try
            {
                secrets.Set("GoogleClientId", id.Password.Trim()); secrets.Set("GoogleClientSecret", secret.Password.Trim());
                if (SettingsWindow.ConnectGoogle == null) { status.Text = "Available once Island is open."; return; }
                await SettingsWindow.ConnectGoogle();
                status.Text = "Connected"; status.Foreground = Ui.Green;
            }
            catch { status.Text = "Google sign-in failed or was cancelled."; status.Foreground = Ui.Orange; }
        }, 30);
        button.HorizontalAlignment = HorizontalAlignment.Left;
        var stack = new StackPanel();
        stack.Children.Add(Ui.Text("Google", 15, Ui.Ink, FontWeights.SemiBold));
        stack.Children.Add(status); stack.Children.Add(id); stack.Children.Add(secret); stack.Children.Add(button);
        var card = Ui.Card(stack, 12, 14); return card;
    }

    private UIElement Check(string title, bool on, Action<bool> change)
    {
        bool state = on;
        var label = Ui.Text(title, 14.5, Ui.Ink, FontWeights.SemiBold); label.VerticalAlignment = VerticalAlignment.Center;
        var knob = new Border { Width = 16, Height = 16, CornerRadius = new CornerRadius(8), Background = Ui.Ink, Margin = new Thickness(3), HorizontalAlignment = state ? HorizontalAlignment.Right : HorizontalAlignment.Left };
        var track = new Border { Width = 44, Height = 26, CornerRadius = new CornerRadius(13), Background = state ? Ui.B("#0A84FF") : Ui.Chip, Child = knob, Cursor = Cursors.Hand };
        track.MouseLeftButtonUp += (_, e) => { state = !state; knob.HorizontalAlignment = state ? HorizontalAlignment.Right : HorizontalAlignment.Left; track.Background = state ? Ui.B("#0A84FF") : Ui.Chip; change(state); settings.Save(); e.Handled = true; };
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        grid.ColumnDefinitions.Add(new ColumnDefinition()); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(label); Grid.SetColumn(track, 1); grid.Children.Add(track);
        return Ui.Card(grid, 12, 14);
    }

    private static UIElement Pill()
    {
        var pill = new Border { Width = 180, Height = 36, CornerRadius = new CornerRadius(0, 0, 18, 18), Background = Brushes.Black, BorderBrush = Ui.Line, BorderThickness = new Thickness(1), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 18) };
        var text = Ui.Text("Island", 13, Ui.Ink, FontWeights.SemiBold); text.HorizontalAlignment = HorizontalAlignment.Center; text.VerticalAlignment = VerticalAlignment.Center;
        pill.Child = text; return pill;
    }

    private static TextBlock Heading(string text) { var t = Ui.Text(text, 28, Ui.Ink, FontWeights.SemiBold); t.TextWrapping = TextWrapping.Wrap; t.Margin = new Thickness(0, 0, 0, 10); return t; }
    private static TextBlock Copy(string text) { var t = Ui.Text(text, 15, Ui.Soft); t.TextWrapping = TextWrapping.Wrap; t.Margin = new Thickness(0, 0, 0, 12); return t; }
}
