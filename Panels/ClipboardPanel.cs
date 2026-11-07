using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
namespace Island;

/// <summary>One clipboard history item. Kind: text | link | image | files.</summary>
internal sealed class ClipboardEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Kind { get; set; } = "text";
    public string Text { get; set; } = "";
    public List<string> Files { get; set; } = new();
    public string ImagePath { get; set; } = "";
    public string Hash { get; set; } = "";
    public int Width { get; set; }
    public int Height { get; set; }
    public long Size { get; set; }
    public string Source { get; set; } = "";
    public DateTime Time { get; set; } = DateTime.Now;
    public bool Pinned { get; set; }
}
internal sealed class ClipboardData { public List<ClipboardEntry> Items { get; set; } = new(); }

/// <summary>Searchable clipboard history (text, links, images, files) with source app and age. Click an item to copy it back.</summary>
internal sealed class ClipboardPanel : IslandPanel
{
    public override string Id => "clipboard";
    public override string Title => "Clipboard";
    public override string Glyph => "";
    public override int Order => 40;
    public override Color Accent => Ui.C("#40C8E0");

    private const int MaxItems = 200, MaxText = 20000, MaxRows = 80, WM_CLIPBOARDUPDATE = 0x031D;

    private static class Native
    {
        [DllImport("user32.dll", SetLastError = true)] public static extern bool AddClipboardFormatListener(nint hwnd);
        [DllImport("user32.dll", SetLastError = true)] public static extern bool RemoveClipboardFormatListener(nint hwnd);
        [DllImport("user32.dll")] public static extern nint GetClipboardOwner();
        [DllImport("user32.dll")] public static extern nint GetForegroundWindow();
        [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(nint hwnd, out uint pid);
    }

    private ClipboardData data = new();
    private bool loaded, listening, capturePending;
    private long suppressUntil;
    private int filter;
    private string search = "";
    private StackPanel? list;
    private ScrollViewer? scroll;
    private TextBlock? status;
    private Border? clearBtn;
    private bool clearArmed;
    private DispatcherTimer? saveTimer, statusTimer;
    private readonly Dictionary<string, ImageSource?> thumbs = new();
    private static string ImageDir { get { var d = Path.Combine(Store.Folder, "clipboard"); Directory.CreateDirectory(d); return d; } }

    // ---------------------------------------------------------------- lifecycle
    public override void Start()
    {
        Load();
        try
        {
            if (Hub.Handle != 0 && !listening) { listening = Native.AddClipboardFormatListener(Hub.Handle); Hub.WindowMessage += OnWindowMessage; }
        }
        catch { }
    }

    private void Load()
    {
        if (loaded) return; loaded = true;
        data = Store.Load<ClipboardData>("clipboard"); data.Items ??= new();
        data.Items.RemoveAll(e => e.Kind == "image" && !File.Exists(e.ImagePath));
    }

    private void OnWindowMessage(int msg, nint wp, nint lp)
    {
        if (msg != WM_CLIPBOARDUPDATE || capturePending) return;
        capturePending = true;
        Application.Current?.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => TryCapture(0)));
    }

    /// <summary>Reads the clipboard; it may be locked by the app that just wrote it, so retry a few times.</summary>
    private void TryCapture(int attempt)
    {
        try { CaptureCore(); capturePending = false; }
        catch
        {
            if (attempt >= 6) { capturePending = false; return; }
            var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(70 * (attempt + 1)) };
            t.Tick += (_, _) => { t.Stop(); TryCapture(attempt + 1); }; t.Start();
        }
    }

    // ---------------------------------------------------------------- capture
    private void CaptureCore()
    {
        if (Environment.TickCount64 < suppressUntil) return;   // we just put this on the clipboard ourselves
        ClipboardEntry? entry = null;
        if (Clipboard.ContainsFileDropList())
        {
            var files = Clipboard.GetFileDropList().Cast<string>().ToList();
            if (files.Count == 0) return;
            long size = 0; foreach (var f in files) { try { if (File.Exists(f)) size += new FileInfo(f).Length; } catch { } }
            entry = new ClipboardEntry { Kind = "files", Files = files, Size = size, Text = string.Join("\n", files) };
        }
        else if (Clipboard.ContainsText())
        {
            var text = Clipboard.GetText(); if (string.IsNullOrWhiteSpace(text)) return;
            if (text.Length > MaxText) text = text[..MaxText];
            entry = new ClipboardEntry { Kind = IsLink(text) ? "link" : "text", Text = text };
        }
        else if (Clipboard.ContainsImage())
        {
            var bmp = Clipboard.GetImage(); if (bmp == null) return;
            var enc = new PngBitmapEncoder(); enc.Frames.Add(BitmapFrame.Create(bmp));
            using var ms = new MemoryStream(); enc.Save(ms); var bytes = ms.ToArray();
            var hash = Convert.ToHexString(SHA1.HashData(bytes));
            var existing = data.Items.FirstOrDefault(i => i.Kind == "image" && i.Hash == hash && File.Exists(i.ImagePath));
            if (existing != null) { Bump(existing); return; }
            var id = Guid.NewGuid().ToString("N"); var path = Path.Combine(ImageDir, id + ".png");
            File.WriteAllBytes(path, bytes);
            entry = new ClipboardEntry { Id = id, Kind = "image", ImagePath = path, Hash = hash, Width = bmp.PixelWidth, Height = bmp.PixelHeight, Size = bytes.Length };
        }
        if (entry == null) return;
        entry.Source = SourceApp();
        var dup = data.Items.FirstOrDefault(i => i.Kind == entry.Kind && i.Text == entry.Text && entry.Kind != "image");
        if (dup != null) { Bump(dup); return; }
        data.Items.Insert(0, entry); Trim(); Changed();
    }

    private static bool IsLink(string text)
    {
        var t = text.Trim(); if (t.Length < 4 || t.Any(char.IsWhiteSpace)) return false;
        return t.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || t.StartsWith("https://", StringComparison.OrdinalIgnoreCase) || t.StartsWith("www.", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Move an existing item to the top with a fresh timestamp.</summary>
    private void Bump(ClipboardEntry e) { data.Items.Remove(e); e.Time = DateTime.Now; data.Items.Insert(0, e); Changed(); }

    private void Trim()
    {
        while (data.Items.Count > MaxItems)
        {
            var victim = data.Items.LastOrDefault(i => !i.Pinned) ?? data.Items[^1]; Remove(victim, false);
        }
    }

    private void Remove(ClipboardEntry e, bool notify = true)
    {
        data.Items.Remove(e); thumbs.Remove(e.Id);
        if (e.Kind == "image") { try { File.Delete(e.ImagePath); } catch { } }
        if (notify) Changed();
    }

    private void Changed()
    {
        saveTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        saveTimer.Tick -= SaveTick; saveTimer.Tick += SaveTick; saveTimer.Stop(); saveTimer.Start();
        if (IsBuilt) RenderList();
    }
    private void SaveTick(object? s, EventArgs e) { saveTimer?.Stop(); Store.Save("clipboard", data); }

    // ---------------------------------------------------------------- source app
    private static readonly Dictionary<string, string> Friendly = new(StringComparer.OrdinalIgnoreCase)
    {
        ["chrome"] = "Chrome", ["msedge"] = "Edge", ["firefox"] = "Firefox", ["brave"] = "Brave", ["opera"] = "Opera", ["code"] = "VS Code", ["devenv"] = "Visual Studio",
        ["explorer"] = "Explorer", ["notepad"] = "Notepad", ["WindowsTerminal"] = "Terminal", ["powershell"] = "PowerShell", ["cmd"] = "Command Prompt",
        ["WINWORD"] = "Word", ["EXCEL"] = "Excel", ["POWERPNT"] = "PowerPoint", ["OUTLOOK"] = "Outlook", ["ms-teams"] = "Teams", ["Slack"] = "Slack",
        ["Discord"] = "Discord", ["Spotify"] = "Spotify", ["Obsidian"] = "Obsidian", ["Notion"] = "Notion", ["ScreenClippingHost"] = "Snipping Tool", ["SnippingTool"] = "Snipping Tool",
    };

    private static string SourceApp()
    {
        try
        {
            var hwnd = Native.GetClipboardOwner(); if (hwnd == 0) hwnd = Native.GetForegroundWindow(); if (hwnd == 0) return "Unknown";
            Native.GetWindowThreadProcessId(hwnd, out uint pid); if (pid == 0) return "Unknown";
            if (pid == (uint)Environment.ProcessId) return "Island";
            using var p = Process.GetProcessById((int)pid); var name = p.ProcessName;
            if (Friendly.TryGetValue(name, out var f)) return f;
            return name.Length == 0 ? "Unknown" : char.ToUpperInvariant(name[0]) + name[1..];
        }
        catch { return "Unknown"; }
    }

    // ---------------------------------------------------------------- copy back
    private void CopyBack(ClipboardEntry e)
    {
        suppressUntil = Environment.TickCount64 + 1000;
        bool ok = false;
        for (int i = 0; i < 5 && !ok; i++)
        {
            try
            {
                switch (e.Kind)
                {
                    case "image":
                        var bi = new BitmapImage(); bi.BeginInit(); bi.CacheOption = BitmapCacheOption.OnLoad; bi.UriSource = new Uri(e.ImagePath); bi.EndInit(); bi.Freeze();
                        Clipboard.SetImage(bi); break;
                    case "files":
                        var sc = new System.Collections.Specialized.StringCollection(); foreach (var f in e.Files.Where(File.Exists).Concat(e.Files.Where(Directory.Exists))) sc.Add(f);
                        if (sc.Count == 0) { Flash("Files no longer exist"); return; }
                        Clipboard.SetFileDropList(sc); break;
                    default: Clipboard.SetDataObject(e.Text, true); break;
                }
                ok = true;
            }
            catch { System.Threading.Thread.Sleep(30); }
        }
        suppressUntil = Environment.TickCount64 + 800;
        if (!ok) { Flash("Clipboard is busy, try again"); return; }
        Flash("Copied"); Bump(e);
    }

    // ---------------------------------------------------------------- UI
    protected override FrameworkElement Build()
    {
        Load();
        var g = new Grid { Width = Ui.ContentW, Height = Ui.ContentH };

        var box = Ui.Input("Search your clipboard"); box.Padding = new Thickness(12, 6, 12, 6); box.FontSize = 13.5;
        box.TextChanged += (_, _) => { search = box.Text.Trim(); scroll?.ScrollToTop(); RenderList(); };
        var seg = new SegmentedControl(new[] { "All", "Text", "Links", "Images", "Files" }, 0, Accent, i => { filter = i; scroll?.ScrollToTop(); RenderList(); }) { Margin = new Thickness(12, 0, 0, 0) };
        var bar = new Grid { Height = 36 }; bar.ColumnDefinitions.Add(new ColumnDefinition()); bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        bar.Children.Add(box); Grid.SetColumn(seg, 1); bar.Children.Add(seg);

        list = new StackPanel(); scroll = Ui.Scroll(list); Grid.SetRow(scroll, 2);
        var body = new Grid(); body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(8) }); body.RowDefinitions.Add(new RowDefinition());
        body.Children.Add(bar); body.Children.Add(scroll);

        status = Ui.Text("", 12.5, Ui.B(Accent), FontWeights.SemiBold); status.VerticalAlignment = VerticalAlignment.Center; status.Margin = new Thickness(0, 0, 12, 0);
        var note = Ui.Text("Passwords you copy are saved too", 12, Ui.Dim); note.VerticalAlignment = VerticalAlignment.Center; note.Margin = new Thickness(0, 0, 8, 0);
        clearBtn = Ui.IconButton("", "Clear history (keeps pinned items)", ClearHistory, 28);
        var right = new StackPanel { Orientation = Orientation.Horizontal }; right.Children.Add(status); right.Children.Add(note); right.Children.Add(clearBtn);
        g.Children.Add(Ui.Titled("Clipboard", "", body, Accent, right));
        RenderList();
        return g;
    }

    private void ClearHistory()
    {
        if (!clearArmed) { clearArmed = true; Flash("Click again to clear", Ui.Red); return; }
        clearArmed = false;
        foreach (var e in data.Items.Where(i => !i.Pinned).ToList()) Remove(e, false);
        Changed(); Flash("History cleared");
    }

    private void Flash(string msg, Brush? fg = null)
    {
        if (status == null) return; status.Text = msg; status.Foreground = fg ?? Ui.B(Accent);
        statusTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(2200) };
        statusTimer.Tick -= ClearStatus; statusTimer.Tick += ClearStatus; statusTimer.Stop(); statusTimer.Start();
    }
    private void ClearStatus(object? s, EventArgs e) { statusTimer?.Stop(); clearArmed = false; if (status != null) status.Text = ""; }

    private bool Matches(ClipboardEntry e)
    {
        if (filter == 1 && e.Kind != "text") return false; if (filter == 2 && e.Kind != "link") return false;
        if (filter == 3 && e.Kind != "image") return false; if (filter == 4 && e.Kind != "files") return false;
        if (search.Length == 0) return true;
        foreach (var tok in search.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            if (!(e.Text.Contains(tok, StringComparison.OrdinalIgnoreCase) || e.Source.Contains(tok, StringComparison.OrdinalIgnoreCase) || e.Kind.Contains(tok, StringComparison.OrdinalIgnoreCase))) return false;
        return true;
    }

    private void RenderList()
    {
        if (list == null) return; list.Children.Clear();
        var items = data.Items.Where(Matches).OrderByDescending(e => e.Pinned).ThenByDescending(e => e.Time).ToList();
        if (items.Count == 0)
        {
            var p = new StackPanel { Margin = new Thickness(0, 34, 0, 0), HorizontalAlignment = HorizontalAlignment.Center };
            p.Children.Add(Ui.Glyph("", 28, Ui.B(Accent, 0x99)));
            var t = Ui.Text(data.Items.Count == 0 ? "Nothing copied yet" : "No matches", 15, Ui.Soft, FontWeights.SemiBold); t.Margin = new Thickness(0, 8, 0, 2); t.HorizontalAlignment = HorizontalAlignment.Center; p.Children.Add(t);
            var h = Ui.Text(data.Items.Count == 0 ? "Copy something and it shows up here, with where it came from." : "Try another word or filter.", 13, Ui.Dim); h.HorizontalAlignment = HorizontalAlignment.Center; p.Children.Add(h);
            list.Children.Add(p); return;
        }
        foreach (var e in items.Take(MaxRows)) list.Children.Add(Row(e));
        if (items.Count > MaxRows) { var m = Ui.Text($"Showing the latest {MaxRows} of {items.Count}. Search to find older items.", 12.5, Ui.Dim); m.Margin = new Thickness(6, 6, 0, 8); list.Children.Add(m); }
    }

    private static Color KindColor(string kind) => kind switch { "link" => Ui.C("#0A84FF"), "image" => Ui.C("#BF5AF2"), "files" => Ui.C("#FF9F0A"), _ => Ui.C("#40C8E0") };
    private static string KindGlyph(string kind) => kind switch { "link" => "", "image" => "", "files" => "", _ => "" };

    private ImageSource? Thumb(ClipboardEntry e)
    {
        if (thumbs.TryGetValue(e.Id, out var t)) return t;
        try
        {
            var bi = new BitmapImage(); bi.BeginInit(); bi.CacheOption = BitmapCacheOption.OnLoad; bi.DecodePixelWidth = 96; bi.UriSource = new Uri(e.ImagePath); bi.EndInit(); bi.Freeze(); t = bi;
        }
        catch { t = null; }
        thumbs[e.Id] = t; return t;
    }

    private UIElement Row(ClipboardEntry e)
    {
        var tint = KindColor(e.Kind);
        // leading icon or thumbnail
        var lead = new Border { Width = 42, Height = 42, CornerRadius = new CornerRadius(10), Background = Ui.B(tint, 0x26), VerticalAlignment = VerticalAlignment.Center, ClipToBounds = true };
        var th = e.Kind == "image" ? Thumb(e) : null;
        if (th != null) lead.Child = new Image { Source = th, Stretch = Stretch.UniformToFill };
        else lead.Child = Ui.Glyph(KindGlyph(e.Kind), 18, Ui.B(tint));

        // preview + meta
        string preview = e.Kind switch
        {
            "image" => $"Image  {e.Width} × {e.Height}",
            "files" => e.Files.Count == 0 ? "Files" : Path.GetFileName(e.Files[0].TrimEnd('\\', '/')) is { Length: > 0 } n ? (e.Files.Count > 1 ? $"{n} and {e.Files.Count - 1} more" : n) : e.Files[0],
            _ => Preview(e.Text),
        };
        var pt = Ui.Text(preview, 14.5, e.Kind == "link" ? Ui.B(tint) : Ui.Ink, FontWeights.Medium); pt.TextWrapping = TextWrapping.Wrap; pt.MaxHeight = 40; if (e.Kind != "text") pt.TextWrapping = TextWrapping.NoWrap;
        string detail = e.Kind switch
        {
            "image" => FormatSize(e.Size),
            "files" => (e.Files.Count == 1 ? "1 file" : e.Files.Count + " files") + (e.Size > 0 ? " · " + FormatSize(e.Size) : ""),
            "link" => e.Text.Trim().Length + " chars",
            _ => Words(e.Text) + (Words(e.Text) == 1 ? " word" : " words") + " · " + e.Text.Length + " chars",
        };
        var meta = Ui.Text($"{e.Source} · {NotesPanel.Ago(e.Time)} · {detail}", 12.5, Ui.Dim); meta.Margin = new Thickness(0, 2, 0, 0);
        var col = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 8, 0) }; col.Children.Add(pt); col.Children.Add(meta);

        // hover actions
        var pin = SmallBtn(e.Pinned ? "" : "", e.Pinned ? "Unpin" : "Pin to the top", e.Pinned ? Ui.B(Accent) : Ui.Soft, () => { e.Pinned = !e.Pinned; Changed(); });
        var del = SmallBtn("", "Delete", Ui.Soft, () => Remove(e));
        var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Visibility = e.Pinned ? Visibility.Visible : Visibility.Collapsed };
        actions.Children.Add(pin); actions.Children.Add(del);
        if (!e.Pinned) del.Visibility = Visibility.Visible;

        var grid = new Grid(); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); grid.ColumnDefinitions.Add(new ColumnDefinition()); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(lead); Grid.SetColumn(col, 1); grid.Children.Add(col); Grid.SetColumn(actions, 2); grid.Children.Add(actions);
        var row = new Border { CornerRadius = new CornerRadius(12), Padding = new Thickness(8, 7, 8, 7), Margin = new Thickness(0, 0, 0, 3), Cursor = Cursors.Hand, Background = Brushes.Transparent, Child = grid };
        row.MouseEnter += (_, _) => { row.Background = Ui.Chip; actions.Visibility = Visibility.Visible; };
        row.MouseLeave += (_, _) => { row.Background = Brushes.Transparent; if (!e.Pinned) actions.Visibility = Visibility.Collapsed; };
        row.MouseLeftButtonUp += (_, ev) => { CopyBack(e); ev.Handled = true; };
        return row;
    }

    private static Border SmallBtn(string glyph, string tip, Brush fg, Action click)
    {
        var b = new Border { Width = 30, Height = 30, CornerRadius = new CornerRadius(15), Background = Brushes.Transparent, Cursor = Cursors.Hand, ToolTip = tip, Margin = new Thickness(2, 0, 0, 0), Child = Ui.Glyph(glyph, 13, fg) };
        b.MouseEnter += (_, _) => b.Background = Ui.ChipHover; b.MouseLeave += (_, _) => b.Background = Brushes.Transparent;
        b.MouseLeftButtonUp += (_, ev) => { click(); ev.Handled = true; };
        return b;
    }

    private static string Preview(string text)
    {
        var t = text.Trim(); if (t.Length > 240) t = t[..240];
        return string.Join(" ", t.Split(new[] { '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).Where(s => s.Length > 0));
    }
    private static int Words(string t) { int w = 0; bool inW = false; foreach (var c in t) { if (char.IsWhiteSpace(c)) inW = false; else if (!inW) { inW = true; w++; } } return w; }
    private static string FormatSize(long b) => b < 1024 ? b + " B" : b < 1024 * 1024 ? (b / 1024.0).ToString("0.#") + " KB" : (b / 1048576.0).ToString("0.#") + " MB";

    public override void OnShown() { if (IsBuilt) RenderList(); }   // refresh relative times
    public override void OnHidden() { clearArmed = false; if (saveTimer is { IsEnabled: true }) { saveTimer.Stop(); Store.Save("clipboard", data); } }
}
