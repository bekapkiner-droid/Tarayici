package com.tarayici.app

import android.app.Activity
import android.app.AlertDialog
import android.content.Context
import android.graphics.Color
import android.os.Bundle
import android.view.Gravity
import android.view.KeyEvent
import android.view.View
import android.view.inputmethod.EditorInfo
import android.view.inputmethod.InputMethodManager
import android.webkit.WebChromeClient
import android.webkit.WebResourceRequest
import android.webkit.WebResourceResponse
import android.webkit.WebSettings
import android.webkit.WebView
import android.webkit.WebViewClient
import android.widget.Button
import android.widget.EditText
import android.widget.FrameLayout
import android.widget.LinearLayout
import android.widget.PopupMenu
import android.widget.Toast
import org.json.JSONArray
import org.json.JSONObject
import java.io.ByteArrayInputStream
import java.net.URLEncoder

/**
 * Az RAM kullanan tarayici: ayni anda yalnizca BIR sayfa (WebView) canlidir.
 * Diger sekmeler sadece adres/baslik tutar; sekme degisince eski sayfa yok edilir.
 */
class MainActivity : Activity() {

    private class Tab(var url: String, var title: String = "Yeni sekme")

    private val home = "https://www.google.com"
    private val blocked = listOf(
        "doubleclick.net", "googlesyndication.com", "googleadservices.com",
        "google-analytics.com", "googletagmanager.com", "googletagservices.com",
        "facebook.net", "scorecardresearch.com", "quantserve.com", "taboola.com",
        "outbrain.com", "adnxs.com", "criteo.com", "hotjar.com", "mixpanel.com",
        "amazon-adsystem.com", "adsrvr.org", "moatads.com", "ads-twitter.com",
    )

    private val tabs = mutableListOf<Tab>()
    private var cur = 0
    private var web: WebView? = null
    private var noImages = false
    private var customView: View? = null
    private var customCb: WebChromeClient.CustomViewCallback? = null

    private lateinit var holder: FrameLayout
    private lateinit var addr: EditText
    private lateinit var tabBtn: Button

    private val prefs by lazy { getSharedPreferences("tarayici", Context.MODE_PRIVATE) }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        buildUi()
        restoreTabs()
        val link = intent?.dataString
        if (link != null) tabs.add(Tab(link)).also { cur = tabs.size - 1 }
        if (tabs.isEmpty()) tabs.add(Tab(home))
        cur = cur.coerceIn(0, tabs.size - 1)
        showTab(cur)
    }

    override fun onNewIntent(intent: android.content.Intent?) {
        super.onNewIntent(intent)
        intent?.dataString?.let { newTab(it) }
    }

    // ---- arayuz ----------------------------------------------------------
    private fun dp(v: Int) = (v * resources.displayMetrics.density).toInt()

    private fun btn(text: String, onClick: (View) -> Unit) = Button(this).apply {
        this.text = text
        isAllCaps = false
        minWidth = 0
        minimumWidth = dp(44)
        setPadding(dp(4), 0, dp(4), 0)
        setOnClickListener { onClick(it) }
    }

    private fun buildUi() {
        val root = LinearLayout(this).apply { orientation = LinearLayout.VERTICAL }
        val bar = LinearLayout(this).apply {
            orientation = LinearLayout.HORIZONTAL
            gravity = Gravity.CENTER_VERTICAL
            setBackgroundColor(Color.parseColor("#EEEEEE"))
            setPadding(dp(4), dp(4), dp(4), dp(4))
        }
        addr = EditText(this).apply {
            setSingleLine()
            hint = "Adres yaz veya ara"
            imeOptions = EditorInfo.IME_ACTION_GO
            setOnEditorActionListener { _, _, _ -> navigate(text.toString()); true }
            setOnKeyListener { _, code, ev ->
                if (code == KeyEvent.KEYCODE_ENTER && ev.action == KeyEvent.ACTION_UP) {
                    navigate(text.toString()); true
                } else false
            }
        }
        tabBtn = btn("1") { showTabList() }
        val menuBtn = btn("⋮") { showMenu(it) }
        bar.addView(btn("◀") { web?.goBack() })
        bar.addView(addr, LinearLayout.LayoutParams(0, LinearLayout.LayoutParams.WRAP_CONTENT, 1f))
        bar.addView(tabBtn)
        bar.addView(menuBtn)
        holder = FrameLayout(this)
        root.addView(bar)
        root.addView(holder, LinearLayout.LayoutParams(-1, 0, 1f))
        setContentView(root)
    }

    private fun showMenu(anchor: android.view.View) {
        val m = PopupMenu(this, anchor)
        m.menu.add(0, 1, 0, "Yenile")
        m.menu.add(0, 2, 0, "İleri")
        m.menu.add(0, 3, 0, "Yeni sekme")
        m.menu.add(0, 4, 0, "Bu sekmeyi kapat")
        m.menu.add(0, 5, 0, "★ Favorilere ekle")
        m.menu.add(0, 6, 0, "Favoriler")
        m.menu.add(0, 7, 0, if (noImages) "Resimleri aç" else "Resimleri kapat")
        m.setOnMenuItemClickListener {
            when (it.itemId) {
                1 -> web?.reload()
                2 -> web?.goForward()
                3 -> newTab(home)
                4 -> closeTab(cur)
                5 -> addBookmark()
                6 -> showBookmarks()
                7 -> { noImages = !noImages; web?.settings?.loadsImagesAutomatically = !noImages; web?.reload() }
            }
            true
        }
        m.show()
    }

    // ---- sekmeler --------------------------------------------------------
    private fun showTab(i: Int) {
        saveCurrentUrl()
        dropWeb()
        cur = i
        tabBtn.text = tabs.size.toString()
        makeWeb(tabs[i].url)
    }

    private fun newTab(url: String) {
        tabs.add(Tab(url))
        showTab(tabs.size - 1)
    }

    private fun closeTab(i: Int) {
        if (tabs.size == 1) { tabs[0] = Tab(home); showTab(0); return }
        dropWeb()
        tabs.removeAt(i)
        showTab((if (i >= tabs.size) tabs.size - 1 else i).coerceAtLeast(0))
    }

    private fun showTabList() {
        saveCurrentUrl()
        val names = tabs.mapIndexed { i, t -> (if (i == cur) "● " else "") + t.title.take(40) }
        AlertDialog.Builder(this).setTitle("Sekmeler")
            .setItems(names.toTypedArray()) { _, i -> if (i != cur) showTab(i) }
            .setPositiveButton("+ Yeni sekme") { _, _ -> newTab(home) }
            .show()
    }

    private fun saveCurrentUrl() {
        val w = web ?: return
        val u = w.url
        if (u != null && cur in tabs.indices) tabs[cur].url = u
    }

    private fun dropWeb() {
        val w = web ?: return
        web = null
        holder.removeView(w)
        w.stopLoading()
        w.loadUrl("about:blank")
        w.clearHistory()
        w.destroy()
    }

    private fun makeWeb(url: String) {
        val w = WebView(this)
        w.settings.apply {
            javaScriptEnabled = true
            domStorageEnabled = true
            loadsImagesAutomatically = !noImages
            mediaPlaybackRequiresUserGesture = true
            setGeolocationEnabled(false)
            setSupportMultipleWindows(false)
            allowFileAccess = false
            builtInZoomControls = true
            displayZoomControls = false
            cacheMode = WebSettings.LOAD_DEFAULT
        }
        w.webViewClient = object : WebViewClient() {
            override fun shouldInterceptRequest(v: WebView, r: WebResourceRequest): WebResourceResponse? {
                val host = r.url.host ?: return null
                return if (blocked.any { host.contains(it) })
                    WebResourceResponse("text/plain", "utf-8", ByteArrayInputStream(ByteArray(0)))
                else null
            }

            override fun shouldOverrideUrlLoading(v: WebView, r: WebResourceRequest): Boolean {
                val s = r.url.scheme
                return s != "http" && s != "https" && s != "about" && s != "data"
            }

            override fun onPageFinished(v: WebView, u: String) {
                if (v === web) {
                    addr.setText(u)
                    if (cur in tabs.indices) tabs[cur].url = u
                }
            }
        }
        w.webChromeClient = object : WebChromeClient() {
            @Suppress("DEPRECATION")
            override fun onShowCustomView(view: View, cb: CustomViewCallback) {
                if (customView != null) { cb.onCustomViewHidden(); return }
                customView = view
                customCb = cb
                (window.decorView as FrameLayout).addView(view, FrameLayout.LayoutParams(-1, -1))
                window.decorView.systemUiVisibility = (View.SYSTEM_UI_FLAG_FULLSCREEN
                    or View.SYSTEM_UI_FLAG_HIDE_NAVIGATION or View.SYSTEM_UI_FLAG_IMMERSIVE_STICKY
                    or View.SYSTEM_UI_FLAG_LAYOUT_FULLSCREEN or View.SYSTEM_UI_FLAG_LAYOUT_HIDE_NAVIGATION)
            }

            @Suppress("DEPRECATION")
            override fun onHideCustomView() {
                val v = customView ?: return
                (window.decorView as FrameLayout).removeView(v)
                customView = null
                window.decorView.systemUiVisibility = View.SYSTEM_UI_FLAG_VISIBLE
                customCb?.onCustomViewHidden()
                customCb = null
            }

            override fun onReceivedTitle(v: WebView, t: String?) {
                if (v === web && cur in tabs.indices && !t.isNullOrBlank()) tabs[cur].title = t
            }
        }
        web = w
        holder.addView(w, FrameLayout.LayoutParams(-1, -1))
        addr.setText(url)
        w.loadUrl(url)
    }

    // ---- gezinti ---------------------------------------------------------
    private fun navigate(input: String) {
        var t = input.trim()
        if (t.isEmpty()) return
        t = if (t.contains(" ") || (!t.contains(".") && !t.contains("://") && !t.startsWith("localhost")))
            "https://www.google.com/search?q=" + URLEncoder.encode(t, "UTF-8")
        else if (!t.contains("://")) "https://$t" else t
        web?.loadUrl(t)
        (getSystemService(Context.INPUT_METHOD_SERVICE) as InputMethodManager)
            .hideSoftInputFromWindow(addr.windowToken, 0)
        web?.requestFocus()
    }

    @Deprecated("Deprecated in Java")
    override fun onBackPressed() {
        val w = web
        if (customView != null) { w?.webChromeClient?.onHideCustomView(); return }
        when {
            w != null && w.canGoBack() -> w.goBack()
            tabs.size > 1 -> closeTab(cur)
            else -> super.onBackPressed()
        }
    }

    // ---- favoriler -------------------------------------------------------
    private fun bookmarks(): JSONArray = try { JSONArray(prefs.getString("fav", "[]")) } catch (e: Exception) { JSONArray() }

    private fun addBookmark() {
        val w = web ?: return
        val url = w.url ?: return
        val arr = bookmarks()
        for (i in 0 until arr.length()) if (arr.getJSONObject(i).getString("url") == url) return
        arr.put(JSONObject().put("title", w.title ?: url).put("url", url))
        prefs.edit().putString("fav", arr.toString()).apply()
        Toast.makeText(this, "Favorilere eklendi", Toast.LENGTH_SHORT).show()
    }

    private fun showBookmarks() {
        val arr = bookmarks()
        if (arr.length() == 0) { Toast.makeText(this, "Henüz favori yok", Toast.LENGTH_SHORT).show(); return }
        val names = Array(arr.length()) { arr.getJSONObject(it).getString("title").take(40) }
        val d = AlertDialog.Builder(this).setTitle("Favoriler (silmek için basılı tut)")
            .setItems(names) { _, i -> web?.loadUrl(arr.getJSONObject(i).getString("url")) }
            .create()
        d.show()
        d.listView.setOnItemLongClickListener { _, _, i, _ ->
            val n = JSONArray()
            for (j in 0 until arr.length()) if (j != i) n.put(arr.getJSONObject(j))
            prefs.edit().putString("fav", n.toString()).apply()
            d.dismiss()
            Toast.makeText(this, "Silindi", Toast.LENGTH_SHORT).show()
            true
        }
    }

    // ---- sekmeleri hatirla ----------------------------------------------
    override fun onPause() {
        super.onPause()
        saveCurrentUrl()
        val arr = JSONArray()
        tabs.forEach { arr.put(JSONObject().put("url", it.url).put("title", it.title)) }
        prefs.edit().putString("tabs", arr.toString()).putInt("cur", cur).apply()
    }

    private fun restoreTabs() {
        try {
            val arr = JSONArray(prefs.getString("tabs", "[]"))
            for (i in 0 until arr.length()) {
                val o = arr.getJSONObject(i)
                tabs.add(Tab(o.getString("url"), o.optString("title", "Sekme")))
            }
            cur = prefs.getInt("cur", 0)
        } catch (e: Exception) { tabs.clear() }
    }

    override fun onDestroy() {
        dropWeb()
        super.onDestroy()
    }
}
