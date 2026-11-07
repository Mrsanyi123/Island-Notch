using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
namespace Island;

internal sealed class AnalyticsConfig { public string Provider { get; set; } = "datafast"; public string GaProperty { get; set; } = ""; public int Range { get; set; } = 1; }
internal sealed record AnalyticsItem(string Name, double Value);
internal sealed record AnalyticsTotals(double Visitors, double Pageviews, double? Bounce, double? Duration, double? Revenue);
internal sealed class AnalyticsSnapshot
{
    public AnalyticsTotals Cur { get; set; } = new(0, 0, null, null, null);
    public AnalyticsTotals? Prev { get; set; }
    public string Currency { get; set; } = "";
    public List<double> Series { get; set; } = new();
    public List<AnalyticsItem> Pages { get; set; } = new();
    public List<AnalyticsItem> Sources { get; set; } = new();
    public DateTime At { get; set; } = DateTime.Now;
    /// <summary>Non-fatal problems (e.g. one of the breakdown calls failed).</summary>
    public string Warning { get; set; } = "";
}
/// <summary>Error with a message that is safe to show to the user as-is.</summary>
internal sealed class AnalyticsException : Exception { public AnalyticsException(string m) : base(m) { } }

/// <summary>DataFast (datafa.st/api/v1/analytics/*, Bearer df_ key) and Google Analytics 4 Data API clients. Parsing is tolerant: missing fields become null / 0.</summary>
internal static class AnalyticsProviders
{
    private const string GaScopeHint = "Google refused access. Disconnect and reconnect Google in Settings (the Analytics permission was added recently) and make sure the \"Google Analytics Data API\" is enabled in your Google Cloud project.";

    // ---------- shared helpers ----------
    private static double? Num(JsonElement e, string name)
    {
        if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(name, out var v)) return null;
        return Val(v);
    }
    private static double? Val(JsonElement v)
    {
        if (v.ValueKind == JsonValueKind.Number) return v.GetDouble();
        if (v.ValueKind == JsonValueKind.String && double.TryParse(v.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var d)) return d;
        return null;
    }
    private static JsonElement? Arr(JsonElement e, string name) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Array ? v : null;
    private static string D(DateTime d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    /// <summary>(start, end, prevStart, prevEnd) for 0 = today, 1 = 7 days, 2 = 30 days.</summary>
    private static (DateTime, DateTime, DateTime, DateTime) Window(int range)
    {
        var t = DateTime.Today;
        return range == 0 ? (t, t, t.AddDays(-1), t.AddDays(-1)) : range == 1 ? (t.AddDays(-6), t, t.AddDays(-13), t.AddDays(-7)) : (t.AddDays(-29), t, t.AddDays(-59), t.AddDays(-30));
    }
    private static async Task<JsonElement?> Soft(Task<JsonElement> t, Action<string> warn) { try { return await t; } catch (Exception ex) { warn(ex.Message); return null; } }

    // ---------- DataFast ----------
    private static async Task<JsonElement> DfGet(string key, string path, string query)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, "https://datafa.st/api/v1/analytics/" + path + "?" + query);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        try
        {
            using var res = await Api.Client.SendAsync(req); string body = await res.Content.ReadAsStringAsync();
            if (!res.IsSuccessStatusCode)
            {
                string msg = ""; try { using var d = JsonDocument.Parse(body); if (d.RootElement.TryGetProperty("error", out var er)) msg = er.ValueKind == JsonValueKind.Object ? Api.Text(er, "message") : er.ValueKind == JsonValueKind.String ? er.GetString() ?? "" : ""; } catch { }
                throw new AnalyticsException(res.StatusCode switch
                {
                    HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => "DataFast rejected the API key (" + (int)res.StatusCode + "). Use a website key that starts with df_.",
                    HttpStatusCode.TooManyRequests => "DataFast rate limit reached, retrying shortly.",
                    _ => "DataFast error " + (int)res.StatusCode + (msg.Length > 0 ? ": " + msg : "")
                });
            }
            using var doc = JsonDocument.Parse(body); return doc.RootElement.Clone();
        }
        catch (AnalyticsException) { throw; }
        catch (HttpRequestException) { throw new AnalyticsException("Can't reach DataFast. Check your connection."); }
        catch (TaskCanceledException) { throw new AnalyticsException("DataFast timed out."); }
        catch (JsonException) { throw new AnalyticsException("DataFast returned an unexpected response."); }
    }
    /// <summary>First object of a { data: [ {...} ] } (or { data: {...} }) response.</summary>
    private static JsonElement? First(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("data", out var d)) return null;
        if (d.ValueKind == JsonValueKind.Array) return d.GetArrayLength() > 0 ? d[0] : null;
        return d.ValueKind == JsonValueKind.Object ? d : null;
    }
    private static AnalyticsTotals DfTotals(JsonElement? o) => o is { } e ? new AnalyticsTotals(Num(e, "visitors") ?? 0, Num(e, "pageviews") ?? 0, Num(e, "bounce_rate"), Num(e, "avg_session_duration"), Num(e, "revenue")) : new AnalyticsTotals(0, 0, null, null, null);
    private static List<AnalyticsItem> DfItems(JsonElement? root, string nameField)
    {
        var res = new List<AnalyticsItem>();
        if (root is { } r && Arr(r, "data") is { } arr)
            foreach (var row in arr.EnumerateArray())
            {
                string n = Api.Text(row, nameField); if (n.Length == 0) n = "(none)";
                res.Add(new AnalyticsItem(n, Num(row, "visitors") ?? 0));
            }
        return res;
    }

    public static async Task<AnalyticsSnapshot> FetchDataFast(string key, int range)
    {
        var (s, e, ps, pe) = Window(range); string cur = "startAt=" + D(s) + "&endAt=" + D(e), prv = "startAt=" + D(ps) + "&endAt=" + D(pe);
        string warn = ""; void Warn(string m) { if (warn.Length == 0) warn = m; }
        const string fields = "fields=visitors,pageviews,bounce_rate,avg_session_duration,revenue,currency";
        var tOverview = DfGet(key, "overview", fields + "&" + cur);
        var tPrev = Soft(DfGet(key, "overview", fields + "&" + prv), Warn);
        var tSeries = Soft(DfGet(key, "timeseries", "fields=visitors&interval=" + (range == 0 ? "hour" : "day") + "&limit=1000&" + cur), Warn);
        var tPages = Soft(DfGet(key, "pages", "fields=path,visitors&limit=10&" + cur), Warn);
        var tRefs = Soft(DfGet(key, "referrers", "fields=referrer,visitors&limit=10&" + cur), Warn);
        var overview = await tOverview; // the one call that must succeed
        var snap = new AnalyticsSnapshot { Cur = DfTotals(First(overview)) };
        if (First(overview) is { } o) snap.Currency = Api.Text(o, "currency");
        var prev = await tPrev; if (prev is { } p) snap.Prev = DfTotals(First(p));
        var ts = await tSeries; if (ts is { } t && Arr(t, "data") is { } rows) foreach (var row in rows.EnumerateArray()) snap.Series.Add(Num(row, "visitors") ?? 0);
        snap.Pages = DfItems(await tPages, "path"); snap.Sources = DfItems(await tRefs, "referrer"); snap.Warning = warn;
        return snap;
    }
    public static async Task<int> LiveDataFast(string key)
    {
        var r = await DfGet(key, "realtime", "");
        return (int)(First(r) is { } o ? Num(o, "visitors") ?? 0 : 0);
    }

    // ---------- Google Analytics 4 ----------
    private static async Task<JsonElement> GaPost(string property, string method, object body)
    {
        string token; try { token = await Hub.Dashboard.Auth.Access(); } catch { throw new AnalyticsException("Google sign-in failed. Reconnect Google in Settings."); }
        using var req = new HttpRequestMessage(HttpMethod.Post, "https://analyticsdata.googleapis.com/v1beta/properties/" + Uri.EscapeDataString(property) + ":" + method) { Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json") };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        try
        {
            using var res = await Api.Client.SendAsync(req); string text = await res.Content.ReadAsStringAsync();
            if (!res.IsSuccessStatusCode)
                throw new AnalyticsException(res.StatusCode switch
                {
                    HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => GaScopeHint,
                    HttpStatusCode.BadRequest or HttpStatusCode.NotFound => "Google Analytics rejected the request. Check that the property ID is the numeric GA4 property ID.",
                    HttpStatusCode.TooManyRequests => "Google Analytics quota reached, retrying shortly.",
                    _ => "Google Analytics error " + (int)res.StatusCode
                });
            using var doc = JsonDocument.Parse(text); return doc.RootElement.Clone();
        }
        catch (AnalyticsException) { throw; }
        catch (HttpRequestException) { throw new AnalyticsException("Can't reach Google Analytics. Check your connection."); }
        catch (TaskCanceledException) { throw new AnalyticsException("Google Analytics timed out."); }
        catch (JsonException) { throw new AnalyticsException("Google Analytics returned an unexpected response."); }
    }
    private static object Dates(DateTime s, DateTime e) => new[] { new { startDate = D(s), endDate = D(e) } };
    private static object Metrics(params string[] names) => names.Select(n => new { name = n }).ToArray();
    private static object Dims(params string[] names) => names.Select(n => new { name = n }).ToArray();

    private static AnalyticsTotals GaTotals(JsonElement r)
    {
        double[] v = new double[4]; bool[] has = new bool[4];
        if (Arr(r, "rows") is { } rows && rows.GetArrayLength() > 0 && Arr(rows[0], "metricValues") is { } mv)
        {
            int i = 0; foreach (var m in mv.EnumerateArray()) { if (i < 4 && Num(m, "value") is { } d) { v[i] = d; has[i] = true; } i++; }
        }
        return new AnalyticsTotals(v[0], v[1], has[2] ? v[2] * 100 : null, has[3] ? v[3] : null, null); // GA bounceRate is a 0..1 fraction
    }
    private static List<AnalyticsItem> GaItems(JsonElement r)
    {
        var res = new List<AnalyticsItem>();
        if (Arr(r, "rows") is { } rows)
            foreach (var row in rows.EnumerateArray())
            {
                string n = Arr(row, "dimensionValues") is { } dv && dv.GetArrayLength() > 0 ? Api.Text(dv[0], "value") : "";
                double val = Arr(row, "metricValues") is { } mv && mv.GetArrayLength() > 0 ? Val(mv[0].TryGetProperty("value", out var x) ? x : default) ?? 0 : 0;
                res.Add(new AnalyticsItem(n.Length == 0 ? "(not set)" : n, val));
            }
        return res;
    }

    public static async Task<AnalyticsSnapshot> FetchGa(string property, int range)
    {
        var (s, e, ps, pe) = Window(range); string warn = ""; void Warn(string m) { if (warn.Length == 0) warn = m; }
        object Totals(DateTime a, DateTime b) => new { dateRanges = Dates(a, b), metrics = Metrics("activeUsers", "screenPageViews", "bounceRate", "averageSessionDuration") };
        string dim = range == 0 ? "dateHour" : "date";
        var tTotals = GaPost(property, "runReport", Totals(s, e));
        var tPrev = Soft(GaPost(property, "runReport", Totals(ps, pe)), Warn);
        var tSeries = Soft(GaPost(property, "runReport", new { dateRanges = Dates(s, e), dimensions = Dims(dim), metrics = Metrics("activeUsers"), limit = 1000 }), Warn);
        var tPages = Soft(GaPost(property, "runReport", new { dateRanges = Dates(s, e), dimensions = Dims("pagePath"), metrics = Metrics("screenPageViews"), orderBys = new[] { new { metric = new { metricName = "screenPageViews" }, desc = true } }, limit = 10 }), Warn);
        var tSrc = Soft(GaPost(property, "runReport", new { dateRanges = Dates(s, e), dimensions = Dims("sessionSource"), metrics = Metrics("sessions"), orderBys = new[] { new { metric = new { metricName = "sessions" }, desc = true } }, limit = 10 }), Warn);
        var snap = new AnalyticsSnapshot { Cur = GaTotals(await tTotals) };
        var prev = await tPrev; if (prev is { } p) snap.Prev = GaTotals(p);
        if (await tSeries is { } ts)
        {
            var map = new Dictionary<string, double>();
            foreach (var it in GaItems(ts)) map[it.Name] = it.Value;
            if (range == 0) for (int h = 0; h <= DateTime.Now.Hour; h++) snap.Series.Add(map.GetValueOrDefault(DateTime.Today.ToString("yyyyMMdd", CultureInfo.InvariantCulture) + h.ToString("00", CultureInfo.InvariantCulture)));
            else for (var d = s; d <= e; d = d.AddDays(1)) snap.Series.Add(map.GetValueOrDefault(d.ToString("yyyyMMdd", CultureInfo.InvariantCulture)));
        }
        if (await tPages is { } pg) snap.Pages = GaItems(pg);
        if (await tSrc is { } sr) snap.Sources = GaItems(sr);
        snap.Warning = warn; return snap;
    }
    public static async Task<int> LiveGa(string property)
    {
        var r = await GaPost(property, "runRealtimeReport", new { metrics = Metrics("activeUsers") });
        return (int)GaTotals(r).Visitors;
    }
}
