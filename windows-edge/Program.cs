using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
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

    // ------------------------------------------------------------------ tema
    class Theme
    {
        public Color Strip, Active, Bar, Input, Text, Muted, Accent, Hover, Border, PageBack;

        public static Theme Dark()
        {
            return new Theme
            {
                Strip = Color.FromArgb(0x1B, 0x1C, 0x1E), Active = Color.FromArgb(0x2B, 0x2D, 0x31),
                Bar = Color.FromArgb(0x2B, 0x2D, 0x31), Input = Color.FromArgb(0x1B, 0x1C, 0x1E),
                Text = Color.FromArgb(0xE8, 0xEA, 0xED), Muted = Color.FromArgb(0x9A, 0xA0, 0xA6),
                Accent = Color.FromArgb(0x3F, 0xB8, 0xD4), Hover = Color.FromArgb(0x3C, 0x3F, 0x44),
                Border = Color.FromArgb(0x3C, 0x3F, 0x44), PageBack = Color.FromArgb(0x12, 0x12, 0x12)
            };
        }

        public static Theme Light()
        {
            return new Theme
            {
                Strip = Color.FromArgb(0xE3, 0xE6, 0xEA), Active = Color.White,
                Bar = Color.White, Input = Color.FromArgb(0xF1, 0xF3, 0xF4),
                Text = Color.FromArgb(0x20, 0x21, 0x24), Muted = Color.FromArgb(0x5F, 0x63, 0x68),
                Accent = Color.FromArgb(0x0B, 0x7A, 0x93), Hover = Color.FromArgb(0xDA, 0xDC, 0xE0),
                Border = Color.FromArgb(0xDA, 0xDC, 0xE0), PageBack = Color.White
            };
        }
    }

    class ThemeColors : ProfessionalColorTable
    {
        readonly Theme t;
        public ThemeColors(Theme t) { this.t = t; UseSystemColors = false; }
        public override Color ToolStripDropDownBackground { get { return t.Bar; } }
        public override Color ImageMarginGradientBegin { get { return t.Bar; } }
        public override Color ImageMarginGradientMiddle { get { return t.Bar; } }
        public override Color ImageMarginGradientEnd { get { return t.Bar; } }
        public override Color MenuBorder { get { return t.Border; } }
        public override Color MenuItemBorder { get { return t.Hover; } }
        public override Color MenuItemSelected { get { return t.Hover; } }
        public override Color MenuItemSelectedGradientBegin { get { return t.Hover; } }
        public override Color MenuItemSelectedGradientEnd { get { return t.Hover; } }
        public override Color SeparatorDark { get { return t.Border; } }
        public override Color SeparatorLight { get { return t.Border; } }
    }

    // ------------------------------------------------------------- sekme seridi
    class TabStrip : Control
    {
        public event Action<int> TabSelected;
        public event Action<int> TabClosed;
        public event Action NewTabRequested;

        public List<string> Titles = new List<string>();
        public int Selected = -1;
        public Theme T = Theme.Dark();

        int hover = -1;
        bool hoverClose, hoverPlus;
        const int PlusW = 36, MaxW = 220, MinW = 90;
        readonly Font iconFont = new Font("Segoe MDL2 Assets", 8f);
        readonly Font textFont = new Font("Segoe UI", 9f);

        public TabStrip()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Height = 38;
        }

        int TabW()
        {
            int n = Math.Max(1, Titles.Count);
            return Math.Max(MinW, Math.Min(MaxW, (Width - PlusW - 10) / n));
        }
        Rectangle TabRect(int i) { int w = TabW(); return new Rectangle(6 + i * w, 6, w - 2, Height - 6); }
        Rectangle CloseRect(int i) { var r = TabRect(i); return new Rectangle(r.Right - 26, r.Top + (r.Height - 18) / 2 + 1, 18, 18); }
        Rectangle PlusRect() { return new Rectangle(6 + Titles.Count * TabW() + 2, 8, 28, 28); }

        static GraphicsPath TopRound(Rectangle r, int rad)
        {
            var p = new GraphicsPath();
            p.AddArc(r.Left, r.Top, rad * 2, rad * 2, 180, 90);
            p.AddArc(r.Right - rad * 2, r.Top, rad * 2, rad * 2, 270, 90);
            p.AddLine(r.Right, r.Bottom, r.Left, r.Bottom);
            p.CloseFigure();
            return p;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(T.Strip);
            for (int i = 0; i < Titles.Count; i++)
            {
                var r = TabRect(i);
                bool sel = i == Selected;
                if (sel || i == hover)
                    using (var path = TopRound(r, 8))
                    using (var br = new SolidBrush(sel ? T.Active : T.Hover))
                        g.FillPath(br, path);
                var tr = new Rectangle(r.Left + 12, r.Top, r.Width - 12 - 30, r.Height);
                TextRenderer.DrawText(g, Titles[i], textFont, tr, sel ? T.Text : T.Muted,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                if (sel || i == hover)
                {
                    var cr = CloseRect(i);
                    if (hoverClose && i == hover)
                        using (var br = new SolidBrush(T.Hover))
                            g.FillEllipse(br, cr);
                    TextRenderer.DrawText(g, "", iconFont, cr, T.Muted,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                }
            }
            var pr = PlusRect();
            if (hoverPlus)
                using (var br = new SolidBrush(T.Hover))
                    g.FillEllipse(br, pr);
            TextRenderer.DrawText(g, "", iconFont, pr, T.Text,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }

        int HitTab(Point p)
        {
            for (int i = 0; i < Titles.Count; i++) if (TabRect(i).Contains(p)) return i;
            return -1;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int h = HitTab(e.Location);
            bool hc = h >= 0 && CloseRect(h).Contains(e.Location);
            bool hp = PlusRect().Contains(e.Location);
            if (h != hover || hc != hoverClose || hp != hoverPlus)
            {
                hover = h; hoverClose = hc; hoverPlus = hp;
                Invalidate();
            }
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            hover = -1; hoverClose = false; hoverPlus = false;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            int h = HitTab(e.Location);
            if (PlusRect().Contains(e.Location) && e.Button == MouseButtons.Left)
            {
                if (NewTabRequested != null) NewTabRequested();
            }
            else if (h >= 0)
            {
                if (e.Button == MouseButtons.Middle || (e.Button == MouseButtons.Left && CloseRect(h).Contains(e.Location)))
                { if (TabClosed != null) TabClosed(h); }
                else if (e.Button == MouseButtons.Left)
                { if (TabSelected != null) TabSelected(h); }
            }
            base.OnMouseDown(e);
        }
    }

    // ------------------------------------------------------------ adres kutusu
    class RoundedBox : Panel
    {
        public Color Fill = Color.White, Edge = Color.Gray, Page = Color.White;
        public readonly TextBox Box;
        bool focused;

        public RoundedBox()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Box = new TextBox { BorderStyle = BorderStyle.None, Font = new Font("Segoe UI", 10f) };
            Box.Enter += (s, e) => { focused = true; Invalidate(); };
            Box.Leave += (s, e) => { focused = false; Invalidate(); };
            Controls.Add(Box);
            Resize += (s, e) => LayoutBox();
        }

        public Color Accent = Color.DodgerBlue;

        public void LayoutBox()
        {
            Box.BackColor = Fill;
            Box.Left = 16;
            Box.Width = Math.Max(10, Width - 32);
            Box.Top = (Height - Box.Height) / 2;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Page);
            var r = new Rectangle(0, 0, Width - 1, Height - 1);
            int d = Math.Min(Height - 1, 28);
            using (var p = new GraphicsPath())
            {
                p.AddArc(r.Left, r.Top, d, d, 90, 180);
                p.AddArc(r.Right - d, r.Top, d, d, 270, 180);
                p.CloseFigure();
                using (var br = new SolidBrush(Fill)) g.FillPath(br, p);
                using (var pen = new Pen(focused ? Accent : Edge, focused ? 1.6f : 1f)) g.DrawPath(pen, p);
            }
        }
    }

    // ------------------------------------------------------------- ana pencere
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
        static readonly string ThemeFile = Path.Combine(DataDir, "tema_edge.txt");

        [DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int val, int size);

        readonly string startUrl;
        readonly List<Tab> tabs = new List<Tab>();
        int cur = -1;
        bool busy, noImages, dark = true;
        WebView2 web;
        CoreWebView2Environment env;
        Theme th;

        TabStrip strip;
        RoundedBox addrBox;
        TextBox addr;
        Panel host, loadBar, toolbar;
        TableLayoutPanel row;
        readonly List<Button> icons = new List<Button>();
        readonly ToolTip tip = new ToolTip();
        FormBorderStyle prevBorder;
        FormWindowState prevState;
        Rectangle prevBounds;

        public MainForm(string startUrl)
        {
            this.startUrl = startUrl;
            Text = "Tarayıcı";
            Font = new Font("Segoe UI", 9f);
            Width = 1100; Height = 750;
            MinimumSize = new Size(520, 360);
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
            try { if (File.Exists(ThemeFile)) dark = File.ReadAllText(ThemeFile).Trim() != "acik"; } catch { }
            BuildUi();
            ApplyTheme();
            Load += async (s, e) => await Init();
            FormClosing += (s, e) => SaveTabs();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            ApplyTitleBar();
        }

        void ApplyTitleBar()
        {
            if (!IsHandleCreated) return;
            try
            {
                int v = dark ? 1 : 0;
                if (DwmSetWindowAttribute(Handle, 20, ref v, 4) != 0) DwmSetWindowAttribute(Handle, 19, ref v, 4);
            }
            catch { }
        }

        // ---- arayuz ---------------------------------------------------------
        Button Icon_(string glyph, string tooltip, EventHandler click)
        {
            var b = new Button
            {
                Text = glyph, Font = new Font("Segoe MDL2 Assets", 11f), Width = 38, Height = 32,
                FlatStyle = FlatStyle.Flat, Margin = new Padding(2, 2, 2, 2), TabStop = false,
                UseVisualStyleBackColor = false, Cursor = Cursors.Hand
            };
            b.FlatAppearance.BorderSize = 0;
            b.Click += click;
            tip.SetToolTip(b, tooltip);
            icons.Add(b);
            return b;
        }

        void BuildUi()
        {
            host = new Panel { Dock = DockStyle.Fill };
            loadBar = new Panel { Dock = DockStyle.Top, Height = 2, Visible = false };

            strip = new TabStrip { Dock = DockStyle.Top };
            strip.TabSelected += i => { if (i != cur) Fire(ShowTab(i)); };
            strip.TabClosed += i => Fire(CloseTab(i));
            strip.NewTabRequested += () => Fire(NewTab(Home));

            toolbar = new Panel { Dock = DockStyle.Top, Height = 46 };
            row = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, ColumnCount = 6, RowCount = 1, Padding = new Padding(6, 5, 6, 5)
            };
            for (int i = 0; i < 6; i++)
                row.ColumnStyles.Add(i == 3 ? new ColumnStyle(SizeType.Percent, 100f) : new ColumnStyle(SizeType.AutoSize));

            addrBox = new RoundedBox { Dock = DockStyle.Fill, Margin = new Padding(6, 1, 6, 1) };
            addr = addrBox.Box;
            addr.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; Navigate(addr.Text); }
            };
            addr.Enter += (s, e) => BeginInvoke((Action)(() => addr.SelectAll()));

            Button fav = null, opt = null;
            fav = Icon_("", "Favoriler", (s, e) => ShowFav(fav));
            opt = Icon_("", "Seçenekler", (s, e) => ShowOptions(opt));
            row.Controls.Add(Icon_("", "Geri", (s, e) => { if (web != null && web.CanGoBack) web.GoBack(); }), 0, 0);
            row.Controls.Add(Icon_("", "İleri", (s, e) => { if (web != null && web.CanGoForward) web.GoForward(); }), 1, 0);
            row.Controls.Add(Icon_("", "Yenile (F5)", (s, e) => { if (web != null) web.Reload(); }), 2, 0);
            row.Controls.Add(addrBox, 3, 0);
            row.Controls.Add(fav, 4, 0);
            row.Controls.Add(opt, 5, 0);
            toolbar.Controls.Add(row);

            Controls.Add(host);
            Controls.Add(loadBar);
            Controls.Add(toolbar);
            Controls.Add(strip);
        }

        void ApplyTheme()
        {
            th = dark ? Theme.Dark() : Theme.Light();
            BackColor = th.Strip;
            strip.T = th;
            strip.Invalidate();
            toolbar.BackColor = th.Bar;
            row.BackColor = th.Bar;
            loadBar.BackColor = th.Accent;
            host.BackColor = th.PageBack;
            addrBox.Fill = th.Input; addrBox.Edge = th.Border; addrBox.Page = th.Bar; addrBox.Accent = th.Accent;
            addr.ForeColor = th.Text;
            addrBox.LayoutBox();
            addrBox.Invalidate();
            foreach (var b in icons)
            {
                b.BackColor = th.Bar; b.ForeColor = th.Text;
                b.FlatAppearance.MouseOverBackColor = th.Hover;
                b.FlatAppearance.MouseDownBackColor = th.Hover;
            }
            ApplyTitleBar();
            ApplyWebScheme();
        }

        void ApplyWebScheme()
        {
            try
            {
                if (web != null && web.CoreWebView2 != null)
                    web.CoreWebView2.Profile.PreferredColorScheme =
                        dark ? CoreWebView2PreferredColorScheme.Dark : CoreWebView2PreferredColorScheme.Light;
            }
            catch { }
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
            RefreshStrip();
            await ShowTab(Math.Max(0, Math.Min(start, tabs.Count - 1)));
        }

        // ---- sekmeler -------------------------------------------------------
        void RefreshStrip()
        {
            strip.Titles = tabs.Select(t => string.IsNullOrEmpty(t.Title) ? "Sekme" : t.Title).ToList();
            strip.Selected = cur;
            strip.Invalidate();
        }

        async Task NewTab(string url)
        {
            tabs.Add(new Tab { Url = url });
            RefreshStrip();
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
                ApplyWebScheme();
                RefreshStrip();
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
            if (i < cur) { cur--; RefreshStrip(); return; }
            if (i > cur) { RefreshStrip(); return; }
            cur = -1;
            RefreshStrip();
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
                if (!addr.Focused) addr.Text = c.Source;
                if (cur >= 0 && cur < tabs.Count) tabs[cur].Url = c.Source;
            };
            c.DocumentTitleChanged += (s, e) =>
            {
                if (wv != web || cur < 0 || cur >= tabs.Count) return;
                tabs[cur].Title = c.DocumentTitle;
                RefreshStrip();
            };
            c.NavigationStarting += (s, e) => { if (wv == web) loadBar.Visible = true; };
            c.NavigationCompleted += (s, e) => { if (wv == web) loadBar.Visible = false; };
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
            toolbar.Visible = !on;
            strip.Visible = !on;
            loadBar.Visible = false;
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
        ContextMenuStrip NewMenu()
        {
            var m = new ContextMenuStrip { Renderer = new ToolStripProfessionalRenderer(new ThemeColors(th)) };
            m.BackColor = th.Bar; m.ForeColor = th.Text; m.ShowImageMargin = false;
            m.Font = new Font("Segoe UI", 9.5f);
            return m;
        }

        ToolStripItem Item(ToolStripItemCollection items, string text, EventHandler click)
        {
            var it = items.Add(text, null, click);
            it.ForeColor = th.Text; it.BackColor = th.Bar;
            it.Padding = new Padding(4, 4, 4, 4);
            return it;
        }

        void ShowOptions(Control anchor)
        {
            var m = NewMenu();
            Item(m.Items, dark ? "Açık moda geç" : "Koyu moda geç", (s, e) => ToggleTheme());
            Item(m.Items, noImages ? "Resimleri aç" : "Resimleri kapat", (s, e) => ToggleImages());
            Item(m.Items, "Bu sekmeyi kapat", (s, e) => Fire(CloseTab(cur)));
            Item(m.Items, "Tanı (edge://gpu)", (s, e) => Fire(NewTab("edge://gpu")));
            m.Show(anchor, new Point(0, anchor.Height));
        }

        void ToggleTheme()
        {
            dark = !dark;
            try { File.WriteAllText(ThemeFile, dark ? "koyu" : "acik"); } catch { }
            ApplyTheme();
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
            var m = NewMenu();
            Item(m.Items, "★  Bu sayfayı favorilere ekle", (s, e) =>
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
                    Item(m.Items, Trunc(fav[0], 40), (s, e) => { if (web != null && web.CoreWebView2 != null) web.CoreWebView2.Navigate(fav[1]); });
                }
                m.Items.Add(new ToolStripSeparator());
                var rm = new ToolStripMenuItem("Favoriden sil") { ForeColor = th.Text, BackColor = th.Bar };
                foreach (var f in favs)
                {
                    var fav = f;
                    var it = rm.DropDownItems.Add(Trunc(fav[0], 40), null, (s, e) => { favs.Remove(fav); SaveFavs(favs); });
                    it.ForeColor = th.Text; it.BackColor = th.Bar;
                }
                rm.DropDown.BackColor = th.Bar;
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
