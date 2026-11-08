using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Windows.Media.Control;
using Windows.Storage.Streams;
namespace Island;

/// <summary>
/// Follows the current Windows media session. Transport buttons call the session and never activate the island.
/// Album art is shown only when the session provides a thumbnail.
/// </summary>
internal sealed class NowPlayingPanel : IslandPanel
{
    public override string Id => "nowplaying";
    public override string Title => "Now playing";
    public override string Glyph => "\uE189";
    public override int Order => 8;
    public override Color Accent => Ui.C("#BF5AF2");

    private GlobalSystemMediaTransportControlsSessionManager? manager;
    private GlobalSystemMediaTransportControlsSession? session;
    private string title = "", artist = "", trackKey = "";
    private bool playing, primed;
    private Border? artBox, empty, playBtn;
    private TextBlock? titleText, artistText, playGlyph, timeText;
    private Rectangle? progress;
    private Image? art;

    public override string? ActiveTaskText
    {
        get
        {
            if (!playing) return null;
            string line = title.Length == 0 ? artist : artist.Length == 0 ? title : title + "  ·  " + artist;
            if (line.Length == 0) line = "Now playing";
            return line.Length > 42 ? line[..41] + "…" : line;
        }
    }

    public override string? RestingText => ActiveTaskText;

    public override void Start() => _ = Attach();

    public override void Tick()
    {
        var current = session;
        if (!IsBuilt || current == null) return;
        try { PaintTransport(current.GetPlaybackInfo(), current.GetTimelineProperties()); }
        catch { }
    }

    protected override FrameworkElement Build()
    {
        var root = new Grid { Width = Ui.ContentW, Height = Ui.ContentH };
        var body = new Grid();
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(248) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
        body.ColumnDefinitions.Add(new ColumnDefinition());

        art = new Image { Stretch = Stretch.UniformToFill };
        artBox = new Border { Width = 248, Height = 248, CornerRadius = new CornerRadius(22), Background = Ui.Chip, ClipToBounds = true, Child = art, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
        var note = Ui.Glyph("\uE189", 54, Ui.Faint); note.IsHitTestVisible = false;
        var artHost = new Grid { Width = 248, Height = 248, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
        artHost.Children.Add(artBox); artHost.Children.Add(note);
        body.Children.Add(artHost);

        var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var kicker = Ui.Label("Now playing"); kicker.Foreground = Ui.B(Accent);
        titleText = Ui.Text("Nothing playing", 32, Ui.Ink, FontWeights.SemiBold); titleText.TextWrapping = TextWrapping.NoWrap;
        artistText = Ui.Text(" ", 16, Ui.Soft); artistText.Margin = new Thickness(0, 6, 0, 0);
        var track = new Grid { Height = 4, Margin = new Thickness(0, 22, 0, 0) };
        track.Children.Add(new Border { Background = Ui.Chip, CornerRadius = new CornerRadius(2) });
        progress = new Rectangle { Width = 0, Height = 4, RadiusX = 2, RadiusY = 2, HorizontalAlignment = HorizontalAlignment.Left, Fill = Ui.B(Accent) };
        track.Children.Add(progress);
        timeText = Ui.Text(" ", 12, Ui.Faint); timeText.Margin = new Thickness(0, 8, 0, 0);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 18, 0, 0) };
        buttons.Children.Add(Transport("\uE100", "Previous", () => _ = Call(s => s.TrySkipPreviousAsync())));
        playBtn = Transport("\uE768", "Play", () => _ = TogglePlay()); playBtn.Margin = new Thickness(10, 0, 10, 0); playGlyph = (TextBlock)playBtn.Child;
        buttons.Children.Add(playBtn);
        buttons.Children.Add(Transport("\uE101", "Next", () => _ = Call(s => s.TrySkipNextAsync())));
        stack.Children.Add(kicker); stack.Children.Add(titleText); stack.Children.Add(artistText); stack.Children.Add(track); stack.Children.Add(timeText); stack.Children.Add(buttons);
        Grid.SetColumn(stack, 2); body.Children.Add(stack);

        empty = new Border { Background = Ui.B("#E6000000"), CornerRadius = new CornerRadius(18), Visibility = Visibility.Collapsed };
        var quiet = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        quiet.Children.Add(Ui.Glyph("\uE189", 36, Ui.Faint));
        var msg = Ui.Text("Nothing is playing", 18, Ui.Soft, FontWeights.SemiBold); msg.HorizontalAlignment = HorizontalAlignment.Center; msg.Margin = new Thickness(0, 12, 0, 0);
        var sub = Ui.Text("Start music or a video in any app. Island follows the Windows media session.", 13.5, Ui.Dim); sub.HorizontalAlignment = HorizontalAlignment.Center; sub.Margin = new Thickness(0, 6, 0, 0); sub.TextAlignment = TextAlignment.Center; sub.TextWrapping = TextWrapping.Wrap; sub.MaxWidth = 420;
        quiet.Children.Add(msg); quiet.Children.Add(sub); empty.Child = quiet;

        var host = new Grid(); host.Children.Add(body); host.Children.Add(empty);
        var alert = AlertToggle();
        var card = Ui.Titled("Session", "\uE189", host, Accent, alert, 18);
        root.Children.Add(card);
        Paint();
        return root;
    }

    private Border AlertToggle()
    {
        bool on = Settings.Load().NowPlayingAlerts;
        var label = Ui.Text(on ? "Track alerts on" : "Track alerts off", 12.5, on ? Ui.B(Accent) : Ui.Dim, FontWeights.SemiBold);
        var box = new Border { Height = 30, Padding = new Thickness(12, 0, 12, 0), CornerRadius = new CornerRadius(15), Background = on ? Ui.B(Accent, 0x2A) : Ui.Chip, Cursor = System.Windows.Input.Cursors.Hand, Child = label };
        box.MouseLeftButtonUp += (_, e) =>
        {
            var s = Settings.Load(); s.NowPlayingAlerts = !s.NowPlayingAlerts; s.Save();
            label.Text = s.NowPlayingAlerts ? "Track alerts on" : "Track alerts off";
            label.Foreground = s.NowPlayingAlerts ? Ui.B(Accent) : Ui.Dim;
            box.Background = s.NowPlayingAlerts ? Ui.B(Accent, 0x2A) : Ui.Chip;
            e.Handled = true;
        };
        return box;
    }

    private static Border Transport(string glyph, string tip, Action click)
    {
        var b = Ui.IconButton(glyph, tip, click, 48);
        return b;
    }

    private async Task Attach()
    {
        try
        {
            manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            manager.CurrentSessionChanged += (_, _) => Run(BindSession);
            Run(BindSession);
        }
        catch { Run(Clear); }
    }

    private void BindSession()
    {
        if (session != null)
        {
            session.MediaPropertiesChanged -= OnProperties;
            session.PlaybackInfoChanged -= OnPlayback;
        }
        session = manager?.GetCurrentSession();
        if (session == null) { Clear(); return; }
        session.MediaPropertiesChanged += OnProperties;
        session.PlaybackInfoChanged += OnPlayback;
        try { PaintTransport(session.GetPlaybackInfo(), session.GetTimelineProperties()); } catch { }
        _ = LoadProperties(session);
    }

    private void OnProperties(GlobalSystemMediaTransportControlsSession sender, MediaPropertiesChangedEventArgs args) => _ = LoadProperties(sender);
    private void OnPlayback(GlobalSystemMediaTransportControlsSession sender, PlaybackInfoChangedEventArgs args)
    {
        Run(() => { if (sender != session) return; try { PaintTransport(sender.GetPlaybackInfo(), sender.GetTimelineProperties()); } catch { } });
    }

    private async Task LoadProperties(GlobalSystemMediaTransportControlsSession source)
    {
        try
        {
            var props = await source.TryGetMediaPropertiesAsync();
            if (props == null) return;
            byte[]? artBytes = null;
            try { artBytes = await ReadArt(props.Thumbnail); } catch { artBytes = null; }
            string nextTitle = props.Title ?? "";
            string nextArtist = props.Artist ?? "";
            Run(() =>
            {
                if (source != session) return;
                title = nextTitle.Trim(); artist = nextArtist.Trim();
                string key = title + "\n" + artist;
                if (!primed) { primed = true; trackKey = key; }
                else if (key.Length > 1 && key != trackKey)
                {
                    trackKey = key;
                    if (playing && Settings.Load().NowPlayingAlerts) Hub.Alert(title.Length > 0 ? title : "Now playing", artist, "\uE189", "#BF5AF2");
                }
                ApplyArt(artBytes);
                Paint();
            });
        }
        catch { }
    }

    private static async Task<byte[]?> ReadArt(IRandomAccessStreamReference? reference)
    {
        if (reference == null) return null;
        using var stream = await reference.OpenReadAsync();
        ulong raw = stream.Size;
        if (raw == 0 || raw > 8 * 1024 * 1024) return null;
        uint size = (uint)raw;
        using var reader = new DataReader(stream.GetInputStreamAt(0));
        await reader.LoadAsync(size);
        var bytes = new byte[size];
        reader.ReadBytes(bytes);
        return bytes;
    }

    private void ApplyArt(byte[]? bytes)
    {
        if (art == null) return;
        if (bytes == null || bytes.Length == 0) { art.Source = null; return; }
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = new MemoryStream(bytes);
            image.EndInit();
            image.Freeze();
            art.Source = image;
        }
        catch { art.Source = null; }
    }

    private void PaintTransport(GlobalSystemMediaTransportControlsSessionPlaybackInfo info, GlobalSystemMediaTransportControlsSessionTimelineProperties timeline)
    {
        playing = info.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
        if (playGlyph != null) playGlyph.Text = playing ? "\uE769" : "\uE768";
        if (playBtn != null) playBtn.ToolTip = playing ? "Pause" : "Play";
        Paint();
        if (!IsBuilt || progress == null || timeText == null) return;
        var length = timeline.EndTime - timeline.StartTime;
        var position = timeline.Position - timeline.StartTime;
        if (length <= TimeSpan.Zero || position < TimeSpan.Zero) { progress.Width = 0; timeText.Text = ""; return; }
        double frac = Math.Clamp(position.TotalSeconds / length.TotalSeconds, 0, 1);
        progress.Width = 560 * frac;
        timeText.Text = Clock(position) + "   ·   " + Clock(length);
    }

    private void Paint()
    {
        bool live = session != null && (title.Length > 0 || artist.Length > 0 || playing);
        if (empty != null) empty.Visibility = live ? Visibility.Collapsed : Visibility.Visible;
        if (titleText != null) titleText.Text = title.Length > 0 ? title : live ? "Now playing" : "Nothing playing";
        if (artistText != null) artistText.Text = artist.Length > 0 ? artist : " ";
    }

    private void Clear()
    {
        session = null; playing = false; title = ""; artist = "";
        if (art != null) art.Source = null;
        if (progress != null) progress.Width = 0;
        if (timeText != null) timeText.Text = "";
        if (playGlyph != null) playGlyph.Text = "\uE768";
        Paint();
    }

    private async Task TogglePlay()
    {
        var current = session; if (current == null) return;
        try
        {
            if (playing) await current.TryPauseAsync();
            else await current.TryPlayAsync();
        }
        catch { }
    }

    private async Task Call(Func<GlobalSystemMediaTransportControlsSession, global::Windows.Foundation.IAsyncOperation<bool>> action)
    {
        var current = session; if (current == null) return;
        try { await action(current); } catch { }
    }

    private static void Run(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null || dispatcher.CheckAccess()) action();
        else dispatcher.BeginInvoke(action);
    }

    private static string Clock(TimeSpan value)
    {
        if (value < TimeSpan.Zero) value = TimeSpan.Zero;
        return value.TotalHours >= 1 ? value.ToString(@"h\:mm\:ss") : value.ToString(@"m\:ss");
    }
}
