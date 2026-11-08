using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
namespace Island;

/// <summary>
/// Watches the user's Downloads folder. A growing file is named on the pill; once its size stays put, one finished alert fires.
/// Temp and partial browser files are ignored. Rows reveal the file in Explorer and never open or upload it.
/// </summary>
internal sealed class DownloadsPanel : IslandPanel
{
    public override string Id => "downloads";
    public override string Title => "Downloads";
    public override string Glyph => "\uE896";
    public override int Order => 42;
    public override Color Accent => Ui.C("#64D2FF");

    private static readonly string[] Ignored = { ".crdownload", ".tmp", ".partial", ".part", ".download", ".opdownload", ".filepart" };
    private static readonly Guid DownloadsId = new("374DE290-123F-4565-9164-39C4925E467B");

    private sealed class Item
    {
        public string Path = "";
        public long Size;
        public DateTime Changed = DateTime.UtcNow;
        public bool Growing = true;
        public bool Alerted;
    }

    private readonly object gate = new();
    private readonly Dictionary<string, Item> items = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, long> baseline = new(StringComparer.OrdinalIgnoreCase);
    private FileSystemWatcher? watcher;
    private string? folder;
    private string missing = "";
    private StackPanel? list;
    private TextBlock? empty;

    public override string? ActiveTaskText
    {
        get
        {
            lock (gate)
            {
                var live = items.Values.Where(i => i.Growing).OrderByDescending(i => i.Changed).FirstOrDefault();
                if (live == null) return null;
                string name = System.IO.Path.GetFileName(live.Path);
                return name.Length > 36 ? name[..35] + "…" : name;
            }
        }
    }

    public override void Start()
    {
        folder = DownloadsFolder();
        if (folder == null || !Directory.Exists(folder)) { missing = "Downloads folder was not found."; return; }
        try
        {
            foreach (var path in Directory.EnumerateFiles(folder)) baseline[path] = SafeSize(path);
            watcher = new FileSystemWatcher(folder) { NotifyFilter = NotifyFilters.FileName | NotifyFilters.Size | NotifyFilters.LastWrite | NotifyFilters.CreationTime, IncludeSubdirectories = false, EnableRaisingEvents = true };
            watcher.Created += (_, e) => Touch(e.FullPath, true);
            watcher.Changed += (_, e) => Touch(e.FullPath, false);
            watcher.Renamed += (_, e) => { Drop(e.OldFullPath); Touch(e.FullPath, true); };
            watcher.Deleted += (_, e) => Drop(e.FullPath);
            watcher.Error += (_, _) => { };
        }
        catch { missing = "Downloads could not be watched."; }
    }

    public override void Tick()
    {
        bool changed = false;
        List<Item> finished = new();
        lock (gate)
        {
            foreach (var item in items.Values)
            {
                if (!item.Growing) continue;
                if (!File.Exists(item.Path)) { item.Growing = false; changed = true; continue; }
                long size = SafeSize(item.Path);
                if (size != item.Size) { item.Size = size; item.Changed = DateTime.UtcNow; changed = true; continue; }
                if (size > 0 && (DateTime.UtcNow - item.Changed).TotalSeconds >= 2 && !item.Alerted)
                {
                    item.Growing = false; item.Alerted = true; changed = true; finished.Add(item);
                }
            }
        }
        foreach (var item in finished) Hub.Alert("Download finished", System.IO.Path.GetFileName(item.Path), "\uE896", "#64D2FF");
        if (changed && IsBuilt) Render();
    }

    protected override FrameworkElement Build()
    {
        list = new StackPanel();
        empty = Ui.Text(missing.Length > 0 ? missing : "Files you download show up here. Island does not open them.", 14, Ui.Dim);
        empty.TextWrapping = TextWrapping.Wrap; empty.Margin = new Thickness(4, 8, 4, 0);
        var body = new Grid();
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        body.RowDefinitions.Add(new RowDefinition());
        body.Children.Add(empty);
        var scroll = Ui.Scroll(list); Grid.SetRow(scroll, 1); body.Children.Add(scroll);
        var card = Ui.Titled("Downloads", "\uE896", body, Accent, null, 16);
        var root = new Grid { Width = Ui.ContentW, Height = Ui.ContentH }; root.Children.Add(card);
        Render();
        return root;
    }

    public override void OnShown() { if (IsBuilt) Render(); }

    private void Touch(string path, bool created)
    {
        if (Ignore(path) || Directory.Exists(path)) return;
        long size = SafeSize(path);
        lock (gate)
        {
            if (!created && baseline.TryGetValue(path, out long before) && size <= before && !items.ContainsKey(path)) return;
            baseline.Remove(path);
            if (!items.TryGetValue(path, out var item))
            {
                item = new Item { Path = path, Size = size };
                items[path] = item;
            }
            else if (size != item.Size) { item.Size = size; item.Changed = DateTime.UtcNow; item.Growing = true; }
        }
        if (IsBuilt) Application.Current?.Dispatcher.BeginInvoke(new Action(Render));
    }

    private void Drop(string path)
    {
        lock (gate) items.Remove(path);
        if (IsBuilt) Application.Current?.Dispatcher.BeginInvoke(new Action(Render));
    }

    private void Render()
    {
        if (list == null || empty == null) return;
        List<Item> rows;
        lock (gate) rows = items.Values.OrderByDescending(i => i.Changed).Take(12).ToList();
        empty.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        list.Children.Clear();
        foreach (var item in rows)
        {
            var row = new Grid { Height = 46, Cursor = Cursors.Hand };
            row.ColumnDefinitions.Add(new ColumnDefinition()); row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var name = Ui.Text(System.IO.Path.GetFileName(item.Path), 14.5, Ui.Ink, FontWeights.SemiBold);
            var meta = Ui.Text((item.Growing ? "Downloading  ·  " : "Finished  ·  ") + Size(item.Size), 12, Ui.Dim);
            var col = new StackPanel { VerticalAlignment = VerticalAlignment.Center }; col.Children.Add(name); col.Children.Add(meta);
            var hint = Ui.Text("Show in Explorer", 12, Ui.B(Accent), FontWeights.SemiBold); hint.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(hint, 1); row.Children.Add(col); row.Children.Add(hint);
            string path = item.Path;
            row.MouseEnter += (_, _) => row.Background = Ui.Chip;
            row.MouseLeave += (_, _) => row.Background = Brushes.Transparent;
            row.MouseLeftButtonUp += (_, e) => { Reveal(path); e.Handled = true; };
            list.Children.Add(row);
            list.Children.Add(new Border { Height = 1, Background = Ui.Line });
        }
    }

    private static void Reveal(string path)
    {
        try
        {
            if (!File.Exists(path)) return;
            Process.Start(new ProcessStartInfo("explorer.exe", "/select,\"" + path + "\"") { UseShellExecute = true });
            Hub.Collapse?.Invoke();
        }
        catch { }
    }

    private static bool Ignore(string path)
    {
        string name = System.IO.Path.GetFileName(path);
        if (name.Length == 0 || name.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase) || name.Equals("thumbs.db", StringComparison.OrdinalIgnoreCase)) return true;
        string ext = System.IO.Path.GetExtension(name);
        return Ignored.Any(x => ext.Equals(x, StringComparison.OrdinalIgnoreCase));
    }

    private static long SafeSize(string path) { try { return new FileInfo(path).Length; } catch { return 0; } }

    private static string Size(long bytes)
    {
        if (bytes < 1024) return bytes + " B";
        if (bytes < 1024 * 1024) return (bytes / 1024d).ToString("0.#") + " KB";
        return (bytes / 1024d / 1024d).ToString("0.#") + " MB";
    }

    private static string? DownloadsFolder()
    {
        var id = DownloadsId;
        if (SHGetKnownFolderPath(ref id, 0, 0, out nint ptr) == 0 && ptr != 0)
        {
            try { return Marshal.PtrToStringUni(ptr); }
            finally { Marshal.FreeCoTaskMem(ptr); }
        }
        string fallback = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        return Directory.Exists(fallback) ? fallback : null;
    }

    [DllImport("shell32.dll")]
    private static extern int SHGetKnownFolderPath(ref Guid id, uint flags, nint token, out nint path);
}
