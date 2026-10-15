using Microsoft.Win32;
using System.ComponentModel;
using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace VideoStudio;

public partial class MainWindow : Window
{
    readonly DataStore store;
    readonly AppData data;
    readonly DownloadEngine engine;
    readonly CancellationTokenSource windowLifetime = new();
    internal Task VersionRefreshTask { get; private set; } = Task.CompletedTask;
    MediaInfo? media;
    CancellationTokenSource? operation;
    CancellationTokenSource? thumbnailCancellation;
    bool audioOnly, ready, busy, closeAfterCancel;
    string? lastFile;
    string currentPage = "new";

    public MainWindow(DataStore? dataStore = null)
    {
        store = dataStore ?? new DataStore(); data = store.Load();
        engine = new DownloadEngine(sessionDirectory: Path.Combine(store.DirectoryPath, "sessions"));
        ThemeManager.Apply(data.Preferences.Theme);
        InitializeComponent();
        RefreshPreferences(); BuildThemeCards(); RefreshHistory(); ShowPage("new");
        ready = true; UpdateMode(); RefreshEngineStatus();
        DataObject.AddPastingHandler(UrlBox, OnUrlPasted);
        if (store.Warning != null) FooterStatus.Text = store.Warning;
        Closing += WindowClosing;
        Closed += (_, _) => windowLifetime.Cancel();
        Activated += (_, _) => { if (currentPage == "history") RefreshHistory(); };
    }

    void RefreshPreferences()
    {
        FolderLabel.Text = data.Preferences.DownloadFolder; FolderLabel.ToolTip = data.Preferences.DownloadFolder;
        foreach (ComboBoxItem item in AudioQualityBox.Items) if ((string)item.Tag == data.Preferences.AudioQuality) AudioQualityBox.SelectedItem = item;
        CookieLabel.Text = string.IsNullOrEmpty(data.Preferences.CookiesFile) ? "Oturum dosyası kullanılmıyor." : data.Preferences.CookiesFile;
        CookieLabel.ToolTip = CookieLabel.Text; RemoveCookieButton.IsEnabled = !string.IsNullOrEmpty(data.Preferences.CookiesFile) && !busy;
        DataLocationLabel.Text = store.DirectoryPath;
    }
    void Save()
    {
        try { store.Save(data); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { ShowError("Ayarlar kaydedilemedi", e.Message); }
    }
    void RefreshEngineStatus()
    {
        var missing = engine.MissingTools;
        EngineStatus.Text = missing.Length == 0 ? "Motor hazır" : "Motor eksik";
        EngineDot.SetResourceReference(ShapeFill, missing.Length == 0 ? "Mint" : "Danger");
        try
        {
            var versions = Path.Combine(engine.ToolsPath, "versions.json");
            if (File.Exists(versions))
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(versions));
                ToolVersions.Text = string.Join("\n", doc.RootElement.EnumerateArray().Where(a => !a.String("project").StartsWith("yt-dlp/yt-dlp", StringComparison.Ordinal)).Select(a => a.String("project") + "  ·  " + a.String("version")));
            }
            else ToolVersions.Text = "yt-dlp · FFmpeg · ffprobe · Deno";
        }
        catch { ToolVersions.Text = "yt-dlp · FFmpeg · ffprobe · Deno"; }
        if (missing.Length > 0) ToolVersions.Text += "\nEksik: " + string.Join(", ", missing);
        VersionRefreshTask = RefreshEngineVersionAsync();
    }
    async Task RefreshEngineVersionAsync()
    {
        try
        {
            var installed = await engine.GetVersionAsync(windowLifetime.Token).ConfigureAwait(false);
            if (windowLifetime.IsCancellationRequested || Dispatcher.HasShutdownStarted) return;
            await Dispatcher.InvokeAsync(() => { if (!windowLifetime.IsCancellationRequested) ToolVersions.Text = "yt-dlp · " + installed + "\n" + ToolVersions.Text; }, System.Windows.Threading.DispatcherPriority.Normal, windowLifetime.Token).Task.ConfigureAwait(false);
        }
        catch (OperationCanceledException) { }
        catch (Exception e) when (e is IOException or InvalidOperationException or System.ComponentModel.Win32Exception) { }
    }
    static readonly DependencyProperty ShapeFill = System.Windows.Shapes.Shape.FillProperty;

    void Navigate(object sender, RoutedEventArgs e) => ShowPage((string)((Button)sender).Tag);
    public void ShowPage(string page)
    {
        if (page == "history") RefreshHistory();
        currentPage = page;
        NewPage.Visibility = page == "new" ? Visibility.Visible : Visibility.Collapsed;
        HistoryPage.Visibility = page == "history" ? Visibility.Visible : Visibility.Collapsed;
        ThemesPage.Visibility = page == "themes" ? Visibility.Visible : Visibility.Collapsed;
        SettingsPage.Visibility = page == "settings" ? Visibility.Visible : Visibility.Collapsed;
        var buttons = new[] { NewNav, HistoryNav, ThemesNav, SettingsNav };
        foreach (var button in buttons) { if ((string)button.Tag == page) button.SetResourceReference(Button.BackgroundProperty, "Selection"); else button.Background = Brushes.Transparent; }
        PageCrumb.Text = "  /  " + (page switch { "history" => "İndirilenler", "themes" => "Görünüm", "settings" => "Ayarlar", _ => "Yeni indirme" });
    }
    void BuildThemeCards()
    {
        ThemeCards.Children.Clear();
        foreach (var theme in ThemeManager.Themes)
        {
            var content = new StackPanel();
            var swatch = new Border { Height = 90, CornerRadius = new(10), Background = new LinearGradientBrush(ThemeManager.Brush(theme.Top).Color, ThemeManager.Brush(theme.Bottom).Color, 40), Margin = new(0, 0, 0, 15) };
            var sample = new Border { Background = ThemeManager.Brush(theme.Surface), CornerRadius = new(7), Width = 100, Height = 45, BorderBrush = ThemeManager.Brush(theme.Accent), BorderThickness = new(1) };
            sample.Child = new TextBlock { Text = "↓  video studio", Foreground = ThemeManager.Brush(theme.Ink), FontSize = 11, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            swatch.Child = sample; content.Children.Add(swatch);
            content.Children.Add(ThemeManager.Text(theme.Name, 15));
            var description = ThemeManager.Text(theme.Description, 11, "Muted"); description.Margin = new(0, 7, 0, 0); content.Children.Add(description);
            var button = new Button { Content = content, Tag = theme.Id, Padding = new(18), Margin = new(7), HorizontalContentAlignment = HorizontalAlignment.Stretch, BorderThickness = new(theme.Id == data.Preferences.Theme ? 2 : 1) };
            if (theme.Id == data.Preferences.Theme) button.SetResourceReference(Button.BorderBrushProperty, "Accent");
            button.Click += (_, _) => { data.Preferences.Theme = theme.Id; ThemeManager.Apply(theme.Id); Save(); BuildThemeCards(); UpdateMode(); ShowPage(currentPage); };
            ThemeCards.Children.Add(button);
        }
        ActiveThemeLabel.Text = "Seçili görünüm: " + ThemeManager.Current.Name;
    }
    async void Paste(object sender, RoutedEventArgs e)
    {
        try { if (Clipboard.ContainsText()) { UrlBox.Text = Clipboard.GetText().Trim(); UrlBox.CaretIndex = UrlBox.Text.Length; await AnalyzeAsync(); } else FooterStatus.Text = "Panoda bir bağlantı yok."; }
        catch (System.Runtime.InteropServices.COMException) { FooterStatus.Text = "Panoya erişilemedi. Ctrl + V ile yapıştırabilirsin."; }
    }
    void OnUrlPasted(object sender, DataObjectPastingEventArgs e)
    {
        if (!busy) Dispatcher.BeginInvoke(new Action(async () => { if (!busy) await AnalyzeAsync(); }), System.Windows.Threading.DispatcherPriority.Background);
    }
    async void UrlKeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Enter && !busy) { e.Handled = true; await AnalyzeAsync(); } }
    void UrlChanged(object sender, TextChangedEventArgs e)
    {
        if (UrlPlaceholder == null) return;
        UrlPlaceholder.Visibility = string.IsNullOrEmpty(UrlBox.Text) ? Visibility.Visible : Visibility.Collapsed;
        if (!ready || busy) return;
        FailureCard.Visibility = Visibility.Collapsed;
        media = null; thumbnailCancellation?.Cancel(); ThumbnailImage.Source = null; ThumbnailImage.Visibility = Visibility.Collapsed; PreviewPlaceholder.Visibility = Visibility.Visible;
        PlatformLabel.Text = "BAĞLANTI BEKLENİYOR"; DurationBadge.Visibility = Visibility.Collapsed;
        VideoTitle.Text = "Bağlantı incelenmeye hazır"; VideoAuthor.Text = "Bilgileri görmek için Bağlantıyı incele'ye bas."; VideoBadges.Children.Clear(); QualityBox.ItemsSource = null;
        UpdateMode();
    }
    async void Analyze(object sender, RoutedEventArgs e) => await AnalyzeAsync();
    async Task AnalyzeAsync()
    {
        if (busy) return;
        string url;
        try { url = DownloadEngine.ValidateUrl(UrlBox.Text); engine.EnsureTools(); }
        catch (Exception e) { ShowError("Bağlantı kontrolü", e.Message); return; }
        BeginOperation("Video bilgileri alınıyor…", "Platformdan çözünürlükler ve ses bilgisi alınıyor.");
        media = null; UpdateMode(); thumbnailCancellation?.Cancel();
        try
        {
            var info = await engine.AnalyzeAsync(url, data.Preferences.CookiesFile, operation!.Token);
            media = info; PresentMedia();
            TransferTitle.Text = "Video hazır."; TransferDetail.Text = "Biçim ve kaliteyi seçip indirmeyi başlatabilirsin."; TransferBar.IsIndeterminate = false; TransferBar.Value = 100;
            FooterStatus.Text = "Bağlantı incelendi. " + media.Qualities.Count + " çözünürlük bulundu.";
            if (media.Qualities.Count == 0) audioOnly = true;
            else if (!media.HasAudio) audioOnly = false;
            _ = LoadThumbnailAsync(info);
        }
        catch (OperationCanceledException) { TransferTitle.Text = "İnceleme iptal edildi."; TransferDetail.Text = "Yeni bir bağlantı deneyebilirsin."; FooterStatus.Text = "İşlem iptal edildi."; }
        catch (PlatformAccessException e) { ShowPlatformFailure(e); }
        catch (Exception e) { TransferTitle.Text = "Video incelenemedi."; TransferDetail.Text = "Bağlantıyı kontrol edip yeniden dene."; ShowError("Bağlantı incelenemedi", e.Message); }
        finally { EndOperation(); }
    }
    void PresentMedia()
    {
        if (media == null) return;
        VideoTitle.Text = media.Title; VideoTitle.ToolTip = media.Title; VideoAuthor.Text = string.IsNullOrEmpty(media.Author) ? "Kaynak bilgisi sağlanmadı." : media.Author;
        PlatformLabel.Text = media.Platform.ToUpperInvariant(); DurationText.Text = media.DurationLabel; DurationBadge.Visibility = Visibility.Visible;
        ThumbnailImage.Source = null; ThumbnailImage.Visibility = Visibility.Collapsed; PreviewPlaceholder.Visibility = Visibility.Visible;
        VideoBadges.Children.Clear();
        void Badge(string text) { var b = new Border { Style = (Style)FindResource("Chip") }; b.Child = ThemeManager.Text(text, 10); VideoBadges.Children.Add(b); }
        if (media.Qualities.Count > 0) Badge(media.Qualities[0].Label);
        Badge(media.HasAudio ? "Ses mevcut" : "Sessiz video");
        QualityBox.ItemsSource = media.Qualities; QualityBox.SelectedIndex = media.Qualities.Count > 0 ? 0 : -1;
    }
    async Task LoadThumbnailAsync(MediaInfo info)
    {
        thumbnailCancellation?.Cancel(); thumbnailCancellation = new();
        var token = thumbnailCancellation.Token;
        if (!Uri.TryCreate(info.Thumbnail, UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http")) return;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromSeconds(25));
            var bytes = await engine.ReadRemoteAsync(uri, 10 * 1024 * 1024, timeout.Token);
            using var stream = new MemoryStream(bytes);
            var image = new BitmapImage(); image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad; image.DecodePixelWidth = 900; image.StreamSource = stream; image.EndInit(); image.Freeze();
            if (ReferenceEquals(info, media) && !token.IsCancellationRequested) { ThumbnailImage.Source = image; ThumbnailImage.Visibility = Visibility.Visible; PreviewPlaceholder.Visibility = Visibility.Collapsed; }
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or NotSupportedException or IOException or System.IO.FileFormatException or ArgumentException or InvalidOperationException) { }
    }
    void ChooseVideo(object sender, RoutedEventArgs e) { audioOnly = false; UpdateMode(); }
    void ChooseAudio(object sender, RoutedEventArgs e) { audioOnly = true; UpdateMode(); }
    void UpdateMode()
    {
        if (!ready) return;
        VideoModeButton.SetResourceReference(Button.BackgroundProperty, audioOnly ? "Raised" : "Selection");
        AudioModeButton.SetResourceReference(Button.BackgroundProperty, audioOnly ? "Selection" : "Raised");
        VideoModeButton.SetResourceReference(Button.BorderBrushProperty, audioOnly ? "GlassEdge" : "Accent");
        AudioModeButton.SetResourceReference(Button.BorderBrushProperty, audioOnly ? "Accent" : "GlassEdge");
        QualityHeading.Text = audioOnly ? "SES KALİTESİ" : "ÇÖZÜNÜRLÜK";
        QualityBox.Visibility = audioOnly ? Visibility.Collapsed : Visibility.Visible; AudioQualityBox.Visibility = audioOnly ? Visibility.Visible : Visibility.Collapsed;
        QualityHint.Text = audioOnly ? "Yalnızca ses kaydedilir. Kalite kaynak sese bağlıdır." : "Kaliteler kaynak videoya göre listelenir.";
        DownloadButton.Content = audioOnly ? "↓   MP3 olarak indir" : "↓   MP4 olarak indir";
        DownloadButton.IsEnabled = !busy && media != null && (audioOnly ? media.HasAudio : media.Qualities.Count > 0);
        QualityBox.IsEnabled = !busy && media?.Qualities.Count > 0; AudioQualityBox.IsEnabled = !busy;
        VideoModeButton.IsEnabled = !busy && (media == null || media.Qualities.Count > 0); AudioModeButton.IsEnabled = !busy && (media == null || media.HasAudio);
    }
    void AudioQualityChanged(object sender, SelectionChangedEventArgs e) { if (ready && AudioQualityBox.SelectedItem is ComboBoxItem item) { data.Preferences.AudioQuality = (string)item.Tag; Save(); } }
    void SelectFolder(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "İndirme klasörünü seç", Multiselect = false };
        if (Directory.Exists(data.Preferences.DownloadFolder)) dialog.InitialDirectory = data.Preferences.DownloadFolder;
        if (dialog.ShowDialog(this) == true) { data.Preferences.DownloadFolder = dialog.FolderName; RefreshPreferences(); Save(); }
    }
    async void StartDownload(object sender, RoutedEventArgs e)
    {
        if (busy || media == null) return;
        var info = media; var quality = QualityBox.SelectedItem as QualityOption;
        var mode = audioOnly; var bitrate = data.Preferences.AudioQuality;
        try { _ = DownloadEngine.FormatArguments(info, quality, mode, bitrate); }
        catch (Exception ex) { ShowError("Kalite seçimi", ex.Message); return; }
        BeginOperation(mode ? "Ses indiriliyor…" : "Video indiriliyor…", info.Title);
        Save();
        var token = operation!.Token;
        var activeOperation = operation;
        var progress = new Progress<TransferProgress>(p =>
        {
            if (!busy || !ReferenceEquals(operation, activeOperation) || token.IsCancellationRequested) return;
            TransferBar.IsIndeterminate = p.Processing; TransferBar.Value = p.Percent;
            TransferTitle.Text = p.Stage;
            TransferDetail.Text = p.Processing ? "Dosya hazırlanıyor. Bu işlem biraz sürebilir." : $"%{p.Percent:0.0}" + (p.Speed.Length > 0 ? "  ·  " + p.Speed : "") + (p.Eta.Length > 0 ? "  ·  " + p.Eta + " kaldı" : "");
        });
        try
        {
            lastFile = await engine.DownloadAsync(info, quality, mode, bitrate, data.Preferences.DownloadFolder, data.Preferences.CookiesFile, progress, token);
            var format = mode ? "MP3 · " + bitrate + " kbps" : "MP4 · " + quality!.Label;
            data.History.RemoveAll(h => h.FilePath.Equals(lastFile, StringComparison.OrdinalIgnoreCase));
            data.History.Insert(0, new() { Title = info.Title, Url = info.Url, FilePath = lastFile, Format = format });
            if (data.History.Count > 200) data.History.RemoveRange(200, data.History.Count - 200);
            Save(); RefreshHistory();
            TransferTitle.Text = "İndirme tamamlandı."; TransferDetail.Text = Path.GetFileName(lastFile); TransferBar.IsIndeterminate = false; TransferBar.Value = 100; ResultButton.Visibility = Visibility.Visible;
            FooterStatus.Text = "Kaydedildi: " + lastFile;
        }
        catch (OperationCanceledException) { TransferTitle.Text = "İndirme iptal edildi."; TransferDetail.Text = "Aynı ayarlarla yeniden başlatarak mevcut parçalardan devam etmeyi deneyebilirsin."; FooterStatus.Text = "İndirme iptal edildi."; }
        catch (PlatformAccessException ex) { ShowPlatformFailure(ex); }
        catch (Exception ex) { TransferTitle.Text = "İndirme tamamlanamadı."; TransferDetail.Text = "Yeniden deneyebilir veya Ayarlar'dan motoru güncelleyebilirsin."; ShowError("İndirme hatası", ex.Message); }
        finally { EndOperation(); }
    }
    void BeginOperation(string title, string detail)
    {
        busy = true; operation = new();
        FailureCard.Visibility = Visibility.Collapsed;
        UrlBox.IsEnabled = PasteButton.IsEnabled = AnalyzeButton.IsEnabled = FolderButton.IsEnabled = CookieButton.IsEnabled = RemoveCookieButton.IsEnabled = UpdateButton.IsEnabled = false;
        TransferCard.Visibility = Visibility.Visible; ResultButton.Visibility = Visibility.Collapsed; CancelButton.Visibility = Visibility.Visible; CancelButton.IsEnabled = true;
        TransferTitle.Text = title; TransferDetail.Text = detail; TransferBar.IsIndeterminate = true; TransferBar.Value = 0; FooterStatus.Text = title;
        UpdateMode();
    }
    void EndOperation()
    {
        busy = false; operation?.Dispose(); operation = null;
        UrlBox.IsEnabled = PasteButton.IsEnabled = AnalyzeButton.IsEnabled = FolderButton.IsEnabled = CookieButton.IsEnabled = UpdateButton.IsEnabled = true;
        RemoveCookieButton.IsEnabled = !string.IsNullOrEmpty(data.Preferences.CookiesFile);
        CancelButton.Visibility = Visibility.Collapsed; TransferBar.IsIndeterminate = false; UpdateMode();
        if (closeAfterCancel) Close();
    }
    void CancelOperation(object sender, RoutedEventArgs e) { operation?.Cancel(); CancelButton.IsEnabled = false; TransferTitle.Text = "İşlem durduruluyor…"; }
    void SelectCookies(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = "Netscape biçimli cookies.txt dosyanı seç", Filter = "Çerez dosyası (*.txt)|*.txt|Tüm dosyalar (*.*)|*.*" };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            using var reader = File.OpenText(dialog.FileName);
            var header = reader.ReadLine() ?? "";
            if (!header.Contains("Netscape", StringComparison.OrdinalIgnoreCase) && !header.Contains("HTTP Cookie File", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Netscape biçimli bir cookies.txt dosyası seç.");
            data.Preferences.CookiesFile = dialog.FileName; RefreshPreferences(); Save();
        }
        catch (Exception ex) { ShowError("Oturum dosyası", ex.Message); }
    }
    void RemoveCookies(object sender, RoutedEventArgs e) { data.Preferences.CookiesFile = ""; RefreshPreferences(); Save(); }
    async void UpdateEngine(object sender, RoutedEventArgs e)
    {
        if (busy) return;
        BeginOperation("İndirme motoru güncelleniyor…", "Resmi paket indiriliyor ve SHA256 doğrulanıyor.");
        ShowPage("new");
        try
        {
            var version = await engine.UpdateAsync(operation!.Token);
            RefreshEngineStatus();
            TransferTitle.Text = "Motor güncel: " + version; TransferDetail.Text = "İndirmeye hazırsın."; TransferBar.Value = 100; FooterStatus.Text = "Motor güncellendi: " + version;
        }
        catch (OperationCanceledException) { TransferTitle.Text = "Güncelleme iptal edildi."; TransferDetail.Text = "Mevcut motor korundu."; }
        catch (Exception ex) { TransferTitle.Text = "Güncelleme tamamlanamadı."; TransferDetail.Text = "Mevcut motor korundu."; ShowError("Güncelleme hatası", ex.Message); }
        finally { EndOperation(); }
    }
    int RefreshHistory()
    {
        var removed = data.History.RemoveAll(record => !File.Exists(record.FilePath));
        if (removed > 0) Save();
        HistoryList.ItemsSource = null; HistoryList.ItemsSource = data.History;
        EmptyHistory.Visibility = data.History.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        return removed;
    }
    void RefreshHistoryClicked(object sender, RoutedEventArgs e)
    {
        var removed = RefreshHistory();
        FooterStatus.Text = removed > 0 ? $"{removed} silinmiş dosya listeden kaldırıldı. {data.History.Count} indirme mevcut." : $"İndirilenler güncel. {data.History.Count} indirme mevcut.";
    }
    void OpenHistoryFile(object sender, RoutedEventArgs e) { if (((Button)sender).Tag is DownloadRecord record) OpenFile(record.FilePath); }
    void RevealHistoryFile(object sender, RoutedEventArgs e) { if (((Button)sender).Tag is DownloadRecord record) RevealFile(record.FilePath); }
    void ShowLastResult(object sender, RoutedEventArgs e) { if (lastFile != null) RevealFile(lastFile); }
    void OpenFile(string path)
    {
        try { if (!File.Exists(path)) throw new FileNotFoundException("Dosya taşınmış veya silinmiş: " + path); Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
        catch (Exception ex) { ShowError("Dosya açılamadı", ex.Message); }
    }
    void RevealFile(string path)
    {
        try { if (!File.Exists(path)) throw new FileNotFoundException("Dosya taşınmış veya silinmiş: " + path); var start = new ProcessStartInfo("explorer.exe") { UseShellExecute = false }; start.ArgumentList.Add("/select,"); start.ArgumentList.Add(path); Process.Start(start); }
        catch (Exception ex) { ShowError("Klasör açılamadı", ex.Message); }
    }
    void WindowClosing(object? sender, CancelEventArgs e)
    {
        thumbnailCancellation?.Cancel();
        if (busy) { e.Cancel = true; closeAfterCancel = true; operation?.Cancel(); FooterStatus.Text = "İşlem durduruluyor, ardından pencere kapanacak."; }
    }
    public void ShowPlatformFailure(PlatformAccessException error)
    {
        FailureTitle.Text = error.Kind switch { PlatformFailureKind.RateLimited => error.Platform + " isteği sınırlandırdı (429)", PlatformFailureKind.HumanVerification => error.Platform + " doğrulama istiyor", PlatformFailureKind.SignInRequired => "Bu içerik oturum gerektiriyor", _ => "Bağlantı incelenemedi" };
        FailureMessage.Text = error.Message; FailureDetails.Text = error.Details;
        FailureDetailsExpander.Visibility = string.IsNullOrEmpty(error.Details) ? Visibility.Collapsed : Visibility.Visible;
        FailureDetailsExpander.IsExpanded = false; FailureCard.Visibility = Visibility.Visible;
        TransferTitle.Text = FailureTitle.Text; TransferDetail.Text = "Yukarıdaki adımları izleyebilirsin.";
        TransferBar.IsIndeterminate = false; FooterStatus.Text = FailureTitle.Text;
    }
    public void ShowInitialFailure(string url, string output)
    {
        UrlBox.Text = DownloadEngine.ValidateUrl(url); ShowPlatformFailure(PlatformAccessException.FromOutput(output));
    }
    void OpenVideoInBrowser(object sender, RoutedEventArgs e)
    {
        try { OpenWebPage(DownloadEngine.ValidateUrl(UrlBox.Text)); }
        catch (Exception ex) { ShowError("Bağlantı açılamadı", ex.Message); }
    }
    void ConfigureSession(object sender, RoutedEventArgs e) => ShowPage("settings");
    void OpenYouTubeHelp(object sender, RoutedEventArgs e) => OpenWebPage("https://github.com/yt-dlp/yt-dlp/wiki/FAQ#http-error-429-too-many-requests-or-402-payment-required");
    void OpenWebPage(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception ex) { ShowError("Tarayıcı açılamadı", ex.Message); }
    }
    void ShowError(string title, string message) { FooterStatus.Text = title + ": " + message.Split('\n')[0]; if (!closeAfterCancel) MessageBox.Show(this, message, title, MessageBoxButton.OK, MessageBoxImage.Information); }

    public void SetPreviewMedia(MediaInfo info) { media = info; PresentMedia(); UpdateMode(); }
}
