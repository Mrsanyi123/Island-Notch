using System;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
namespace Island;

/// <summary>A banner shown in the notch for a few seconds (sale, timer finished, reminder...).</summary>
internal sealed record AlertItem(string Title, string Subtitle, string Glyph, Color Tint, bool IconFont);

/// <summary>Shared services every panel can use. Set up by IslandWindow before any panel runs.</summary>
internal static class Hub
{
    public static Dashboard Dashboard { get; internal set; } = null!;
    /// <summary>HWND of the island window (0 until the window is created).</summary>
    public static nint Handle { get; internal set; }

    /// <summary>Raised for every Win32 message the island window receives (msg, wParam, lParam). Used e.g. by the clipboard listener.</summary>
    public static event Action<int, nint, nint>? WindowMessage;
    internal static void RaiseWindowMessage(int msg, nint wp, nint lp) => WindowMessage?.Invoke(msg, wp, lp);

    public static event Action<AlertItem>? AlertRequested;
    /// <summary>Show a banner in the notch. glyph is either one Segoe Fluent Icons char (iconFont=true) or a plain text symbol like "$".</summary>
    public static void Alert(string title, string subtitle = "", string glyph = "", string tintHex = "#FF9F0A", bool iconFont = true)
        => AlertRequested?.Invoke(new AlertItem(title, subtitle, glyph, (Color)ColorConverter.ConvertFromString(tintHex)!, iconFont));

    /// <summary>Open the Settings window.</summary>
    public static Action? OpenSettings { get; internal set; }
    /// <summary>Show an island panel by id and expand.</summary>
    public static Action<string>? ShowPanel { get; internal set; }
    public static event Action<bool>? PauseChanged;
    public static bool Paused { get; private set; }
    public static void SetPaused(bool paused)
    {
        bool changed = Paused != paused;
        Paused = paused;
        if (changed) PauseChanged?.Invoke(paused);
    }
    /// <summary>Refresh revenue/calendar/email now.</summary>
    public static Action? RefreshNow { get; internal set; }

    /// <summary>Collapse the island (e.g. after launching something).</summary>
    public static Action? Collapse { get; internal set; }

    /// <summary>Ask the window to give keyboard focus to a text box (the island is a no-activate window; the window does this automatically on click).</summary>
    public static Action<UIElement>? FocusInput { get; internal set; }

    // ---- notch overlay: a panel may own the collapsed notch's resting content (e.g. the dot-matrix Message) ----
    public static FrameworkElement? NotchOverlay { get; private set; }
    public static double NotchOverlayWidth { get; private set; }
    public static event Action? NotchOverlayChanged;
    /// <summary>Show `element` (fixed layout size, width x 36) in the collapsed notch instead of the normal text. Pass null to remove it.</summary>
    public static void SetNotchOverlay(FrameworkElement? element, double width) { NotchOverlay = element; NotchOverlayWidth = width; NotchOverlayChanged?.Invoke(); }

    /// <summary>Put text on the clipboard (never throws) — retries because the clipboard can be briefly locked.</summary>
    public static void Copy(string text)
    {
        for (int i = 0; i < 5; i++) { try { Clipboard.SetDataObject(text, true); return; } catch { System.Threading.Thread.Sleep(30); } }
    }
    /// <summary>Send Ctrl+V to whatever app is in the foreground (the island never steals focus, so that is the user's text field).</summary>
    public static void PasteIntoForeground() { try { System.Windows.Forms.SendKeys.SendWait("^v"); } catch { } }
}

/// <summary>Tiny JSON persistence under %AppData%\Island\data. Never throws.</summary>
internal static class Store
{
    private static string Dir => Path.Combine(Settings.Folder, "data");
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = false, IncludeFields = false };
    public static T Load<T>(string name) where T : new()
    {
        try { var path = Path.Combine(Dir, name + ".json"); return File.Exists(path) ? JsonSerializer.Deserialize<T>(File.ReadAllText(path), Options) ?? new T() : new T(); }
        catch { return new T(); }
    }
    public static void Save<T>(string name, T value)
    {
        try { Directory.CreateDirectory(Dir); var path = Path.Combine(Dir, name + ".json"); File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(value, Options)); File.Move(path + ".tmp", path, true); } catch { }
    }
    public static string Folder { get { Directory.CreateDirectory(Dir); return Dir; } }
}
