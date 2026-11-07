using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
namespace Island;

// ---- persisted per-file aggregates (public props: System.Text.Json via Store) ----
public sealed class ClaudeDayAgg
{
    public string Day { get; set; } = "";           // yyyy-MM-dd (local)
    public long Tok { get; set; }                   // input + output + cache creation
    public long CacheRead { get; set; }
    public long Msgs { get; set; }
    public long Tools { get; set; }
    public List<string> Sessions { get; set; } = new();
}
public sealed class ClaudeFileAgg
{
    public string Path { get; set; } = "";
    public long Length { get; set; }
    public long WriteTicks { get; set; }
    public string Project { get; set; } = "";
    public long LastTicks { get; set; }             // newest entry timestamp (UTC ticks)
    public List<ClaudeDayAgg> Days { get; set; } = new();
}
public sealed class ClaudeCache { public int Ver { get; set; } = 1; public List<ClaudeFileAgg> Files { get; set; } = new(); }

/// <summary>Computed view model for the panel.</summary>
internal sealed class ClaudeSnapshot
{
    public bool Exists, AnyData;
    public long TokToday, CacheToday, MsgToday, ToolToday, SessToday, BestDay;
    public int Streak, ActiveDays;
    public string CurrentProject = ""; public DateTime LastActive; public DateTime BestDate;
    public DateTime GridStart;                      // Monday of the first grid column
    public int GridCols = 16;
    public Dictionary<DateTime, long> DayTokens = new();
    public List<(string Name, long Tok)> ProjToday = new(), Proj7 = new();
    public DateTime At;
}

/// <summary>Reads Claude Code transcripts (%USERPROFILE%\.claude\projects\**\*.jsonl) incrementally into per-day aggregates.</summary>
internal static class ClaudeUsage
{
    public static string Root => System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "projects");
    private static readonly object Gate = new();
    private static ClaudeCache? cache;

    public static ClaudeSnapshot Compute()
    {
        lock (Gate)
        {
            var snap = new ClaudeSnapshot { At = DateTime.Now };
            try
            {
                if (!Directory.Exists(Root)) return snap;
                snap.Exists = true;
                cache ??= Store.Load<ClaudeCache>("claude-usage");
                var old = new Dictionary<string, ClaudeFileAgg>(StringComparer.OrdinalIgnoreCase);
                foreach (var f in cache.Files) old[f.Path] = f;
                var next = new List<ClaudeFileAgg>(); bool changed = false;
                foreach (var path in Directory.EnumerateFiles(Root, "*.jsonl", SearchOption.AllDirectories))
                {
                    try
                    {
                        var fi = new FileInfo(path);
                        if (old.TryGetValue(path, out var prev) && prev.Length == fi.Length && prev.WriteTicks == fi.LastWriteTimeUtc.Ticks) { next.Add(prev); continue; }
                        var agg = ParseFile(path, fi); next.Add(agg); changed = true;
                    }
                    catch { if (old.TryGetValue(path, out var keep)) next.Add(keep); }
                }
                if (next.Count != cache.Files.Count) changed = true;
                cache.Files = next;
                if (changed) Store.Save("claude-usage", cache);
                Summarize(snap, next);
            }
            catch { }
            return snap;
        }
    }

    private struct Entry { public string Day; public long Tok, Cache; public string Session; }

    private static ClaudeFileAgg ParseFile(string path, FileInfo fi)
    {
        var agg = new ClaudeFileAgg { Path = path, Length = fi.Length, WriteTicks = fi.LastWriteTimeUtc.Ticks };
        var days = new Dictionary<string, ClaudeDayAgg>(); var sessions = new Dictionary<string, HashSet<string>>();
        var entries = new Dictionary<string, Entry>(); var toolIds = new HashSet<string>();
        string lastCwd = ""; long lastTicks = 0;
        ClaudeDayAgg Day(string d) { if (!days.TryGetValue(d, out var a)) { a = new ClaudeDayAgg { Day = d }; days[d] = a; sessions[d] = new HashSet<string>(); } return a; }

        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 16);
        using var sr = new StreamReader(fs, Encoding.UTF8, true, 1 << 16);
        string? line; int n = 0;
        while ((line = sr.ReadLine()) != null)
        {
            if (line.Length < 20 || line[0] != '{') continue;
            bool maybe = line.Contains("\"type\":\"assistant\"", StringComparison.Ordinal) || line.Contains("\"type\":\"user\"", StringComparison.Ordinal);
            if (!maybe) continue;
            try
            {
                using var doc = JsonDocument.Parse(line); var root = doc.RootElement;
                string type = Str(root, "type");
                if (type != "assistant" && type != "user") continue;
                if (!root.TryGetProperty("timestamp", out var tsEl) || tsEl.ValueKind != JsonValueKind.String || !DateTimeOffset.TryParse(tsEl.GetString(), null, System.Globalization.DateTimeStyles.RoundtripKind, out var ts)) continue;
                string day = ts.LocalDateTime.ToString("yyyy-MM-dd"); string sid = Str(root, "sessionId"); string cwd = Str(root, "cwd");
                if (ts.UtcTicks >= lastTicks) { lastTicks = ts.UtcTicks; if (cwd.Length > 0) lastCwd = cwd; }
                else if (lastCwd.Length == 0 && cwd.Length > 0) lastCwd = cwd;
                if (!root.TryGetProperty("message", out var msg) || msg.ValueKind != JsonValueKind.Object) continue;

                if (type == "assistant")
                {
                    string key = Str(msg, "id") + "|" + Str(root, "requestId"); if (key == "|") key = Str(root, "uuid") + "#" + (n++);
                    long inp = 0, outp = 0, cc = 0, cr = 0;
                    if (msg.TryGetProperty("usage", out var u) && u.ValueKind == JsonValueKind.Object) { inp = Num(u, "input_tokens"); outp = Num(u, "output_tokens"); cc = Num(u, "cache_creation_input_tokens"); cr = Num(u, "cache_read_input_tokens"); }
                    entries[key] = new Entry { Day = day, Tok = inp + outp + cc, Cache = cr, Session = sid };   // streamed duplicates: the last one carries the final counts
                    if (msg.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array)
                        foreach (var item in content.EnumerateArray())
                            if (item.ValueKind == JsonValueKind.Object && Str(item, "type") == "tool_use" && toolIds.Add(Str(item, "id") + "#" + key)) Day(day).Tools++;
                }
                else
                {
                    if (root.TryGetProperty("isMeta", out var m) && m.ValueKind == JsonValueKind.True) continue;
                    if (root.TryGetProperty("isSidechain", out var sc) && sc.ValueKind == JsonValueKind.True) continue;
                    if (!msg.TryGetProperty("content", out var c)) continue;
                    bool prompt = false;
                    if (c.ValueKind == JsonValueKind.String) { var s = c.GetString() ?? ""; prompt = s.Trim().Length > 0 && !s.StartsWith("<local-command", StringComparison.Ordinal) && !s.StartsWith("[Request interrupted", StringComparison.Ordinal); }
                    else if (c.ValueKind == JsonValueKind.Array)
                    {
                        bool text = false, result = false;
                        foreach (var item in c.EnumerateArray()) { if (item.ValueKind != JsonValueKind.Object) continue; var t = Str(item, "type"); if (t == "tool_result") result = true; else if (t == "text") text = true; }
                        prompt = text && !result;
                    }
                    if (prompt) { Day(day).Msgs++; if (sid.Length > 0) sessions[day].Add(sid); }
                }
            }
            catch { /* partial / malformed line: skip */ }
        }
        foreach (var e in entries.Values)
        {
            var d = Day(e.Day); d.Tok += e.Tok; d.CacheRead += e.Cache; if (e.Session.Length > 0) sessions[e.Day].Add(e.Session);
        }
        foreach (var d in days.Values) d.Sessions = sessions[d.Day].ToList();
        agg.Days = days.Values.OrderBy(d => d.Day, StringComparer.Ordinal).ToList(); agg.LastTicks = lastTicks;
        agg.Project = ProjectName(lastCwd, path);
        return agg;
    }

    private static string ProjectName(string cwd, string path)
    {
        var t = cwd.TrimEnd('\\', '/');
        int i = Math.Max(t.LastIndexOf('\\'), t.LastIndexOf('/'));
        if (t.Length > 0) return i >= 0 && i < t.Length - 1 ? t[(i + 1)..] : t;
        // fallback: the encoded folder name is the path with separators turned into '-'
        var dir = new DirectoryInfo(System.IO.Path.GetDirectoryName(path) ?? "");
        while (dir.Parent != null && !string.Equals(dir.Parent.Name, "projects", StringComparison.OrdinalIgnoreCase)) dir = dir.Parent;
        var enc = dir.Name; int j = enc.LastIndexOf('-');
        return j >= 0 && j < enc.Length - 1 ? enc[(j + 1)..] : enc;
    }

    private static void Summarize(ClaudeSnapshot s, List<ClaudeFileAgg> files)
    {
        DateTime today = DateTime.Today; string tk = today.ToString("yyyy-MM-dd");
        var tok = new Dictionary<string, long>(); var any = new HashSet<string>(); var sess = new HashSet<string>();
        var pToday = new Dictionary<string, long>(); var p7 = new Dictionary<string, long>(); string seven = today.AddDays(-6).ToString("yyyy-MM-dd");
        long newest = 0;
        foreach (var f in files)
        {
            if (f.LastTicks > newest && f.Project.Length > 0) { newest = f.LastTicks; s.CurrentProject = f.Project; s.LastActive = new DateTime(f.LastTicks, DateTimeKind.Utc).ToLocalTime(); }
            foreach (var d in f.Days)
            {
                tok[d.Day] = tok.GetValueOrDefault(d.Day) + d.Tok;
                if (d.Tok > 0 || d.Msgs > 0 || d.Tools > 0) any.Add(d.Day);
                if (d.Day == tk) { s.TokToday += d.Tok; s.CacheToday += d.CacheRead; s.MsgToday += d.Msgs; s.ToolToday += d.Tools; foreach (var x in d.Sessions) sess.Add(x); pToday[f.Project] = pToday.GetValueOrDefault(f.Project) + d.Tok; }
                if (string.CompareOrdinal(d.Day, seven) >= 0 && string.CompareOrdinal(d.Day, tk) <= 0) p7[f.Project] = p7.GetValueOrDefault(f.Project) + d.Tok;
            }
        }
        s.SessToday = sess.Count; s.AnyData = any.Count > 0;
        // streak: consecutive active days ending today (or yesterday, if today has not started yet)
        var cur = any.Contains(tk) ? today : today.AddDays(-1);
        while (any.Contains(cur.ToString("yyyy-MM-dd"))) { s.Streak++; cur = cur.AddDays(-1); }
        // grid window: GridCols weeks, Monday-first, ending with this week
        var monday = today.AddDays(-(((int)today.DayOfWeek + 6) % 7)); s.GridStart = monday.AddDays(-7 * (s.GridCols - 1));
        for (var d = s.GridStart; d <= today; d = d.AddDays(1))
        {
            string k = d.ToString("yyyy-MM-dd"); long v = tok.GetValueOrDefault(k); s.DayTokens[d] = v;
            if (any.Contains(k)) s.ActiveDays++;
            if (v > s.BestDay) { s.BestDay = v; s.BestDate = d; }
        }
        s.ProjToday = pToday.Where(p => p.Value > 0).OrderByDescending(p => p.Value).Select(p => (p.Key, p.Value)).ToList();
        s.Proj7 = p7.Where(p => p.Value > 0).OrderByDescending(p => p.Value).Select(p => (p.Key, p.Value)).ToList();
    }

    private static string Str(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
    private static long Num(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var n) ? n : 0;
}
