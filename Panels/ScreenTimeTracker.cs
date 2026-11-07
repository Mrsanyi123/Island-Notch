using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
namespace Island;

/// <summary>Persisted screen time: day (yyyy-MM-dd) -> exe name -> seconds, plus display names and exe paths.</summary>
public sealed class ScreenTimeData
{
    public Dictionary<string, Dictionary<string, long>> Days { get; set; } = new();
    public Dictionary<string, string> Names { get; set; } = new();
    public Dictionary<string, string> Paths { get; set; } = new();
}

/// <summary>Counts foreground-app seconds (called once a second from the UI thread) and persists them every ~30 s and on exit.</summary>
internal sealed class ScreenTimeTracker
{
    private const int IdleSeconds = 120, KeepDays = 30;
    private static readonly HashSet<string> SkipExe = new(StringComparer.OrdinalIgnoreCase) { "logonui.exe", "lockapp.exe", "searchhost.exe", "startmenuexperiencehost.exe", "shellexperiencehost.exe", "textinputhost.exe", "applicationframehost.exe", "dwm.exe", "csrss.exe" };
    private static readonly HashSet<string> SkipClass = new(StringComparer.Ordinal) { "Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd", "NotifyIconOverflowWindow", "TopLevelWindowForOverflowXamlIsland", "XamlExplorerHostIslandWindow" };

    private readonly object gate = new();
    private ScreenTimeData data = new();
    private readonly int ownPid = Environment.ProcessId;
    private readonly Dictionary<int, (string Exe, string Path, DateTime At)> procCache = new();
    private DateTime lastTick = DateTime.MinValue, lastFlush = DateTime.UtcNow; private double carry; private bool dirty;
    public string? CurrentExe { get; private set; }

    public void Load()
    {
        lock (gate)
        {
            data = Store.Load<ScreenTimeData>("screentime");
            data.Days ??= new(); data.Names ??= new(); data.Paths ??= new();
            var cutoff = DateTime.Today.AddDays(-KeepDays).ToString("yyyy-MM-dd");
            foreach (var k in data.Days.Keys.Where(k => k.Length != 10 || string.CompareOrdinal(k, cutoff) < 0).ToList()) data.Days.Remove(k);
            foreach (var k in data.Days.Keys.ToList()) data.Days[k] = data.Days[k] ?? new();
        }
        AppDomain.CurrentDomain.ProcessExit += (_, _) => Flush();
    }

    public void Tick()
    {
        try
        {
            var now = DateTime.UtcNow; double dt = lastTick == DateTime.MinValue ? 1 : Math.Clamp((now - lastTick).TotalSeconds, 0, 3); lastTick = now;
            CurrentExe = null;
            if (!Idle() && Foreground() is { } fg)
            {
                CurrentExe = fg.Exe; carry += dt; long whole = (long)carry;
                if (whole > 0)
                {
                    carry -= whole;
                    lock (gate)
                    {
                        string day = DateTime.Today.ToString("yyyy-MM-dd");
                        if (!data.Days.TryGetValue(day, out var apps)) data.Days[day] = apps = new();
                        apps[fg.Exe] = apps.GetValueOrDefault(fg.Exe) + whole; dirty = true;
                        if (!data.Names.ContainsKey(fg.Exe)) { data.Names[fg.Exe] = DisplayName(fg.Path, fg.Exe); data.Paths[fg.Exe] = fg.Path; }
                    }
                }
            }
            if (dirty && (now - lastFlush).TotalSeconds >= 30) Flush();
        }
        catch { }
    }

    public void Flush()
    {
        try { lock (gate) { if (!dirty) return; Store.Save("screentime", data); dirty = false; lastFlush = DateTime.UtcNow; } } catch { }
    }

    // ---- queries (UI thread) ----
    public Dictionary<string, long> AppsOn(DateTime day) { lock (gate) { return data.Days.TryGetValue(day.ToString("yyyy-MM-dd"), out var a) ? new Dictionary<string, long>(a) : new(); } }
    public long TotalOn(DateTime day) { lock (gate) { return data.Days.TryGetValue(day.ToString("yyyy-MM-dd"), out var a) ? a.Values.Sum() : 0; } }
    public bool HasAnyData() { lock (gate) { return data.Days.Values.Any(d => d.Count > 0); } }
    public string NameOf(string exe) { lock (gate) { return data.Names.TryGetValue(exe, out var n) && n.Length > 0 ? n : System.IO.Path.GetFileNameWithoutExtension(exe); } }
    public string? PathOf(string exe) { lock (gate) { return data.Paths.TryGetValue(exe, out var p) ? p : null; } }

    // ---- win32 ----
    private (string Exe, string Path)? Foreground()
    {
        var hwnd = Native.GetForegroundWindow(); if (hwnd == 0) return null;
        var cls = new StringBuilder(64); if (Native.GetClassName(hwnd, cls, cls.Capacity) > 0 && SkipClass.Contains(cls.ToString())) return null;
        Native.GetWindowThreadProcessId(hwnd, out uint pidU); int pid = (int)pidU; if (pid == 0 || pid == ownPid) return null;
        if (!procCache.TryGetValue(pid, out var c) || (DateTime.UtcNow - c.At).TotalSeconds > 60)
        {
            var path = ImagePath(pid); c = (path.Length > 0 ? System.IO.Path.GetFileName(path) : "", path, DateTime.UtcNow); procCache[pid] = c;
            if (procCache.Count > 200) foreach (var k in procCache.Where(p => (DateTime.UtcNow - p.Value.At).TotalMinutes > 5).Select(p => p.Key).ToList()) procCache.Remove(k);
        }
        if (c.Exe.Length == 0 || SkipExe.Contains(c.Exe)) return null;
        return (c.Exe.ToLowerInvariant(), c.Path);
    }
    private static string ImagePath(int pid)
    {
        var h = Native.OpenProcess(0x1000 /*PROCESS_QUERY_LIMITED_INFORMATION*/, false, pid); if (h == 0) return "";
        try { var sb = new StringBuilder(1024); int len = sb.Capacity; return Native.QueryFullProcessImageName(h, 0, sb, ref len) ? sb.ToString() : ""; }
        finally { Native.CloseHandle(h); }
    }
    private static bool Idle()
    {
        var li = new Native.LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<Native.LASTINPUTINFO>() };
        if (!Native.GetLastInputInfo(ref li)) return false;
        uint idleMs = unchecked((uint)Environment.TickCount - li.dwTime); return idleMs > IdleSeconds * 1000u;
    }
    private static string DisplayName(string path, string exe)
    {
        try
        {
            var vi = FileVersionInfo.GetVersionInfo(path);
            if (!string.IsNullOrWhiteSpace(vi.FileDescription)) return vi.FileDescription!.Trim();
            if (!string.IsNullOrWhiteSpace(vi.ProductName)) return vi.ProductName!.Trim();
        }
        catch { }
        return System.IO.Path.GetFileNameWithoutExtension(exe);
    }

    private static class Native
    {
        [StructLayout(LayoutKind.Sequential)] public struct LASTINPUTINFO { public uint cbSize; public uint dwTime; }
        [DllImport("user32.dll")] public static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);
        [DllImport("user32.dll")] public static extern nint GetForegroundWindow();
        [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(nint hWnd, out uint pid);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetClassName(nint hWnd, StringBuilder text, int count);
        [DllImport("kernel32.dll")] public static extern nint OpenProcess(uint access, bool inherit, int pid);
        [DllImport("kernel32.dll")] public static extern bool CloseHandle(nint h);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] public static extern bool QueryFullProcessImageName(nint h, int flags, StringBuilder exe, ref int size);
    }
}
