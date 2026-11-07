using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
namespace Island;

/// <summary>One local reminder.</summary>
internal sealed class CalendarReminder
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Text { get; set; } = "";
    public DateTime Due { get; set; }
    public bool Done { get; set; }
    public DateTime? DoneAt { get; set; }
    public bool Notified { get; set; }
}
internal sealed class CalendarReminderStore { public List<CalendarReminder> Items { get; set; } = new(); }

/// <summary>Calendar: 7-day Google agenda beside local reminders that alert in the notch when due.</summary>
internal sealed class CalendarPanel : IslandPanel
{
    public override string Id => "calendar";
    public override string Title => "Calendar";
    public override string Glyph => "";
    public override int Order => 30;
    public override Color Accent => Ui.C("#FF453A");

    private CalendarReminderStore? store;
    private readonly StackPanel agenda = new(), list = new();
    private readonly TextBlock count = Ui.Text("", 12.5, Ui.Dim, FontWeights.SemiBold);
    private TextBox? input, timeBox; private TextBlock? msg;
    private bool shown, wired; private int ticks;

    private CalendarReminderStore Items => store ??= Store.Load<CalendarReminderStore>("reminders");

    public override string? RestingText
    {
        get
        {
            try
            {
                var now = DateTime.Now; var next = Items.Items.Where(r => !r.Done && r.Due > now.AddHours(-12) && r.Due <= now.AddHours(24)).OrderBy(r => r.Due).FirstOrDefault();
                if (next == null) return null;
                string s = next.Text + " · " + next.Due.ToString("t", CultureInfo.CurrentCulture); if (s.Length <= 28) return s;
                string time = " · " + next.Due.ToString("t", CultureInfo.CurrentCulture); return next.Text[..Math.Max(1, 27 - time.Length)] + "…" + time;
            }
            catch { return null; }
        }
    }

    public override void Start() { _ = Items; }

    public override void Tick()
    {
        try
        {
            var now = DateTime.Now; bool changed = false;
            foreach (var r in Items.Items)
                if (!r.Done && !r.Notified && r.Due <= now)
                {
                    r.Notified = true; changed = true;
                    try { Hub.Alert(r.Text, "Reminder", "", "#FF453A"); } catch { }
                }
            if (Items.Items.RemoveAll(r => r.Done && r.DoneAt is { } d && now - d > TimeSpan.FromDays(1)) > 0) changed = true;
            if (changed) Save();
            // repaint now and then so "overdue" turns red and cleared items vanish while the panel is open
            if (IsBuilt && shown && (changed || ++ticks % 30 == 0)) RenderReminders();
        }
        catch { }
    }

    public override void OnShown() { shown = true; if (IsBuilt) { RenderAgenda(); RenderReminders(); } }
    public override void OnHidden() { shown = false; }
    private void Save() => Store.Save("reminders", Items);

    // ---------- layout ----------
    protected override FrameworkElement Build()
    {
        var g = new Grid { Width = Ui.ContentW, Height = Ui.ContentH };
        g.ColumnDefinitions.Add(new ColumnDefinition()); g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) }); g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(440) });
        g.Children.Add(Ui.Titled("Agenda", "", Ui.Scroll(agenda), Accent));

        var body = new Grid(); body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); body.RowDefinitions.Add(new RowDefinition());
        var add = new StackPanel(); body.Children.Add(add);
        var row = new Grid { Height = 36 }; row.ColumnDefinitions.Add(new ColumnDefinition()); row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(78) }); row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        input = Ui.Input("Remind me to…"); input.MaxLength = 80; input.KeyDown += (_, e) => { if (e.Key == Key.Enter) { AddCustom(); e.Handled = true; } };
        timeBox = Ui.Input("HH:mm"); timeBox.MaxLength = 5; timeBox.Margin = new Thickness(6, 0, 0, 0); timeBox.ToolTip = "Custom time, 24 h (e.g. 15:30)"; Grid.SetColumn(timeBox, 1);
        timeBox.KeyDown += (_, e) => { if (e.Key == Key.Enter) { AddCustom(); e.Handled = true; } };
        var addBtn = Ui.Button("Add", Accent, AddCustom, 36); addBtn.Margin = new Thickness(6, 0, 0, 0); addBtn.MinWidth = 56; addBtn.Padding = new Thickness(12, 0, 12, 0); Grid.SetColumn(addBtn, 2);
        row.Children.Add(input); row.Children.Add(timeBox); row.Children.Add(addBtn); add.Children.Add(row);
        var chips = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        foreach (var (label, make) in new (string, Func<DateTime>)[] {
            ("In 10 min", () => DateTime.Now.AddMinutes(10)), ("In 1 hour", () => DateTime.Now.AddHours(1)),
            ("Tonight 6 pm", () => { var t = DateTime.Today.AddHours(18); return t > DateTime.Now ? t : t.AddDays(1); }), ("Tomorrow 9 am", () => DateTime.Today.AddDays(1).AddHours(9)) })
            chips.Children.Add(TimeChip(label, make));
        add.Children.Add(chips);
        msg = Ui.Text("", 12.5, Ui.Orange); msg.Margin = new Thickness(0, 6, 0, 0); msg.Visibility = Visibility.Collapsed; add.Children.Add(msg);
        var sc = Ui.Scroll(list); sc.Margin = new Thickness(0, 8, 0, 0); Grid.SetRow(sc, 1); body.Children.Add(sc);
        var right = Ui.Titled("Reminders", "", body, Accent, count); Grid.SetColumn(right, 2); g.Children.Add(right);

        if (!wired) { wired = true; Hub.Dashboard.Changed += () => { try { Application.Current?.Dispatcher.BeginInvoke(new Action(RenderAgenda)); } catch { } }; }
        RenderAgenda(); RenderReminders();
        return g;
    }

    private Border TimeChip(string label, Func<DateTime> due)
    {
        var t = Ui.Text(label, 12.5, Ui.Soft, FontWeights.SemiBold); t.VerticalAlignment = VerticalAlignment.Center;
        var b = new Border { Background = Ui.Chip, CornerRadius = new CornerRadius(14), Padding = new Thickness(10, 5, 10, 5), Margin = new Thickness(0, 0, 6, 0), Cursor = Cursors.Hand, Child = t, ToolTip = "Add the reminder for this time" };
        b.MouseEnter += (_, _) => { b.Background = Ui.B(Accent, 0x44); t.Foreground = Ui.Ink; }; b.MouseLeave += (_, _) => { b.Background = Ui.Chip; t.Foreground = Ui.Soft; };
        b.MouseLeftButtonUp += (_, e) => { AddReminder(due()); e.Handled = true; }; return b;
    }

    private void Warn(string s) { if (msg == null) return; msg.Text = s; msg.Visibility = s.Length > 0 ? Visibility.Visible : Visibility.Collapsed; }

    private void AddCustom()
    {
        var txt = timeBox?.Text.Trim() ?? "";
        if (txt.Length == 0) { AddReminder(DateTime.Now.AddHours(1)); return; }
        if (!TimeSpan.TryParseExact(txt.Replace('.', ':'), new[] { "h\\:mm", "hh\\:mm", "h" }, CultureInfo.InvariantCulture, out var ts) || ts.TotalHours >= 24) { Warn("Use 24-hour HH:mm, like 15:30"); return; }
        var due = DateTime.Today + ts; if (due <= DateTime.Now) due = due.AddDays(1); AddReminder(due);
    }

    private void AddReminder(DateTime due)
    {
        try
        {
            var text = input?.Text.Trim() ?? ""; if (text.Length == 0) { Warn("Type what to remind you about first"); input?.Focus(); return; }
            Warn(""); Items.Items.Add(new CalendarReminder { Text = text, Due = due }); Save();
            if (input != null) input.Text = ""; if (timeBox != null) timeBox.Text = ""; RenderReminders();
        }
        catch { }
    }

    // ---------- agenda ----------
    private void RenderAgenda()
    {
        try
        {
            agenda.Children.Clear(); var d = Hub.Dashboard;
            if (d == null) return;
            if (!d.Auth.Connected)
            {
                Empty("Connect Google to see your week here.", "Open Settings"); return;
            }
            var today = DateTime.Today; var events = d.Agenda.Where(m => m.Start.LocalDateTime.Date >= today && m.Start.LocalDateTime.Date < today.AddDays(7)).OrderBy(m => m.Start).ToList();
            if (events.Count == 0)
            {
                if (d.CalendarError.Length > 0) { var e = Ui.Text(d.CalendarError, 13, Ui.Dim); e.TextWrapping = TextWrapping.Wrap; agenda.Children.Add(e); }
                else Empty("Nothing on the calendar for the next seven days.", null);
                return;
            }
            int idx = 0; DateTime? last = null;
            foreach (var m in events)
            {
                var local = m.Start.LocalDateTime; bool past = !m.AllDay && local < DateTime.Now;
                if (last != local.Date)
                {
                    last = local.Date; string name = local.Date == today ? "Today" : local.Date == today.AddDays(1) ? "Tomorrow" : local.ToString("ddd d", CultureInfo.CurrentCulture);
                    var h = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, agenda.Children.Count == 0 ? 0 : 10, 0, 4) };
                    h.Children.Add(Ui.Text(name.ToUpperInvariant(), 12, local.Date == today ? Ui.B(Accent) : Ui.Dim, FontWeights.Bold));
                    if (local.Date == today || local.Date == today.AddDays(1)) { var dt = Ui.Text(local.ToString("ddd d MMM", CultureInfo.CurrentCulture), 12, Ui.Dim); dt.Margin = new Thickness(8, 0, 0, 0); h.Children.Add(dt); }
                    agenda.Children.Add(h);
                }
                var row = new Grid { Margin = new Thickness(0, 0, 0, 2), Opacity = past ? .55 : 1 };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(72) }); row.ColumnDefinitions.Add(new ColumnDefinition()); row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var bg = new Border { CornerRadius = new CornerRadius(10), Background = Brushes.Transparent }; Grid.SetColumnSpan(bg, 4); row.Children.Add(bg);
                row.MouseEnter += (_, _) => bg.Background = Ui.B("#1A1A1F"); row.MouseLeave += (_, _) => bg.Background = Brushes.Transparent;
                var bar = new Border { Width = 3, CornerRadius = new CornerRadius(2), Background = Ui.B(Ui.Palette[idx++ % Ui.Palette.Length]), Margin = new Thickness(8, 6, 0, 6) }; row.Children.Add(bar);
                var time = Ui.Text(m.AllDay ? "All day" : local.ToString("t", CultureInfo.CurrentCulture), 13, Ui.Dim, FontWeights.Medium); time.VerticalAlignment = VerticalAlignment.Center; time.Margin = new Thickness(10, 0, 0, 0); Grid.SetColumn(time, 1); row.Children.Add(time);
                var title = Ui.Text(string.IsNullOrWhiteSpace(m.Title) ? "(no title)" : m.Title, 14.5, Ui.Ink, FontWeights.SemiBold); title.VerticalAlignment = VerticalAlignment.Center; title.Margin = new Thickness(0, 7, 8, 7); Grid.SetColumn(title, 2);
                if (m.Url.Length > 0) { title.Cursor = Cursors.Hand; var url = m.Url; title.MouseLeftButtonUp += (_, e) => { Api.Open(url); e.Handled = true; }; }
                row.Children.Add(title);
                if (m.Join.Length > 0)
                {
                    var join = Ui.Text("Join", 13, Ui.Blue, FontWeights.SemiBold); join.VerticalAlignment = VerticalAlignment.Center; join.Margin = new Thickness(0, 0, 10, 0); join.Cursor = Cursors.Hand; var ju = m.Join;
                    join.MouseEnter += (_, _) => join.TextDecorations = TextDecorations.Underline; join.MouseLeave += (_, _) => join.TextDecorations = null;
                    join.MouseLeftButtonUp += (_, e) => { Api.Open(ju); e.Handled = true; }; Grid.SetColumn(join, 3); row.Children.Add(join);
                }
                agenda.Children.Add(row);
            }
        }
        catch { }
    }

    private void Empty(string text, string? button)
    {
        var t = Ui.Text(text, 14, Ui.Dim); t.TextWrapping = TextWrapping.Wrap; t.Margin = new Thickness(0, 6, 0, 12); agenda.Children.Add(t);
        if (button != null) { var b = Ui.Button(button, Accent, () => Hub.OpenSettings?.Invoke()); b.HorizontalAlignment = HorizontalAlignment.Left; agenda.Children.Add(b); }
    }

    // ---------- reminders ----------
    private void RenderReminders()
    {
        try
        {
            list.Children.Clear(); var now = DateTime.Now;
            var items = Items.Items.OrderBy(r => r.Done).ThenBy(r => r.Due).ToList(); int open = items.Count(r => !r.Done);
            count.Text = open == 0 ? "" : open + " open";
            if (items.Count == 0)
            {
                var t = Ui.Text("No reminders. Type one above and pick a time, and it will pop up on the notch when it is due.", 13.5, Ui.Dim); t.TextWrapping = TextWrapping.Wrap; t.Margin = new Thickness(0, 4, 0, 0); list.Children.Add(t); return;
            }
            foreach (var r in items)
            {
                var item = r; bool overdue = !item.Done && item.Due < now;
                var row = new Grid { Margin = new Thickness(0, 0, 0, 2), Background = Brushes.Transparent, MinHeight = 44 };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34) }); row.ColumnDefinitions.Add(new ColumnDefinition()); row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var bg = new Border { CornerRadius = new CornerRadius(10), Background = Brushes.Transparent, IsHitTestVisible = false }; Grid.SetColumnSpan(bg, 3); row.Children.Add(bg);
                // round checkbox
                var box = new Grid { Width = 22, Height = 22, Cursor = Cursors.Hand, Background = Brushes.Transparent, ToolTip = item.Done ? "Mark as not done" : "Mark done" };
                box.Children.Add(new Ellipse { Stroke = item.Done ? Ui.B(Accent) : overdue ? Ui.Red : Ui.Dim, StrokeThickness = 2, Fill = item.Done ? Ui.B(Accent) : Brushes.Transparent });
                if (item.Done) box.Children.Add(Ui.Glyph("", 12, Brushes.Black));
                box.MouseLeftButtonUp += (_, e) => { item.Done = !item.Done; item.DoneAt = item.Done ? DateTime.Now : null; Save(); RenderReminders(); e.Handled = true; };
                Grid.SetColumn(box, 0); row.Children.Add(box);
                var col = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 6, 6, 6) }; Grid.SetColumn(col, 1);
                var title = Ui.Text(item.Text, 14.5, item.Done ? Ui.Dim : Ui.Ink, FontWeights.SemiBold); if (item.Done) title.TextDecorations = TextDecorations.Strikethrough; col.Children.Add(title);
                col.Children.Add(Ui.Text(When(item.Due, now), 12.5, overdue ? Ui.Red : Ui.Dim, overdue ? FontWeights.SemiBold : FontWeights.Normal));
                row.Children.Add(col); if (item.Done) col.Opacity = .6;
                var del = Ui.IconButton("", "Delete", () => { Items.Items.Remove(item); Save(); RenderReminders(); }, 28, false); del.Opacity = 0; del.Margin = new Thickness(0, 0, 6, 0); Grid.SetColumn(del, 2); row.Children.Add(del);
                row.MouseEnter += (_, _) => { bg.Background = Ui.B("#1A1A1F"); del.Opacity = 1; }; row.MouseLeave += (_, _) => { bg.Background = Brushes.Transparent; del.Opacity = 0; };
                list.Children.Add(row);
            }
        }
        catch { }
    }

    private static string When(DateTime due, DateTime now)
    {
        string time = due.ToString("t", CultureInfo.CurrentCulture);
        if (due < now && due.Date == now.Date) return "Overdue · " + time;
        if (due < now) return "Overdue · " + due.ToString("ddd d", CultureInfo.CurrentCulture) + " " + time;
        if (due.Date == now.Date) return "Today · " + time; if (due.Date == now.Date.AddDays(1)) return "Tomorrow · " + time;
        return due.ToString("ddd d MMM", CultureInfo.CurrentCulture) + " · " + time;
    }
}
