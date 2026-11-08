using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
namespace Island;

public sealed class ShelfItem
{
    public string Kind { get; set; } = "text";
    public string Text { get; set; } = "";
    public List<string> Files { get; set; } = new();
}

public sealed class ShelfData { public List<ShelfItem> Items { get; set; } = new(); }

/// <summary>Local drop shelf. Items live in %AppData%\Island\data and survive collapse.</summary>
internal static class ShelfBoard
{
    private static ShelfData? data;
    public static event Action? Changed;
    public static IReadOnlyList<ShelfItem> Items { get { Ensure(); return data!.Items; } }

    public static void Ensure()
    {
        if (data != null) return;
        data = Store.Load<ShelfData>("shelf");
        data.Items ??= new();
    }

    public static bool Add(IDataObject source)
    {
        Ensure();
        ShelfItem? item = null;
        if (source.GetDataPresent(DataFormats.FileDrop) && source.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
            item = new ShelfItem { Kind = "files", Files = files.ToList(), Text = string.Join("\n", files) };
        else if (TextOf(source) is string text && text.Trim().Length > 0)
        {
            text = text.Trim();
            if (text.Length > 20000) text = text[..20000];
            bool link = (text.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || text.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) && !text.Any(char.IsWhiteSpace);
            item = new ShelfItem { Kind = link ? "link" : "text", Text = text };
        }
        if (item == null) return false;
        var dup = data!.Items.FirstOrDefault(i => i.Kind == item.Kind && i.Text == item.Text);
        if (dup != null) data.Items.Remove(dup);
        data.Items.Insert(0, item);
        while (data.Items.Count > 40) data.Items.RemoveAt(data.Items.Count - 1);
        Store.Save("shelf", data);
        Changed?.Invoke();
        return true;
    }

    public static void Remove(ShelfItem item)
    {
        Ensure();
        data!.Items.Remove(item);
        Store.Save("shelf", data);
        Changed?.Invoke();
    }

    public static void Copy(ShelfItem item)
    {
        if (item.Kind == "files")
        {
            var list = new StringCollection();
            foreach (var f in item.Files) list.Add(f);
            for (int i = 0; i < 5; i++) { try { Clipboard.SetFileDropList(list); return; } catch { System.Threading.Thread.Sleep(30); } }
            return;
        }
        Hub.Copy(item.Text);
    }

    private static string? TextOf(IDataObject source)
    {
        if (source.GetDataPresent(DataFormats.UnicodeText)) return source.GetData(DataFormats.UnicodeText) as string;
        if (source.GetDataPresent(DataFormats.Text)) return source.GetData(DataFormats.Text) as string;
        return null;
    }
}

internal sealed class ShelfPanel : IslandPanel
{
    public override string Id => "shelf";
    public override string Title => "Shelf";
    public override string Glyph => "\uE8B7";
    public override int Order => 46;
    public override Color Accent => Ui.C("#FF9F0A");

    private StackPanel? list;
    private TextBlock? empty;
    private Point dragStart;
    private ShelfItem? dragItem;

    public override void Start() { ShelfBoard.Ensure(); ShelfBoard.Changed += () => { if (IsBuilt) Render(); }; }

    protected override FrameworkElement Build()
    {
        ShelfBoard.Ensure();
        list = new StackPanel();
        empty = Ui.Text("Drop files, text, or links on the island. They stay here until you remove them.", 14, Ui.Dim);
        empty.TextWrapping = TextWrapping.Wrap;
        var body = new Grid();
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        body.RowDefinitions.Add(new RowDefinition());
        body.Children.Add(empty);
        var scroll = Ui.Scroll(list); Grid.SetRow(scroll, 1); scroll.Margin = new Thickness(0, 8, 0, 0); body.Children.Add(scroll);
        var root = new Grid { Width = Ui.ContentW, Height = Ui.ContentH };
        root.Children.Add(Ui.Titled("Shelf", "\uE8B7", body, Accent, null, 16));
        Render();
        return root;
    }

    private void Render()
    {
        if (list == null || empty == null) return;
        list.Children.Clear();
        var items = ShelfBoard.Items;
        empty.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        foreach (var item in items) list.Children.Add(Row(item));
    }

    private UIElement Row(ShelfItem item)
    {
        string title = item.Kind == "files" ? (item.Files.Count == 1 ? System.IO.Path.GetFileName(item.Files[0]) : item.Files.Count + " files") : item.Text.Replace("\r", " ").Replace("\n", " ");
        if (title.Length > 80) title = title[..79] + "…";
        var grid = new Grid { Height = 44 };
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var col = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        col.Children.Add(Ui.Text(title, 14.5, Ui.Ink, FontWeights.SemiBold));
        col.Children.Add(Ui.Text(item.Kind == "link" ? "Link" : item.Kind == "files" ? "Files  ·  drag out" : "Text  ·  drag out", 12, Ui.Dim));
        var copy = Ui.Button("Copy", Accent, () => ShelfBoard.Copy(item), 28);
        var remove = Ui.Button("Remove", Ui.C("#FF453A"), () => ShelfBoard.Remove(item), 28); remove.Margin = new Thickness(8, 0, 0, 0);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        actions.Children.Add(copy); actions.Children.Add(remove);
        Grid.SetColumn(actions, 1);
        grid.Children.Add(col); grid.Children.Add(actions);
        var row = new Border { CornerRadius = new CornerRadius(12), Padding = new Thickness(8, 4, 8, 4), Margin = new Thickness(0, 0, 0, 4), Background = Brushes.Transparent, Cursor = Cursors.Hand, Child = grid };
        row.MouseEnter += (_, _) => row.Background = Ui.Chip;
        row.MouseLeave += (_, _) => row.Background = Brushes.Transparent;
        row.PreviewMouseLeftButtonDown += (_, e) => { dragStart = e.GetPosition(row); dragItem = item; };
        row.PreviewMouseMove += (_, e) =>
        {
            if (e.LeftButton != MouseButtonState.Pressed || dragItem != item) return;
            var p = e.GetPosition(row);
            if (Math.Abs(p.X - dragStart.X) + Math.Abs(p.Y - dragStart.Y) < 8) return;
            var data = new DataObject();
            if (item.Kind == "files") data.SetFileDropList(ToCollection(item.Files));
            else data.SetText(item.Text);
            dragItem = null;
            DragDrop.DoDragDrop(row, data, DragDropEffects.Copy);
        };
        return row;
    }

    private static StringCollection ToCollection(List<string> files)
    {
        var list = new StringCollection();
        foreach (var f in files) list.Add(f);
        return list;
    }
}
