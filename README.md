# Tarayıcı

Az RAM kullanan hafif tarayıcı. 8 GB RAM'li bilgisayarlar için.

## Windows (ana sürüm)
1. [Releases](../../releases) sayfasından **Tarayici-Windows.zip** dosyasını indir (yaklaşık 0,4 MB).
2. ZIP'i bir klasöre **tamamen çıkar** (WinRAR'ın içinden açma).
3. **Tarayici.exe** dosyasına çift tıkla.

Windows'un kendi Edge motorunu (WebView2) kullanır. Windows 11'de hazır gelir; yoksa uygulama indirme adresini gösterir.

**RAM'i nasıl azaltır?** Aynı anda yalnızca bir sayfa canlıdır, diğer sekmeler sadece adres/başlık tutar. Reklam ve izleyiciler engellenir, resimler isteğe bağlı kapatılır. Giriş bilgileri ve favoriler `~/.tarayici` klasöründe saklanır, sekmeler kapanıp açılınca geri gelir.

**Kısayollar:** Ctrl+T yeni sekme · Ctrl+W kapat · Ctrl+L adres çubuğu · F5 yenile

**İpucu:** İşlemci yüksekse YouTube'da dişli (⚙) menüsünden kaliteyi 720p yap. Ekran kartı seçimi için: Ayarlar → Sistem → Ekran → Grafikler bölümünde Tarayici.exe'yi "Güç tasarrufu" yapmak Optimus'lu dizüstülerde işe yarar.

## Android
Releases sayfasından **Tarayici.apk** indirip telefona kur (bilinmeyen kaynak izni gerekir). Kaynak: `android/`.

## Yedek: Python sürümü
`tarayici.py` (Qt WebEngine). Kurulum: `pip install -r requirements.txt`, sonra `python tarayici.py` veya `Tarayici.bat`. Elle derlenen exe: Actions → "Windows exe (Python yedek, elle)".

## Klasörler
- `windows-edge/` — ana Windows sürümü (C#, WebView2)
- `android/` — Android uygulaması
- `tarayici.py` — Python yedek sürümü
