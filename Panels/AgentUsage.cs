using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
namespace Island;

internal sealed class AgentSnapshot
{
    public bool Found;
    public long TokensToday;
    public int EventsToday;
    public string Project = "";
    public string Status = "";
    public bool Active;
}

/// <summary>Reads local Codex or Copilot logs when the folder already exists. Never creates it and never calls a cloud API.</summary>
internal static class AgentUsage
{
    public static AgentSnapshot Codex() => Read(CodexRoot(), "codex", true);
    public static AgentSnapshot Copilot() => Read(CopilotRoot(), "copilot", false);

    public static string? CodexRoot()
    {
        string home = Environment.GetEnvironmentVariable("CODEX_HOME") ?? "";
        if (home.Length == 0) home = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex");
        string sessions = Path.Combine(home, "sessions");
        return Directory.Exists(sessions) ? sessions : null;
    }

    public static string? CopilotRoot()
    {
        string home = Environment.GetEnvironmentVariable("COPILOT_HOME") ?? "";
        if (home.Length == 0) home = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".copilot");
        string sessions = Path.Combine(home, "session-state");
        if (Directory.Exists(sessions)) return sessions;
        string code = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Code", "User", "workspaceStorage");
        return Directory.Exists(code) ? code : null;
    }

    private static readonly Dictionary<string, (long Length, long Tokens, int Events, string Project, DateTime Newest)> cache = new();

    private static AgentSnapshot Read(string? root, string kind, bool codex)
    {
        var snap = new AgentSnapshot();
        if (root == null) { snap.Status = "not found"; return snap; }
        snap.Found = true;
        string today = DateTime.Now.ToString("yyyy-MM-dd");
        DateTime newest = DateTime.MinValue;
        try
        {
            IEnumerable<string> files = codex ? Directory.EnumerateFiles(root, "rollout-*.jsonl", SearchOption.AllDirectories) : CopilotFiles(root);
            foreach (var path in files)
            {
                if (path.EndsWith(".zst", StringComparison.OrdinalIgnoreCase)) continue;
                var fi = new FileInfo(path);
                if (!cache.TryGetValue(path, out var hit) || hit.Length != fi.Length)
                {
                    hit = codex ? ParseCodex(path, fi.Length) : ParseCopilot(path, fi.Length);
                    cache[path] = hit;
                }
                if (hit.Newest.Date != DateTime.Today && fi.LastWriteTime.Date != DateTime.Today) continue;
                snap.TokensToday += hit.Tokens;
                snap.EventsToday += hit.Events;
                if (hit.Newest >= newest && hit.Project.Length > 0) { newest = hit.Newest; snap.Project = hit.Project; }
                if (fi.LastWriteTime > DateTime.Now.AddMinutes(-3)) snap.Active = true;
            }
        }
        catch { snap.Status = "could not read logs"; return snap; }
        if (snap.Project.Length == 0) snap.Project = "No project";
        snap.Status = snap.TokensToday > 0 ? Compact(snap.TokensToday) + " tok today" : snap.EventsToday > 0 ? snap.EventsToday + " events today" : "No activity today";
        return snap;
    }

    private static (long Length, long Tokens, int Events, string Project, DateTime Newest) ParseCodex(string path, long length)
    {
        long tokens = 0; int events = 0; string project = ""; DateTime newest = DateTime.MinValue;
        foreach (var line in Lines(path))
        {
            if (line.Length < 20 || line[0] != '{') continue;
            try
            {
                using var doc = JsonDocument.Parse(line);
                var root = doc.RootElement;
                bool today = false;
                if (root.TryGetProperty("timestamp", out var ts) && ts.ValueKind == JsonValueKind.String && DateTimeOffset.TryParse(ts.GetString(), out var when))
                {
                    if (when.LocalDateTime > newest) newest = when.LocalDateTime;
                    today = when.LocalDateTime.Date == DateTime.Today;
                    if (today) events++;
                }
                if (!root.TryGetProperty("payload", out var payload) || payload.ValueKind != JsonValueKind.Object) continue;
                if (payload.TryGetProperty("cwd", out var cwd) && cwd.ValueKind == JsonValueKind.String)
                {
                    string name = Name(cwd.GetString());
                    if (name.Length > 0) project = name;
                }
                if (!today) continue;
                if (payload.TryGetProperty("info", out var info) && info.ValueKind == JsonValueKind.Object && info.TryGetProperty("last_token_usage", out var last))
                    tokens += Math.Max(Num(last, "total_tokens"), Num(last, "input_tokens") + Num(last, "output_tokens"));
            }
            catch { }
        }
        return (length, tokens, events, project, newest);
    }

    private static (long Length, long Tokens, int Events, string Project, DateTime Newest) ParseCopilot(string path, long length)
    {
        if (path.EndsWith("workspace.yaml", StringComparison.OrdinalIgnoreCase)) return (length, 0, 0, "", DateTime.MinValue);
        long tokens = 0; int events = 0; string project = ""; DateTime newest = File.GetLastWriteTime(path);
        string yaml = Path.Combine(Path.GetDirectoryName(path) ?? "", "workspace.yaml");
        if (File.Exists(yaml)) project = Cwd(yaml);
        if (path.EndsWith(".jsonl", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var line in Lines(path))
            {
                bool metrics = line.Contains("modelMetrics", StringComparison.Ordinal) || line.Contains("inputTokens", StringComparison.Ordinal);
                if (!metrics && !line.Contains("timestamp", StringComparison.Ordinal)) continue;
                try
                {
                    using var doc = JsonDocument.Parse(line);
                    if (metrics) Walk(doc.RootElement, ref tokens, ref newest);
                    if (doc.RootElement.TryGetProperty("timestamp", out var ts) && ts.ValueKind == JsonValueKind.String && DateTimeOffset.TryParse(ts.GetString(), out var when))
                    {
                        if (when.LocalDateTime > newest) newest = when.LocalDateTime;
                        if (when.LocalDateTime.Date == DateTime.Today) events++;
                    }
                }
                catch { }
            }
        }
        else events = newest.Date == DateTime.Today ? 1 : 0;
        return (length, tokens, events, project, newest);
    }

    private static void Walk(JsonElement el, ref long tokens, ref DateTime newest)
    {
        if (el.ValueKind == JsonValueKind.Object)
        {
            if (el.TryGetProperty("inputTokens", out _) || el.TryGetProperty("outputTokens", out _))
            {
                long n = Num(el, "inputTokens") + Num(el, "outputTokens") + Num(el, "reasoningTokens");
                if (n > 0) tokens += n;
            }
            foreach (var prop in el.EnumerateObject()) Walk(prop.Value, ref tokens, ref newest);
        }
        else if (el.ValueKind == JsonValueKind.Array)
            foreach (var item in el.EnumerateArray()) Walk(item, ref tokens, ref newest);
    }

    private static IEnumerable<string> CopilotFiles(string root)
    {
        if (root.EndsWith("workspaceStorage", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var dir in Directory.EnumerateDirectories(root))
            {
                string chats = Path.Combine(dir, "chatSessions");
                if (!Directory.Exists(chats)) continue;
                foreach (var file in Directory.EnumerateFiles(chats)) yield return file;
            }
            yield break;
        }
        foreach (var file in Directory.EnumerateFiles(root, "events.jsonl", SearchOption.AllDirectories)) yield return file;
    }

    private static IEnumerable<string> Lines(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 16);
        using var sr = new StreamReader(fs);
        string? line;
        while ((line = sr.ReadLine()) != null) yield return line;
    }

    private static string Cwd(string yaml)
    {
        try
        {
            foreach (var line in File.ReadLines(yaml))
            {
                var t = line.Trim();
                if (t.StartsWith("cwd:", StringComparison.OrdinalIgnoreCase)) return Name(t[4..].Trim().Trim('"'));
            }
        }
        catch { }
        return "";
    }

    private static string Name(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return "";
        string trimmed = path.Trim().TrimEnd('\\', '/');
        int slash = Math.Max(trimmed.LastIndexOf('\\'), trimmed.LastIndexOf('/'));
        return slash >= 0 ? trimmed[(slash + 1)..] : trimmed;
    }

    private static long Num(JsonElement el, string name)
    {
        if (!el.TryGetProperty(name, out var v)) return 0;
        if (v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out long n)) return n;
        return 0;
    }

    public static string Compact(long n) => n >= 1000 ? (n / 1000d).ToString("0.#") + "k" : n.ToString();
}
