#!/usr/bin/env python3
"""Tarayici - az RAM kullanan hafif tarayici.

RAM tasarrufu stratejisi:
  * Ayni anda SADECE BIR sayfa (QWebEngineView) canlidir. Diger sekmeler
    yalnizca URL/baslik tutar (birkac KB). Sekme degisince eski sayfa yok edilir.
  * Tek render islemi, GPU kapali, kucuk JS heap, arka plan servisleri kapali.
  * Disk onbellegi yok (off-the-record profil), bellek onbellegi kucuk.
  * Reklam/izleyici alan adlari engellenir, istege bagli resimsiz mod.
  * Arka planda 5 dk bos kalan sekmeler zaten bosaltilmis durumdadir.
"""
import json
import os
import sys

# Chromium bayraklari QApplication'dan ONCE ayarlanmali.
_FLAGS = [
    "--renderer-process-limit=1",
    "--disable-extensions",
    "--disable-background-networking",
    "--disable-sync",
    "--disable-translate",
    "--disable-default-apps",
    "--disable-breakpad",
    "--disable-component-update",
    "--no-pings",
    "--disk-cache-size=1",
    "--media-cache-size=1",
    "--js-flags=--max-old-space-size=256",
    "--enable-gpu-rasterization",
    "--ignore-gpu-blocklist",
    "--disable-features=Translate,OptimizationHints,MediaRouter,"
    "InterestFeedContentSuggestions,AutofillServerCommunication,"
    "CertificateTransparencyComponentUpdater",
    "--enable-features=BackForwardCache:enabled/false",
]
# Ekran karti varsayilan acik (islemci yukunu dusurur). Cok eski/sorunlu
# bilgisayarlarda TARAYICI_GPU_KAPALI=1 ile kapatilabilir.
if os.environ.get("TARAYICI_GPU_KAPALI"):
    _FLAGS += ["--disable-gpu", "--disable-software-rasterizer"]
os.environ.setdefault("QTWEBENGINE_CHROMIUM_FLAGS", " ".join(_FLAGS))

from PySide6.QtCore import QUrl, Qt
from PySide6.QtGui import QAction, QIcon, QKeySequence
from PySide6.QtWebEngineCore import (QWebEnginePage, QWebEngineProfile,
                                     QWebEngineScript,
                                     QWebEngineSettings,
                                     QWebEngineUrlRequestInterceptor)
from PySide6.QtWebEngineWidgets import QWebEngineView
from PySide6.QtWidgets import (QApplication, QLineEdit, QMainWindow, QMenu,
                               QStackedWidget, QTabBar, QToolBar, QVBoxLayout,
                               QWidget, QLabel)

HOME = "https://www.google.com"
SEARCH = "https://www.google.com/search?q={}"
DATA_DIR = os.path.join(os.path.expanduser("~"), ".tarayici")
BOOKMARKS_FILE = os.path.join(DATA_DIR, "favoriler.json")

BLOCKED = (
    "doubleclick.net", "googlesyndication.com", "googleadservices.com",
    "google-analytics.com", "googletagmanager.com", "googletagservices.com",
    "adservice.google.", "facebook.net", "connect.facebook.com",
    "scorecardresearch.com", "quantserve.com", "taboola.com", "outbrain.com",
    "adnxs.com", "criteo.com", "hotjar.com", "mixpanel.com", "segment.io",
    "amazon-adsystem.com", "adsrvr.org", "moatads.com", "yandex.ru/metrika",
    "mc.yandex.", "ads-twitter.com", "analytics.tiktok.com",
)


# YouTube'a VP9/AV1 yerine H.264 sunmasini soyler. Ekran kartlari H.264'u
# donanimla cozer, boylece islemci yuku ciddi sekilde duser. H.264 destegi
# yoksa hicbir sey yapmaz (video bozulmasin diye).
H264_JS = r"""
(function () {
  var ms = window.MediaSource;
  if (!ms || !ms.isTypeSupported) return;
  var orig = ms.isTypeSupported.bind(ms);
  if (!orig('video/mp4; codecs="avc1.42E01E"')) return;
  var bad = /vp0?9|av01|av1/i;
  // 30 kareden fazlasini ve 720p ustunu reddet (islemciyi rahatlatir)
  function heavy(t) {
    var f = /framerate=(\d+)/i.exec(t), h = /height=(\d+)/i.exec(t);
    return (f && +f[1] > 30) || (h && +h[1] > 720);
  }
  ms.isTypeSupported = function (t) {
    return (bad.test(t) || heavy(t)) ? false : orig(t);
  };
  var can = HTMLMediaElement.prototype.canPlayType;
  HTMLMediaElement.prototype.canPlayType = function (t) {
    return bad.test(t) ? "" : can.call(this, t);
  };
})();
"""


class Blocker(QWebEngineUrlRequestInterceptor):
    def interceptRequest(self, info):
        host = info.requestUrl().host()
        if any(b in host for b in BLOCKED):
            info.block(True)


class Tab:
    __slots__ = ("url", "title")

    def __init__(self, url):
        self.url = url
        self.title = "Yeni sekme"


class Browser(QMainWindow):
    def __init__(self):
        super().__init__()
        self.setWindowTitle("Tarayici")
        self.resize(1100, 750)
        self.tabs: list[Tab] = []
        self.current = -1
        self.view: QWebEngineView | None = None
        self.no_images = False

        # Disk'e yazmayan, kucuk onbellekli profil
        # Giris bilgileri (cerezler) diskte saklanir; onbellek yalnizca bellekte.
        os.makedirs(DATA_DIR, exist_ok=True)
        self.profile = QWebEngineProfile("tarayici", self)
        self.profile.setPersistentStoragePath(os.path.join(DATA_DIR, "profil"))
        self.profile.setHttpCacheType(QWebEngineProfile.MemoryHttpCache)
        self.profile.setHttpCacheMaximumSize(16 * 1024 * 1024)
        self.profile.setPersistentCookiesPolicy(
            QWebEngineProfile.ForcePersistentCookies)
        script = QWebEngineScript()
        script.setName("h264")
        script.setSourceCode(H264_JS)
        script.setInjectionPoint(QWebEngineScript.DocumentCreation)
        script.setWorldId(QWebEngineScript.MainWorld)
        script.setRunsOnSubFrames(True)
        self.profile.scripts().insert(script)
        self.bookmarks = self.load_bookmarks()
        self.blocker = Blocker(self)
        self.profile.setUrlRequestInterceptor(self.blocker)

        # Arayuz
        self.bar = QTabBar(movable=False, tabsClosable=True, expanding=False,
                           documentMode=True)
        self.bar.currentChanged.connect(self.switch_tab)
        self.bar.tabCloseRequested.connect(self.close_tab)

        tb = self.tb = QToolBar()
        tb.setMovable(False)
        self.addToolBar(tb)
        self.act_back = tb.addAction("◀", lambda: self.view and self.view.back())
        self.act_fwd = tb.addAction("▶", lambda: self.view and self.view.forward())
        tb.addAction("⟳", lambda: self.view and self.view.reload())
        self.addr = QLineEdit()
        self.addr.setPlaceholderText("Adres yaz veya ara…")
        self.addr.returnPressed.connect(self.navigate)
        tb.addWidget(self.addr)
        tb.addAction("+", lambda: self.new_tab(HOME))
        tb.addAction("Tanı", lambda: self.new_tab("chrome://gpu"))
        self.fav_menu = QMenu(self)
        self.fav_menu.aboutToShow.connect(self.build_fav_menu)
        fav_btn = tb.addAction("★ Favoriler")
        fav_btn.triggered.connect(lambda: self.fav_menu.exec(
            self.mapToGlobal(self.rect().topLeft()) + self.addr.pos() + self.addr.rect().bottomLeft()))
        self.img_act = QAction("Resimler: Açık", self)
        self.img_act.triggered.connect(self.toggle_images)
        tb.addAction(self.img_act)

        self.holder = QStackedWidget()
        self.status = QLabel("")
        central = QWidget()
        lay = QVBoxLayout(central)
        lay.setContentsMargins(0, 0, 0, 0)
        lay.setSpacing(0)
        lay.addWidget(self.bar)
        lay.addWidget(self.holder, 1)
        self.setCentralWidget(central)
        self.statusBar().addPermanentWidget(self.status)

        for key, fn in (("Ctrl+T", lambda: self.new_tab(HOME)),
                        ("Ctrl+W", lambda: self.close_tab(self.bar.currentIndex())),
                        ("Ctrl+L", lambda: (self.addr.setFocus(), self.addr.selectAll())),
                        ("F5", lambda: self.view and self.view.reload())):
            a = QAction(self)
            a.setShortcut(QKeySequence(key))
            a.triggered.connect(fn)
            self.addAction(a)

        self.new_tab(sys.argv[1] if len(sys.argv) > 1 else HOME)

    # ---- sekmeler -------------------------------------------------------
    def new_tab(self, url):
        self.tabs.append(Tab(url))
        i = self.bar.addTab("Yeni sekme")
        self.bar.setCurrentIndex(i)  # switch_tab tetiklenir
        if self.current != i:
            self.switch_tab(i)

    def switch_tab(self, i):
        if i < 0 or i >= len(self.tabs) or i == self.current and self.view:
            return
        self.save_state()
        self.drop_view()
        self.current = i
        self.make_view(self.tabs[i].url)

    def close_tab(self, i):
        if len(self.tabs) == 1:
            self.close()
            return
        was_current = i == self.current
        del self.tabs[i]
        self.bar.blockSignals(True)
        self.bar.removeTab(i)
        self.bar.blockSignals(False)
        if was_current:
            self.drop_view()
            self.current = -1
            self.bar.blockSignals(True)
            self.bar.setCurrentIndex(max(0, i - 1))
            self.bar.blockSignals(False)
            self.switch_tab(self.bar.currentIndex())
        elif i < self.current:
            self.current -= 1

    def save_state(self):
        if self.view and 0 <= self.current < len(self.tabs):
            self.tabs[self.current].url = self.view.url().toString()

    def drop_view(self):
        """Eski sayfayi tamamen yok et -> render bellegi iade edilir."""
        if not self.view:
            return
        v, self.view = self.view, None
        self.holder.removeWidget(v)
        page = v.page()
        v.setPage(None)
        page.deleteLater()
        v.deleteLater()

    def make_view(self, url):
        v = QWebEngineView()
        page = QWebEnginePage(self.profile, v)
        v.setPage(page)
        s = page.settings()
        s.setAttribute(QWebEngineSettings.PluginsEnabled, False)
        s.setAttribute(QWebEngineSettings.WebGLEnabled, False)
        s.setAttribute(QWebEngineSettings.Accelerated2dCanvasEnabled, True)
        s.setAttribute(QWebEngineSettings.AutoLoadIconsForPage, False)
        s.setAttribute(QWebEngineSettings.ScrollAnimatorEnabled, False)
        s.setAttribute(QWebEngineSettings.PlaybackRequiresUserGesture, True)
        s.setAttribute(QWebEngineSettings.LocalStorageEnabled, True)
        s.setAttribute(QWebEngineSettings.AutoLoadImages, not self.no_images)
        s.setAttribute(QWebEngineSettings.FullScreenSupportEnabled, True)
        page.fullScreenRequested.connect(self.on_fullscreen)
        v.titleChanged.connect(self.on_title)
        v.urlChanged.connect(self.on_url)
        # target=_blank linkleri ayni sekmede ac (yeni render nesnesi yok)
        page.createWindow = lambda _t: page
        self.view = v
        self.holder.addWidget(v)
        self.holder.setCurrentWidget(v)
        self.addr.setText(url)
        v.load(QUrl(url))

    def on_fullscreen(self, req):
        """YouTube vb. tam ekran istegi: arayuzu gizle, pencereyi tam ekran yap."""
        req.accept()
        on = req.toggleOn()
        self.bar.setVisible(not on)
        self.tb.setVisible(not on)
        self.statusBar().setVisible(not on)
        if on:
            self.showFullScreen()
        else:
            self.showNormal()

    def on_title(self, t):
        if self.current >= 0:
            self.tabs[self.current].title = t
            self.bar.setTabText(self.current, (t or "Sekme")[:22])

    def on_url(self, u):
        self.addr.setText(u.toString())
        self.addr.setCursorPosition(0)

    # ---- favoriler ------------------------------------------------------
    def load_bookmarks(self):
        try:
            with open(BOOKMARKS_FILE, encoding="utf-8") as f:
                return json.load(f)
        except (OSError, ValueError):
            return []

    def save_bookmarks(self):
        with open(BOOKMARKS_FILE, "w", encoding="utf-8") as f:
            json.dump(self.bookmarks, f, ensure_ascii=False, indent=1)

    def build_fav_menu(self):
        m = self.fav_menu
        m.clear()
        m.addAction("★ Bu sayfayı favorilere ekle", self.add_bookmark)
        if self.bookmarks:
            m.addSeparator()
            for b in self.bookmarks:
                m.addAction(b["title"][:40] or b["url"][:40],
                            lambda u=b["url"]: self.view and self.view.load(QUrl(u)))
            m.addSeparator()
            rm = m.addMenu("Favoriden sil")
            for b in self.bookmarks:
                rm.addAction(b["title"][:40] or b["url"][:40],
                             lambda b=b: self.remove_bookmark(b))

    def add_bookmark(self):
        if not self.view:
            return
        url = self.view.url().toString()
        if not any(b["url"] == url for b in self.bookmarks):
            self.bookmarks.append({"title": self.view.title() or url, "url": url})
            self.save_bookmarks()

    def remove_bookmark(self, b):
        if b in self.bookmarks:
            self.bookmarks.remove(b)
            self.save_bookmarks()

    # ---- gezinti --------------------------------------------------------
    def navigate(self):
        t = self.addr.text().strip()
        if not t:
            return
        if " " in t or ("." not in t and "://" not in t and not t.startswith("localhost")):
            from urllib.parse import quote_plus
            t = SEARCH.format(quote_plus(t))
        elif "://" not in t:
            t = "https://" + t
        if self.view:
            self.view.load(QUrl(t))

    def toggle_images(self):
        self.no_images = not self.no_images
        self.img_act.setText("Resimler: Kapalı" if self.no_images else "Resimler: Açık")
        if self.view:
            self.view.page().settings().setAttribute(
                QWebEngineSettings.AutoLoadImages, not self.no_images)
            self.view.reload()


def main():
    app = QApplication(sys.argv[:1])
    base = getattr(sys, "_MEIPASS", os.path.dirname(os.path.abspath(__file__)))
    app.setWindowIcon(QIcon(os.path.join(base, "logo.png")))
    w = Browser()
    w.show()
    sys.exit(app.exec())


if __name__ == "__main__":
    main()
