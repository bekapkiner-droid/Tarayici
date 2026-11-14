using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace Tarayici
{
    static class Program
    {
        [DllImport("user32.dll")]
        static extern bool SetProcessDPIAware();

        [STAThread]
        static void Main(string[] args)
        {
            SetProcessDPIAware();
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm(args.Length > 0 ? args[0] : null));
        }
    }

    class Tab { public string Url; public string Title = "Yeni sekme"; }

    /// Az RAM: ayni anda yalnizca BIR sayfa (WebView2) canli. Diger sekmeler sadece adres/baslik tutar.
    class MainForm : Form
    {
        const string Home = "https://www.google.com";
        const string Flags =
            "--renderer-process-limit=1 --disable-extensions --disable-background-networking " +
            "--disable-sync --disable-translate --disk-cache-size=52428800 " +
            "--js-flags=--max-old-space-size=256 " +
            "--disable-features=Translate,MediaRouter,OptimizationHints";

        static readonly string[] Blocked = {
            "doubleclick.net", "googlesyndication.com", "googleadservices.com",
            "google-analytics.com", "googletagmanager.com", "googletagservices.com",
            "facebook.net", "scorecardresearch.com", "quantserve.com", "taboola.com",
            "outbrain.com", "adnxs.com", "criteo.com", "hotjar.com", "mixpanel.com",
            "amazon-adsystem.com", "adsrvr.org", "moatads.com", "ads-twitter.com"
        };

        static readonly string DataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".tarayici");
        static readonly string FavFile = Path.Combine(DataDir, "favoriler_edge.txt");
        static readonly string TabFile = Path.Combine(DataDir, "sekmeler_edge.txt");

        readonly string startUrl;
        readonly List<Tab> tabs = new List<Tab>();
        int cur = -1;
        bool busy, suppress, noImages;
        WebView2 web;
        CoreWebView2Environment env;

        TabControl bar;
        TextBox addr;
        Panel host;
        TableLayoutPanel top;
        FormBorderStyle prevBorder;
        FormWindowState prevState;
        Rectangle prevBounds;

        public MainForm(string startUrl)
        {
            this.startUrl = startUrl;
            Text = "Tarayıcı";
            Width = 1100; Height = 750;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
            BuildUi();
            Load += async (s, e) => await Init();
            FormClosing += (s, e) => SaveTabs();
        }

        // ---- arayuz ---------------------------------------------------------
        Button Btn(string text, int width, EventHandler click)
        {
            var b = new Button
            {
                Text = text, Width = width, Height = 28, FlatStyle = FlatStyle.Flat,
                Margin = new Padding(2), TabStop = false
            };
            b.Click += click;
            return b;
        }

        void BuildUi()
        {
            host = new Panel { Dock = DockStyle.Fill };
            bar = new TabControl { Dock = DockStyle.Top, Height = 28, Padding = new Point(12, 3) };
            bar.SelectedIndexChanged += (s, e) =>
            {
                if (!suppress && bar.SelectedIndex >= 0 && bar.SelectedIndex != cur)
                    Fire(ShowTab(bar.SelectedIndex));
            };
            bar.MouseUp += (s, e) =>
            {
                if (e.Button != MouseButtons.Middle) return;
                for (int i = 0; i < bar.TabCount; i++)
                    if (bar.GetTabRect(i).Contains(e.Location)) { Fire(CloseTab(i)); break; }
            };

            top = new TableLayoutPanel
            {
                Dock = DockStyle.Top, Height = 36, ColumnCount = 8, RowCount = 1,
                Padding = new Padding(2)
            };
            for (int i = 0; i < 8; i++)
                top.ColumnStyles.Add(i == 3 ? new ColumnStyle(SizeType.Percent, 100f)
                                            : new ColumnStyle(SizeType.AutoSize));
            addr = new TextBox { Dock = DockStyle.Fill, Margin = new Padding(4, 5, 4, 0) };
            addr.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; Navigate(addr.Text); }
            };

            Button fav = null, opt = null;
            fav = Btn("★ Favoriler", 90, (s, e) => ShowFav(fav));
            opt = Btn("⋮", 32, (s, e) => ShowOptions(opt));
            top.Controls.Add(Btn("◀", 32, (s, e) => { if (web != null && web.CanGoBack) web.GoBack(); }), 0, 0);
            top.Controls.Add(Btn("▶", 32, (s, e) => { if (web != null && web.CanGoForward) web.GoForward(); }), 1, 0);
            top.Controls.Add(Btn("⟳", 32, (s, e) => { if (web != null) web.Reload(); }), 2, 0);
            top.Controls.Add(addr, 3, 0);
            top.Controls.Add(Btn("+", 32, (s, e) => Fire(NewTab(Home))), 4, 0);
            top.Controls.Add(fav, 5, 0);
            top.Controls.Add(opt, 6, 0);

            Controls.Add(host);
            Controls.Add(bar);
            Controls.Add(top);
        }

        static void Fire(Task t)
        {
            t.ContinueWith(x => { var _ = x.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
        }

        // ---- baslangic ------------------------------------------------------
        async Task Init()
        {
            try
            {
                Directory.CreateDirectory(DataDir);
                var opts = new CoreWebView2EnvironmentOptions(Flags);
                env = await CoreWebView2Environment.CreateAsync(null, Path.Combine(DataDir, "webview"), opts);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Microsoft Edge WebView2 bulunamadı.\n\nhttps://go.microsoft.com/fwlink/p/?LinkId=2124703 " +
                    "adresinden indirip kurun.\n\n" + ex.Message, "Tarayıcı");
                Close();
                return;
            }
            int start = LoadTabs();
            if (!string.IsNullOrEmpty(startUrl)) { tabs.Add(new Tab { Url = startUrl }); start = tabs.Count - 1; }
            if (tabs.Count == 0) tabs.Add(new Tab { Url = Home });
            RebuildBar();
            await ShowTab(Math.Max(0, Math.Min(start, tabs.Count - 1)));
        }

        // ---- sekmeler -------------------------------------------------------
        void RebuildBar()
        {
            suppress = true;
            bar.TabPages.Clear();
            foreach (var t in tabs) bar.TabPages.Add(new TabPage(Trunc(t.Title, 22)));
            if (cur >= 0 && cur < bar.TabCount) bar.SelectedIndex = cur;
            suppress = false;
        }

        void SelectBarTab(int i)
        {
            suppress = true;
            if (i >= 0 && i < bar.TabCount) bar.SelectedIndex = i;
            suppress = false;
        }

        async Task NewTab(string url)
        {
            tabs.Add(new Tab { Url = url });
            RebuildBar();
            await ShowTab(tabs.Count - 1);
        }

        async Task ShowTab(int i)
        {
            if (busy || env == null || i < 0 || i >= tabs.Count) return;
            busy = true;
            try
            {
                SaveCurrentUrl();
                var wv = new WebView2 { Dock = DockStyle.Fill };
                host.Controls.Add(wv);
                await wv.EnsureCoreWebView2Async(env);
                Configure(wv);
                var old = web;
                web = wv; cur = i;
                SelectBarTab(i);
                addr.Text = tabs[i].Url;
                wv.CoreWebView2.Navigate(tabs[i].Url);
                if (old != null) { host.Controls.Remove(old); old.Dispose(); }
                wv.Focus();
            }
            finally { busy = false; }
        }

        async Task CloseTab(int i)
        {
            if (i < 0 || i >= tabs.Count) return;
            if (tabs.Count == 1) { Close(); return; }
            tabs.RemoveAt(i);
            if (i < cur) { cur--; RebuildBar(); return; }
            if (i > cur) { RebuildBar(); return; }
            cur = -1;
            RebuildBar();
            await ShowTab(Math.Max(0, Math.Min(i, tabs.Count - 1)));
        }

        void SaveCurrentUrl()
        {
            if (web != null && web.CoreWebView2 != null && cur >= 0 && cur < tabs.Count)
            {
                var u = web.CoreWebView2.Source;
                if (!string.IsNullOrEmpty(u)) tabs[cur].Url = u;
            }
        }

        void SaveTabs()
        {
            try
            {
                SaveCurrentUrl();
                var lines = new List<string> { cur.ToString() };
                lines.AddRange(tabs.Select(t => Clean(t.Url) + "\t" + Clean(t.Title)));
                File.WriteAllLines(TabFile, lines);
            }
            catch { }
        }

        int LoadTabs()
        {
            try
            {
                if (!File.Exists(TabFile)) return 0;
                var lines = File.ReadAllLines(TabFile);
                foreach (var l in lines.Skip(1))
                {
                    var p = l.Split('\t');
                    if (p[0].Length > 0) tabs.Add(new Tab { Url = p[0], Title = p.Length > 1 ? p[1] : "Sekme" });
                }
                int c; return int.TryParse(lines[0], out c) ? c : 0;
            }
            catch { tabs.Clear(); return 0; }
        }

        // ---- web ayarlari ---------------------------------------------------
        void Configure(WebView2 wv)
        {
            var c = wv.CoreWebView2;
            var st = c.Settings;
            st.IsStatusBarEnabled = false;
            st.AreDevToolsEnabled = false;
            st.IsPasswordAutosaveEnabled = true;
            st.IsGeneralAutofillEnabled = true;
            try { c.MemoryUsageTargetLevel = CoreWebView2MemoryUsageTargetLevel.Low; } catch { }

            foreach (var d in Blocked)
                c.AddWebResourceRequestedFilter("*" + d + "/*", CoreWebView2WebResourceContext.All);
            if (noImages) c.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.Image);
            c.WebResourceRequested += (s, e) =>
            {
                bool block = false;
                Uri u;
                if (Uri.TryCreate(e.Request.Uri, UriKind.Absolute, out u))
                    block = Blocked.Any(b => u.Host.EndsWith(b));
                if (!block && noImages && e.ResourceContext == CoreWebView2WebResourceContext.Image) block = true;
                if (block) e.Response = env.CreateWebResourceResponse(null, 403, "Blocked", "");
            };

            // yeni pencere acilmasin, ayni sekmede ac (ekstra bellek yok)
            c.NewWindowRequested += (s, e) => { e.Handled = true; c.Navigate(e.Uri); };
            c.SourceChanged += (s, e) =>
            {
                if (wv != web) return;
                addr.Text = c.Source;
                if (cur >= 0 && cur < tabs.Count) tabs[cur].Url = c.Source;
            };
            c.DocumentTitleChanged += (s, e) =>
            {
                if (wv != web || cur < 0 || cur >= tabs.Count) return;
                tabs[cur].Title = c.DocumentTitle;
                if (cur < bar.TabCount) bar.TabPages[cur].Text = Trunc(c.DocumentTitle, 22);
            };
            c.ContainsFullScreenElementChanged += (s, e) => { if (wv == web) SetFullscreen(c.ContainsFullScreenElement); };
            // Klavye kisayollari: sayfa odaktayken JS ile yakalanir
            var _ = c.AddScriptToExecuteOnDocumentCreatedAsync(KeyScript);
            c.WebMessageReceived += (s, e) =>
            {
                string m;
                try { m = e.TryGetWebMessageAsString(); } catch { return; }
                BeginInvoke((Action)(() => HandleKey(m)));
            };
        }

        const string KeyScript =
            "document.addEventListener('keydown', function (e) {" +
            "  if (e.ctrlKey && !e.shiftKey && !e.altKey) {" +
            "    var k = (e.key || '').toLowerCase();" +
            "    if (k === 't' || k === 'w' || k === 'l') {" +
            "      e.preventDefault(); window.chrome.webview.postMessage(k);" +
            "    }" +
            "  }" +
            "}, true);";

        void HandleKey(string k)
        {
            if (k == "t") Fire(NewTab(Home));
            else if (k == "w") Fire(CloseTab(cur));
            else if (k == "l") { addr.Focus(); addr.SelectAll(); }
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == (Keys.Control | Keys.T)) { HandleKey("t"); return true; }
            if (keyData == (Keys.Control | Keys.W)) { HandleKey("w"); return true; }
            if (keyData == (Keys.Control | Keys.L)) { HandleKey("l"); return true; }
            if (keyData == Keys.F5) { if (web != null) web.Reload(); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        void SetFullscreen(bool on)
        {
            top.Visible = !on;
            bar.Visible = !on;
            if (on)
            {
                prevBorder = FormBorderStyle; prevState = WindowState; prevBounds = Bounds;
                WindowState = FormWindowState.Normal;
                FormBorderStyle = FormBorderStyle.None;
                Bounds = Screen.FromControl(this).Bounds;
            }
            else
            {
                FormBorderStyle = prevBorder;
                WindowState = prevState;
                if (prevState == FormWindowState.Normal) Bounds = prevBounds;
            }
        }

        // ---- gezinti --------------------------------------------------------
        void Navigate(string input)
        {
            var t = (input ?? "").Trim();
            if (t.Length == 0 || web == null || web.CoreWebView2 == null) return;
            if (t.Contains(" ") || (!t.Contains(".") && !t.Contains("://") && !t.StartsWith("localhost")))
                t = "https://www.google.com/search?q=" + Uri.EscapeDataString(t);
            else if (!t.Contains("://"))
                t = "https://" + t;
            try { web.CoreWebView2.Navigate(t); } catch { }
            web.Focus();
        }

        // ---- menuler / favoriler -------------------------------------------
        void ShowOptions(Control anchor)
        {
            var m = new ContextMenuStrip();
            m.Items.Add(noImages ? "Resimleri aç" : "Resimleri kapat", null, (s, e) => ToggleImages());
            m.Items.Add("Bu sekmeyi kapat", null, (s, e) => Fire(CloseTab(cur)));
            m.Items.Add("Tanı (edge://gpu)", null, (s, e) => Fire(NewTab("edge://gpu")));
            m.Show(anchor, new Point(0, anchor.Height));
        }

        void ToggleImages()
        {
            noImages = !noImages;
            if (web == null || web.CoreWebView2 == null) return;
            if (noImages) web.CoreWebView2.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.Image);
            else web.CoreWebView2.RemoveWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.Image);
            web.Reload();
        }

        List<string[]> LoadFavs()
        {
            var list = new List<string[]>();
            try
            {
                if (File.Exists(FavFile))
                    foreach (var l in File.ReadAllLines(FavFile))
                    {
                        var p = l.Split('\t');
                        if (p.Length >= 2) list.Add(new[] { p[0], p[1] });
                    }
            }
            catch { }
            return list;
        }

        void SaveFavs(List<string[]> favs)
        {
            try { File.WriteAllLines(FavFile, favs.Select(f => Clean(f[0]) + "\t" + Clean(f[1]))); } catch { }
        }

        void ShowFav(Control anchor)
        {
            var favs = LoadFavs();
            var m = new ContextMenuStrip();
            m.Items.Add("★ Bu sayfayı favorilere ekle", null, (s, e) =>
            {
                if (web == null || web.CoreWebView2 == null) return;
                var url = web.CoreWebView2.Source;
                if (favs.Any(f => f[1] == url)) return;
                favs.Add(new[] { string.IsNullOrEmpty(web.CoreWebView2.DocumentTitle) ? url : web.CoreWebView2.DocumentTitle, url });
                SaveFavs(favs);
            });
            if (favs.Count > 0)
            {
                m.Items.Add(new ToolStripSeparator());
                foreach (var f in favs)
                {
                    var fav = f;
                    m.Items.Add(Trunc(fav[0], 40), null, (s, e) => { if (web != null && web.CoreWebView2 != null) web.CoreWebView2.Navigate(fav[1]); });
                }
                m.Items.Add(new ToolStripSeparator());
                var rm = new ToolStripMenuItem("Favoriden sil");
                foreach (var f in favs)
                {
                    var fav = f;
                    rm.DropDownItems.Add(Trunc(fav[0], 40), null, (s, e) => { favs.Remove(fav); SaveFavs(favs); });
                }
                m.Items.Add(rm);
            }
            m.Show(anchor, new Point(0, anchor.Height));
        }

        static string Trunc(string s, int n)
        {
            if (string.IsNullOrEmpty(s)) return "Sekme";
            return s.Length > n ? s.Substring(0, n) : s;
        }

        static string Clean(string s)
        {
            return (s ?? "").Replace("\t", " ").Replace("\r", " ").Replace("\n", " ");
        }
    }
}
