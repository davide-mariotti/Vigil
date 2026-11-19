using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace VigilApp
{
    // The window is a web page (ui\), shown by WebView2, the Edge engine that ships with Windows 10 and 11.
    // The page comes from the exe's resources: nothing is fetched from the network.
    public class MainForm : Form
    {
        const string Host = "https://vigil.localhost/";
        static readonly Color Bg = Color.FromArgb(0x05, 0x07, 0x06);

        public event Action<Dictionary<string, object>> Message;
        public bool PageReady { get; private set; }

        readonly string dataFolder;
        WebView2 web;

        public MainForm(string dataFolder)
        {
            this.dataFolder = dataFolder;
            Text = "Vigil";
            Icon = L.AppIcon(new Size(32, 32));
            BackColor = Bg;
            StartPosition = FormStartPosition.CenterScreen;
            float dpi;
            using (var g = CreateGraphics()) dpi = g.DpiX / 96f;   // the exe is DPI aware: sizes in real pixels
            ClientSize = new Size((int)(1180 * dpi), (int)(820 * dpi));
            MinimumSize = new Size((int)(760 * dpi), (int)(560 * dpi));
        }

        [DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            // dark title bar (Windows 10 2004+), and on Windows 11 the same black as the page
            int on = 1, black = 0x00060705;   // COLORREF is 0x00BBGGRR
            DwmSetWindowAttribute(Handle, 20, ref on, 4);
            DwmSetWindowAttribute(Handle, 35, ref black, 4);
        }

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            if (Visible) { if (web == null) Open(); }
            else Close_();   // in the tray Vigil weighs a few MB, not the 150 of a browser
        }

        async void Open()
        {
            try
            {
                Libs.LoadNative("WebView2Loader.dll", dataFolder);
                web = new WebView2();
                web.DefaultBackgroundColor = Bg;
                web.Dock = DockStyle.Fill;
                Controls.Add(web);
                var env = await CoreWebView2Environment.CreateAsync(null, dataFolder, null);
                await web.EnsureCoreWebView2Async(env);
                if (web == null) return;   // closed while starting
                var core = web.CoreWebView2;
                core.Settings.AreDevToolsEnabled = false;
                core.Settings.AreDefaultContextMenusEnabled = false;
                core.Settings.IsStatusBarEnabled = false;
                core.Settings.IsZoomControlEnabled = false;
                core.Settings.AreBrowserAcceleratorKeysEnabled = false;
                core.Settings.IsPasswordAutosaveEnabled = false;
                core.Settings.IsGeneralAutofillEnabled = false;
                core.AddWebResourceRequestedFilter(Host + "*", CoreWebView2WebResourceContext.All);
                core.WebResourceRequested += (s, e) => Serve(env, e);
                core.NavigationStarting += (s, e) => { if (!e.Uri.StartsWith(Host)) e.Cancel = true; };   // the page never leaves home
                core.NewWindowRequested += (s, e) => e.Handled = true;
                core.WebMessageReceived += (s, e) =>
                {
                    Dictionary<string, object> m;
                    try { m = Json.Read<Dictionary<string, object>>(e.WebMessageAsJson); } catch { return; }
                    if (m == null) return;
                    object t;
                    if (m.TryGetValue("t", out t) && (string)t == "ready") PageReady = true;
                    if (Message != null) Message(m);
                };
                core.Navigate(Host + "index.html");
            }
            catch (Exception ex)
            {
                Close_();
                var answer = MessageBox.Show(
                    L.T("La finestra di Vigil ha bisogno di Microsoft Edge WebView2, che di solito c'è già in Windows 10 e 11. Vigil continua a lavorare nella tray.\n\nVuoi aprire la pagina per installarlo?\n\n(",
                        "Vigil's window needs Microsoft Edge WebView2, which usually comes with Windows 10 and 11. Vigil keeps working in the tray.\n\nOpen the page to install it?\n\n(") + ex.Message + ")",
                    "Vigil", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                if (answer == DialogResult.Yes)
                    try { System.Diagnostics.Process.Start("explorer.exe", "https://developer.microsoft.com/microsoft-edge/webview2/"); } catch { }
                Hide();
            }
        }

        static readonly Dictionary<string, string> Types = new Dictionary<string, string> {
            { ".html", "text/html; charset=utf-8" }, { ".css", "text/css; charset=utf-8" }, { ".js", "text/javascript; charset=utf-8" },
            { ".woff2", "font/woff2" }, { ".svg", "image/svg+xml" }, { ".png", "image/png" } };

        static void Serve(CoreWebView2Environment env, CoreWebView2WebResourceRequestedEventArgs e)
        {
            string path = new Uri(e.Request.Uri).AbsolutePath.TrimStart('/');
            Stream s = Libs.Open("ui/" + path);
            string type;
            if (s == null || !Types.TryGetValue(Path.GetExtension(path), out type))
            {
                e.Response = env.CreateWebResourceResponse(null, 404, "Not Found", "");
                return;
            }
            e.Response = env.CreateWebResourceResponse(s, 200, "OK", "Content-Type: " + type + "\r\nCache-Control: no-store");
        }

        public void Post(object message)
        {
            if (web == null || web.CoreWebView2 == null || !PageReady) return;
            try { web.CoreWebView2.PostWebMessageAsJson(Json.Write(message)); } catch { }
        }

        void Close_()
        {
            PageReady = false;
            if (web == null) return;
            Controls.Remove(web);
            web.Dispose();
            web = null;
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); }   // closing the window keeps Vigil in the tray
            base.OnFormClosing(e);
        }
    }
}
