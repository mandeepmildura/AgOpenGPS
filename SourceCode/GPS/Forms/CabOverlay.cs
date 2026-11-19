using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows.Forms;
using AgOpenGPS.Properties;
using Newtonsoft.Json.Linq;

namespace AgOpenGPS
{
    // Sandy Soil cab overlay: a big speed card with target speed, and a live
    // tree-marker panel fed by the AiO relay (relay_aio.ps1, GET /status).
    // Display only: nothing here steers or switches anything.
    public partial class FormGPS
    {
        private CabCard speedCard, markerCard, barUturn, barSteer;
        private CabCard[] cabBar;
        private static readonly HttpClient aioHttp = new HttpClient { Timeout = TimeSpan.FromMilliseconds(800) };
        private JObject aioStatus;
        private DateTime aioStatusTime = DateTime.MinValue;
        private bool aioPolling;
        private int markHitsAtFirst = -1, markCount, markWithin10;
        private double markMissSum, lastMarkMiss = -1;
        private int lastHits = -1;

        private void InitCabOverlay()
        {
            ApplySandyLookOnce();
            LoadMarkDay();
            AddScreenSwitchMenu();
            if (Settings.Default.setSandy_cabUi) { InitCabUi(); return; }
            LabelLeftRail();
            lblFix.AutoSize = true;
            lblFix.Font = new Font("Segoe UI Semibold", 11f);
            lblFix.Padding = new Padding(8, 1, 8, 1);
            lblFix.ForeColor = Color.Black;

            speedCard = new CabCard { Size = new Size(190, 118), Location = new Point(12, 44), Cursor = Cursors.Hand };
            speedCard.Click += (s, e) => EditTargetSpeed();
            oglMain.Controls.Add(speedCard);

            markerCard = new CabCard { Visible = false };
            oglMain.Controls.Add(markerCard);

            // Big labelled buttons along the bottom, as in the design. They call the stock
            // handlers, so the stock buttons, sounds and safety checks stay in charge.
            cabBar = new[]
            {
                BarButton("‹  Nudge left", () => btnAdjLeft_Click(this, EventArgs.Empty)),
                BarButton("Snap to line", () => btnSnapToPivot_Click(this, EventArgs.Empty)),
                BarButton("Nudge right  ›", () => btnAdjRight_Click(this, EventArgs.Empty)),
                barUturn = BarButton("U-turn: off", () => btnAutoYouTurn_Click(this, EventArgs.Empty)),
                barSteer = BarButton("Steer off", () => btnAutoSteer_Click(this, EventArgs.Empty)),
            };
            oglMain.Resize += (s, e) => LayoutCabBar();
            LayoutCabBar();
        }

        private CabCard BarButton(string text, Action click)
        {
            var b = new CabCard { Title = text, IsButton = true, Cursor = Cursors.Hand, Visible = false };
            b.Click += (s, e) => { click(); UpdateCabBar(); };
            oglMain.Controls.Add(b);
            return b;
        }

        private void LayoutCabBar()
        {
            const int h = 56, w = 150, gap = 8;
            int y = oglMain.Height - 122 - h; // above the section buttons, bottom tool row and steer gauge
            for (int i = 0; i < 3; i++) cabBar[i].Bounds = new Rectangle(12 + i * (w + gap), y, w, h);
            barSteer.Bounds = new Rectangle(oglMain.Width - 12 - 190, y, 190, h);
            barUturn.Bounds = new Rectangle(barSteer.Left - gap - w, y, w, h);
            markerCard.Bounds = new Rectangle(12, y - gap - 96, oglMain.Width - 24, 96);
        }

        private void UpdateCabBar()
        {
            barUturn.Title = "U-turn: " + (yt.isYouTurnBtnOn ? "on" : "off");
            barSteer.Title = isBtnAutoSteerOn ? "Steer ON" : "Steer off";
            barSteer.Hot = isBtnAutoSteerOn;
            foreach (var b in cabBar)
            {
                b.Visible = isJobStarted; // no field open = no line to steer, nudge or turn on
                b.Day = isDay;
                b.Invalidate();
            }
        }

        // Sun colours (light frame, sand ground, no dark texture) as the starting point.
        // Once per settings file, so a driver's own colour changes afterwards stick.
        private static void ApplySandyLookOnce()
        {
            if (Settings.Default.setSandy_lookVersion >= 1) return;
            Settings.Default.setDisplay_colorDayFrame = Color.FromArgb(244, 243, 239);
            Settings.Default.setDisplay_colorFieldDay = Color.FromArgb(214, 208, 190);
            Settings.Default.setDisplay_isTextureOn = false;
            Settings.Default.setSandy_lookVersion = 1;
            Settings.Default.Save();
        }

        // A word under each left-rail icon, as in the design.
        private void LabelLeftRail()
        {
            var f = new Font("Segoe UI Semibold", 9.5f);
            foreach (var (b, text) in new[] { (btnNavigationSettings, "View"), (btnJobMenu, "Field"), (btnStartAgIO, "AgIO") })
            {
                b.Text = text; b.Font = f; b.ForeColor = Color.Black;
                b.TextImageRelation = TextImageRelation.ImageAboveText;
                b.ImageAlign = ContentAlignment.MiddleCenter; b.TextAlign = ContentAlignment.BottomCenter;
            }
            foreach (var (i, text) in new[] { (toolStripDropDownButton4, "Tools"), (toolStripDropDownButton1, "Settings"), (toolStripBtnFieldTools, "Field tools") })
            {
                i.Text = text; i.Font = f; i.ForeColor = Color.Black;
                i.DisplayStyle = ToolStripItemDisplayStyle.ImageAndText;
                i.TextImageRelation = TextImageRelation.ImageAboveText;
                i.Owner.AutoSize = true; // grow the strip to fit the word
            }
        }

        // Plain words for the GPS state, coloured so it reads at a glance in the sun.
        private void UpdateGpsPill()
        {
            cabNoGps = false;
            string q;
            switch (pn.fixQuality)
            {
                case 4: q = "RTK Fix"; break;
                case 5: q = "RTK Float"; break;
                case 2: q = "DGPS only"; break;
                case 1: q = "GPS only"; break;
                case 8: q = "Simulator"; break;
                case 0: q = "No fix"; break;
                default: q = "Estimate"; break;
            }
            lblFix.ForeColor = Color.Black; // stock night mode turns labels white
            lblFix.Text = q + " · " + pn.satellitesTracked + " sats · age " + pn.age.ToString("N1") + " s";
            lblFix.BackColor = pn.fixQuality == 4 ? Color.FromArgb(198, 232, 198)
                             : pn.fixQuality == 5 || pn.fixQuality == 8 ? Color.FromArgb(255, 222, 170)
                             : Color.FromArgb(255, 196, 196);
        }

        // called when GPS data stops arriving
        private void ShowNoGpsTopBar()
        {
            if (cabUiOn) { cabNoGps = true; return; }
            if (speedCard == null) return;
            lblFix.Text = "No GPS · check AgIO";
            lblFix.BackColor = Color.FromArgb(255, 196, 196);
            lblSpeed.Text = DateTime.Now.ToString("h:mm");
        }

        private void EditTargetSpeed()
        {
            using (var form = new FormNumeric(0, 30, Settings.Default.setDisplay_targetSpeed))
            {
                if (form.ShowDialog(this) != DialogResult.OK) return;
                Settings.Default.setDisplay_targetSpeed = Math.Round(form.ReturnValue, 1);
                Settings.Default.Save();
            }
        }

        // called every half second from the main timer
        private void UpdateCabOverlay()
        {
            if (cabUiOn) { PollAio(); UpdateMarkerStats(); UpdateCabUi(); return; }
            if (speedCard == null) return;

            double kmh = avgSpeed;
            double target = Settings.Default.setDisplay_targetSpeed;
            string unit = isMetric ? "km/h" : "mph";
            double shown = isMetric ? kmh : kmh * 0.621371;
            speedCard.Title = "Speed";
            speedCard.Big = shown.ToString("N1");
            speedCard.BigUnit = unit;
            if (target > 0)
            {
                double diff = kmh - target;
                speedCard.Sub = "Target " + (isMetric ? target : target * 0.621371).ToString("N1") + " · ";
                speedCard.SubAccent = (diff >= 0 ? "+" : "−") + Math.Abs(isMetric ? diff : diff * 0.621371).ToString("N1");
            }
            else
            {
                speedCard.Sub = "Tap to set target";
                speedCard.SubAccent = "";
            }
            speedCard.Day = markerCard.Day = isDay;
            lblSpeed.Text = DateTime.Now.ToString("h:mm"); // speed lives in the card; the top-right corner is the clock
            var railText = isDay ? Color.Black : Color.FromArgb(232, 234, 237);
            btnNavigationSettings.ForeColor = btnJobMenu.ForeColor = btnStartAgIO.ForeColor = railText;
            toolStripDropDownButton4.ForeColor = toolStripDropDownButton1.ForeColor = toolStripBtnFieldTools.ForeColor = railText;
            speedCard.Invalidate();

            UpdateCabBar();
            PollAio();
            UpdateMarkerCard();
        }

        private void PollAio()
        {
            string url = Settings.Default.setAiO_statusUrl;
            if (aioPolling || string.IsNullOrWhiteSpace(url)) return;
            aioPolling = true;
            Task.Run(async () =>
            {
                try
                {
                    string body = await aioHttp.GetStringAsync(url).ConfigureAwait(false);
                    var j = JObject.Parse(body);
                    BeginInvoke((Action)(() => { aioStatus = j; aioStatusTime = DateTime.Now; }));
                }
                catch { } // no relay = no panel; the next poll tries again
                finally { aioPolling = false; }
            });
        }

        // latest AiO numbers, shared by the classic marker card and the new tree panel
        private bool aioFresh;
        private int aioHits, aioTrees, aioNd = -1;
        private double aioNa, aioNc;

        // reads the latest AiO status into the fields above and counts new marks; false = no live data
        private bool UpdateMarkerStats()
        {
            // ponytail: no data for 5 s = panel hidden; a "marker offline" state can come if drivers want it
            aioFresh = aioStatus != null && (DateTime.Now - aioStatusTime).TotalSeconds < 5;
            if (!aioFresh) return false;

            var j = aioStatus;
            int hits = aioHits = (int?)j["hits"] ?? 0;
            aioTrees = (int?)j["trees"] ?? 0;
            aioNd = (int?)j["nd"] ?? -1;
            aioNa = (double?)j["na"] ?? 0; aioNc = (double?)j["nc"] ?? 0;
            double lhm = (double?)j["lhm"] ?? -1;

            // each rise in hits is a new mark; lhm then holds how far that mark landed from its tree
            if (lastHits >= 0 && hits > lastHits && lhm >= 0)
            {
                lastMarkMiss = lhm;
                markCount++;
                markMissSum += lhm;
                if (lhm <= 0.10) markWithin10++;
                markDayDirty = true;
            }
            if (markHitsAtFirst < 0) markHitsAtFirst = hits;
            lastHits = hits;
            return true;
        }

        private void UpdateMarkerCard()
        {
            if (!UpdateMarkerStats()) { markerCard.Visible = false; return; }
            int hits = aioHits, trees = aioTrees, nd = aioNd;
            double na = aioNa, nc = aioNc;

            markerCard.Columns = new[]
            {
                new CabCol("Trees marked", hits.ToString("N0"), trees > 0 ? "of " + trees.ToString("N0") : ""),
                nd < 0 ? new CabCol("Next tree", "–", "")
                       : new CabCol("Next tree", Math.Abs(na).ToString("N1") + " m",
                                    (na >= 0 ? "ahead" : "behind") + " · " + Math.Abs(nc * 100).ToString("N0") + " cm " + (nc >= 0 ? "right" : "left")),
                new CabCol("Last mark", lastMarkMiss < 0 ? "–" : (lastMarkMiss * 100).ToString("N0") + " cm", lastMarkMiss < 0 ? "" : "off"),
                new CabCol("This run", markCount == 0 ? "–" : (markMissSum / markCount * 100).ToString("N0") + " cm",
                           markCount == 0 ? "" : "avg · " + markWithin10 + " of " + markCount + " within 10 cm"),
            };
            markerCard.Progress = trees > 0 ? Math.Min(1.0, (double)hits / trees) : -1;
            markerCard.Visible = true;
            markerCard.Invalidate();
        }
    }

    public struct CabCol
    {
        public string Label, Value, Unit;
        public CabCol(string label, string value, string unit) { Label = label; Value = value; Unit = unit; }
    }

    // Flat dark card drawn in one OnPaint: either a big single value (speed) or columns (job panel).
    public class CabCard : Control
    {
        // Day = sunlight: black on white, bold, dark accents (glare washes out dark screens and pale colours).
        // Night = dark card so it doesn't dazzle. Follows AgOpenGPS's day/night button.
        private static readonly Color[] DayPal = { Color.White, Color.Black, Color.FromArgb(55, 60, 66), Color.FromArgb(176, 64, 0), Color.FromArgb(200, 205, 211), Color.FromArgb(0, 82, 184), Color.FromArgb(90, 90, 90) };
        private static readonly Color[] NightPal = { Color.FromArgb(22, 25, 29), Color.FromArgb(232, 234, 237), Color.FromArgb(154, 163, 173), Color.FromArgb(242, 163, 58), Color.FromArgb(44, 49, 55), Color.FromArgb(79, 179, 255), Color.FromArgb(22, 25, 29) };
        private Color Bg, Fg, Muted, Accent, Track, Bar, Edge;
        public bool Day = true;
        public bool IsButton, Hot; // button: Title centred; Hot: filled with the accent colour (Steer ON)

        public string Title = "", Big = "", BigUnit = "", Sub = "", SubAccent = "";
        public CabCol[] Columns;
        public double Progress = -1;

        public CabCard()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Font = new Font("Segoe UI", 10f);
        }

        private const float Radius = 14;

        private static GraphicsPath Rounded(RectangleF r, float rad)
        {
            var p = new GraphicsPath();
            float d = rad * 2;
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        // clip to the rounded shape so the map shows in the corners
        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            using (var p = Rounded(new RectangleF(0, 0, Width, Height), Radius)) Region = new Region(p);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var p = Day ? DayPal : NightPal;
            Bg = p[0]; Fg = p[1]; Muted = p[2]; Accent = p[3]; Track = p[4]; Bar = p[5]; Edge = p[6];
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            if (Hot) { Bg = Accent; Fg = Color.White; Edge = Accent; }
            g.Clear(Bg);
            using (var path = Rounded(new RectangleF(1, 1, Width - 3, Height - 3), Radius))
            using (var edge = new Pen(Edge, 2)) g.DrawPath(edge, path);

            if (IsButton)
            {
                using (var f = new Font("Segoe UI Semibold", 13f))
                    TextRenderer.DrawText(g, Title, f, ClientRectangle, Fg, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                return;
            }

            using (var small = new Font("Segoe UI Semibold", 10.5f))
            using (var mid = new Font("Segoe UI", 17f, FontStyle.Bold))
            using (var unitF = new Font("Segoe UI Semibold", 12f))
            using (var bigF = new Font("Segoe UI", 42f, FontStyle.Bold))
            using (var fg = new SolidBrush(Fg))
            using (var mut = new SolidBrush(Muted))
            using (var acc = new SolidBrush(Accent))
            {
                if (Columns == null)
                {
                    g.DrawString(Title, small, mut, 14, 8);
                    var bigSize = g.MeasureString(Big, bigF);
                    g.DrawString(Big, bigF, fg, 8, 24);
                    g.DrawString(BigUnit, unitF, mut, 8 + bigSize.Width - 8, 52);
                    var subW = TextRenderer.MeasureText(g, Sub, small, Size.Empty, TextFormatFlags.NoPadding).Width;
                    TextRenderer.DrawText(g, Sub, small, new Point(14, Height - 28), Muted, TextFormatFlags.NoPadding);
                    TextRenderer.DrawText(g, SubAccent, small, new Point(14 + subW, Height - 28), Accent, TextFormatFlags.NoPadding);
                    return;
                }

                float x = 16, colW = (Width - 32) / (float)Columns.Length;
                foreach (var c in Columns)
                {
                    g.DrawString(c.Label, small, mut, x, 10);
                    g.DrawString(c.Value, mid, fg, x - 3, 30);
                    var vw = g.MeasureString(c.Value, mid).Width;
                    g.DrawString(c.Unit, small, mut, new RectangleF(x + vw - 6, 38, colW - vw, 20));
                    x += colW;
                }
                if (Progress >= 0)
                {
                    var r = new RectangleF(16, Height - 20, Width - 32, 6);
                    using (var tb = new SolidBrush(Track)) g.FillRectangle(tb, r);
                    using (var bb = new SolidBrush(Bar)) g.FillRectangle(bb, r.X, r.Y, (float)(r.Width * Progress), r.Height);
                }
            }
        }
    }
}
