using System.Text.Json;

namespace VideoStudio;

public enum PlatformFailureKind { RateLimited, HumanVerification, SignInRequired, Unsupported, Other }

public sealed class PlatformAccessException(PlatformFailureKind kind, string message, string details = "", string platform = "Platform") : InvalidOperationException(message)
{
    public PlatformFailureKind Kind { get; } = kind;
    public string Details { get; } = details;
    public string Platform { get; } = platform;
    public static PlatformFailureKind Classify(string raw)
    {
        bool Has(string value) => raw.Contains(value, StringComparison.OrdinalIgnoreCase);
        if (Has("HTTP Error 429") || Has("HTTP 429") || Has("Too Many Requests")) return PlatformFailureKind.RateLimited;
        if (Has("not a bot") || Has("confirm you’re") || Has("confirm you're") || Has("captcha")) return PlatformFailureKind.HumanVerification;
        if (Has("private video") || Has("login required") || Has("sign in") || Has("log in") || Has("authentication required") || Has("age-restricted")) return PlatformFailureKind.SignInRequired;
        if (Has("Unsupported URL")) return PlatformFailureKind.Unsupported;
        return PlatformFailureKind.Other;
    }
    public static PlatformAccessException FromOutput(string raw, string? platformName = null)
    {
        var kind = Classify(raw);
        var platform = platformName ?? (raw.Contains("[youtube]", StringComparison.OrdinalIgnoreCase) ? "YouTube" : "Platform");
        var message = kind switch
        {
            PlatformFailureKind.RateLimited => $"{platform} çok fazla istek nedeniyle bu bağlantıya geçici sınır koydu (429). Videoyu tarayıcıda açıp varsa doğrulamayı tamamla. Art arda denemek yerine bir süre bekle; sınırın kalkma süresini {platform} belirler. Gerekirse Ayarlar'dan kendi güncel cookies.txt dosyanı seç.",
            PlatformFailureKind.HumanVerification => $"{platform} insan doğrulaması istiyor. Videoyu kendi tarayıcında açıp varsa doğrulamayı tamamla. Ardından kendi oturumuna ait güncel cookies.txt dosyanı seçip tekrar deneyebilirsin.",
            PlatformFailureKind.SignInRequired => "Bu içerik giriş veya hesap erişimi gerektiriyor. Videoya erişebildiğin kendi oturumuna ait güncel cookies.txt dosyasını Ayarlar'dan seçebilirsin.",
            PlatformFailureKind.Unsupported => "Bu bağlantı desteklenmiyor. Profil veya ana sayfa yerine doğrudan video bağlantısını yapıştır.",
            _ => "İşlem tamamlanamadı. Bağlantıyı ve internetini kontrol et. Sorun devam ederse Ayarlar'dan indirme motorunu güncelle."
        };
        var details = string.Join("\n", raw.Split('\n', StringSplitOptions.RemoveEmptyEntries).Where(s => !s.StartsWith("__PROGRESS__") && !s.StartsWith("__FILE__")).TakeLast(8)).Trim();
        return new(kind, message, details, platform);
    }
}

public sealed class YouTubeRequestPolicy
{
    readonly string stateFile;
    readonly Func<DateTimeOffset> now;
    DateTimeOffset blockedUntil;
    DateTimeOffset nextRequest;
    public YouTubeRequestPolicy(string stateFile, Func<DateTimeOffset>? clock = null)
    {
        this.stateFile = stateFile; now = clock ?? (() => DateTimeOffset.UtcNow);
        try
        {
            if (DateTimeOffset.TryParse(File.ReadAllText(stateFile), out var saved) && saved > now() && saved < now().AddHours(1)) blockedUntil = saved;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }
    public static bool IsYouTube(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;
        var host = uri.Host.ToLowerInvariant();
        return host is "youtube.com" or "www.youtube.com" or "m.youtube.com" or "music.youtube.com" or "youtu.be" or "www.youtu.be" or "youtube-nocookie.com" or "www.youtube-nocookie.com";
    }
    public void EnsureAllowed(string url)
    {
        if (!IsYouTube(url) || blockedUntil <= now()) return;
        var seconds = Math.Max(1, (int)Math.Ceiling((blockedUntil - now()).TotalSeconds));
        throw new PlatformAccessException(PlatformFailureKind.RateLimited, $"Az önce YouTube'dan 429 yanıtı alındı. Uygulama tekrar istek göndermeden {seconds} saniye bekliyor. Bu süre YouTube sınırının kalkacağını garanti etmez. Videoyu tarayıcıda açıp varsa doğrulamayı tamamlayabilirsin.", platform: "YouTube");
    }
    public async Task WaitForTurnAsync(string url, CancellationToken token)
    {
        EnsureAllowed(url);
        if (!IsYouTube(url)) return;
        var delay = nextRequest - now();
        if (delay > TimeSpan.Zero) await Task.Delay(delay, token);
        nextRequest = now().AddSeconds(5);
    }
    public void RecordFailure(string url, PlatformFailureKind kind)
    {
        if (!IsYouTube(url) || kind != PlatformFailureKind.RateLimited) return;
        blockedUntil = now().AddMinutes(2);
        try { Directory.CreateDirectory(Path.GetDirectoryName(stateFile)!); File.WriteAllText(stateFile, blockedUntil.ToString("O")); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }
}
