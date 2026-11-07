using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
namespace Island;

/// <summary>Home: month calendar, what's next, today's takings and inbox at a glance.</summary>
internal sealed class HomePanel : IslandPanel
{
    public override string Id => "home";
    public override string Title => "Home";
    public override string Glyph => "";
    public override int Order => 0;
    public override Color Accent => Ui.C("#0A84FF");

    private readonly StackPanel calendarBox = new();
    private readonly StackPanel upNext = new();
    private readonly StackPanel revenue = new();
    private readonly StackPanel mail = new();
    private Border? revenueCard, mailCard, nextCard;
    private DateTime viewMonth = new(DateTime.Today.Year, DateTime.Today.Month, 1);

    protected override FrameworkElement Build()
    {
        var g = new Grid { Width = Ui.ContentW, Height = Ui.ContentH };
        foreach (double w in new[] { 304.0, 10, 274, 10 }) g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(w) }); g.ColumnDefinitions.Add(new ColumnDefinition());
        var cal = Ui.Card(calendarBox, 16); g.Children.Add(cal);
        nextCard = Ui.Titled("Up next", "", upNext, Accent); Grid.SetColumn(nextCard, 2); g.Children.Add(nextCard);
        var right = new Grid(); right.RowDefinitions.Add(new RowDefinition()); right.RowDefinitions.Add(new RowDefinition { Height = new GridLength(10) }); right.RowDefinitions.Add(new RowDefinition());
        revenueCard = Ui.Titled("Revenue", "", revenue, Ui.C("#30D158"), null, 14); mailCard = Ui.Titled("Inbox", "", mail, Ui.C("#FF9F0A"), null, 14); Grid.SetRow(mailCard, 2);
        right.Children.Add(revenueCard); right.Children.Add(mailCard); Grid.SetColumn(right, 4); g.Children.Add(right);
        Hub.Dashboard.Changed += Refresh; Refresh();
        return g;
    }

    public override void OnShown() { if (IsBuilt) Refresh(); }

    private void Refresh()
    {
        var d = Hub.Dashboard; var settings = Settings.Load(); bool connected = d.Auth.Connected;
        BuildCalendar();
        if (nextCard != null) nextCard.Visibility = settings.Calendar ? Visibility.Visible : Visibility.Collapsed;
        if (revenueCard != null) revenueCard.Visibility = settings.Revenue ? Visibility.Visible : Visibility.Collapsed;
        if (mailCard != null) mailCard.Visibility = settings.Email ? Visibility.Visible : Visibility.Collapsed;

        upNext.Children.Clear();
        if (!connected) upNext.Children.Add(Ui.Text("Open Settings to connect Google", 13.5, Ui.Dim));
        else if (d.CalendarError.Length > 0) { var e = Ui.Text(d.CalendarError, 12.5, Ui.Dim); e.TextWrapping = TextWrapping.Wrap; upNext.Children.Add(e); }
        else if (d.Meetings.Count == 0) upNext.Children.Add(Ui.Text("Nothing coming up", 14, Ui.Dim));
        int idx = 0;
        foreach (var m in d.Meetings.Take(4))
        {
            var row = new Grid { Margin = new Thickness(0, 0, 0, 11) }; row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); row.ColumnDefinitions.Add(new ColumnDefinition());
            var bar = new Border { Width = 3, CornerRadius = new CornerRadius(2), Background = Ui.B(Ui.Palette[idx++ % Ui.Palette.Length]), Margin = new Thickness(0, 1, 11, 1) };
            var col = new StackPanel(); Grid.SetColumn(col, 1);
            var title = Ui.Text(m.Title, 15.5, Ui.Ink, FontWeights.SemiBold); Clickable(title, () => Api.Open(m.Url)); col.Children.Add(title);
            var local = m.Start.LocalDateTime; string day = local.Date == DateTime.Today ? "" : local.Date == DateTime.Today.AddDays(1) ? "Tomorrow · " : local.ToString("ddd") + " · ";
            var sub = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 0) }; sub.Children.Add(Ui.Text(day + (m.AllDay ? "All day" : local.ToString("h:mm tt")), 12.5, Ui.Dim));
            if (m.Join.Length > 0) { var join = Ui.Text("Join", 12.5, Ui.Blue, FontWeights.SemiBold); join.Margin = new Thickness(10, 0, 0, 0); Clickable(join, () => Api.Open(m.Join)); sub.Children.Add(join); }
            col.Children.Add(sub); row.Children.Add(bar); row.Children.Add(col); upNext.Children.Add(row);
        }

        revenue.Children.Clear();
        if (!d.RevenueConnected && d.RevenueErrorText.Length == 0) revenue.Children.Add(Ui.Text("Add a Stripe or Polar key in Settings", 13.5, Ui.Dim));
        else
        {
            if (d.RevenueConnected)
            {
                revenue.Children.Add(Ui.Text(d.RevenueToday, 26, Ui.Ink, FontWeights.SemiBold)); revenue.Children.Add(Ui.Text("today  ·  month " + d.RevenueMonth, 12.5, Ui.Dim));
                foreach (var s in d.RevenueRecent.Take(1)) { var t = Ui.Text(s, 12.5, Ui.Dim); t.Margin = new Thickness(0, 5, 0, 0); revenue.Children.Add(t); }
            }
            if (d.RevenueErrorText.Length > 0) { var e = Ui.Text(d.RevenueErrorText, 12, Ui.Orange); e.TextWrapping = TextWrapping.Wrap; e.MaxHeight = 32; revenue.Children.Add(e); }
        }

        mail.Children.Clear();
        if (!connected) mail.Children.Add(Ui.Text("Open Settings to connect Google", 13.5, Ui.Dim));
        else if (d.EmailError.Length > 0) { var e = Ui.Text(d.EmailError, 12.5, Ui.Dim); e.TextWrapping = TextWrapping.Wrap; e.MaxHeight = 34; mail.Children.Add(e); }
        else
        {
            mail.Children.Add(Ui.Text(d.Gmail.Unread + " unread", 22, Ui.Ink, FontWeights.SemiBold));
            foreach (var m in d.Gmail.Recent.Take(2))
            {
                var link = Ui.Text(Clean(m.Sender) + " — " + m.Subject, 12.5, Ui.Dim); link.Margin = new Thickness(0, 4, 0, 0); var id = m.Id;
                Clickable(link, () => Api.Open("https://mail.google.com/mail/u/?authuser=" + Uri.EscapeDataString(new SecretStore().Get("GoogleEmail")) + "#inbox/" + id)); mail.Children.Add(link);
            }
        }
    }

    private static string Clean(string sender) { int i = sender.IndexOf('<'); return (i > 0 ? sender[..i] : sender).Trim().Trim('"'); }
    private static void Clickable(TextBlock t, Action click)
    {
        t.Cursor = System.Windows.Input.Cursors.Hand;
        t.MouseEnter += (_, _) => t.TextDecorations = TextDecorations.Underline; t.MouseLeave += (_, _) => t.TextDecorations = null;
        t.MouseLeftButtonUp += (_, e) => { click(); e.Handled = true; };
    }

    private void BuildCalendar()
    {
        calendarBox.Children.Clear();
        var head = new Grid { Height = 34 };
        head.ColumnDefinitions.Add(new ColumnDefinition()); head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var title = Ui.Text(viewMonth.ToString("MMMM yyyy"), 17, Ui.Ink, FontWeights.SemiBold); title.VerticalAlignment = VerticalAlignment.Center; head.Children.Add(title);
        var prev = Ui.IconButton("", "Previous month", () => { viewMonth = viewMonth.AddMonths(-1); BuildCalendar(); }, 28); prev.Margin = new Thickness(0, 0, 6, 0); Grid.SetColumn(prev, 1);
        var next = Ui.IconButton("", "Next month", () => { viewMonth = viewMonth.AddMonths(1); BuildCalendar(); }, 28); Grid.SetColumn(next, 2);
        head.Children.Add(prev); head.Children.Add(next); calendarBox.Children.Add(head);

        var days = new Grid { Margin = new Thickness(0, 6, 0, 0) };
        for (int c = 0; c < 7; c++) days.ColumnDefinitions.Add(new ColumnDefinition());
        for (int r = 0; r < 7; r++) days.RowDefinitions.Add(new RowDefinition { Height = new GridLength(r == 0 ? 24 : 33) });
        for (int c = 0; c < 7; c++) { var h = Ui.Text("SMTWTFS"[c].ToString(), 11.5, Ui.Dim, FontWeights.Bold); h.HorizontalAlignment = HorizontalAlignment.Center; h.VerticalAlignment = VerticalAlignment.Center; Grid.SetColumn(h, c); days.Children.Add(h); }
        var start = viewMonth.AddDays(-(int)viewMonth.DayOfWeek);
        for (int i = 0; i < 42; i++)
        {
            var d = start.AddDays(i); bool today = d.Date == DateTime.Today; var cell = new Grid();
            if (today) cell.Children.Add(new Ellipse { Width = 28, Height = 28, Fill = Ui.Blue, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 2) });
            var num = Ui.Text(d.Day.ToString(), 14.5, d.Month == viewMonth.Month || today ? Ui.Ink : Ui.Faint, today ? FontWeights.SemiBold : FontWeights.Medium); num.HorizontalAlignment = HorizontalAlignment.Center; num.VerticalAlignment = VerticalAlignment.Center; num.Margin = new Thickness(0, 0, 0, 2); cell.Children.Add(num);
            if (Hub.Dashboard.Agenda.Any(m => m.Start.LocalDateTime.Date == d.Date)) cell.Children.Add(new Ellipse { Width = 4, Height = 4, Fill = today ? Ui.Ink : Ui.Blue, VerticalAlignment = VerticalAlignment.Bottom, HorizontalAlignment = HorizontalAlignment.Center });
            Grid.SetRow(cell, 1 + i / 7); Grid.SetColumn(cell, i % 7); days.Children.Add(cell);
        }
        calendarBox.Children.Add(days);
    }
}
