# Yayın öncesi doğrulama

15 Ekim 2026 — Video Studio 1.1.1, Windows x64, .NET SDK 10.0.301.

| Kontrol | Sonuç | Kapsam |
| --- | --- | --- |
| Release derlemesi | Geçti | .NET 10 WPF uygulaması başarıyla yayımlandı. |
| `--self-test` | 26 kontrol geçti | Format seçimi, MP3 bit hızı, dikey video, güvenli URL, ilerleme, kalıcılık, yedek kurtarma ve tema kontrastları. |
| `--history-test` | 6 kontrol geçti | Silinmiş kayıtların temizlenmesi, mevcut dosyaların korunması ve yeniden başlatmada geçmişin kalıcılığı. |
| `--access-test` | 14 kontrol geçti | 429, insan doğrulaması ve özel içerik ayrımı; YouTube URL normalizasyonu, bekleme kaydı ve hata arayüzü. |
| `--lifecycle-test` | 4 kontrol geçti | Motor sürümü kontrol edilirken pencereyi kapatma ve hızlı aç/kapat döngüleri. |
| `--integration-test` | 14 kontrol geçti | Gerçek 360p/720p HLS analizi; sesli MP4, 192 kbps MP3, ilerleme, önbellek, yinelenen indirme ve süreç ağacını iptal etme. |
| `--preview` | Geçti | İlk ekran, dört tema, görünüm ve ayarlar ekranları WPF üzerinden PNG olarak üretildi. Galeriye seçilen beş görüntü görsel olarak incelendi. |

Toplam **64 otomatik davranış kontrolü** geçti. Entegrasyon testi yerel sunucu ve gerçek yt-dlp/FFmpeg/ffprobe süreçleriyle çalıştırıldı. Bu çalıştırmada canlı platform indirme testi ve ağ üzerinden motor güncelleme testi yapılmadı; sonuçlar tüm platformlar için kesintisiz erişim garantisi değildir.

Komutlar ve yeniden çalıştırma adımları [README](../README.md#doğrulama) içindedir. Ham test çıktıları kişisel dosya yolları ve üretilmiş medya içerebildiğinden depoya eklenmez.
