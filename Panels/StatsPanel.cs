using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Xml.Linq;
namespace Island;

/// <summary>System vitals: CPU / memory / disk rings, network throughput charts and battery state + health.</summary>
internal sealed class StatsPanel : IslandPanel
{
    public override string Id => "stats";
    public override string Title => "Stats";
    public override string Glyph => "\uE950";
    public override int Order => 60;
    public override Color Accent => Ui.C("#5AC8FA");

    private static readonly Color MemColor = Ui.C("#0A84FF"), DiskColor = Ui.C("#BF5AF2"), UpColor = Ui.C("#30D158");
    private const int Samples = 60;

    // latest samples (written from Tick on the UI thread)
    private double cpu, memFrac, memUsedGb, memTotalGb; private bool cpuKnown;
    private ulong prevIdle, prevKernel, prevUser; private bool cpuBase;
    private readonly List<double> down = new(), up = new(); private long prevRx, prevTx, prevNetTicks; private bool netBase;
    private double downRate, upRate;
    private bool shown;
    // battery health (one-time background read)
    private volatile int healthState; // 0 not started, 1 reading, 2 ok, 3 unavailable
    private double healthPct; private int cycles;

    private Ring cpuRing = null!, memRing = null!, diskRing = null!;
    private TextBlock cpuMain = null!, cpuSub = null!, memMain = null!, memSub = null!, diskMain = null!, diskSub = null!;
    private TextBlock downV = null!, upV = null!, batPct = null!, batState = null!, batHealth = null!;
    private AreaChart downChart = null!, upChart = null!; private Border batFill = null!, batTrack = null!;

    public override void Start() { try { SampleCpuMem(); } catch { } }

    public override string? RestingText => cpuKnown ? $"CPU {cpu * 100:0}% · RAM {memFrac * 100:0}%" : null;

    public override void Tick()
    {
        try
        {
            SampleCpuMem();
            if (shown && IsBuilt) { SampleNet(); UpdateUi(); }
        }
        catch { }
    }

    public override void OnShown()
    {
        shown = true; netBase = false; // re-baseline the byte counters so a long hidden gap does not show as a spike
        if (healthState == 0) ReadBatteryHealth();
        if (IsBuilt) { try { SampleNet(); UpdateUi(); } catch { } }
    }
    public override void OnHidden() { shown = false; }

    // ---------------- sampling ----------------
    private void SampleCpuMem()
    {
        if (Native.GetSystemTimes(out var i, out var k, out var u))
        {
            ulong idle = Pack(i), kernel = Pack(k), user = Pack(u);
            if (cpuBase) { ulong di = idle - prevIdle, dk = kernel - prevKernel, du = user - prevUser, tot = dk + du; if (tot > 0) { cpu = Math.Clamp(1.0 - (double)di / tot, 0, 1); cpuKnown = true; } }
            prevIdle = idle; prevKernel = kernel; prevUser = user; cpuBase = true;
        }
        var ms = new Native.MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<Native.MEMORYSTATUSEX>() };
        if (Native.GlobalMemoryStatusEx(ref ms) && ms.ullTotalPhys > 0)
        {
            memTotalGb = ms.ullTotalPhys / 1073741824.0; memUsedGb = (ms.ullTotalPhys - ms.ullAvailPhys) / 1073741824.0; memFrac = memUsedGb / memTotalGb;
        }
    }
    private static ulong Pack(Native.FILETIME f) => ((ulong)f.High << 32) | f.Low;

    private void SampleNet()
    {
        long rx = 0, tx = 0;
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            try
            {
                if (nic.OperationalStatus != OperationalStatus.Up || nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                var st = nic.GetIPStatistics(); rx += st.BytesReceived; tx += st.BytesSent;
            }
            catch { }
        }
        long now = Stopwatch.GetTimestamp();
        if (netBase)
        {
            double dt = (now - prevNetTicks) / (double)Stopwatch.Frequency;
            if (dt > 0.2) { downRate = Math.Max(0, rx - prevRx) / dt; upRate = Math.Max(0, tx - prevTx) / dt; Push(down, downRate); Push(up, upRate); }
        }
        prevRx = rx; prevTx = tx; prevNetTicks = now; netBase = true;
    }
    private static void Push(List<double> l, double v) { l.Add(v); if (l.Count > Samples) l.RemoveAt(0); }

    /// <summary>One-time: `powercfg /batteryreport /xml` on a worker thread, parse Design/FullChargeCapacity. Never invents a value.</summary>
    private void ReadBatteryHealth()
    {
        healthState = 1;
        Task.Run(() =>
        {
            string tmp = Path.Combine(Path.GetTempPath(), "island-battery-" + Environment.ProcessId + ".xml"); int state = 3;
            try
            {
                if (System.Windows.Forms.SystemInformation.PowerStatus.BatteryChargeStatus.HasFlag(System.Windows.Forms.BatteryChargeStatus.NoSystemBattery)) return;
                var psi = new ProcessStartInfo("powercfg.exe", "/batteryreport /xml /output \"" + tmp + "\"") { CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
                using (var p = Process.Start(psi)) { if (p == null) return; p.StandardOutput.ReadToEnd(); if (!p.WaitForExit(30000)) { try { p.Kill(); } catch { } return; } }
                if (!File.Exists(tmp)) return;
                var doc = XDocument.Load(tmp); double design = 0, full = 0; int cyc = 0;
                foreach (var b in doc.Descendants().Where(e => e.Name.LocalName == "Battery" && e.Parent?.Name.LocalName == "Batteries"))
                {
                    double d = Num(b, "DesignCapacity"), f = Num(b, "FullChargeCapacity"); if (d <= 0 || f <= 0) continue;
                    design += d; full += f; cyc = Math.Max(cyc, (int)Num(b, "CycleCount"));
                }
                if (design > 0 && full > 0) { healthPct = Math.Min(100, full / design * 100); cycles = cyc; state = 2; }
            }
            catch { }
            finally { try { if (File.Exists(tmp)) File.Delete(tmp); } catch { } healthState = state; }
        });
    }
    private static double Num(XElement parent, string name)
    {
        var e = parent.Elements().FirstOrDefault(x => x.Name.LocalName == name);
        return e != null && double.TryParse(e.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0;
    }

    // ---------------- ui ----------------
    protected override FrameworkElement Build()
    {
        var g = new Grid { Width = Ui.ContentW, Height = Ui.ContentH };
        for (int i = 0; i < 5; i++) g.ColumnDefinitions.Add(new ColumnDefinition { Width = i % 2 == 1 ? new GridLength(10) : new GridLength(1, GridUnitType.Star) });
        g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(170) }); g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(10) }); g.RowDefinitions.Add(new RowDefinition());
        void Place(UIElement e, int col, int row, int span = 1) { Grid.SetColumn(e, col); Grid.SetRow(e, row); Grid.SetColumnSpan(e, span); g.Children.Add(e); }
        Place(RingTile("CPU", "\uE950", Accent, out cpuRing, out cpuMain, out cpuSub), 0, 0);
        Place(RingTile("Memory", "\uE950", MemColor, out memRing, out memMain, out memSub), 2, 0);
        Place(RingTile("Disk", "\uEDA2", DiskColor, out diskRing, out diskMain, out diskSub), 4, 0);
        Place(BuildNetwork(), 0, 2, 3); Place(BuildBattery(), 4, 2);
        UpdateUi(); return g;
    }

    private static Border RingTile(string title, string glyph, Color tint, out Ring ring, out TextBlock main, out TextBlock sub)
    {
        var body = new Grid(); body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(108) }); body.ColumnDefinitions.Add(new ColumnDefinition());
        ring = new Ring { Width = 100, Height = 100, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center }; body.Children.Add(ring);
        var t = new StackPanel { VerticalAlignment = VerticalAlignment.Center }; Grid.SetColumn(t, 1);
        main = Ui.Text("–", 19, Ui.Ink, FontWeights.SemiBold); sub = Ui.Text(" ", 12.5, Ui.Dim, FontWeights.Medium); sub.TextWrapping = TextWrapping.Wrap; sub.MaxHeight = 36;
        t.Children.Add(main); t.Children.Add(sub); body.Children.Add(t);
        return Ui.Titled(title, glyph, body, tint);
    }

    private Border BuildNetwork()
    {
        var body = new Grid(); body.RowDefinitions.Add(new RowDefinition()); body.RowDefinitions.Add(new RowDefinition());
        body.Children.Add(NetRow(0, "\uE74B", "Download", Accent, out downV, out downChart)); body.Children.Add(NetRow(1, "\uE74A", "Upload", UpColor, out upV, out upChart));
        return Ui.Titled("Network", "\uE968", body, Accent);
    }
    private static Grid NetRow(int row, string glyph, string label, Color tint, out TextBlock value, out AreaChart chart)
    {
        var g = new Grid { Margin = new Thickness(0, row == 0 ? 0 : 4, 0, 0) }; Grid.SetRow(g, row);
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) }); g.ColumnDefinitions.Add(new ColumnDefinition());
        var l = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var top = new StackPanel { Orientation = Orientation.Horizontal }; var gl = Ui.Glyph(glyph, 13, Ui.B(tint)); gl.Margin = new Thickness(0, 0, 6, 0); top.Children.Add(gl); top.Children.Add(Ui.Label(label)); l.Children.Add(top);
        value = Ui.Text("–", 18, Ui.Ink, FontWeights.SemiBold); l.Children.Add(value); g.Children.Add(l);
        chart = new AreaChart { ShowBaseline = false, Margin = new Thickness(0, 0, 0, 0) }; Grid.SetColumn(chart, 1); g.Children.Add(chart);
        return g;
    }

    private Border BuildBattery()
    {
        var body = new Grid(); body.RowDefinitions.Add(new RowDefinition()); body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var row = new Grid { VerticalAlignment = VerticalAlignment.Center }; row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); row.ColumnDefinitions.Add(new ColumnDefinition());
        batPct = Ui.Text("–", 32, Ui.Ink, FontWeights.SemiBold); batPct.VerticalAlignment = VerticalAlignment.Center; batPct.Margin = new Thickness(0, 0, 12, 0); row.Children.Add(batPct);
        var t = new StackPanel { VerticalAlignment = VerticalAlignment.Center }; Grid.SetColumn(t, 1);
        batState = Ui.Text(" ", 13, Ui.Soft, FontWeights.Medium); batHealth = Ui.Text(" ", 13, Ui.Dim, FontWeights.Medium); t.Children.Add(batState); t.Children.Add(batHealth); row.Children.Add(t);
        body.Children.Add(row);
        batFill = new Border { Height = 7, CornerRadius = new CornerRadius(3.5), HorizontalAlignment = HorizontalAlignment.Left, Width = 0, Background = Ui.Green };
        batTrack = new Border { Height = 7, CornerRadius = new CornerRadius(3.5), Background = Ui.B("#26262B"), Child = batFill, Margin = new Thickness(0, 4, 0, 0) }; Grid.SetRow(batTrack, 1); body.Children.Add(batTrack);
        return Ui.Titled("Battery", "\uE83F", body, Ui.Green.Color);
    }

    private void UpdateUi()
    {
        cpuRing.Set(cpu, Accent, $"{cpu * 100:0}%"); cpuMain.Text = cpuKnown ? $"{cpu * 100:0.#}% in use" : "Measuring…"; cpuSub.Text = Environment.ProcessorCount + " logical processors";
        memRing.Set(memFrac, MemColor, $"{memFrac * 100:0}%"); memMain.Text = $"{memUsedGb:0.0} / {memTotalGb:0.#} GB"; memSub.Text = $"{Math.Max(0, memTotalGb - memUsedGb):0.0} GB available";
        try
        {
            var root = Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\"; var di = new DriveInfo(root); double total = di.TotalSize, free = di.AvailableFreeSpace, usedFrac = total > 0 ? 1 - free / total : 0;
            diskRing.Set(usedFrac, DiskColor, $"{usedFrac * 100:0}%"); diskMain.Text = Bytes(free) + " free"; diskSub.Text = "of " + Bytes(total) + " on " + root.TrimEnd('\\');
        }
        catch { diskMain.Text = "Unavailable"; diskSub.Text = " "; }

        downV.Text = Rate(downRate); upV.Text = Rate(upRate); downChart.Set(down, Accent); upChart.Set(up, UpColor);

        var ps = System.Windows.Forms.SystemInformation.PowerStatus;
        if (ps.BatteryChargeStatus.HasFlag(System.Windows.Forms.BatteryChargeStatus.NoSystemBattery)) { batPct.Text = "–"; batState.Text = "No battery"; batHealth.Text = "Desktop power"; batFill.Width = 0; return; }
        double pct = Math.Clamp(ps.BatteryLifePercent, 0, 1); batPct.Text = $"{pct * 100:0}%";
        bool charging = ps.BatteryChargeStatus.HasFlag(System.Windows.Forms.BatteryChargeStatus.Charging), online = ps.PowerLineStatus == System.Windows.Forms.PowerLineStatus.Online;
        int secs = ps.BatteryLifeRemaining;
        batState.Text = charging ? "Charging" : online ? (pct >= .99 ? "Fully charged" : "Plugged in") : "On battery" + (secs > 0 ? " · " + (secs >= 3600 ? $"{secs / 3600}h {secs % 3600 / 60}m" : $"{Math.Max(1, secs / 60)}m") + " left" : "");
        batHealth.Text = healthState == 2 ? $"Health {healthPct:0}%" + (cycles > 0 ? $" · {cycles} cycles" : "") : healthState == 3 ? "Health unavailable" : "Health: reading…";
        batHealth.Foreground = healthState == 2 ? (healthPct >= 80 ? (Brush)Ui.Green : healthPct >= 60 ? Ui.Orange : Ui.Red) : Ui.Dim;
        Color bc = pct <= .15 && !charging && !online ? Ui.Red.Color : Ui.Green.Color; batFill.Background = Ui.B(bc);
        double w = batTrack.ActualWidth; if (w <= 0) w = 230; batFill.Width = Math.Max(4, w * pct);
    }

    private static string Rate(double bps) => bps >= 1048576 ? $"{bps / 1048576:0.0} MB/s" : bps >= 1024 ? $"{bps / 1024:0} KB/s" : $"{bps:0} B/s";
    private static string Bytes(double b) => b >= 1099511627776 ? $"{b / 1099511627776:0.0} TB" : $"{b / 1073741824:0} GB";

    private static class Native
    {
        [StructLayout(LayoutKind.Sequential)] public struct FILETIME { public uint Low; public uint High; }
        [StructLayout(LayoutKind.Sequential)] public struct MEMORYSTATUSEX { public uint dwLength, dwMemoryLoad; public ulong ullTotalPhys, ullAvailPhys, ullTotalPageFile, ullAvailPageFile, ullTotalVirtual, ullAvailVirtual, ullAvailExtendedVirtual; }
        [DllImport("kernel32.dll")] public static extern bool GetSystemTimes(out FILETIME idle, out FILETIME kernel, out FILETIME user);
        [DllImport("kernel32.dll")] public static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX m);
    }
}
