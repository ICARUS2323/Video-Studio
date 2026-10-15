# YouTube 429 / bot doğrulaması

429, YouTube'un o anda bu internet bağlantısından gelen isteklere sınır koyduğunu gösterir. Aynı hata herkese açık bir videoda da oluşabilir. Motoru güncellemek bu sınırı tek başına kaldırmayabilir.

1. Uygulamadaki **Videoyu tarayıcıda aç** düğmesine basın. Videoyu kendi tarayıcınızda açın ve YouTube doğrulama isterse tamamlayın.
2. Art arda tekrar denemek yerine bir süre bekleyin. Uygulama 429'dan sonra iki dakika yeni YouTube isteği göndermez; bu yerel bekleme süresi YouTube'un sınırının kalkma süresi değildir. Diğer platformlar kullanılabilir.
3. YouTube giriş/oturum doğrulaması istemeye devam ederse kendi YouTube oturumunuza ait güncel Netscape biçimli **cookies.txt** dosyasını **Ayarlar > Platform oturumu > cookies.txt seç** ile ekleyin. Dosya oturum bilgisi içerir; paylaşmayın. Uygulama tarayıcıdaki oturumları kendiliğinden okumaz. Dosyayı seçmek erişimin açılacağını garanti etmez.

Oturum dosyasının nasıl dışa aktarılacağını resmi rehberden inceleyebilirsiniz:

- [yt-dlp: HTTP Error 429](https://github.com/yt-dlp/yt-dlp/wiki/FAQ#http-error-429-too-many-requests-or-402-payment-required)
- [yt-dlp: YouTube çerezlerini dışa aktarma](https://github.com/yt-dlp/yt-dlp/wiki/Extractors#exporting-youtube-cookies)

**Motoru güncelle** düğmesi resmi nightly kanalını kullanır. yt-dlp projesi, eski stable sürümde sorun yaşayan kullanıcılara bu kanalı önerir. Paket SHA256 ile doğrulanır; Ayarlar'da gerçek kurulu motor sürümü gösterilir.

Uygulama, iki dakika içinde aynı bağlantıyı yeniden incelediğinizde bilgiyi bellekten kullanır. İndirmeye hemen geçildiğinde de analiz bilgisi kullanılır; böylece video sayfasına gereksiz ikinci istek gönderilmez. Geçici analiz dosyası işlem bitince silinir. Oturum dosyası veya motor değiştiğinde analiz bilgisi yeniden alınır.
