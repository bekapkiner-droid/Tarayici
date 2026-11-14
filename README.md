# Tarayici

8 GB RAM'li bilgisayarlar için az bellek kullanan hafif tarayıcı (Python + Qt WebEngine).

## Kurulum (Windows)
1. Python 3.10+ kur (python.org, "Add to PATH" işaretli).
2. `pip install -r requirements.txt`
3. `python tarayici.py` (veya `python tarayici.py https://ornek.com`)

## RAM'i nasıl azaltır?
- **Aynı anda yalnızca 1 sayfa canlı**: arka plan sekmeleri sadece URL/başlık tutar (KB'lar); sekme değişince eski sayfa yok edilir. Chrome'daki en büyük RAM kaybı (her sekme = ayrı işlem) ortadan kalkar.
- Tek render işlemi, GPU kapalı, JS heap 192 MB ile sınırlı, arka plan servisleri kapalı.
- Disk önbelleği yok, bellek önbelleği 16 MB; çerezler kalıcı değil.
- Reklam/izleyici engelleme, isteğe bağlı **resimsiz mod** (araç çubuğundaki düğme).
- Linkler (`target=_blank`) aynı sekmede açılır.

## Kısayollar
Ctrl+T yeni sekme · Ctrl+W kapat · Ctrl+L adres çubuğu · F5 yenile

## Bilinen ödünler
Sekmeye dönünce sayfa yeniden yüklenir; oturum/çerezler kapanınca silinir; eklenti desteği yok.
