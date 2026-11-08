using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
namespace Island;
internal sealed class Settings
{
    public static string Folder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),"Island");
    public int RevenueMinutes { get; set; }=5;
    public int CalendarMinutes { get; set; }=5;
    public int EmailMinutes { get; set; }=2;
    public bool Revenue { get; set; }=true;
    public bool Calendar { get; set; }=true;
    public bool Email { get; set; }=true;
    public bool SaleAlerts { get; set; }=true;
    public bool MeetingAlerts { get; set; }=true;
    public bool EmailAlerts { get; set; }=true;
    public bool NowPlayingAlerts { get; set; }=true;
    public bool TimerAlerts { get; set; }=true;
    public bool HydrationAlerts { get; set; }=true;
    public bool CleanLinks { get; set; }
    public bool Onboarded { get; set; }
    public List<string> Disabled { get; set; } = new();
    /// <summary>Panel id whose RestingText owns the collapsed notch ("" = rotate, "clock" = time).</summary>
    public string PinnedPanel { get; set; }="";
    public bool Allows(string id)
    {
        if (id is "home" or "apps") return true;
        if (id == "revenue" && !Revenue) return false;
        if (id == "calendar" && !Calendar) return false;
        return Disabled == null || !Disabled.Contains(id);
    }
    public void SetPanel(string id, bool on)
    {
        Disabled ??= new();
        if (on) Disabled.Remove(id); else if (!Disabled.Contains(id)) Disabled.Add(id);
        if (id == "revenue") Revenue = on;
        if (id == "calendar") Calendar = on;
        if (id == "email") Email = on;
    }
    public static Settings Load() { try { return JsonSerializer.Deserialize<Settings>(File.ReadAllText(Path.Combine(Folder,"settings.json")))??new(); } catch { return new(); } }
    public void Save() { Directory.CreateDirectory(Folder);File.WriteAllText(Path.Combine(Folder,"settings.json"),JsonSerializer.Serialize(this)); }
}
