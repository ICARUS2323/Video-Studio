using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace VideoStudio;

public sealed class Preferences
{
    public string Theme { get; set; } = "liquid-gray";
    public string DownloadFolder { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "Video Studio");
    public string AudioQuality { get; set; } = "320";
    public string CookiesFile { get; set; } = "";
}

public sealed class DownloadRecord
{
    public string Title { get; set; } = "";
    public string Url { get; set; } = "";
    public string FilePath { get; set; } = "";
    public string Format { get; set; } = "";
    public DateTimeOffset CompletedAt { get; set; } = DateTimeOffset.Now;
    public string Detail => $"{Format}  ·  {CompletedAt.ToLocalTime():dd.MM.yyyy HH:mm}";
}

public sealed class AppData
{
    public Preferences Preferences { get; set; } = new();
    public List<DownloadRecord> History { get; set; } = new();
}

public sealed class DataStore
{
    public string DirectoryPath { get; }
    public string? Warning { get; private set; }
    static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public DataStore(string? directory = null)
    {
        var portable = directory ?? Path.Combine(AppContext.BaseDirectory, "data");
        try
        {
            Directory.CreateDirectory(portable);
            var probe = Path.Combine(portable, Guid.NewGuid() + ".tmp");
            File.WriteAllText(probe, "");
            File.Delete(probe);
            DirectoryPath = portable;
        }
        catch when (directory == null)
        {
            DirectoryPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VideoStudio");
            Directory.CreateDirectory(DirectoryPath);
        }
    }

    public AppData Load()
    {
        var file = Path.Combine(DirectoryPath, "settings.json");
        if (!File.Exists(file)) return new();
        try { return Validate(JsonSerializer.Deserialize<AppData>(File.ReadAllText(file)) ?? throw new JsonException()); }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException or ArgumentException)
        {
            Warning = "Kayıt dosyası okunamadı. Yedek kayıt denendi.";
            try { return Validate(JsonSerializer.Deserialize<AppData>(File.ReadAllText(file + ".bak")) ?? throw new JsonException()); }
            catch { Warning = "Ayarlar okunamadı; varsayılanlar açıldı. Mevcut kayıt dosyası korunuyor."; return new(); }
        }
    }

    static AppData Validate(AppData data)
    {
        data.Preferences ??= new(); data.History ??= new();
        data.Preferences.Theme = ThemeManager.Normalize(data.Preferences.Theme);
        if (string.IsNullOrWhiteSpace(data.Preferences.DownloadFolder) || !Path.IsPathFullyQualified(data.Preferences.DownloadFolder))
            data.Preferences.DownloadFolder = new Preferences().DownloadFolder;
        if (!new[] { "128", "192", "256", "320" }.Contains(data.Preferences.AudioQuality)) data.Preferences.AudioQuality = "320";
        data.Preferences.CookiesFile ??= "";
        data.History = data.History.Where(h => h != null && !string.IsNullOrEmpty(h.FilePath)).Take(200).ToList();
        return data;
    }

    public void Save(AppData data)
    {
        var file = Path.Combine(DirectoryPath, "settings.json");
        var tmp = file + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(data, Json));
        // Never replace a known-good backup with an invalid primary file recovered by Load.
        if (File.Exists(file) && Warning == null) File.Copy(file, file + ".bak", true);
        File.Move(tmp, file, true);
        Warning = null;
    }
}

public sealed record QualityOption(int Resolution, string FormatId, bool HasAudio, double Fps, long? Bytes, string Codec)
{
    public string Label => (Resolution switch { 2160 => "4K · 2160p", 1440 => "2K · 1440p", 1080 => "Full HD · 1080p", 720 => "HD · 720p", _ => $"{Resolution}p" }) + (Fps >= 49 ? $" · {Fps:0} fps" : "");
    public override string ToString() => Label;
}

public sealed record MediaInfo(string Id, string Title, string Author, string Platform, string Url, string? Thumbnail, double Duration, bool HasAudio, IReadOnlyList<QualityOption> Qualities)
{
    public string DurationLabel => Duration <= 0 ? "Süre bilinmiyor" : TimeSpan.FromSeconds(Duration).ToString(Duration >= 3600 ? @"h\:mm\:ss" : @"m\:ss");

    public static MediaInfo Parse(string json, string originalUrl)
    {
        using var document = JsonDocument.Parse(json);
        var r = document.RootElement;
        if (r.String("_type") is "playlist" or "multi_video") throw new InvalidOperationException("Tek bir videonun bağlantısını yapıştır. Bu bağlantı birden fazla içerik içeriyor.");
        if (r.True("is_live") || r.String("live_status") is "is_live" or "is_upcoming") throw new InvalidOperationException("Canlı yayın yerine tamamlanmış bir video seç.");
        var formats = new List<JsonElement>();
        if (r.TryGetProperty("formats", out var list) && list.ValueKind == JsonValueKind.Array) formats.AddRange(list.EnumerateArray());
        if (formats.Count == 0 && r.String("url") != "") formats.Add(r);
        var usable = formats.Where(f => !f.True("has_drm") && f.String("vcodec") != "none" && (f.Number("height") > 0 || f.Number("width") > 0)).ToList();
        bool audio = formats.Any(f => !f.True("has_drm") && f.String("acodec") is not ("" or "none"));
        var choices = usable.GroupBy(Resolution).Where(g => g.Key > 0).OrderByDescending(g => g.Key)
            .Select(g => g.OrderByDescending(Compatibility).ThenByDescending(f => f.Number("fps")).ThenByDescending(f => f.String("protocol") is "https" or "http" ? 1 : 0).ThenByDescending(f => f.Number("tbr")).First())
            .Select(f => new QualityOption(Resolution(f), f.String("format_id") is { Length: > 0 } id ? id : "best", f.String("acodec") is not ("" or "none"), f.Number("fps"), f.Long("filesize") ?? f.Long("filesize_approx"), f.String("vcodec"))).ToArray();
        if (choices.Length == 0 && !audio) throw new InvalidOperationException("Bu bağlantıda indirilebilir bir video veya ses bulunamadı.");
        return new(r.String("id"), r.String("title") is { Length: > 0 } title ? title : "İsimsiz video", r.String("uploader") is { Length: > 0 } author ? author : r.String("channel"),
            r.String("extractor_key") is { Length: > 0 } platform ? platform : new Uri(originalUrl).Host,
            originalUrl, r.String("thumbnail"), r.Number("duration"), audio, choices);
    }

    static int Resolution(JsonElement f)
    {
        var h = f.Number("height"); var w = f.Number("width");
        return (int)(h > 0 && w > 0 ? Math.Min(h, w) : Math.Max(h, w));
    }
    static int Compatibility(JsonElement f) => (f.String("ext") == "mp4" ? 4 : 0) + (f.String("vcodec").StartsWith("avc", StringComparison.OrdinalIgnoreCase) || f.String("vcodec").StartsWith("h264", StringComparison.OrdinalIgnoreCase) ? 2 : 0);
}

static class JsonExtensions
{
    public static string String(this JsonElement e, string key) => e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
    public static double Number(this JsonElement e, string key) => e.TryGetProperty(key, out var v) && v.TryNumber(out var n) ? n : 0;
    public static long? Long(this JsonElement e, string key) => e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var n) ? n : null;
    public static bool True(this JsonElement e, string key) => e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.True;
    static bool TryNumber(this JsonElement v, out double n) { n = 0; return v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out n); }
}

public sealed record TransferProgress(double Percent, string Speed, string Eta, bool Processing, string Stage);
