# Video Studio

Windows 10/11 için video ve ses indirici. `VideoStudio.exe` dosyasını açın.

1. YouTube, Instagram, X veya desteklenen başka bir platformdaki video bağlantısını kopyalayın.
2. **Yapıştır** düğmesine basın veya Ctrl + V kullanın; bağlantı otomatik incelenir. Elle yazarsanız **Bağlantıyı incele** düğmesine veya Enter'a basın.
3. **MP4 video** veya **MP3 ses** seçin. Videonun sunduğu çözünürlükler listelenir; 4K ancak kaynakta varsa görünür. MP3 için 128, 192, 256 veya 320 kbps seçilebilir. Yüksek bit hızı kaynak sesin kalitesini yükseltmez.
4. Kayıt klasörünü seçip **İndirmeyi başlat** düğmesine basın. İptal edilen indirme daha sonra aynı klasörde aynı ayarlarla yeniden başlatılırsa mevcut parçalardan devam etmeyi dener.

İndirilenler sayfasında dosyaları ve klasörlerini açabilirsiniz. Dosyayı bilgisayardan sildikten sonra **Yenile** düğmesine basarsanız kayıt listeden kalkar. Liste, İndirilenler sayfasını açınca ve bu sayfadayken uygulamaya geri dönünce de otomatik güncellenir. Görünüm sayfasındaki dört tema Pad Studio renkleriyle eşleşir ve seçim otomatik kaydedilir.

Program tek bağlantıdaki tek videoyu indirir; çalma listelerini topluca indirmez. MP4 videoya, kaynakta varsa ses de eklenir. MP3 seçildiğinde yalnızca ses kaydedilir. Canlı yayın ve DRM korumalı içerik desteklenmez. Bazı platformlar giriş gerektirebilir; bu durumda Ayarlar'dan kendi dışa aktardığınız Netscape biçimli cookies.txt dosyasını seçebilirsiniz. Bu dosya oturum bilgisi içerdiğinden paylaşmayın. Tarayıcı oturumları kendiliğinden okunmaz.

Program, tools klasöründeki yt-dlp, FFmpeg, ffprobe ve Deno ile çalışır. Başka bilgisayara taşırken **VideoStudio klasörünün tamamını** kopyalayın. Bu sürüm .NET 10 Desktop Runtime (x64) gerektirir. Ayarlar ve geçmiş varsayılan olarak uygulamanın yanındaki `data` klasöründe tutulur; buraya yazılamazsa Windows kullanıcı profilindeki VideoStudio klasörüne kaydedilir. Kullanılacak gerçek klasör Ayarlar'da görünür. Kaydetme hataları arayüzde bildirilir.

Platform değişikliklerinden sonra indirme hatası olursa Ayarlar'daki **Motoru güncelle** düğmesini kullanın. Yalnızca resmi yt-dlp nightly sürümü indirilir ve yayımlanan SHA256 değeriyle doğrulanır. Gerçek kurulu sürüm Ayarlar'da gösterilir. Diğer motorları geliştirme klasöründeki `node tools/prepare-tools.mjs` komutuyla yenileyebilirsiniz.

YouTube **429** veya **bot doğrulaması** gösterirse uygulamadaki açıklamayı ve **Videoyu tarayıcıda aç** düğmesini kullanın. 429 sonrasında uygulama iki dakika yeniden YouTube isteği göndermez; sınırın ne zaman kalkacağını YouTube belirler. Ayrıntılar: [YOUTUBE-YARDIM.md](YOUTUBE-YARDIM.md).

Geliştirme: .NET 10 SDK ve Node.js ile `tools/build.ps1 -PrepareTools` çalıştırın. Doğrulama için `VideoStudio.exe --self-test <çıktı-klasörü>`, `--integration-test <çıktı-klasörü>` ve tema görüntüleri için `--preview <çıktı-klasörü>` kullanılabilir.
