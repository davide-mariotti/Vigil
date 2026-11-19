using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace VigilApp
{
    public class MainForm : Form
    {
        static readonly Color Accent = Color.FromArgb(0xc2, 0x41, 0x0c);
        const int HistorySize = 600;   // one reading a second: ten minutes

        public CheckBox chkStartup, chkAlerts;
        public NumericUpDown nudAlertAt;
        public event EventHandler StartupChanged, AlertsChanged, RestartAsAdmin, RestartPlain;

        readonly Label lblPc, lblMode;
        readonly Button btnMode;
        readonly Panel banner;
        readonly FastList list;
        readonly Chart chart;
        readonly Dictionary<string, ListViewItem> items = new Dictionary<string, ListViewItem>();
        readonly Dictionary<string, List<float>> history = new Dictionary<string, List<float>>();
        string shownKeys = "", selectedKey;
        bool quiet, driverPageOpened;
        List<Reading> last = new List<Reading>();
        bool lastFull;

        public MainForm(bool startup, bool alertsOn, int alertAt)
        {
            Text = "Vigil " + System.Reflection.Assembly.GetExecutingAssembly().GetName().Version.ToString(3);
            Icon = L.AppIcon(new Size(32, 32));
            Font = new Font("Segoe UI", 9F);
            ClientSize = new Size(760, 700);
            MinimumSize = new Size(560, 480);
            StartPosition = FormStartPosition.CenterScreen;

            // Docked controls: the Fill one goes in first, then the bottom ones from the edge in, then the top ones.
            list = new FastList();
            list.Dock = DockStyle.Fill;
            list.View = View.Details;
            list.FullRowSelect = true;
            list.HideSelection = false;
            list.MultiSelect = false;
            list.HeaderStyle = ColumnHeaderStyle.Nonclickable;
            list.Columns.Add(L.T("Sensore", "Sensor"), 300);
            list.Columns.Add(L.T("Valore", "Value"), 120, HorizontalAlignment.Right);
            list.Columns.Add("Min", 110, HorizontalAlignment.Right);
            list.Columns.Add("Max", 110, HorizontalAlignment.Right);
            list.SelectedIndexChanged += (s, e) =>
            {
                if (list.SelectedItems.Count == 0) return;
                selectedKey = (string)list.SelectedItems[0].Tag;
                ShowChart();
            };
            Controls.Add(list);

            chart = new Chart();
            chart.Dock = DockStyle.Bottom;
            chart.Height = 170;
            chart.Accent = Accent;
            Controls.Add(chart);

            var settings = new FlowLayoutPanel();
            settings.Dock = DockStyle.Bottom;
            settings.Height = 40;
            settings.Padding = new Padding(8, 8, 8, 0);
            settings.WrapContents = false;
            chkStartup = new CheckBox();
            chkStartup.AutoSize = true;
            chkStartup.Text = L.T("Avvia con Windows", "Start with Windows");
            chkStartup.Checked = startup;
            chkStartup.Margin = new Padding(3, 3, 24, 3);
            new ToolTip().SetToolTip(chkStartup, VigilContext.Admin
                ? L.T("Da amministratore: parte con tutti i sensori, senza chiedere conferma (attività pianificata di Windows).",
                      "As administrator: starts with every sensor, no confirmation asked (a Windows scheduled task).")
                : L.T("Parte in modalità base. Per avere tutti i sensori all'accensione, attivalo da amministratore.",
                      "Starts in base mode. For every sensor at logon, turn it on as administrator."));
            chkStartup.CheckedChanged += (s, e) => { if (!quiet) Fire(StartupChanged); };
            chkAlerts = new CheckBox();
            chkAlerts.AutoSize = true;
            chkAlerts.Text = L.T("Avvisami se CPU o GPU superano", "Warn me if CPU or GPU go over");
            chkAlerts.Checked = alertsOn;
            chkAlerts.CheckedChanged += (s, e) => { nudAlertAt.Enabled = chkAlerts.Checked; Fire(AlertsChanged); };
            nudAlertAt = new NumericUpDown();
            nudAlertAt.Minimum = 50;
            nudAlertAt.Maximum = 110;
            nudAlertAt.Value = alertAt;
            nudAlertAt.Width = 50;
            nudAlertAt.Enabled = alertsOn;
            nudAlertAt.ValueChanged += (s, e) => Fire(AlertsChanged);
            var lblDeg = new Label();
            lblDeg.AutoSize = true;
            lblDeg.Margin = new Padding(3, 6, 3, 3);
            lblDeg.Text = L.T("°C per più di un minuto", "°C for over a minute");
            settings.Controls.AddRange(new Control[] { chkStartup, chkAlerts, nudAlertAt, lblDeg });
            Controls.Add(settings);

            banner = new Panel();
            banner.Dock = DockStyle.Top;
            banner.Height = 44;
            banner.Padding = new Padding(12, 6, 10, 6);
            lblMode = new Label();
            lblMode.Dock = DockStyle.Fill;
            lblMode.TextAlign = ContentAlignment.MiddleLeft;
            lblMode.Text = L.T("Lettura dei sensori in corso...", "Reading the sensors...");
            btnMode = new Button();
            btnMode.Dock = DockStyle.Right;
            btnMode.AutoSize = true;
            btnMode.Visible = false;
            btnMode.Click += BtnMode_Click;
            banner.Controls.Add(lblMode);
            banner.Controls.Add(btnMode);
            banner.BackColor = Color.FromArgb(0xf4, 0xf4, 0xf5);
            Controls.Add(banner);

            lblPc = new Label();
            lblPc.Dock = DockStyle.Top;
            lblPc.Padding = new Padding(12, 10, 12, 6);
            lblPc.Font = new Font("Segoe UI", 10F);
            lblPc.Height = 40;
            Controls.Add(lblPc);
        }

        void Fire(EventHandler ev) { if (ev != null) ev(this, EventArgs.Empty); }

        public void SetStartupQuietly(bool on) { quiet = true; chkStartup.Checked = on; quiet = false; }

        // Portable version: no autostart (it would need the registry), a note where the checkbox was
        public void ShowPortable()
        {
            chkStartup.Enabled = false;
            chkStartup.Checked = false;
            chkStartup.Text = L.T("Portable: impostazioni in Vigil.ini", "Portable: settings in Vigil.ini");
            Text += " portable";
        }

        public void SetMode(bool admin, bool driver, string summary)
        {
            lblPc.Text = summary;
            lblPc.Height = TextRenderer.MeasureText(summary, lblPc.Font).Height + lblPc.Padding.Vertical + 4;
            if (!admin)
            {
                lblMode.Text = L.T("Modalità base. Temperatura e consumi della CPU, scheda madre, ventole e dischi si leggono solo da amministratore.",
                                   "Base mode. CPU temperature and power, motherboard, fans and disks can only be read as administrator.");
                btnMode.Text = L.T("Riavvia come amministratore", "Restart as administrator");
                Banner(Color.FromArgb(0xfe, 0xf3, 0xc7), true);
            }
            else if (!driver)
            {
                lblMode.Text = L.T("Manca il driver PawnIO: senza, CPU, scheda madre e ventole restano mute. Installalo, poi riavvia Vigil.",
                                   "The PawnIO driver is missing: without it CPU, motherboard and fans stay silent. Install it, then restart Vigil.");
                btnMode.Text = L.T("Scarica PawnIO", "Get PawnIO");
                Banner(Color.FromArgb(0xfe, 0xf3, 0xc7), true);
            }
            else
            {
                lblMode.Text = L.T("Modalità completa: Vigil legge tutti i sensori che il PC espone.",
                                   "Full mode: Vigil reads every sensor your PC exposes.");
                Banner(Color.FromArgb(0xdc, 0xfc, 0xe7), false);
            }
        }

        void Banner(Color back, bool button)
        {
            banner.BackColor = back;
            btnMode.Visible = button;
        }

        void BtnMode_Click(object sender, EventArgs e)
        {
            if (!VigilContext.Admin) { Fire(RestartAsAdmin); return; }
            if (driverPageOpened) { Fire(RestartPlain); return; }
            // Through Explorer: a browser started by an elevated Vigil would run as administrator too
            Process.Start("explorer.exe", "https://pawnio.eu/");
            driverPageOpened = true;
            btnMode.Text = L.T("Installato: riavvia Vigil", "Installed: restart Vigil");
        }

        public void ShowReadings(List<Reading> all, bool full)
        {
            foreach (var r in all)
            {
                if (r.Value == null) continue;
                List<float> h;
                if (!history.TryGetValue(r.Key, out h)) history[r.Key] = h = new List<float>(HistorySize + 1);
                h.Add(r.Value.Value);
                if (h.Count > HistorySize) h.RemoveAt(0);
            }
            last = all;
            lastFull = full;
            if (Visible && WindowState != FormWindowState.Minimized) Fill();
        }

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            if (Visible) Fill();
        }

        void Fill()
        {
            var rows = last.Where(r => Rules.Visible(r, lastFull))
                           .OrderBy(r => r.GroupRank).ThenBy(r => r.Group).ThenBy(r => r.GroupKey)
                           .ThenBy(r => Rules.TypeRank(r.Type)).ThenBy(r => r.Index).ToList();
            string keys = string.Join("|", rows.Select(r => r.Key));
            list.BeginUpdate();
            if (keys != shownKeys) Rebuild(rows);
            shownKeys = keys;
            foreach (var r in rows)
            {
                ListViewItem it = items[r.Key];
                Set(it, 1, Rules.Format(r.Type, r.Value.Value));
                Set(it, 2, r.Min.HasValue ? Rules.Format(r.Type, r.Min.Value) : "");
                Set(it, 3, r.Max.HasValue ? Rules.Format(r.Type, r.Max.Value) : "");
            }
            list.EndUpdate();
            ShowChart();
        }

        // A sensor appeared or went: lay the list out again, keeping selection and scroll.
        void Rebuild(List<Reading> rows)
        {
            string top = list.TopItem != null ? (string)list.TopItem.Tag : null;
            list.Items.Clear();
            list.Groups.Clear();
            items.Clear();
            var groups = new Dictionary<string, ListViewGroup>();
            foreach (var r in rows)
            {
                ListViewGroup g;
                if (!groups.TryGetValue(r.GroupKey, out g)) { g = new ListViewGroup(r.Group); groups[r.GroupKey] = g; list.Groups.Add(g); }
                var it = new ListViewItem(new[] { r.Name, "", "", "" }, g);
                it.Tag = r.Key;
                list.Items.Add(it);
                items[r.Key] = it;
            }
            if (selectedKey == null || !items.ContainsKey(selectedKey))
            {
                // first look: the main temperature, if there is one
                var main = Rules.MainTemps(rows, lastFull).FirstOrDefault();
                selectedKey = main != null ? main.Key : rows.Count > 0 ? rows[0].Key : null;
            }
            if (selectedKey != null) items[selectedKey].Selected = true;
            if (top != null && items.ContainsKey(top)) list.TopItem = items[top];
        }

        static void Set(ListViewItem it, int i, string text)
        {
            if (it.SubItems[i].Text != text) it.SubItems[i].Text = text;
        }

        void ShowChart()
        {
            Reading r = selectedKey == null ? null : last.FirstOrDefault(x => x.Key == selectedKey);
            List<float> h = null;
            if (r != null) history.TryGetValue(r.Key, out h);
            chart.Data = h;
            chart.Type = r != null ? r.Type : null;
            chart.Title = r != null ? r.Group + "  ·  " + r.Name : null;
            chart.Invalidate();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); }   // closing the window keeps Vigil in the tray
            base.OnFormClosing(e);
        }
    }

    // A ListView that doesn't flicker when its numbers change every second
    public class FastList : ListView
    {
        public FastList() { DoubleBuffered = true; }
    }

    // The last ten minutes of one sensor
    public class Chart : Panel
    {
        public List<float> Data;
        public string Type, Title;
        public Color Accent;

        public Chart()
        {
            DoubleBuffered = true;
            ResizeRedraw = true;
            BackColor = Color.White;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(BackColor);
            if (Data == null || Data.Count < 2)
            {
                TextRenderer.DrawText(g, L.T("Scegli un sensore per vederne gli ultimi 10 minuti.", "Pick a sensor to see its last 10 minutes."),
                                      Font, ClientRectangle, SystemColors.GrayText, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                return;
            }
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var bold = new Font(Font, FontStyle.Bold))
                TextRenderer.DrawText(g, Title, bold, new Point(10, 8), ForeColor);

            var plot = new Rectangle(86, 34, Width - 104, Height - 62);
            if (plot.Width < 20 || plot.Height < 20) return;
            float lo = Data.Min(), hi = Data.Max();
            float pad = Math.Max((hi - lo) * 0.15f, Math.Abs(hi) * 0.02f + 0.5f);
            lo -= pad; hi += pad;
            if (lo < 0 && Data.Min() >= 0) lo = 0;   // a load or a speed never goes below zero

            using (var grid = new Pen(Color.FromArgb(0xe4, 0xe4, 0xe7)))
                for (int i = 0; i <= 2; i++)
                {
                    float v = hi - (hi - lo) * i / 2;
                    int y = plot.Top + plot.Height * i / 2;
                    g.DrawLine(grid, plot.Left, y, plot.Right, y);
                    TextRenderer.DrawText(g, Rules.Format(Type, v), Font, new Rectangle(0, y - 9, plot.Left - 8, 18),
                                          SystemColors.GrayText, TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
                }

            var pts = new PointF[Data.Count];
            float step = plot.Width / 599f;   // the newest reading on the right edge, ten minutes across
            for (int i = 0; i < Data.Count; i++)
                pts[i] = new PointF(plot.Right - (Data.Count - 1 - i) * step, plot.Bottom - (Data[i] - lo) / (hi - lo) * plot.Height);
            using (var line = new Pen(Accent, 2f))
            {
                line.LineJoin = LineJoin.Round;
                g.DrawLines(line, pts);
            }

            var flags = TextFormatFlags.Top;
            TextRenderer.DrawText(g, L.T("10 min fa", "10 min ago"), Font, new Point(plot.Left, plot.Bottom + 6), SystemColors.GrayText, flags);
            Size now = TextRenderer.MeasureText(L.T("ora", "now"), Font);
            TextRenderer.DrawText(g, L.T("ora", "now"), Font, new Point(plot.Right - now.Width, plot.Bottom + 6), SystemColors.GrayText, flags);
        }
    }
}
