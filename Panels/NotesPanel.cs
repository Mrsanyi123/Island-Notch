using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
namespace Island;

internal sealed class NotesItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Text { get; set; } = "";
    public DateTime Modified { get; set; } = DateTime.Now;
}
internal sealed class NotesData { public List<NotesItem> Items { get; set; } = new(); }

/// <summary>Notes: list on the left, editor on the right. First line is the title. Autosaves (debounced).</summary>
internal sealed class NotesPanel : IslandPanel
{
    public override string Id => "notes";
    public override string Title => "Notes";
    public override string Glyph => "";
    public override int Order => 150;
    public override Color Accent => Ui.C("#FFB340");

    private NotesData data = new();
    private bool loaded, suppress;
    private string? selectedId;
    private string search = "";
    private StackPanel? list;
    private TextBox? editor;
    private StackPanel? empty;
    private TextBlock? status, countText;
    private Border? deleteBtn;
    private DispatcherTimer? debounce;

    public override void Start() => Load();
    private void Load() { if (loaded) return; loaded = true; data = Store.Load<NotesData>("notes"); data.Items ??= new(); Sort(); }
    private void Sort() => data.Items = data.Items.OrderByDescending(n => n.Modified).ToList();
    private NotesItem? Current => selectedId == null ? null : data.Items.FirstOrDefault(n => n.Id == selectedId);

    /// <summary>"just now", "5m ago", "3h ago", "2d ago", or a date.</summary>
    internal static string Ago(DateTime t)
    {
        var d = DateTime.Now - t; if (d.TotalSeconds < 45) return "just now";
        if (d.TotalMinutes < 60) return Math.Max(1, (int)d.TotalMinutes) + "m ago";
        if (d.TotalHours < 24) return (int)d.TotalHours + "h ago";
        if (d.TotalDays < 7) return (int)d.TotalDays + "d ago";
        return t.ToString(t.Year == DateTime.Now.Year ? "MMM d" : "MMM d, yyyy");
    }

    protected override FrameworkElement Build()
    {
        Load();
        var g = new Grid { Width = Ui.ContentW, Height = Ui.ContentH };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(318) }); g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) }); g.ColumnDefinitions.Add(new ColumnDefinition());

        // ---- left: search + list ----
        var searchBox = Ui.Input("Search notes"); searchBox.FontSize = 13.5; searchBox.Padding = new Thickness(12, 6, 12, 6);
        searchBox.TextChanged += (_, _) => { search = searchBox.Text.Trim(); RebuildList(); };
        list = new StackPanel();
        var leftBody = new Grid(); leftBody.RowDefinitions.Add(new RowDefinition { Height = new GridLength(36) }); leftBody.RowDefinitions.Add(new RowDefinition { Height = new GridLength(8) }); leftBody.RowDefinitions.Add(new RowDefinition());
        leftBody.Children.Add(searchBox); var sc = Ui.Scroll(list); Grid.SetRow(sc, 2); leftBody.Children.Add(sc);
        var newBtn = Ui.Button("+ New note", Accent, NewNote, 28);
        g.Children.Add(Ui.Titled("Notes", "", leftBody, Accent, newBtn));

        // ---- right: editor ----
        editor = Ui.Input("Start typing. The first line becomes the title.", true);
        editor.FontSize = 15.5; editor.Padding = new Thickness(14, 10, 14, 10); editor.AcceptsTab = true; editor.Background = Ui.B("#141417");
        editor.TextChanged += (_, _) => OnEdited();
        empty = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, IsHitTestVisible = false };
        empty.Children.Add(Ui.Glyph("", 30, Ui.B(Accent, 0x99)));
        var et = Ui.Text("Pick a note or start a new one", 15, Ui.Soft, FontWeights.SemiBold); et.Margin = new Thickness(0, 10, 0, 2); et.HorizontalAlignment = HorizontalAlignment.Center; empty.Children.Add(et);
        var eh = Ui.Text("For the thoughts that outgrow the scratchpad.", 13, Ui.Dim); eh.HorizontalAlignment = HorizontalAlignment.Center; empty.Children.Add(eh);
        var body = new Grid(); body.Children.Add(editor); body.Children.Add(empty);

        status = Ui.Text("", 12.5, Ui.B(Accent), FontWeights.SemiBold); status.VerticalAlignment = VerticalAlignment.Center; status.Margin = new Thickness(0, 0, 14, 0);
        countText = Ui.Text("", 12.5, Ui.Dim, FontWeights.Medium); countText.VerticalAlignment = VerticalAlignment.Center; countText.Margin = new Thickness(0, 0, 12, 0);
        deleteBtn = Ui.IconButton("", "Delete note", DeleteCurrent, 30);
        var right = new StackPanel { Orientation = Orientation.Horizontal }; right.Children.Add(status); right.Children.Add(countText); right.Children.Add(deleteBtn);
        var card = Ui.Titled("Editor", "", body, Accent, right); Grid.SetColumn(card, 2); g.Children.Add(card);

        if (selectedId == null && data.Items.Count > 0) selectedId = data.Items[0].Id;
        RebuildList(); LoadEditor();
        return g;
    }

    private void RebuildList()
    {
        if (list == null) return;
        list.Children.Clear();
        var items = data.Items.Where(n => search.Length == 0 || n.Text.Contains(search, StringComparison.OrdinalIgnoreCase)).ToList();
        if (items.Count == 0)
        {
            var t = Ui.Text(data.Items.Count == 0 ? "No notes yet. Press + New note." : "No notes match.", 13.5, Ui.Dim); t.Margin = new Thickness(4, 6, 0, 0); t.TextWrapping = TextWrapping.Wrap; list.Children.Add(t); return;
        }
        foreach (var n in items) list.Children.Add(Row(n));
    }

    private UIElement Row(NotesItem n)
    {
        var lines = n.Text.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
        bool sel = n.Id == selectedId;
        var title = Ui.Text(lines.Count > 0 ? lines[0] : "New note", 14.5, lines.Count > 0 ? Ui.Ink : Ui.Dim, FontWeights.SemiBold);
        var time = Ui.Text(Ago(n.Modified), 12, Ui.Dim); time.Margin = new Thickness(8, 0, 0, 0); time.VerticalAlignment = VerticalAlignment.Center;
        var top = new Grid(); top.ColumnDefinitions.Add(new ColumnDefinition()); top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        top.Children.Add(title); Grid.SetColumn(time, 1); top.Children.Add(time);
        var col = new StackPanel(); col.Children.Add(top);
        var prev = Ui.Text(lines.Count > 1 ? lines[1] : "No additional text", 12.5, Ui.Dim); prev.Margin = new Thickness(0, 2, 0, 0); col.Children.Add(prev);
        var bar = new Border { Width = 3, CornerRadius = new CornerRadius(2), Background = sel ? Ui.B(Accent) : Brushes.Transparent, Margin = new Thickness(0, 0, 9, 0) };
        var inner = new Grid(); inner.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); inner.ColumnDefinitions.Add(new ColumnDefinition());
        inner.Children.Add(bar); Grid.SetColumn(col, 1); inner.Children.Add(col);
        var row = new Border { CornerRadius = new CornerRadius(12), Padding = new Thickness(8, 8, 10, 8), Margin = new Thickness(0, 0, 0, 3), Cursor = Cursors.Hand, Child = inner, Background = sel ? Ui.B(Accent, 0x1E) : Brushes.Transparent };
        row.MouseEnter += (_, _) => { if (n.Id != selectedId) row.Background = Ui.Chip; };
        row.MouseLeave += (_, _) => row.Background = n.Id == selectedId ? Ui.B(Accent, 0x1E) : Brushes.Transparent;
        var id = n.Id;
        row.MouseLeftButtonUp += (_, e) => { Select(id); e.Handled = true; };
        return row;
    }

    private void Select(string id)
    {
        FlushSave(); PruneEmpty(id);
        selectedId = id; RebuildList(); LoadEditor();
    }

    /// <summary>Remove blank notes the user abandoned (except the one just selected).</summary>
    private void PruneEmpty(string? keep)
    {
        int removed = data.Items.RemoveAll(n => n.Text.Trim().Length == 0 && n.Id != keep && n.Id == selectedId);
        if (removed > 0) Store.Save("notes", data);
    }

    private void NewNote()
    {
        FlushSave();
        var existingBlank = data.Items.FirstOrDefault(n => n.Text.Trim().Length == 0);
        var n = existingBlank ?? new NotesItem();
        if (existingBlank == null) data.Items.Insert(0, n);
        search = ""; selectedId = n.Id; RebuildList(); LoadEditor();
        if (editor != null) Hub.FocusInput?.Invoke(editor);
    }

    private void DeleteCurrent()
    {
        var cur = Current; if (cur == null) return;
        int idx = data.Items.IndexOf(cur); data.Items.Remove(cur);
        debounce?.Stop(); Store.Save("notes", data);
        selectedId = data.Items.Count == 0 ? null : data.Items[Math.Min(idx, data.Items.Count - 1)].Id;
        RebuildList(); LoadEditor();
    }

    private void LoadEditor()
    {
        if (editor == null || empty == null) return;
        var cur = Current; suppress = true;
        try
        {
            editor.Text = cur?.Text ?? "";
            editor.Visibility = cur == null ? Visibility.Collapsed : Visibility.Visible;
            empty.Visibility = cur == null ? Visibility.Visible : Visibility.Collapsed;
            if (deleteBtn != null) deleteBtn.Visibility = cur == null ? Visibility.Collapsed : Visibility.Visible;
            if (status != null) status.Text = "";
            UpdateCount();
        }
        finally { suppress = false; }
    }

    private void UpdateCount()
    {
        if (countText == null) return;
        var t = Current?.Text ?? ""; int words = 0; bool inWord = false;
        foreach (var c in t) { if (char.IsWhiteSpace(c)) inWord = false; else if (!inWord) { inWord = true; words++; } }
        countText.Text = Current == null ? "" : words + (words == 1 ? " word" : " words");
    }

    private void OnEdited()
    {
        if (suppress || editor == null) return;
        var cur = Current; if (cur == null) return;
        cur.Text = editor.Text; cur.Modified = DateTime.Now; UpdateCount();
        if (status != null) status.Text = "Editing…";
        debounce ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
        debounce.Tick -= OnDebounce; debounce.Tick += OnDebounce; debounce.Stop(); debounce.Start();
    }

    private void OnDebounce(object? s, EventArgs e)
    {
        debounce?.Stop(); Sort(); Store.Save("notes", data);
        if (status != null) status.Text = "Saved";
        RebuildList();
    }

    private void FlushSave() { if (debounce is { IsEnabled: true }) { debounce.Stop(); Sort(); Store.Save("notes", data); } }

    public override void OnShown() { if (IsBuilt) RebuildList(); }
    public override void OnHidden() { FlushSave(); }
}
