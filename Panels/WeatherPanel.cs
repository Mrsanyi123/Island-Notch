using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
namespace Island;

/// <summary>Persisted weather settings (location + unit).</summary>
internal sealed class WeatherSettings
{
    public bool HasLocation { get; set; }
    public double Lat { get; set; }
    public double Lon { get; set; }
    public string City { get; set; } = "";
    public bool Fahrenheit { get; set; }
    public bool GeoTried { get; set; }
}

internal sealed record WeatherHour(DateTime Time, double Temp, int Code, int Rain);
internal sealed record WeatherDay(DateTime Date, double Min, double Max, int Code, int Rain);
internal sealed class WeatherData
{
    public DateTime Fetched = DateTime.Now;
    public double Temp, Feels, Wind, Precip; public int Humidity, Code; public bool IsDay = true;
    public List<WeatherHour> Hours = new(); public List<WeatherDay> Days = new();
}

/// <summary>Weather: Open-Meteo forecast (current, next 12 hours, 7 days) for a user-chosen or IP-located city.</summary>
internal sealed class WeatherPanel : IslandPanel
{
    public override string Id => "weather";
    public override string Title => "Weather";
    public override string Glyph => "";
    public override int Order => 90;
    public override Color Accent => Ui.C("#4FA3FF");

    private WeatherSettings cfg = new();
    private WeatherData? data;
    private bool busy, failed, loadedOnce;
    private string? error;
    private Grid? root;
    private readonly StackPanel hero = new(), cityEdit = new();
    private readonly Grid hours = new(), days = new();
    private TextBox? cityBox; private TextBlock? cityMsg;
    private SegmentedControl? units;

    public override string? RestingText
    {
        get
        {
            var d = data; if (d == null) return null;
            string s = Deg(d.Temp) + " " + Cond(d.Code); return s.Length > 28 ? s[..27] + "…" : s;
        }
    }

    public override void Start() { cfg = Store.Load<WeatherSettings>("weather"); _ = LoadAsync(false); }
    public override void Tick() { if (data != null && !busy && (DateTime.Now - data.Fetched).TotalMinutes > 30) _ = LoadAsync(true); }
    public override void OnShown() { if (IsBuilt && !busy && (data == null || (DateTime.Now - data.Fetched).TotalMinutes > 15)) _ = LoadAsync(false); }

    // ---------- data ----------
    private async Task LoadAsync(bool force)
    {
        if (busy) return; busy = true; error = null;
        try
        {
            if (!force && data != null && (DateTime.Now - data.Fetched).TotalMinutes < 15) return;
            if (IsBuilt) Render();
            if (!cfg.HasLocation && !cfg.GeoTried) { cfg.GeoTried = true; await Geolocate(); Store.Save("weather", cfg); }
            if (!cfg.HasLocation) { failed = false; return; }
            var url = string.Create(CultureInfo.InvariantCulture, $"https://api.open-meteo.com/v1/forecast?latitude={cfg.Lat:0.####}&longitude={cfg.Lon:0.####}&current=temperature_2m,apparent_temperature,relative_humidity_2m,precipitation,weather_code,wind_speed_10m,is_day&hourly=temperature_2m,weather_code,precipitation_probability&daily=weather_code,temperature_2m_max,temperature_2m_min,precipitation_probability_max&timezone=auto&forecast_days=7");
            using var doc = JsonDocument.Parse(await Api.Client.GetStringAsync(url));
            data = Parse(doc.RootElement); failed = false; loadedOnce = true;
        }
        catch (Exception ex) { failed = true; error = ex is TaskCanceledException ? "Timed out" : "Couldn't reach the forecast"; }
        finally { busy = false; try { if (IsBuilt) Render(); } catch { } }
    }

    private static double D(JsonElement e, string n, double def = 0) => e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : def;
    private static double At(JsonElement a, int i) => a.ValueKind == JsonValueKind.Array && i < a.GetArrayLength() && a[i].ValueKind == JsonValueKind.Number ? a[i].GetDouble() : 0;
    private static DateTime T(JsonElement a, int i) => DateTime.TryParse(a[i].GetString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var t) ? t : DateTime.MinValue;

    private static WeatherData Parse(JsonElement r)
    {
        var w = new WeatherData(); var c = r.GetProperty("current");
        w.Temp = D(c, "temperature_2m"); w.Feels = D(c, "apparent_temperature", w.Temp); w.Humidity = (int)D(c, "relative_humidity_2m"); w.Precip = D(c, "precipitation");
        w.Code = (int)D(c, "weather_code"); w.Wind = D(c, "wind_speed_10m"); w.IsDay = D(c, "is_day", 1) > 0;
        DateTime now = c.TryGetProperty("time", out var ct) && DateTime.TryParse(ct.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var cn) ? cn : DateTime.Now;
        var h = r.GetProperty("hourly"); var ht = h.GetProperty("time"); var temps = h.GetProperty("temperature_2m"); var codes = h.GetProperty("weather_code");
        h.TryGetProperty("precipitation_probability", out var rain);
        int start = 0; for (int i = 0; i < ht.GetArrayLength(); i++) if (T(ht, i) >= now.Date.AddHours(now.Hour)) { start = i; break; }
        for (int i = start; i < Math.Min(start + 12, ht.GetArrayLength()); i++) w.Hours.Add(new WeatherHour(T(ht, i), At(temps, i), (int)At(codes, i), (int)At(rain, i)));
        var d = r.GetProperty("daily"); var dt = d.GetProperty("time"); d.TryGetProperty("precipitation_probability_max", out var dr);
        for (int i = 0; i < dt.GetArrayLength(); i++) w.Days.Add(new WeatherDay(T(dt, i), At(d.GetProperty("temperature_2m_min"), i), At(d.GetProperty("temperature_2m_max"), i), (int)At(d.GetProperty("weather_code"), i), (int)At(dr, i)));
        return w;
    }

    /// <summary>IP geolocation, once. Best effort: city-level accuracy at most.</summary>
    private async Task Geolocate()
    {
        try
        {
            using var d = JsonDocument.Parse(await Api.Client.GetStringAsync("https://ipapi.co/json/")); var e = d.RootElement;
            if (e.TryGetProperty("latitude", out var la) && e.TryGetProperty("longitude", out var lo) && la.ValueKind == JsonValueKind.Number && lo.ValueKind == JsonValueKind.Number)
            { cfg.Lat = la.GetDouble(); cfg.Lon = lo.GetDouble(); cfg.City = e.TryGetProperty("city", out var c) ? c.GetString() ?? "" : ""; cfg.HasLocation = true; return; }
        }
        catch { }
        try
        {
            using var d = JsonDocument.Parse(await Api.Client.GetStringAsync("http://ip-api.com/json/")); var e = d.RootElement;
            if (e.TryGetProperty("lat", out var la) && e.TryGetProperty("lon", out var lo) && la.ValueKind == JsonValueKind.Number && lo.ValueKind == JsonValueKind.Number)
            { cfg.Lat = la.GetDouble(); cfg.Lon = lo.GetDouble(); cfg.City = e.TryGetProperty("city", out var c) ? c.GetString() ?? "" : ""; cfg.HasLocation = true; }
        }
        catch { }
    }

    private async Task SetCity(string name)
    {
        name = name.Trim(); if (name.Length == 0) return;
        if (cityMsg != null) { cityMsg.Text = "Searching…"; cityMsg.Foreground = Ui.Dim; }
        try
        {
            using var d = JsonDocument.Parse(await Api.Client.GetStringAsync("https://geocoding-api.open-meteo.com/v1/search?count=1&name=" + Uri.EscapeDataString(name)));
            if (d.RootElement.TryGetProperty("results", out var res) && res.GetArrayLength() > 0)
            {
                var r = res[0]; cfg.Lat = D(r, "latitude"); cfg.Lon = D(r, "longitude"); cfg.HasLocation = true;
                string n = r.TryGetProperty("name", out var nn) ? nn.GetString() ?? name : name, cc = r.TryGetProperty("country", out var co) ? co.GetString() ?? "" : "";
                cfg.City = cc.Length > 0 ? n + ", " + cc : n; Store.Save("weather", cfg); data = null;
                if (cityBox != null) cityBox.Text = ""; cityEdit.Visibility = Visibility.Collapsed; hero.Visibility = Visibility.Visible;
                await LoadAsync(true);
            }
            else if (cityMsg != null) { cityMsg.Text = "No city found for \"" + name + "\""; cityMsg.Foreground = Ui.Orange; }
        }
        catch { if (cityMsg != null) { cityMsg.Text = "Couldn't search, check your connection"; cityMsg.Foreground = Ui.Orange; } }
    }

    // ---------- formatting ----------
    private string Deg(double c) => Math.Round(cfg.Fahrenheit ? c * 9 / 5 + 32 : c).ToString("0", CultureInfo.InvariantCulture).Replace("-0", "0") + "°";
    private string Wind(double kmh) => cfg.Fahrenheit ? Math.Round(kmh * 0.621371) + " mph" : Math.Round(kmh) + " km/h";

    private static string Cond(int code) => code switch
    {
        0 => "Clear", 1 => "Mostly clear", 2 => "Partly cloudy", 3 => "Overcast", 45 or 48 => "Fog", 51 or 53 or 55 => "Drizzle", 56 or 57 => "Freezing drizzle",
        61 => "Light rain", 63 => "Rain", 65 => "Heavy rain", 66 or 67 => "Freezing rain", 71 => "Light snow", 73 => "Snow", 75 => "Heavy snow", 77 => "Snow grains",
        80 or 81 or 82 => "Showers", 85 or 86 => "Snow showers", 95 => "Thunderstorm", 96 or 99 => "Thunder, hail", _ => "Unknown"
    };
    private static string Icon(int code, bool day) => code switch
    {
        0 => day ? "☀️" : "🌙", 1 => day ? "🌤️" : "🌙", 2 => day ? "⛅" : "☁️", 3 => "☁️", 45 or 48 => "🌫️", 51 or 53 or 55 or 56 or 57 => "🌦️",
        61 or 63 or 65 or 66 or 67 or 80 or 81 or 82 => "🌧️", 71 or 73 or 75 or 77 or 85 or 86 => "🌨️", 95 or 96 or 99 => "⛈️", _ => "🌡️"
    };
    private static TextBlock Emoji(string e, double size) => new() { Text = e, FontFamily = new FontFamily("Segoe UI Emoji"), FontSize = size, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Foreground = Ui.Ink };
    private static bool IsDayHour(DateTime t) => t.Hour is >= 6 and < 20;

    // ---------- UI ----------
    protected override FrameworkElement Build()
    {
        if (!loadedOnce && data == null && cfg.City.Length == 0 && !cfg.HasLocation) cfg = Store.Load<WeatherSettings>("weather");
        var g = new Grid { Width = Ui.ContentW, Height = Ui.ContentH }; root = g;
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(290) }); g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) }); g.ColumnDefinitions.Add(new ColumnDefinition());

        // left: hero + inline city editor (they share the card)
        var heroHost = new Grid(); heroHost.Children.Add(hero); heroHost.Children.Add(cityEdit); cityEdit.Visibility = Visibility.Collapsed; BuildCityEdit();
        g.Children.Add(Ui.Card(heroHost, 16));

        var right = new Grid { }; Grid.SetColumn(right, 2); right.RowDefinitions.Add(new RowDefinition { Height = new GridLength(122) }); right.RowDefinitions.Add(new RowDefinition { Height = new GridLength(10) }); right.RowDefinitions.Add(new RowDefinition());
        var hc = Ui.Card(null, 12); var hg = new Grid(); hg.RowDefinitions.Add(new RowDefinition { Height = new GridLength(20) }); hg.RowDefinitions.Add(new RowDefinition());
        hg.Children.Add(Ui.Label("Next 12 hours")); Grid.SetRow(hours, 1); hg.Children.Add(hours); hc.Child = hg; right.Children.Add(hc);
        var dc = Ui.Card(null, 12); var dg = new Grid(); dg.RowDefinitions.Add(new RowDefinition { Height = new GridLength(20) }); dg.RowDefinitions.Add(new RowDefinition());
        dg.Children.Add(Ui.Label("7-day range")); Grid.SetRow(days, 1); dg.Children.Add(days); dc.Child = dg; Grid.SetRow(dc, 2); right.Children.Add(dc);
        g.Children.Add(right);
        Render();
        return g;
    }

    private void BuildCityEdit()
    {
        cityEdit.Children.Clear();
        cityEdit.Children.Add(Ui.Label("Location"));
        var t = Ui.Text("Which city?", 22, Ui.Ink, FontWeights.SemiBold); t.Margin = new Thickness(0, 6, 0, 4); cityEdit.Children.Add(t);
        var hint = Ui.Text("Type a city and press Enter. It is looked up with Open-Meteo and remembered.", 13, Ui.Dim); hint.TextWrapping = TextWrapping.Wrap; hint.Margin = new Thickness(0, 0, 0, 12); cityEdit.Children.Add(hint);
        cityBox = Ui.Input("e.g. Budapest"); cityBox.Margin = new Thickness(0, 0, 0, 10);
        cityBox.KeyDown += (_, e) => { if (e.Key == Key.Enter) { _ = SetCity(cityBox.Text); e.Handled = true; } };
        cityEdit.Children.Add(cityBox);
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(Ui.Button("Set city", Accent, () => { if (cityBox != null) _ = SetCity(cityBox.Text); }));
        var cancel = Ui.Button("Cancel", Ui.C("#A0A0A8"), () => { cityEdit.Visibility = Visibility.Collapsed; hero.Visibility = Visibility.Visible; }); cancel.Margin = new Thickness(8, 0, 0, 0); row.Children.Add(cancel);
        cityEdit.Children.Add(row);
        cityMsg = Ui.Text("", 12.5, Ui.Dim); cityMsg.Margin = new Thickness(0, 10, 0, 0); cityMsg.TextWrapping = TextWrapping.Wrap; cityEdit.Children.Add(cityMsg);
    }

    private void ShowCityEdit() { if (cityMsg != null) cityMsg.Text = ""; hero.Visibility = Visibility.Collapsed; cityEdit.Visibility = Visibility.Visible; cityBox?.Focus(); }

    private Border Chip(string label, string value)
    {
        var s = new StackPanel(); s.Children.Add(Ui.Text(label.ToUpperInvariant(), 11.5, Ui.Dim, FontWeights.Bold)); s.Children.Add(Ui.Text(value, 15, Ui.Ink, FontWeights.SemiBold));
        return new Border { Background = Ui.Chip, CornerRadius = new CornerRadius(12), Padding = new Thickness(10, 5, 10, 5), Margin = new Thickness(0, 0, 0, 6), Child = s };
    }

    private void Render()
    {
        if (root == null) return;
        try { RenderHero(); RenderHours(); RenderDays(); } catch { }
    }

    private void RenderHero()
    {
        hero.Children.Clear(); var d = data;
        // header: city + refresh
        var head = new Grid { Height = 28 }; head.ColumnDefinitions.Add(new ColumnDefinition()); head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var city = Ui.Text(cfg.City.Length > 0 ? cfg.City : cfg.HasLocation ? "Your location" : "Weather", 16, Ui.Ink, FontWeights.SemiBold); city.VerticalAlignment = VerticalAlignment.Center; head.Children.Add(city);
        var refresh = Ui.IconButton("", "Refresh", () => { if (!busy) _ = LoadAsync(true); }, 28); Grid.SetColumn(refresh, 1); head.Children.Add(refresh); hero.Children.Add(head);

        if (!cfg.HasLocation && d == null)
        {
            var t = Ui.Text(busy ? "Finding your location…" : "Pick a city to see its weather.", 14, Ui.Dim); t.TextWrapping = TextWrapping.Wrap; t.Margin = new Thickness(0, 14, 0, 12); hero.Children.Add(t);
            if (!busy) hero.Children.Add(Ui.Button("Choose city", Accent, ShowCityEdit)); return;
        }
        if (d == null)
        {
            var t = Ui.Text(failed ? (error ?? "Couldn't load the forecast") + ". Check your connection and press retry." : "Loading forecast…", 14, failed ? Ui.Orange : Ui.Dim); t.TextWrapping = TextWrapping.Wrap; t.Margin = new Thickness(0, 14, 0, 12); hero.Children.Add(t);
            if (failed) hero.Children.Add(Ui.Button("Retry", Accent, () => { if (!busy) _ = LoadAsync(true); }));
            hero.Children.Add(CityLink()); return;
        }
        var temp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 0), Height = 64 };
        temp.Children.Add(Ui.Text(Deg(d.Temp), 54, Ui.Ink, FontWeights.SemiBold)); var ic = Emoji(Icon(d.Code, d.IsDay), 38); ic.Margin = new Thickness(12, 0, 0, 0); temp.Children.Add(ic); hero.Children.Add(temp);
        hero.Children.Add(Ui.Text(Cond(d.Code), 17, Ui.Soft, FontWeights.Medium));
        var today = d.Days.FirstOrDefault();
        string hl = today == null ? "" : "H " + Deg(today.Max) + "   L " + Deg(today.Min);
        var hlt = Ui.Text(hl, 13.5, B(Accent), FontWeights.SemiBold); hlt.Margin = new Thickness(0, 1, 0, 8); hero.Children.Add(hlt);
        var grid = new UniformGrid { Columns = 2 };
        grid.Children.Add(Chip("Feels like", Deg(d.Feels))); grid.Children.Add(Chip("Humidity", d.Humidity + "%"));
        grid.Children.Add(Chip("Wind", Wind(d.Wind))); grid.Children.Add(Chip("Rain", (d.Precip > 0 ? d.Precip.ToString("0.0", CultureInfo.InvariantCulture) + " mm" : today != null ? today.Rain + "% today" : "none")));
        foreach (FrameworkElement c in grid.Children) c.Margin = new Thickness(c == grid.Children[0] || c == grid.Children[2] ? 0 : 6, 0, c == grid.Children[0] || c == grid.Children[2] ? 6 : 0, 6);
        hero.Children.Add(grid);
        // footer: change city, units, status
        var foot = new Grid { Height = 32 }; foot.ColumnDefinitions.Add(new ColumnDefinition()); foot.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var left = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        left.Children.Add(CityLink());
        string status = failed ? "Offline, showing " + d.Fetched.ToString("h:mm tt", CultureInfo.CurrentCulture) : busy ? "Updating…" : "Updated " + d.Fetched.ToString("h:mm tt", CultureInfo.CurrentCulture);
        left.Children.Add(Ui.Text(status, 12, failed ? Ui.Orange : Ui.Dim)); foot.Children.Add(left);
        units = new SegmentedControl(new[] { "°C", "°F" }, cfg.Fahrenheit ? 1 : 0, Accent, i => { cfg.Fahrenheit = i == 1; Store.Save("weather", cfg); Render(); }) { VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(units, 1); foot.Children.Add(units); hero.Children.Add(foot);
    }

    private TextBlock CityLink()
    {
        var l = Ui.Text("Change city", 13, Ui.Blue, FontWeights.SemiBold); l.Cursor = Cursors.Hand; l.Margin = new Thickness(0, 12, 0, 0);
        l.Margin = new Thickness(0);
        l.MouseEnter += (_, _) => l.TextDecorations = TextDecorations.Underline; l.MouseLeave += (_, _) => l.TextDecorations = null;
        l.MouseLeftButtonUp += (_, e) => { ShowCityEdit(); e.Handled = true; }; return l;
    }

    private static SolidColorBrush B(Color c) => Ui.B(c);

    private void RenderHours()
    {
        hours.Children.Clear(); hours.ColumnDefinitions.Clear(); var d = data;
        if (d == null || d.Hours.Count == 0) { hours.Children.Add(Ui.Text(failed ? "Forecast unavailable. Retry from the refresh button." : "Waiting for the forecast…", 13.5, Ui.Dim)); return; }
        for (int i = 0; i < d.Hours.Count; i++)
        {
            hours.ColumnDefinitions.Add(new ColumnDefinition()); var h = d.Hours[i]; bool first = i == 0;
            var col = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            col.Children.Add(new TextBlock { Text = first ? "Now" : h.Time.ToString("h tt", CultureInfo.CurrentCulture), FontSize = 12.5, FontWeight = first ? FontWeights.Bold : FontWeights.Medium, Foreground = first ? B(Accent) : Ui.Dim, HorizontalAlignment = HorizontalAlignment.Center });
            var em = Emoji(Icon(h.Code, IsDayHour(h.Time)), 20); em.Margin = new Thickness(0, 3, 0, 3); col.Children.Add(em);
            col.Children.Add(new TextBlock { Text = Deg(h.Temp), FontSize = 15, FontWeight = FontWeights.SemiBold, Foreground = Ui.Ink, HorizontalAlignment = HorizontalAlignment.Center });
            col.Children.Add(new TextBlock { Text = h.Rain >= 5 ? h.Rain + "%" : " ", FontSize = 12.5, FontWeight = FontWeights.Medium, Foreground = h.Rain >= 40 ? Ui.Blue : Ui.Dim, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 1, 0, 0) });
            Grid.SetColumn(col, i); hours.Children.Add(col);
        }
    }

    private void RenderDays()
    {
        days.Children.Clear(); days.RowDefinitions.Clear(); var d = data;
        if (d == null || d.Days.Count == 0) { days.Children.Add(Ui.Text(failed ? "Forecast unavailable." : "Waiting for the forecast…", 13.5, Ui.Dim)); return; }
        double gmin = d.Days.Min(x => x.Min), gmax = d.Days.Max(x => x.Max), span = Math.Max(gmax - gmin, 1);
        for (int i = 0; i < d.Days.Count; i++)
        {
            days.RowDefinitions.Add(new RowDefinition()); var day = d.Days[i]; bool today = i == 0;
            var row = new Grid { Margin = new Thickness(0, 1, 0, 1) };
            foreach (var w in new[] { 64.0, 30, 46, 42, -1, 42 }) row.ColumnDefinitions.Add(new ColumnDefinition { Width = w < 0 ? new GridLength(1, GridUnitType.Star) : new GridLength(w) });
            var bg = new Border { CornerRadius = new CornerRadius(8), Background = today ? Ui.B(Accent, 0x22) : Brushes.Transparent }; Grid.SetColumnSpan(bg, 6); row.Children.Add(bg);
            void Put(UIElement e, int c) { Grid.SetColumn(e, c); row.Children.Add(e); }
            var name = Ui.Text(today ? "Today" : day.Date.ToString("ddd", CultureInfo.CurrentCulture), 14, today ? B(Accent) : Ui.Ink, today ? FontWeights.Bold : FontWeights.Medium); name.VerticalAlignment = VerticalAlignment.Center; name.Margin = new Thickness(8, 0, 0, 0); Put(name, 0);
            Put(Emoji(Icon(day.Code, true), 16), 1);
            var rain = Ui.Text(day.Rain >= 5 ? day.Rain + "%" : "", 12.5, day.Rain >= 40 ? Ui.Blue : Ui.Dim, FontWeights.Medium); rain.VerticalAlignment = VerticalAlignment.Center; rain.HorizontalAlignment = HorizontalAlignment.Center; Put(rain, 2);
            var lo = Ui.Text(Deg(day.Min), 14, Ui.Dim, FontWeights.Medium); lo.HorizontalAlignment = HorizontalAlignment.Right; lo.VerticalAlignment = VerticalAlignment.Center; lo.Margin = new Thickness(0, 0, 8, 0); Put(lo, 3);
            // range bar positioned on the week's global min..max
            var bar = new Grid { Height = 6, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 0, 0) };
            bar.Children.Add(new Border { Background = Ui.B("#2A2A30"), CornerRadius = new CornerRadius(3) });
            var inner = new Grid(); inner.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(day.Min - gmin, 0) / span, GridUnitType.Star) });
            inner.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max((day.Max - day.Min) / span, 0.06), GridUnitType.Star) });
            inner.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(gmax - day.Max, 0) / span, GridUnitType.Star) });
            var fill = new Border { CornerRadius = new CornerRadius(3), Background = new LinearGradientBrush(Ui.C("#64D2FF"), Ui.C("#FF9F0A"), 0) }; Grid.SetColumn(fill, 1); inner.Children.Add(fill); bar.Children.Add(inner); Put(bar, 4);
            var hi = Ui.Text(Deg(day.Max), 14, Ui.Ink, FontWeights.SemiBold); hi.Margin = new Thickness(8, 0, 0, 0); hi.VerticalAlignment = VerticalAlignment.Center; Put(hi, 5);
            Grid.SetRow(row, i); days.Children.Add(row);
        }
    }
}
