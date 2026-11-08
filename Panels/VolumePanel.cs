using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
namespace Island;

/// <summary>Per-app mixer and the default microphone mute. Levels never go above 100 percent, and apps are not moved between devices.</summary>
internal sealed class VolumePanel : IslandPanel
{
    public override string Id => "volume";
    public override string Title => "Volume";
    public override string Glyph => "\uE767";
    public override int Order => 9;
    public override Color Accent => Ui.C("#0A84FF");

    private static bool micMuted;
    private bool shown;
    private int tick;
    private StackPanel? sessions;
    private TextBlock? status, micLabel;
    private SoundsSlider? master;

    public override string? ActiveTaskText => micMuted ? "Mic muted" : null;

    public override void Start() { try { micMuted = VolumeDevices.IsMicMuted(); } catch { micMuted = false; } }
    public override void OnShown() { shown = true; Paint(); }
    public override void OnHidden() => shown = false;
    public override void Tick()
    {
        try { micMuted = VolumeDevices.IsMicMuted(); } catch { }
        if (!IsBuilt || !shown || Mouse.Captured != null || ++tick % 2 != 0) return;
        Paint();
    }

    protected override FrameworkElement Build()
    {
        var root = new Grid { Width = Ui.ContentW, Height = Ui.ContentH };
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(300) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        root.ColumnDefinitions.Add(new ColumnDefinition());

        var left = new StackPanel();
        master = new SoundsSlider(0.5, Accent, (v, _) => VolumeDevices.SetOutput(v));
        var masterBox = new StackPanel();
        masterBox.Children.Add(Ui.Label("Output"));
        masterBox.Children.Add(Ui.Text("Default speakers", 16, Ui.Ink, FontWeights.SemiBold));
        var hint = Ui.Text("This is the default output. It does not route apps to another device.", 12.5, Ui.Dim); hint.TextWrapping = TextWrapping.Wrap; hint.Margin = new Thickness(0, 4, 0, 10); masterBox.Children.Add(hint);
        masterBox.Children.Add(master);
        left.Children.Add(Ui.Card(masterBox, 16, 16));
        var mic = new Grid { Margin = new Thickness(0, 10, 0, 0) };
        mic.ColumnDefinitions.Add(new ColumnDefinition()); mic.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var micText = new StackPanel();
        micText.Children.Add(Ui.Text("Microphone", 16, Ui.Ink, FontWeights.SemiBold));
        micLabel = Ui.Text("Default microphone", 12.5, Ui.Dim); micLabel.Margin = new Thickness(0, 3, 0, 0); micText.Children.Add(micLabel);
        var micBtn = Ui.Button("Mute", Ui.C("#FF453A"), ToggleMic, 32);
        Grid.SetColumn(micBtn, 1); mic.Children.Add(micText); mic.Children.Add(micBtn);
        left.Children.Add(Ui.Card(mic, 16, 16));
        status = Ui.Text("", 12.5, Ui.Dim); status.TextWrapping = TextWrapping.Wrap; status.Margin = new Thickness(4, 10, 0, 0); left.Children.Add(status);
        root.Children.Add(left);

        sessions = new StackPanel();
        var right = Ui.Titled("Apps", "\uE767", Ui.Scroll(sessions), Accent, Ui.Button("Refresh", Accent, Paint, 28), 14);
        Grid.SetColumn(right, 2); root.Children.Add(right);
        Paint();
        return root;
    }

    private void ToggleMic()
    {
        try { micMuted = !micMuted; VolumeDevices.SetMicMuted(micMuted); micMuted = VolumeDevices.IsMicMuted(); }
        catch { status!.Text = "The default microphone could not be changed."; }
        PaintMic();
    }

    private void Paint()
    {
        IReadOnlyList<VolumeSession> rows;
        float output;
        try
        {
            micMuted = VolumeDevices.IsMicMuted();
            output = VolumeDevices.Output();
            rows = VolumeDevices.Sessions();
            if (status != null) status.Text = "";
        }
        catch
        {
            rows = Array.Empty<VolumeSession>();
            output = 0;
            if (status != null) status.Text = "Audio devices were not found.";
        }
        if (master != null && Mouse.Captured != master) master.SetSilent(output);
        PaintMic();
        if (sessions == null) return;
        sessions.Children.Clear();
        if (rows.Count == 0) { sessions.Children.Add(Ui.Text("No app is playing audio.", 14, Ui.Dim)); return; }
        foreach (var row in rows) sessions.Children.Add(SessionRow(row));
    }

    private void PaintMic()
    {
        if (micLabel == null) return;
        micLabel.Text = micMuted ? "Default microphone muted" : "Default microphone is live";
        micLabel.Foreground = micMuted ? Ui.Red : Ui.Dim;
    }

    private UIElement SessionRow(VolumeSession row)
    {
        var box = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
        var head = new Grid(); head.ColumnDefinitions.Add(new ColumnDefinition()); head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.Children.Add(Ui.Text(row.Name, 14, Ui.Ink, FontWeights.SemiBold));
        var mute = Ui.Button(row.Muted ? "Unmute" : "Mute", row.Muted ? Ui.C("#FF453A") : Accent, () => { VolumeDevices.SetSessionMute(row.Control, !row.Muted); Paint(); }, 26);
        Grid.SetColumn(mute, 1); head.Children.Add(mute);
        var slider = new SoundsSlider(row.Level, Accent, (v, _) => VolumeDevices.SetSessionVolume(row.Control, v));
        box.Children.Add(head); box.Children.Add(slider);
        return box;
    }
}

internal sealed class VolumeSession
{
    public string Name = "";
    public float Level;
    public bool Muted;
    public object? Control;
}

internal static class VolumeDevices
{
    private static readonly Guid Context = Guid.NewGuid();
    private static readonly List<object> held = new();

    public static bool IsMicMuted()
    {
        var device = Default(1);
        var endpoint = Endpoint(device);
        try { return endpoint.Muted; }
        finally { endpoint.ReleaseVolume(); Release(device); }
    }

    public static void SetMicMuted(bool muted)
    {
        var device = Default(1);
        var endpoint = Endpoint(device);
        try { endpoint.Set(muted); }
        finally { endpoint.ReleaseVolume(); Release(device); }
    }

    public static float Output()
    {
        var device = Default(0);
        var endpoint = Endpoint(device);
        try { return endpoint.Level; }
        finally { endpoint.ReleaseVolume(); Release(device); }
    }

    public static void SetOutput(double level)
    {
        var device = Default(0);
        var endpoint = Endpoint(device);
        try { endpoint.SetLevel((float)Math.Clamp(level, 0, 1)); }
        finally { endpoint.ReleaseVolume(); Release(device); }
    }

    public static IReadOnlyList<VolumeSession> Sessions()
    {
        foreach (var old in held) Release(old);
        held.Clear();
        var list = new List<VolumeSession>();
        var device = Default(0);
        try
        {
            var iid = typeof(IAudioSessionManager2).GUID;
            device.Activate(ref iid, 23, 0, out var obj);
            var manager = (IAudioSessionManager2)obj;
            manager.GetSessionEnumerator(out var enumerator);
            enumerator.GetCount(out int count);
            for (int i = 0; i < count; i++)
            {
                enumerator.GetSession(i, out var control);
                var simple = (ISimpleAudioVolume)control;
                simple.GetMasterVolume(out float level);
                simple.GetMute(out bool muted);
                if (level <= 0 && !muted && !HasProcess(control)) { Release(control); continue; }
                held.Add(control);
                list.Add(new VolumeSession { Name = NameOf(control), Level = Math.Clamp(level, 0, 1), Muted = muted, Control = control });
            }
            Release(enumerator); Release(manager);
        }
        finally { Release(device); }
        return list;
    }

    public static void SetSessionVolume(object? control, double level)
    {
        if (control is not ISimpleAudioVolume simple) return;
        var ctx = Context;
        simple.SetMasterVolume((float)Math.Clamp(level, 0, 1), ref ctx);
    }

    public static void SetSessionMute(object? control, bool muted)
    {
        if (control is not ISimpleAudioVolume simple) return;
        var ctx = Context;
        simple.SetMute(muted, ref ctx);
    }

    private static bool HasProcess(IAudioSessionControl2 control)
    {
        try { return control.GetProcessId(out _) >= 0; } catch { return false; }
    }

    private static string NameOf(IAudioSessionControl2 control)
    {
        try
        {
            if (control.IsSystemSoundsSession() == 0) return "System sounds";
            if (control.GetProcessId(out uint pid) >= 0 && pid != 0)
            {
                try { return Process.GetProcessById((int)pid).ProcessName; } catch { return "App " + pid; }
            }
        }
        catch { }
        nint name = 0;
        try
        {
            if (control.GetDisplayName(out name) >= 0 && name != 0)
            {
                var text = Marshal.PtrToStringUni(name);
                if (!string.IsNullOrWhiteSpace(text)) return text;
            }
        }
        catch { }
        finally { if (name != 0) Marshal.FreeCoTaskMem(name); }
        return "App";
    }

    private static IMMDevice Default(int flow)
    {
        var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
        enumerator.GetDefaultAudioEndpoint(flow, 1, out var device);
        Release(enumerator);
        return device;
    }

    private static EndpointVolume Endpoint(IMMDevice device)
    {
        var iid = typeof(IAudioEndpointVolume).GUID;
        device.Activate(ref iid, 23, 0, out var obj);
        return new EndpointVolume((IAudioEndpointVolume)obj);
    }

    private static void Release(object? com) { try { if (com != null && Marshal.IsComObject(com)) Marshal.ReleaseComObject(com); } catch { } }

    private sealed class EndpointVolume
    {
        private readonly IAudioEndpointVolume volume;
        public EndpointVolume(IAudioEndpointVolume volume) => this.volume = volume;
        public float Level { get { volume.GetMasterVolumeLevelScalar(out float v); return Math.Clamp(v, 0, 1); } }
        public bool Muted { get { volume.GetMute(out bool m); return m; } }
        public void SetLevel(float level) { var ctx = Context; volume.SetMasterVolumeLevelScalar(level, ref ctx); }
        public void Set(bool muted) { var ctx = Context; volume.SetMute(muted, ref ctx); }
        public void ReleaseVolume() => Release(volume);
    }
}

[ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
internal class MMDeviceEnumerator { }

[ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDeviceEnumerator
{
    void EnumAudioEndpoints(int dataFlow, int stateMask, out nint devices);
    void GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice device);
}

[ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDevice
{
    void Activate(ref Guid iid, uint clsCtx, nint activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object instance);
}

[ComImport, Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioSessionManager2
{
    void GetAudioSessionControl(ref Guid guid, uint flags, [MarshalAs(UnmanagedType.IUnknown)] out object control);
    void GetSimpleAudioVolume(ref Guid guid, uint flags, [MarshalAs(UnmanagedType.IUnknown)] out object volume);
    void GetSessionEnumerator(out IAudioSessionEnumerator enumerator);
}

[ComImport, Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioSessionEnumerator
{
    void GetCount(out int count);
    void GetSession(int index, out IAudioSessionControl2 session);
}

[ComImport, Guid("BFB7FF88-7239-4FC9-8FA2-07C950BE9C6D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioSessionControl2
{
    [PreserveSig] int GetState(out int state);
    [PreserveSig] int GetDisplayName(out nint name);
    [PreserveSig] int SetDisplayName([MarshalAs(UnmanagedType.LPWStr)] string value, ref Guid context);
    [PreserveSig] int GetIconPath(out nint path);
    [PreserveSig] int SetIconPath([MarshalAs(UnmanagedType.LPWStr)] string value, ref Guid context);
    [PreserveSig] int GetGroupingParam(out Guid grouping);
    [PreserveSig] int SetGroupingParam(ref Guid grouping, ref Guid context);
    [PreserveSig] int RegisterAudioSessionNotification(nint client);
    [PreserveSig] int UnregisterAudioSessionNotification(nint client);
    [PreserveSig] int GetSessionIdentifier(out nint identifier);
    [PreserveSig] int GetSessionInstanceIdentifier(out nint identifier);
    [PreserveSig] int GetProcessId(out uint processId);
    [PreserveSig] int IsSystemSoundsSession();
    [PreserveSig] int SetDuckingPreference([MarshalAs(UnmanagedType.Bool)] bool optOut);
}

[ComImport, Guid("87CE5498-68D6-44E5-9215-6DA47EF883D8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface ISimpleAudioVolume
{
    void SetMasterVolume(float level, ref Guid context);
    void GetMasterVolume(out float level);
    void SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid context);
    void GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
}

[ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioEndpointVolume
{
    void RegisterControlChangeNotify(nint client);
    void UnregisterControlChangeNotify(nint client);
    void GetChannelCount(out uint count);
    void SetMasterVolumeLevel(float levelDb, ref Guid context);
    void SetMasterVolumeLevelScalar(float level, ref Guid context);
    void GetMasterVolumeLevel(out float levelDb);
    void GetMasterVolumeLevelScalar(out float level);
    void SetChannelVolumeLevel(uint index, float levelDb, ref Guid context);
    void SetChannelVolumeLevelScalar(uint index, float level, ref Guid context);
    void GetChannelVolumeLevel(uint index, out float levelDb);
    void GetChannelVolumeLevelScalar(uint index, out float level);
    void SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid context);
    void GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
}
