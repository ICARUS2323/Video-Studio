using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace VideoStudio;

public static class Validation
{
    const string FixtureJson = """
    {"id":"fixture","title":"Bir video, birçok olasılık.","uploader":"Video Studio","extractor_key":"YouTube","duration":203,"formats":[
    {"format_id":"low","height":360,"width":640,"vcodec":"avc1","acodec":"mp4a","ext":"mp4"},
    {"format_id":"hd","height":720,"width":1280,"vcodec":"avc1","acodec":"none","ext":"mp4"},
    {"format_id":"full","height":1080,"width":1920,"vcodec":"avc1","acodec":"none","ext":"mp4","fps":60},
    {"format_id":"ultra","height":2160,"width":3840,"vcodec":"vp9","acodec":"none","ext":"webm"},
    {"format_id":"audio","vcodec":"none","acodec":"mp4a","ext":"m4a"},
    {"format_id":"protected","height":4320,"width":7680,"vcodec":"avc1","acodec":"none","has_drm":true,"ext":"mp4"}
    ]}
    """;

    public static async Task<int> RunAsync(string command, string output, string? url = null)
    {
        var lines = new List<string>();
        try
        {
            if (command == "--self-test") SelfTest(output, lines);
            else if (command == "--integration-test") await IntegrationTest(output, lines);
            else if (command == "--online-test") await OnlineTest(output, lines, url ?? throw new ArgumentException("URL required"));
            else if (command == "--network-test") await NetworkTest(output, lines);
            else if (command == "--history-test") await HistoryTest(output, lines);
            else if (command == "--access-test") await AccessTest(output, lines);
            else if (command == "--lifecycle-test") await LifecycleTest(output, lines);
            else await Previews(output, lines);
            lines.Add("PASS: " + command);
            await File.WriteAllLinesAsync(Path.Combine(output, "result.txt"), lines); return 0;
        }
        catch (Exception ex)
        {
            lines.Add("FAIL: " + ex);
            await File.WriteAllLinesAsync(Path.Combine(output, "result.txt"), lines); return 1;
        }
    }
    static void Assert(bool condition, string label, List<string> lines) { if (!condition) throw new Exception(label); lines.Add("PASS: " + label); }
    static void Throws(Action action, string label, List<string> lines)
    {
        try { action(); } catch (Exception e) when (e is ArgumentException or InvalidOperationException) { lines.Add("PASS: " + label); return; }
        throw new Exception(label);
    }
    static void SelfTest(string output, List<string> lines)
    {
        var info = MediaInfo.Parse(FixtureJson, "https://youtube.com/watch?v=fixture");
        Assert(info.Qualities.Select(q => q.Resolution).SequenceEqual(new[] { 2160, 1080, 720, 360 }), "Only available resolutions, highest first, DRM excluded", lines);
        Assert(info.HasAudio && info.DurationLabel == "3:23", "Audio presence and duration", lines);
        var args = DownloadEngine.FormatArguments(info, info.Qualities[0], false, "320");
        Assert(args[1] == "ultra+bestaudio[ext=m4a]/ultra+bestaudio" && args.Contains("mp4"), "4K preserved with separate audio and MP4 output", lines);
        Assert(DownloadEngine.FormatArguments(info, info.Qualities.Last(), false, "320")[1] == "low", "Combined video/audio format downloaded once", lines);
        var mp3 = DownloadEngine.FormatArguments(info, null, true, "192");
        Assert(mp3.Contains("-x") && mp3.Contains("mp3") && mp3.Contains("192K"), "MP3 extraction uses selected bitrate", lines);
        var portrait = MediaInfo.Parse("""{"title":"Portrait","formats":[{"format_id":"p","width":1080,"height":1920,"vcodec":"h264","acodec":"aac","ext":"mp4"}]}""", "https://instagram.com/reel/example");
        Assert(portrait.Qualities[0].Resolution == 1080, "Portrait video quality uses short edge", lines);
        Throws(() => DownloadEngine.FormatArguments(info with { HasAudio = false }, null, true, "320"), "Silent video cannot create fake MP3", lines);
        Throws(() => DownloadEngine.FormatArguments(info, new(8000, "invented", false, 30, null, ""), false, "320"), "Absent quality rejected", lines);
        Throws(() => DownloadEngine.FormatArguments(info, null, true, "999"), "Invalid bitrate rejected", lines);
        Throws(() => MediaInfo.Parse("""{"is_live":true}""", "https://youtube.com/watch?v=live"), "Live stream rejected", lines);
        Throws(() => MediaInfo.Parse("""{"_type":"playlist"}""", "https://youtube.com/playlist?list=x"), "Playlist rejected", lines);
        foreach (var invalid in new[] { "", "file:///C:/Windows/test", "--exec calc", "javascript:alert(1)", "https://u:p@youtube.com/v" }) Throws(() => DownloadEngine.ValidateUrl(invalid), "Unsafe URL rejected: " + invalid, lines);
        Assert(DownloadEngine.ValidateUrl(" https://x.com/example/status/1 ") == "https://x.com/example/status/1", "X URL accepted", lines);
        var progress = DownloadEngine.ParseProgress("""{"status":"downloading","downloaded_bytes":500,"total_bytes":1000,"speed":1048576,"eta":62}""");
        Assert(progress?.Percent == 50 && progress.Speed.Contains("MB") && progress.Eta == "1:02", "Structured progress decoded", lines);
        Assert(DownloadEngine.ParseProgress("bad") == null, "Malformed progress ignored", lines);
        var store = new DataStore(Path.Combine(output, "persistence"));
        var data = new AppData(); data.Preferences.Theme = "aurora"; data.Preferences.AudioQuality = "192";
        data.History.Add(new() { Title = "Türkçe: ğüşiöç", FilePath = Path.Combine(output, "örnek.mp4"), Format = "MP4 · 4K" });
        store.Save(data); data.Preferences.Theme = "porcelain"; store.Save(data);
        var loaded = new DataStore(store.DirectoryPath).Load();
        Assert(loaded.Preferences.Theme == "porcelain" && loaded.History[0].Title == "Türkçe: ğüşiöç" && loaded.Preferences.AudioQuality == "192", "Preferences and history survive restart with Turkish paths", lines);
        File.WriteAllText(Path.Combine(store.DirectoryPath, "settings.json"), "broken");
        var recoveredStore = new DataStore(store.DirectoryPath); var recovered = recoveredStore.Load();
        Assert(recovered.Preferences.Theme == "aurora" && recoveredStore.Warning != null, "Invalid primary recovered from good backup", lines);
        recoveredStore.Save(recovered);
        Assert(JsonDocument.Parse(File.ReadAllText(Path.Combine(store.DirectoryPath, "settings.json.bak"))).RootElement.GetProperty("Preferences").String("Theme") == "aurora", "Recovery preserves valid backup", lines);
        foreach (var theme in ThemeManager.Themes)
        {
            ThemeManager.Apply(theme.Id);
            Assert(Contrast(ThemeManager.Brush(theme.Ink).Color, ThemeManager.Brush(theme.Top).Color) >= 4.5, "Readable foreground contrast: " + theme.Name, lines);
        }
    }
    static double Contrast(Color a, Color b)
    {
        double Lum(Color c) { double F(byte v) { var n = v / 255.0; return n <= .04045 ? n / 12.92 : Math.Pow((n + .055) / 1.055, 2.4); } return .2126 * F(c.R) + .7152 * F(c.G) + .0722 * F(c.B); }
        var la = Lum(a); var lb = Lum(b); return (Math.Max(la, lb) + .05) / (Math.Min(la, lb) + .05);
    }

    static async Task IntegrationTest(string output, List<string> lines)
    {
        var engine = new DownloadEngine(sessionDirectory: Path.Combine(output, "sessions")); engine.EnsureTools();
        var fixture = Path.Combine(output, "fixture"); Directory.CreateDirectory(fixture);
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        foreach (var resolution in new[] { 360, 720 })
        {
            var width = resolution == 360 ? 640 : 1280;
            var result = await ProcessRunner.RunAsync(Path.Combine(engine.ToolsPath, "ffmpeg.exe"), ["-hide_banner", "-loglevel", "error", "-y", "-f", "lavfi", "-i", $"testsrc2=size={width}x{resolution}:rate=15", "-f", "lavfi", "-i", "sine=frequency=440:sample_rate=44100", "-t", "2.5", "-c:v", "libx264", "-preset", "ultrafast", "-g", "15", "-c:a", "aac", "-b:a", "128k", "-f", "hls", "-hls_time", "1", "-hls_list_size", "0", Path.Combine(fixture, $"q{resolution}.m3u8")], timeout.Token);
            Assert(result.ExitCode == 0, $"Generated real {resolution}p HLS video with audio", lines);
        }
        await File.WriteAllTextAsync(Path.Combine(fixture, "master.m3u8"), "#EXTM3U\n#EXT-X-STREAM-INF:BANDWIDTH=800000,RESOLUTION=640x360,CODECS=\"avc1.42c01e,mp4a.40.2\"\nq360.m3u8\n#EXT-X-STREAM-INF:BANDWIDTH=2000000,RESOLUTION=1280x720,CODECS=\"avc1.42c01f,mp4a.40.2\"\nq720.m3u8\n");
        await using var server = new FixtureServer(fixture);
        var media = await engine.AnalyzeAsync(server.BaseUrl + "master.m3u8", null, timeout.Token);
        var metadataRequests = server.MasterRequests;
        var reused = await engine.AnalyzeAsync(server.BaseUrl + "master.m3u8", null, timeout.Token);
        Assert(ReferenceEquals(media, reused) && server.MasterRequests == metadataRequests, "Repeated analysis uses cache without another webpage request", lines);
        Assert(media.Qualities.Select(q => q.Resolution).SequenceEqual(new[] { 720, 360 }), "Actual yt-dlp analysis discovers only 720p and 360p", lines);
        Assert(media.HasAudio, "Actual analysis detects audio", lines);
        var destination = Path.Combine(output, "Türkçe indirmeler");
        var progressReports = new List<TransferProgress>();
        var progress = new SynchronousProgress<TransferProgress>(p => { lock (progressReports) progressReports.Add(p); });
        var mp4 = await engine.DownloadAsync(media, media.Qualities.Last(), false, "320", destination, null, progress, timeout.Token);
        Assert(server.MasterRequests == metadataRequests, "Download reuses analyzed metadata instead of fetching the webpage again", lines);
        var probe = await ProbeAsync(engine, mp4, timeout.Token);
        using (var doc = JsonDocument.Parse(probe.Output))
        {
            var streams = doc.RootElement.GetProperty("streams").EnumerateArray().ToArray();
            Assert(streams.Any(s => s.String("codec_type") == "video" && s.Number("height") == 360) && streams.Any(s => s.String("codec_type") == "audio"), "MP4 is really 360p and contains audible audio stream", lines);
        }
        var mp3 = await engine.DownloadAsync(media, null, true, "192", destination, null, progress, timeout.Token);
        var audioProbe = await ProbeAsync(engine, mp3, timeout.Token);
        using (var doc = JsonDocument.Parse(audioProbe.Output))
        {
            var streams = doc.RootElement.GetProperty("streams").EnumerateArray().ToArray();
            Assert(streams.Length == 1 && streams[0].String("codec_type") == "audio" && streams[0].String("codec_name") == "mp3", "MP3 contains only MP3 audio, no video", lines);
            var rate = doc.RootElement.GetProperty("format").String("bit_rate");
            Assert(long.TryParse(rate, out var n) && n >= 185000 && n < 220000, "MP3 bitrate matches 192 kbps selection", lines);
        }
        var again = await engine.DownloadAsync(media, media.Qualities.Last(), false, "320", destination, null, null, timeout.Token);
        Assert(again == mp4 && Directory.GetFiles(destination, "*.mp4").Length == 1, "Duplicate download reuses complete file without overwrite", lines);
        Assert(progressReports.Any(p => p.Percent > 0), "Real transfer reports machine-readable progress", lines);
        Assert(!Directory.GetFiles(Path.Combine(output, "sessions"), "*.info.json").Any(), "Signed metadata temporary files are removed after transfers", lines);
        using (var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(800)))
        {
            var stopwatch = Stopwatch.StartNew(); bool cancelled = false;
            try { await ProcessRunner.RunAsync(Path.Combine(engine.ToolsPath, "ffmpeg.exe"), ["-hide_banner", "-loglevel", "error", "-re", "-f", "lavfi", "-i", "sine=frequency=440", "-t", "120", "-f", "null", "-"], cancel.Token); }
            catch (OperationCanceledException) { cancelled = true; }
            Assert(cancelled && stopwatch.Elapsed < TimeSpan.FromSeconds(8), "Cancellation terminates active encoder promptly", lines);
        }
        // Validate killing a real child encoder too, as yt-dlp starts FFmpeg as a child.
        var pidFile = Path.Combine(output, "child-pid.txt");
        using (var cancelTree = new CancellationTokenSource(TimeSpan.FromSeconds(3)))
        {
            bool cancelled = false;
            var script = "const p=new Deno.Command(Deno.args[0],{args:['-hide_banner','-loglevel','error','-re','-f','lavfi','-i','sine=frequency=440','-t','120','-f','null','-'],stdout:'null',stderr:'null'}).spawn();await Deno.writeTextFile(Deno.args[1],String(p.pid));await p.status;";
            try { await ProcessRunner.RunAsync(Path.Combine(engine.ToolsPath, "deno.exe"), ["eval", script, Path.Combine(engine.ToolsPath, "ffmpeg.exe"), pidFile], cancelTree.Token); }
            catch (OperationCanceledException) { cancelled = true; }
            var childPid = int.Parse(await File.ReadAllTextAsync(pidFile));
            bool alive; try { using var child = Process.GetProcessById(childPid); alive = !child.HasExited; } catch (ArgumentException) { alive = false; }
            Assert(cancelled && !alive, "Cancellation also kills child FFmpeg process", lines);
        }
        lines.Add("MP4: " + mp4); lines.Add("MP3: " + mp3);
    }
    static Task<ProcessResult> ProbeAsync(DownloadEngine engine, string path, CancellationToken token) => ProcessRunner.RunAsync(Path.Combine(engine.ToolsPath, "ffprobe.exe"), ["-v", "quiet", "-show_streams", "-show_format", "-of", "json", path], token);

    static async Task OnlineTest(string output, List<string> lines, string url)
    {
        var engine = new DownloadEngine(sessionDirectory: Path.Combine(output, "sessions"));
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(4));
        var info = await engine.AnalyzeAsync(url, null, timeout.Token);
        lines.Add("Analyzed: " + info.Title + " · " + info.Platform);
        lines.Add("Available: " + string.Join(", ", info.Qualities.Select(q => q.Label + " (" + q.FormatId + ")")));
        await File.WriteAllLinesAsync(Path.Combine(output, "progress.txt"), lines);
        Assert(info.HasAudio && info.Qualities.Count > 0, "Live platform returns usable video and audio", lines);
        var quality = info.Qualities.Last();
        var mp4 = await engine.DownloadAsync(info, quality, false, "128", output, null, null, timeout.Token);
        using (var probe = JsonDocument.Parse((await ProbeAsync(engine, mp4, timeout.Token)).Output))
        {
            var streams = probe.RootElement.GetProperty("streams").EnumerateArray().ToArray();
            Assert(streams.Any(s => s.String("codec_type") == "video" && Math.Min(s.Number("height"), s.Number("width")) == quality.Resolution) && streams.Any(s => s.String("codec_type") == "audio"), "Real online MP4 preserves selected resolution and includes audio", lines);
        }
        await File.WriteAllLinesAsync(Path.Combine(output, "progress.txt"), lines);
        var mp3 = await engine.DownloadAsync(info, null, true, "128", output, null, null, timeout.Token);
        using (var probe = JsonDocument.Parse((await ProbeAsync(engine, mp3, timeout.Token)).Output))
        {
            var streams = probe.RootElement.GetProperty("streams").EnumerateArray().ToArray();
            Assert(streams.Length == 1 && streams[0].String("codec_name") == "mp3", "Real online MP3 contains only audio", lines);
        }
        lines.Add("MP4: " + mp4); lines.Add("MP3: " + mp3);
    }
    static async Task NetworkTest(string output, List<string> lines)
    {
        var engine = new DownloadEngine(sessionDirectory: Path.Combine(output, "sessions"));
        using var token = new CancellationTokenSource(TimeSpan.FromMinutes(4));
        var thumbnail = await engine.ReadRemoteAsync(new Uri("https://i.ytimg.com/vi/jNQXAC9IVRw/hqdefault.jpg"), 10 * 1024 * 1024, token.Token);
        using var imageStream = new MemoryStream(thumbnail);
        var image = new BitmapImage(); image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad; image.StreamSource = imageStream; image.EndInit();
        Assert(image.PixelWidth > 100, "Real YouTube thumbnail downloaded securely and decoded by WPF", lines);
        await File.WriteAllBytesAsync(Path.Combine(output, "thumbnail.jpg"), thumbnail, token.Token);
        var version = await engine.UpdateAsync(token.Token);
        Assert(version.Length > 0 && new FileInfo(engine.YtDlp).Length > 1_000_000, "Engine update downloaded from official release and SHA256 verified: " + version, lines);
        Assert(!Directory.GetFiles(Path.Combine(output, "sessions")).Any(), "Network fallback removes all temporary download files", lines);
    }
    sealed class SynchronousProgress<T>(Action<T> report) : IProgress<T> { public void Report(T value) => report(value); }

    static async Task LifecycleTest(string output, List<string> lines)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var window = new MainWindow(new DataStore(Path.Combine(output, "profile-" + attempt))) { ShowInTaskbar = false, Left = -20000, Top = -20000, WindowStartupLocation = WindowStartupLocation.Manual };
            window.Show(); window.Close();
            await window.VersionRefreshTask.WaitAsync(TimeSpan.FromSeconds(10));
            Assert(window.VersionRefreshTask.IsCompletedSuccessfully, "Closing during engine version check cancels cleanly: " + (attempt + 1), lines);
        }
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        Assert(true, "Dispatcher remains usable after rapid open/close cycles", lines);
    }

    static async Task AccessTest(string output, List<string> lines)
    {
        const string raw = "WARNING: [youtube] HqxlJ3QEOUE: Unable to download webpage: HTTP Error 429: Too Many Requests\nWARNING: Unable to fetch GVS PO Token: Missing required Visitor Data\nERROR: [youtube] HqxlJ3QEOUE: Sign in to confirm you’re not a bot. Use --cookies-from-browser or --cookies for the authentication.";
        Assert(PlatformAccessException.Classify(raw) == PlatformFailureKind.RateLimited, "429 takes priority over bot/cookies warnings in the reported failure", lines);
        Assert(PlatformAccessException.Classify("Sign in to confirm you're not a bot") == PlatformFailureKind.HumanVerification, "Human verification distinguished from private content", lines);
        Assert(PlatformAccessException.Classify("Private video. Sign in") == PlatformFailureKind.SignInRequired, "Private video remains an authentication error", lines);
        Assert(PlatformAccessException.Classify("WARNING: cookies might help\nERROR: connection reset") == PlatformFailureKind.Other, "Generic cookies hint does not misclassify a network error", lines);
        var canonical = DownloadEngine.ValidateUrl("https://youtu.be/HqxlJ3QEOUE?list=RDHqxlJ3QEOUE");
        Assert(canonical == "https://www.youtube.com/watch?v=HqxlJ3QEOUE", "User's YouTube radio URL normalized to one video", lines);
        Assert(DownloadEngine.ValidateUrl("https://www.youtube.com/shorts/HqxlJ3QEOUE?si=test") == canonical, "Shorts URL normalized consistently", lines);
        Assert(!YouTubeRequestPolicy.IsYouTube("https://youtube.com.evil.example/watch?v=HqxlJ3QEOUE"), "Lookalike domains do not receive YouTube-specific handling", lines);
        var clock = DateTimeOffset.UtcNow;
        var state = Path.Combine(output, "policy", "youtube-request-state.txt");
        var policy = new YouTubeRequestPolicy(state, () => clock);
        policy.RecordFailure(canonical, PlatformFailureKind.RateLimited);
        Throws(() => policy.EnsureAllowed(canonical), "Immediate retry blocked locally after 429", lines);
        new YouTubeRequestPolicy(state, () => clock).EnsureAllowed("https://x.com/example/status/1");
        Assert(true, "YouTube cooldown leaves other platforms available", lines);
        Throws(() => new YouTubeRequestPolicy(state, () => clock).EnsureAllowed(canonical), "Cooldown survives application restart", lines);
        clock = clock.AddMinutes(3); policy.EnsureAllowed(canonical);
        Assert(true, "Local cooldown expires without promising server-side unblock", lines);
        new YouTubeRequestPolicy(Path.Combine(output, "youtube-request-state.txt")).RecordFailure(canonical, PlatformFailureKind.RateLimited);
        var engine = new DownloadEngine(sessionDirectory: Path.Combine(output, "sessions"));
        var stopwatch = Stopwatch.StartNew(); bool blocked = false;
        try { await engine.AnalyzeAsync(canonical, null, CancellationToken.None); }
        catch (PlatformAccessException e) when (e.Kind == PlatformFailureKind.RateLimited) { blocked = true; }
        Assert(blocked && stopwatch.Elapsed < TimeSpan.FromSeconds(1), "Engine rejects immediate retry before starting a network subprocess", lines);
        var window = new MainWindow(new DataStore(Path.Combine(output, "profile"))) { ShowInTaskbar = false, Left = -20000, Top = -20000, WindowStartupLocation = WindowStartupLocation.Manual };
        try
        {
            window.ShowInitialFailure(canonical, raw); window.Show();
            await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            Assert(((FrameworkElement)window.FindName("FailureCard")).Visibility == Visibility.Visible && ((TextBlock)window.FindName("FailureTitle")).Text.Contains("429"), "Actionable 429 panel shown without a modal error dialog", lines);
            Assert(!((Button)window.FindName("DownloadButton")).IsEnabled, "Blocked analysis does not enable a fake download", lines);
            CaptureWindow(window, Path.Combine(output, "youtube-429.png"));
        }
        finally { window.Close(); }
    }

    static async Task HistoryTest(string output, List<string> lines)
    {
        var store = new DataStore(Path.Combine(output, "profile"));
        var fixture = Path.Combine(output, "fixtures"); Directory.CreateDirectory(fixture);
        var video = Path.Combine(fixture, "örnek video.mp4"); var audio = Path.Combine(fixture, "örnek ses.mp3");
        await File.WriteAllTextAsync(video, "video fixture"); await File.WriteAllTextAsync(audio, "audio fixture");
        var data = new AppData(); data.Preferences.Theme = "aurora"; data.Preferences.AudioQuality = "192";
        data.History.AddRange([
            new() { Title = "Mevcut video", FilePath = video, Format = "MP4 · 1080p" },
            new() { Title = "Mevcut ses", FilePath = audio, Format = "MP3 · 192 kbps" },
            new() { Title = "Önceden silinmiş video", FilePath = Path.Combine(fixture, "missing.mp4"), Format = "MP4" }
        ]);
        store.Save(data);
        var window = new MainWindow(store) { ShowInTaskbar = false, Left = -20000, Top = -20000, WindowStartupLocation = WindowStartupLocation.Manual };
        try
        {
            var list = (ItemsControl)window.FindName("HistoryList");
            var refresh = (Button)window.FindName("RefreshHistoryButton");
            var empty = (FrameworkElement)window.FindName("EmptyHistory");
            Assert(list.Items.Count == 2, "Startup removes already deleted files and keeps existing video/audio", lines);
            window.Show(); window.ShowPage("history");
            await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            File.Delete(video);
            refresh.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert(list.Items.Count == 1 && ((DownloadRecord)list.Items[0]).FilePath == audio, "Yenile button removes deleted MP4 and retains existing MP3", lines);
            Assert(await File.ReadAllTextAsync(audio) == "audio fixture", "Refreshing history leaves existing media untouched", lines);
            var reloaded = new DataStore(store.DirectoryPath).Load();
            Assert(reloaded.History.Count == 1 && reloaded.History[0].FilePath == audio && reloaded.Preferences.Theme == "aurora" && reloaded.Preferences.AudioQuality == "192", "Cleaned history survives restart without changing preferences", lines);
            CaptureWindow(window, Path.Combine(output, "history-refreshed.png"));
            window.ShowPage("new"); File.Delete(audio); window.ShowPage("history");
            Assert(list.Items.Count == 0 && empty.Visibility == Visibility.Visible && refresh.IsEnabled, "Opening history automatically removes deleted MP3 and shows empty state", lines);
            refresh.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert(list.Items.Count == 0 && new DataStore(store.DirectoryPath).Load().History.Count == 0, "Repeated refresh is safe and empty history stays saved", lines);
            CaptureWindow(window, Path.Combine(output, "history-empty.png"));
        }
        finally { window.Close(); }
    }

    static void CaptureWindow(Window window, string path)
    {
        window.UpdateLayout();
        var visual = (FrameworkElement)window.Content;
        var image = new RenderTargetBitmap((int)visual.ActualWidth, (int)visual.ActualHeight, 96, 96, PixelFormats.Pbgra32); image.Render(visual);
        var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(image));
        using var file = File.Create(path); png.Save(file);
    }

    static async Task Previews(string output, List<string> lines)
    {
        var window = new MainWindow(new DataStore(Path.Combine(output, "profile"))) { ShowInTaskbar = false, Left = -20000, Top = -20000, WindowStartupLocation = WindowStartupLocation.Manual };
        window.Show();
        await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        void Capture(string filename)
        {
            CaptureWindow(window, Path.Combine(output, filename));
            lines.Add("Rendered: " + filename);
        }
        Capture("empty-state.png");
        var info = MediaInfo.Parse(FixtureJson, "https://www.youtube.com/watch?v=fixture");
        window.SetPreviewMedia(info);
        foreach (var theme in ThemeManager.Themes)
        {
            ThemeManager.Apply(theme.Id); await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle); Capture("theme-" + theme.Id + ".png");
        }
        ThemeManager.Apply("liquid-gray"); window.ShowPage("themes"); await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle); Capture("themes.png");
        window.ShowPage("settings"); await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle); Capture("settings.png");
        window.Close();
    }
}

sealed class FixtureServer : IAsyncDisposable
{
    readonly TcpListener listener = new(IPAddress.Loopback, 0);
    readonly CancellationTokenSource stop = new();
    readonly string folder;
    readonly Task serving;
    int masterRequests;
    public int MasterRequests => Volatile.Read(ref masterRequests);
    public string BaseUrl { get; }
    public FixtureServer(string folder)
    {
        this.folder = folder; listener.Start(); BaseUrl = "http://127.0.0.1:" + ((IPEndPoint)listener.LocalEndpoint).Port + "/";
        serving = Serve();
    }
    async Task Serve()
    {
        while (!stop.IsCancellationRequested)
        {
            try { var client = await listener.AcceptTcpClientAsync(stop.Token); _ = Respond(client); }
            catch (OperationCanceledException) { break; } catch (SocketException) when (stop.IsCancellationRequested) { break; }
        }
    }
    async Task Respond(TcpClient client)
    {
        using (client)
        try
        {
            await using var stream = client.GetStream();
            using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
            var first = await reader.ReadLineAsync(stop.Token); if (first == null) return;
            while (await reader.ReadLineAsync(stop.Token) is { Length: > 0 }) { }
            var parts = first.Split(' '); var name = Path.GetFileName(Uri.UnescapeDataString(parts[1].Split('?')[0]));
            if (name == "master.m3u8" && parts[0] != "HEAD") Interlocked.Increment(ref masterRequests);
            var file = Path.Combine(folder, name);
            var bytes = File.Exists(file) ? await File.ReadAllBytesAsync(file, stop.Token) : Encoding.UTF8.GetBytes("Missing");
            var type = name.EndsWith(".m3u8") ? "application/vnd.apple.mpegurl" : name.EndsWith(".ts") ? "video/mp2t" : "video/mp4";
            var header = Encoding.ASCII.GetBytes($"HTTP/1.1 {(File.Exists(file) ? "200 OK" : "404 Not Found")}\r\nContent-Type: {type}\r\nContent-Length: {bytes.Length}\r\nConnection: close\r\n\r\n");
            await stream.WriteAsync(header, stop.Token); if (parts[0] != "HEAD") await stream.WriteAsync(bytes, stop.Token);
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException or SocketException) { }
    }
    public async ValueTask DisposeAsync() { stop.Cancel(); listener.Stop(); await serving; stop.Dispose(); }
}
