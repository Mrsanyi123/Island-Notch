using System;
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
    public bool CleanLinks { get; set; }
    /// <summary>Panel id whose RestingText owns the collapsed notch ("" = rotate, "clock" = time).</summary>
    public string PinnedPanel { get; set; }="";
    public static Settings Load() { try { return JsonSerializer.Deserialize<Settings>(File.ReadAllText(Path.Combine(Folder,"settings.json")))??new(); } catch { return new(); } }
    public void Save() { Directory.CreateDirectory(Folder);File.WriteAllText(Path.Combine(Folder,"settings.json"),JsonSerializer.Serialize(this)); }
}
