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
        private CabCard speedCard, markerCard;
        private static readonly HttpClient aioHttp = new HttpClient { Timeout = TimeSpan.FromMilliseconds(800) };
        private JObject aioStatus;
        private DateTime aioStatusTime = DateTime.MinValue;
        private bool aioPolling;
        private int markHitsAtFirst = -1, markCount, markWithin10;
        private double markMissSum, lastMarkMiss = -1;
        private int lastHits = -1;

        private void InitCabOverlay()
        {
            speedCard = new CabCard { Size = new Size(190, 118), Location = new Point(12, 44), Cursor = Cursors.Hand };
            speedCard.Click += (s, e) => EditTargetSpeed();
            oglMain.Controls.Add(speedCard);

            markerCard = new CabCard
            {
                Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom,
                Location = new Point(12, oglMain.Height - 96 - 120), // clear of the bottom buttons and steer gauge
                Size = new Size(oglMain.Width - 24, 96),
                Visible = false
            };
            oglMain.Controls.Add(markerCard);
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
            speedCard.Invalidate();

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

        private void UpdateMarkerCard()
        {
            bool fresh = aioStatus != null && (DateTime.Now - aioStatusTime).TotalSeconds < 5;
            if (!fresh)
            {
                // ponytail: hide after 5 s without data; a "marker offline" state can come if drivers want it
                markerCard.Visible = aioStatus != null && markerCard.Visible && (DateTime.Now - aioStatusTime).TotalSeconds < 5;
                return;
            }

            var j = aioStatus;
            int hits = (int?)j["hits"] ?? 0;
            int trees = (int?)j["trees"] ?? 0;
            int nd = (int?)j["nd"] ?? -1;
            double na = (double?)j["na"] ?? 0, nc = (double?)j["nc"] ?? 0, lhm = (double?)j["lhm"] ?? -1;

            // each rise in hits is a new mark; lhm then holds how far that mark landed from its tree
            if (lastHits >= 0 && hits > lastHits && lhm >= 0)
            {
                lastMarkMiss = lhm;
                markCount++;
                markMissSum += lhm;
                if (lhm <= 0.10) markWithin10++;
            }
            if (markHitsAtFirst < 0) markHitsAtFirst = hits;
            lastHits = hits;

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
        private static readonly Color[] DayPal = { Color.White, Color.Black, Color.FromArgb(55, 60, 66), Color.FromArgb(176, 64, 0), Color.FromArgb(200, 205, 211), Color.FromArgb(0, 82, 184), Color.Black };
        private static readonly Color[] NightPal = { Color.FromArgb(22, 25, 29), Color.FromArgb(232, 234, 237), Color.FromArgb(154, 163, 173), Color.FromArgb(242, 163, 58), Color.FromArgb(44, 49, 55), Color.FromArgb(79, 179, 255), Color.FromArgb(22, 25, 29) };
        private Color Bg, Fg, Muted, Accent, Track, Bar, Edge;
        public bool Day = true;

        public string Title = "", Big = "", BigUnit = "", Sub = "", SubAccent = "";
        public CabCol[] Columns;
        public double Progress = -1;

        public CabCard()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Font = new Font("Segoe UI", 10f);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var p = Day ? DayPal : NightPal;
            Bg = p[0]; Fg = p[1]; Muted = p[2]; Accent = p[3]; Track = p[4]; Bar = p[5]; Edge = p[6];
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            g.Clear(Bg);
            using (var edge = new Pen(Edge, 3)) g.DrawRectangle(edge, 1, 1, Width - 3, Height - 3);

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
