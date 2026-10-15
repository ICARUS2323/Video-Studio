using System.Diagnostics;
using System.Globalization;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace VideoStudio;

public sealed class DownloadEngine
{
    public string ToolsPath { get; }
    readonly string sessionPath;
    readonly YouTubeRequestPolicy requestPolicy;
    readonly Dictionary<string, (MediaInfo Info, string Json, string Session, DateTimeOffset Created)> metadataCache = new();
    string? cachedEngineVersion;
    public string YtDlp => Path.Combine(ToolsPath, "yt-dlp.exe");
    public DownloadEngine(string? tools = null, string? sessionDirectory = null)
    {
        ToolsPath = tools ?? Path.Combine(AppContext.BaseDirectory, "tools");
        sessionPath = sessionDirectory ?? Path.Combine(AppContext.BaseDirectory, "data", "sessions");
        requestPolicy = new YouTubeRequestPolicy(Path.Combine(Path.GetDirectoryName(sessionPath)!, "youtube-request-state.txt"));
    }
    public string[] MissingTools => new[] { "yt-dlp.exe", "ffmpeg.exe", "ffprobe.exe", "deno.exe" }.Where(f => !File.Exists(Path.Combine(ToolsPath, f))).ToArray();
    public void EnsureTools() { if (MissingTools.Length > 0) throw new InvalidOperationException("İndirme motoru eksik: " + string.Join(", ", MissingTools) + ". Uygulama klasörünün tamamını kullan."); }

    public static string ValidateUrl(string input)
    {
        var value = input.Trim();
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || (uri.Scheme != "https" && uri.Scheme != "http") || string.IsNullOrEmpty(uri.Host) || !string.IsNullOrEmpty(uri.UserInfo))
            throw new ArgumentException("Geçerli bir video bağlantısı yapıştır (https:// ile başlamalı).");
        if (YouTubeRequestPolicy.IsYouTube(uri.AbsoluteUri))
        {
            string? videoId = null;
            var parts = uri.AbsolutePath.Trim('/').Split('/');
            if (uri.Host.EndsWith("youtu.be", StringComparison.OrdinalIgnoreCase)) videoId = parts.FirstOrDefault();
            else if (parts.Length > 1 && parts[0] is "shorts" or "embed" or "live") videoId = parts[1];
            else if (parts[0] == "watch") videoId = uri.Query.TrimStart('?').Split('&').Select(p => p.Split('=', 2)).FirstOrDefault(p => p.Length == 2 && p[0] == "v")?.ElementAt(1);
            if (videoId?.Length == 11 && videoId.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_')) return "https://www.youtube.com/watch?v=" + videoId;
        }
        return uri.AbsoluteUri;
    }

    List<string> BaseArguments(string url)
    {
        var args = new List<string> { "--ignore-config", "--no-plugin-dirs", "--no-cache-dir", "--no-playlist", "--no-colors", "--encoding", "utf-8", "--socket-timeout", "20", "--retries", "2", "--extractor-retries", "2", "--ffmpeg-location", ToolsPath, "--js-runtimes", "deno:" + Path.Combine(ToolsPath, "deno.exe") };
        if (YouTubeRequestPolicy.IsYouTube(url)) args.AddRange(["--sleep-requests", "1", "--sleep-interval", "5", "--max-sleep-interval", "10", "--retries", "0", "--extractor-retries", "0", "--fragment-retries", "0"]);
        return args;
    }
    static string SessionIdentity(string? file) => string.IsNullOrWhiteSpace(file) ? "" : Path.GetFullPath(file) + "|" + File.GetLastWriteTimeUtc(file).Ticks;
    bool TryCached(string url, string? cookieFile, out (MediaInfo Info, string Json, string Session, DateTimeOffset Created) cached)
    {
        if (metadataCache.TryGetValue(url, out cached) && DateTimeOffset.UtcNow - cached.Created < TimeSpan.FromMinutes(2) && cached.Session == SessionIdentity(cookieFile)) return true;
        metadataCache.Remove(url); cached = default; return false;
    }
    PlatformAccessException Failure(string url, string raw)
    {
        var failure = PlatformAccessException.FromOutput(raw, YouTubeRequestPolicy.IsYouTube(url) ? "YouTube" : "Platform"); requestPolicy.RecordFailure(url, failure.Kind); metadataCache.Remove(url); return failure;
    }

    async Task<string?> UseCookiesAsync(List<string> args, string? cookieFile, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(cookieFile)) return null;
        if (!File.Exists(cookieFile)) throw new InvalidOperationException("Seçilen cookies.txt dosyası bulunamıyor. Ayarlar'dan dosyayı yeniden seç veya kaldır.");
        Directory.CreateDirectory(sessionPath);
        var copy = Path.Combine(sessionPath, Guid.NewGuid() + ".txt");
        try { await using var source = File.OpenRead(cookieFile); await using var target = File.Create(copy); await source.CopyToAsync(target, token); }
        catch { TryDelete(copy); throw; }
        args.AddRange(["--cookies", copy]);
        return copy;
    }

    public async Task<MediaInfo> AnalyzeAsync(string url, string? cookieFile, CancellationToken token)
    {
        EnsureTools(); url = ValidateUrl(url);
        token.ThrowIfCancellationRequested(); requestPolicy.EnsureAllowed(url);
        if (TryCached(url, cookieFile, out var cached)) return cached.Info;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromMinutes(2));
        var args = BaseArguments(url); args.AddRange(["--skip-download", "--dump-single-json"]);
        string? cookieCopy = null;
        try
        {
            cookieCopy = await UseCookiesAsync(args, cookieFile, timeout.Token);
            await requestPolicy.WaitForTurnAsync(url, timeout.Token);
            args.AddRange(["--", url]);
            var result = await ProcessRunner.RunAsync(YtDlp, args, timeout.Token);
            if (result.ExitCode != 0) throw Failure(url, result.Error);
            var info = MediaInfo.Parse(result.Output, url);
            if (metadataCache.Count >= 8) metadataCache.Clear();
            metadataCache[url] = (info, result.Output, SessionIdentity(cookieFile), DateTimeOffset.UtcNow);
            return info;
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { throw new TimeoutException("Bağlantı analizi zaman aşımına uğradı. Bağlantıyı ve internetini kontrol edip yeniden dene."); }
        finally { TryDelete(cookieCopy); }
    }

    public static List<string> FormatArguments(MediaInfo media, QualityOption? quality, bool audioOnly, string bitrate)
    {
        if (audioOnly)
        {
            if (!media.HasAudio) throw new InvalidOperationException("Bu videoda ses akışı bulunmuyor.");
            if (!new[] { "128", "192", "256", "320" }.Contains(bitrate)) throw new ArgumentException("Geçersiz ses kalitesi.");
            return ["-f", "bestaudio/best", "-x", "--audio-format", "mp3", "--audio-quality", bitrate + "K"];
        }
        if (quality == null || !media.Qualities.Contains(quality)) throw new ArgumentException("Videodaki çözünürlüklerden birini seç.");
        if (quality.FormatId.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('-' or '_' or '.'))) throw new InvalidOperationException("Platformun format kimliği desteklenmiyor.");
        var selector = quality.HasAudio || !media.HasAudio ? quality.FormatId : $"{quality.FormatId}+bestaudio[ext=m4a]/{quality.FormatId}+bestaudio";
        return ["-f", selector, "--merge-output-format", "mp4", "--remux-video", "mp4"];
    }

    public async Task<string> DownloadAsync(MediaInfo media, QualityOption? quality, bool audioOnly, string bitrate, string folder, string? cookies, IProgress<TransferProgress>? progress, CancellationToken token)
    {
        EnsureTools();
        requestPolicy.EnsureAllowed(media.Url);
        if (!Path.IsPathFullyQualified(folder)) throw new ArgumentException("Geçerli bir kayıt klasörü seç.");
        Directory.CreateDirectory(folder);
        var probe = Path.Combine(folder, ".videostudio-" + Guid.NewGuid() + ".tmp");
        await File.WriteAllTextAsync(probe, "", token); File.Delete(probe);
        var args = BaseArguments(media.Url); args.AddRange(FormatArguments(media, quality, audioOnly, bitrate));
        var suffix = audioOnly ? "mp3-" + bitrate : quality!.Resolution + "p";
        args.AddRange(["--newline", "--progress", "--progress-delta", "0.3", "--progress-template", "download:__PROGRESS__%(progress)j", "--print", "after_move:__FILE__%(filepath)j", "--no-simulate", "--windows-filenames", "--trim-filenames", "190", "--no-overwrites", "--continue", "--paths", folder, "-o", $"%(title).120B [%(id)s] [{suffix}].%(ext)s"]);
        string? resultPath = null;
        void Line(string line)
        {
            if (line.StartsWith("__PROGRESS__")) { var p = ParseProgress(line[12..]); if (p != null) progress?.Report(p); }
            else if (line.StartsWith("__FILE__")) { try { resultPath = JsonSerializer.Deserialize<string>(line[8..]); } catch (JsonException) { } }
            else if (line.StartsWith("[Merger]") || line.StartsWith("[ExtractAudio]") || line.StartsWith("[VideoRemuxer]") || line.StartsWith("[Fixup"))
                progress?.Report(new(100, "", "", true, audioOnly ? "MP3 hazırlanıyor…" : "Video ve ses birleştiriliyor…"));
        }
        string? cookieCopy = null;
        string? infoCopy = null;
        try
        {
            cookieCopy = await UseCookiesAsync(args, cookies, token);
            if (TryCached(media.Url, cookies, out var snapshot))
            {
                Directory.CreateDirectory(sessionPath); infoCopy = Path.Combine(sessionPath, Guid.NewGuid() + ".info.json");
                await File.WriteAllTextAsync(infoCopy, snapshot.Json, token);
                args.AddRange(["--load-info-json", infoCopy]);
            }
            else { await requestPolicy.WaitForTurnAsync(media.Url, token); args.AddRange(["--", media.Url]); }
            var result = await ProcessRunner.RunAsync(YtDlp, args, token, Line);
            if (result.ExitCode != 0) throw Failure(media.Url, result.Error + "\n" + result.Output);
            if (string.IsNullOrWhiteSpace(resultPath) || !File.Exists(resultPath)) throw new InvalidOperationException("İndirme tamamlandı ancak çıktı dosyası bulunamadı.");
            var extension = audioOnly ? ".mp3" : ".mp4";
            if (!Path.GetExtension(resultPath).Equals(extension, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Dosya istenen biçime dönüştürülemedi.");
            return Path.GetFullPath(resultPath);
        }
        finally { TryDelete(cookieCopy); TryDelete(infoCopy); }
    }

    public static TransferProgress? ParseProgress(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json); var r = doc.RootElement;
            var total = r.Number("total_bytes"); if (total <= 0) total = r.Number("total_bytes_estimate");
            var percent = total > 0 ? Math.Clamp(100 * r.Number("downloaded_bytes") / total, 0, 100) : 0;
            var speed = r.Number("speed"); var eta = r.Number("eta");
            return new(percent, speed > 0 ? FormatBytes(speed) + "/sn" : "", eta > 0 ? TimeSpan.FromSeconds(Math.Min(eta, 86400)).ToString(@"m\:ss") : "", r.String("status") == "finished", r.String("status") == "finished" ? "Dosya işleniyor…" : "İndiriliyor…");
        }
        catch (JsonException) { return null; }
    }
    public static string FormatBytes(double bytes) => bytes >= 1073741824 ? $"{bytes / 1073741824:0.0} GB" : bytes >= 1048576 ? $"{bytes / 1048576:0.0} MB" : $"{bytes / 1024:0.0} KB";
    public static string FriendlyError(string raw)
    {
        return PlatformAccessException.FromOutput(raw).Message;
    }
    static string Tail(string raw) => string.Join("\n", raw.Split('\n', StringSplitOptions.RemoveEmptyEntries).Where(s => !s.StartsWith("__PROGRESS__") && !s.StartsWith("__FILE__")).TakeLast(7)).Trim();
    static void TryDelete(string? path) { if (path != null) try { File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { } }

    public async Task<string> UpdateAsync(CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromMinutes(4));
        using var doc = JsonDocument.Parse(await ReadRemoteAsync(new Uri("https://api.github.com/repos/yt-dlp/yt-dlp-nightly-builds/releases/latest"), 2 * 1024 * 1024, timeout.Token));
        var root = doc.RootElement;
        var asset = root.GetProperty("assets").EnumerateArray().First(a => a.String("name") == "yt-dlp.exe");
        var uri = new Uri(asset.String("browser_download_url"));
        if (uri.Scheme != "https" || uri.Host != "github.com" || !uri.AbsolutePath.StartsWith("/yt-dlp/yt-dlp-nightly-builds/releases/download/", StringComparison.Ordinal)) throw new InvalidOperationException("Güncelleme adresi doğrulanamadı.");
        var expected = asset.String("digest"); if (!expected.StartsWith("sha256:") || expected.Length != 71) throw new InvalidOperationException("Güncellemenin SHA256 bilgisi eksik.");
        var bytes = await ReadRemoteAsync(uri, 64 * 1024 * 1024, timeout.Token);
        if (!Convert.ToHexString(SHA256.HashData(bytes)).Equals(expected[7..], StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Güncelleme doğrulanamadı. Mevcut motor korundu.");
        Directory.CreateDirectory(ToolsPath);
        var temp = YtDlp + ".new";
        try { await File.WriteAllBytesAsync(temp, bytes, token); token.ThrowIfCancellationRequested(); File.Move(temp, YtDlp, true); }
        finally { TryDelete(temp); }
        cachedEngineVersion = null; metadataCache.Clear();
        return root.String("tag_name");
    }
    public async Task<string> GetVersionAsync(CancellationToken token = default)
    {
        if (cachedEngineVersion != null) return cachedEngineVersion;
        if (!File.Exists(YtDlp)) return "eksik";
        var result = await ProcessRunner.RunAsync(YtDlp, ["--version"], token).ConfigureAwait(false);
        return cachedEngineVersion = result.ExitCode == 0 ? result.Output.Trim() : "bilinmiyor";
    }
    public Task<byte[]> ReadRemoteAsync(Uri uri, int maximumBytes, CancellationToken token) => NetworkFetch.GetAsync(uri, maximumBytes, Path.Combine(ToolsPath, "deno.exe"), sessionPath, token);
}

public sealed record ProcessResult(int ExitCode, string Output, string Error);

public static class ProcessRunner
{
    public static async Task<ProcessResult> RunAsync(string executable, IEnumerable<string> arguments, CancellationToken token, Action<string>? onLine = null)
    {
        token.ThrowIfCancellationRequested();
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8 };
        foreach (var arg in arguments) start.ArgumentList.Add(arg);
        using var process = new Process { StartInfo = start };
        process.Start();
        using var registration = token.Register(() => { try { if (!process.HasExited) process.Kill(true); } catch (InvalidOperationException) { } catch (System.ComponentModel.Win32Exception) { } });
        async Task<string> Read(StreamReader reader)
        {
            var output = new StringBuilder();
            while (await reader.ReadLineAsync().ConfigureAwait(false) is { } line)
            {
                if (output.Length < 20_000_000) output.AppendLine(line);
                onLine?.Invoke(line);
            }
            return output.ToString();
        }
        var stdout = Read(process.StandardOutput); var stderr = Read(process.StandardError);
        await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
        var outText = await stdout.ConfigureAwait(false); var errText = await stderr.ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        return new(process.ExitCode, outText, errText);
    }
}
