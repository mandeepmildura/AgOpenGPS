using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using AgOpenGPS.Properties;
using OpenTK.Graphics.OpenGL;

namespace AgOpenGPS
{
    // Sandy Soil cab screen v3: the map fills the window and everything else is a floating
    // rounded island drawn in GDI+ (flat fills, 1.5 px edges, one line-icon set, one palette).
    // Display and button wiring only: every button calls the stock handler, so stock safety
    // checks stay in charge. "Classic screen" in the top-left menu switches back.
    public partial class FormGPS
    {
        private bool cabUiOn, cabNoGps, cabLayingOut, markDayDirty, cabMapStyled;
        public bool CabUiOn => cabUiOn; // map drawing (vehicle, tool, lines) restyles itself when this is on
        private int cabTick;
        private int cabGlY => cabUiOn ? (int)(180 * CabIsland.S) : 0; // moves the stock on-map turn arrows clear of the speed dial
        private CabIsland isHead, isXt, isStat, isRail, isSpeed, isZoom, isTrees, isDock;
        private Control[] cabPopups;

        private void InitCabUi()
        {
            cabUiOn = true;
            MinimumSize = new Size(640, 480); // small or heavily scaled cab screens: the islands shrink to fit (CabIsland.S)
            isHead = Island(PaintHead); isXt = Island(PaintXt); isStat = Island(PaintStat);
            isRail = Island(PaintRail); isSpeed = Island(PaintSpeed); isZoom = Island(PaintZoom);
            isTrees = Island(PaintTrees); isDock = Island(PaintDock);
            isDock.Radius = 30;

            // stock pop-up panels live on the form; lift them above the full-window map when they open
            cabPopups = new Control[] { panelNavigation, flp1, panelSim, lblHardwareMessage, panelDrag };
            foreach (var c in cabPopups)
                c.VisibleChanged += (s, e) =>
                {
                    var k = (Control)s;
                    if (!k.Visible) return;
                    if (k == panelNavigation) k.Location = new Point(isRail.Right + 8, isRail.Top); // beside the rail, not over it
                    k.BringToFront();
                };

            oglMain.Resize += (s, e) => CabLayout();
            CabLayout();
        }

        private CabIsland Island(Action<Graphics, CabIsland> painter)
        {
            var i = new CabIsland { Painter = painter };
            oglMain.Controls.Add(i);
            return i;
        }

        // one menu entry to switch between this screen and the classic one (takes effect on restart)
        private void AddScreenSwitchMenu()
        {
            bool cab = Settings.Default.setSandy_cabUi;
            var mi = new ToolStripMenuItem(cab ? "Classic screen" : "New screen");
            mi.Click += (s, e) =>
            {
                Settings.Default.setSandy_cabUi = !cab;
                Settings.Default.Save();
                TimedMessageBox(4000, mi.Text, "Restart AgOpenGPS to switch");
            };
            fileToolStripMenuItem.DropDownItems.Add(mi);
            if (!cab) return;
            // The cab screen has no window buttons, and nothing near a driving hand may close the app:
            // Exit lives here, behind an "are you sure?" question.
            Action<string, EventHandler> add = (text, click) => fileToolStripMenuItem.DropDownItems.Add(new ToolStripMenuItem(text, null, click));
            fileToolStripMenuItem.DropDownItems.Add(new ToolStripSeparator());
            add("Minimise", btnMinimizeMainForm_Click);
            add("Window / full screen", btnMaximizeMainForm_Click);
            add("Exit AgOpenGPS", (s, e) =>
            {
                // the stock exit box counts down and closes by itself, so ask first
                if (AgOpenGPS.Forms.FormDialog.ShowQuestion("Exit AgOpenGPS", "Are you sure you want to close AgOpenGPS?") == DialogResult.OK) btnShutdown_Click(s, e);
            });
        }

        // called after the stock layout code and on every map resize
        private void CabLayout()
        {
            if (!cabUiOn || isDock == null || cabLayingOut) return;
            cabLayingOut = true;
            if (oglMain.Bounds != ClientRectangle) oglMain.Bounds = ClientRectangle;
            oglMain.BringToFront();
            foreach (var c in cabPopups) if (c.Visible) c.BringToFront();

            // The layout is designed for 1350 x 900. On anything smaller every island shrinks by the same factor,
            // so nothing overlaps and the bottom row never falls off the screen.
            CabIsland.S = Math.Max(0.4f, Math.Min(1f, Math.Min(oglMain.Width / 1350f, oglMain.Height / 900f)));
            float w = oglMain.Width / CabIsland.S, h = oglMain.Height / CabIsland.S;
            isXt.Put(w / 2 - 150, 16, 300, 106);
            isSpeed.Put(128, 100, 292, 156);
            isZoom.Put(w - 80, 100, 64, 132);
            isTrees.Put(16, h - 232, w - 32, 104);
            isDock.Put(16, h - 112, w - 32, 96);
            cabLayingOut = false;
            UpdateCabUi();
        }

        // called every half second from the main timer, and after every island button press
        private void UpdateCabUi()
        {
            if (!cabUiOn || isDock == null) return;
            CabTheme.Day = isDay;

            int headW = 72 + 68 + Math.Max(TextRenderer.MeasureText(HeadTitle(), CabDraw.F20b).Width,
                                      TextRenderer.MeasureText(HeadSub(), CabDraw.F14).Width);
            float lw = oglMain.Width / CabIsland.S;
            isHead.Put(16, 16, Math.Max(120, Math.Min(headW, lw / 2 - 182)), 68);
            int statW = 10 + TextRenderer.MeasureText(GpsWords() + GpsDetail(), CabDraw.F17b).Width + 44
                      + TextRenderer.MeasureText(ClockText(), CabDraw.F20b).Width + 22;
            isStat.Put(lw - 16 - statW, 16, statW, 68);
            isRail.Put(16, 100, 96, 16 + RailCount() * 74 + (RailCount() - 1) * 4);

            isXt.Visible = isJobStarted && (ct.isContourBtnOn || trk.idx > -1);
            isTrees.Visible = aioFresh;
            isDock.Visible = isJobStarted;
            int simBottom = oglMain.Height - (int)((!isJobStarted ? 16 : aioFresh ? 244 : 124) * CabIsland.S); // sits above whatever is showing
            panelSim.Location = new Point((oglMain.Width - panelSim.Width) / 2, simBottom - panelSim.Height);

            foreach (var i in new[] { isHead, isXt, isStat, isRail, isSpeed, isZoom, isTrees, isDock }) i.Invalidate();

            if (markDayDirty && ++cabTick % 60 == 0) SaveMarkDay(); // at most every 30 s
        }

        // Clean top-down map, heading up, no floor grid. Called from the main timer, so it runs once AFTER the
        // stock settings have loaded (they would overwrite it). Not saved: the View panel can still change it.
        private void CabStyleMapOnce()
        {
            if (cabMapStyled) return;
            cabMapStyled = true;
            camera.PitchInDegrees = 0;
            camera.FollowDirectionHint = true;
            isGridOn = false;
        }

        private void CabDo(Action a) { a(); UpdateCabUi(); }

        // Opens a stock drop-down menu. It appears at its stock position (Windows Forms pins a menu to its
        // owner button, which now sits hidden behind the map) and Windows keeps it on screen.
        private static void CabDrop(ToolStripDropDownItem item) => item.ShowDropDown();

        // ---- text sources -------------------------------------------------------------------
        private string HeadTitle() => isJobStarted ? displayFieldName + (aioFresh ? " · tree marking" : "") : "No field open";
        private string HeadSub() => RegistrySettings.vehicleProfileName + " · " + RegistrySettings.toolProfileName;
        private static string ClockText() => DateTime.Now.ToString("h:mm tt").ToLowerInvariant();

        private string GpsWords()
        {
            if (cabNoGps) return "No GPS";
            switch (pn.fixQuality)
            {
                case 4: return "RTK Fix";
                case 5: return "RTK Float";
                case 2: return "DGPS only";
                case 1: return "GPS only";
                case 8: return "Simulator";
                case 0: return "No fix";
                default: return "Estimate";
            }
        }

        private string GpsDetail() => cabNoGps ? "  check AgIO" : "  " + pn.satellitesTracked + " sats · " + pn.age.ToString("N1") + " s";

        // small distances: mm in metric, inches otherwise
        private string CabSmall(double metres)
        {
            if (!isMetric) return (metres * 39.37).ToString("N1") + " in";
            return Math.Abs(metres) >= 1 ? metres.ToString("N1") + " m" : (metres * 1000).ToString("N0") + " mm"; // "4,800 mm" reads badly
        }

        // ---- painters -----------------------------------------------------------------------
        private void PaintHead(Graphics g, CabIsland isl)
        {
            var menu = new RectangleF(10, 10, 48, 48);
            CabDraw.Tile(g, isl, menu, "menu", null, null, () => CabDrop(fileToolStripMenuItem), 0, 16);
            // minimise sits at the far end of the title, top-left: easy to find, well away from the steering hand
            CabDraw.Tile(g, isl, new RectangleF(isl.Width - 58, 10, 48, 48), "min", null, null, () => btnMinimizeMainForm_Click(this, EventArgs.Empty), 0, 16);
            g.SetClip(new RectangleF(70, 0, isl.Width - 70 - 64, isl.Height));
            string title = HeadTitle(); // a long field name ends in "…" instead of being cut mid-letter
            if (CabDraw.Measure(g, title, CabDraw.F20b) > isl.Width - 72 - 66)
            {
                while (title.Length > 1 && CabDraw.Measure(g, title + "…", CabDraw.F20b) > isl.Width - 72 - 66) title = title.Substring(0, title.Length - 1);
                title += "…";
            }
            CabDraw.Text(g, title, CabDraw.F20b, CabTheme.Ink, 72, 10);
            CabDraw.Text(g, HeadSub(), CabDraw.F14, CabTheme.Muted, 72, 38);
            g.ResetClip();
        }

        private void PaintStat(Graphics g, CabIsland isl)
        {
            bool alarm = isRTK_AlarmOn && sounds.isRTKAlarming; // stock RTK alarm: the chip goes red (the big map text is hidden)
            bool good = !cabNoGps && pn.fixQuality == 4, fair = !cabNoGps && !alarm && (pn.fixQuality == 5 || pn.fixQuality == 8);
            Color fg = good ? CabTheme.Ok : fair ? CabTheme.Warn : CabTheme.Bad;
            Color bg = good ? CabTheme.OkSoft : fair ? CabTheme.WarnSoft : CabTheme.BadSoft;
            float wWords = CabDraw.Measure(g, GpsWords(), CabDraw.F17b), wDet = CabDraw.Measure(g, GpsDetail(), CabDraw.F17);
            var chip = new RectangleF(10, 10, 16 + 12 + 10 + wWords + wDet + 16, 48);
            CabDraw.Fill(g, chip, 16, bg);
            using (var b = new SolidBrush(fg)) g.FillEllipse(b, chip.X + 16, chip.Y + 18, 12, 12);
            CabDraw.TextMid(g, GpsWords(), CabDraw.F17b, fg, chip.X + 38, 34);
            CabDraw.TextMid(g, GpsDetail(), CabDraw.F17, CabTheme.Ink, chip.X + 38 + wWords, 34);
            CabDraw.TextMid(g, ClockText(), CabDraw.F20b, CabTheme.Ink, chip.Right + 16, 34);
        }

        private void PaintXt(Graphics g, CabIsland isl)
        {
            double mm = double.IsNaN(avgPivDistance) ? 0 : avgPivDistance; // stock smoothed cross-track, mm; positive = vehicle right of the line
            int lit = 5 + (int)Math.Max(-5, Math.Min(5, Math.Round(mm / 20.0)));
            float x = (isl.Width - (11 * 19 + 10 * 5)) / 2f;
            for (int i = 0; i < 11; i++, x += 24)
            {
                Color c = i == lit ? (lit == 5 ? CabTheme.Ok : CabTheme.Accent) : i == 5 ? CabTheme.Ink : CabTheme.Ring;
                CabDraw.Fill(g, new RectangleF(x, 12, 19, 9), 4.5f, c);
            }
            string val = isMetric ? Math.Abs(mm).ToString("N0") + " mm" : Math.Abs(mm * 0.03937).ToString("N1") + " in";
            string side = Math.Abs(mm) < 10 ? "on line" : mm > 0 ? "right of line" : "left of line";
            float wv = CabDraw.Measure(g, val, CabDraw.F30b), ws = CabDraw.Measure(g, side, CabDraw.F16s);
            float sx = (isl.Width - (wv + 8 + ws)) / 2f;
            CabDraw.TextMid(g, val, CabDraw.F30b, CabTheme.Ink, sx, 50);
            CabDraw.TextMid(g, side, CabDraw.F16s, CabTheme.Muted, sx + wv + 8, 54);
            CabDraw.TextCentre(g, RowText(), CabDraw.F14s, CabTheme.Muted, isl.Width / 2f, 85);
        }

        // which pass we are on (stock numbering, e.g. 31L) and how far the line has been nudged
        private string RowText()
        {
            if (ct.isContourBtnOn || trk.idx < 0) return "Contour";
            bool ab = trk.gArr[trk.idx].mode == TrackMode.AB;
            int away = ab ? ABLine.howManyPathsAway : curve.howManyPathsAway;
            bool sameWay = ab ? ABLine.isHeadingSameWay : curve.isHeadingSameWay;
            double nudge = trk.gArr[trk.idx].nudgeDistance;
            return "Row " + (away > -1 ? (away + 1) + "R" : -away + "L") + (sameWay ? " ↑" : " ↓")
                + (nudge == 0 ? "" : " · line moved " + CabSmall(Math.Abs(nudge)));
        }

        private int RailCount() => isJobStarted ? 7 : 6;

        private void PaintRail(Graphics g, CabIsland isl)
        {
            float y = 8;
            Action<string, string, Action, int> tile = (icon, label, click, style) =>
            {
                CabDraw.Tile(g, isl, new RectangleF(8, y, 80, 74), icon, label, null, click, style, 18, true);
                y += 78;
            };
            tile("field", "Field", () => btnJobMenu_Click(this, EventArgs.Empty), isJobStarted ? 2 : 1);
            tile("tracks", "Tracks", () => btnTrack_Click(this, EventArgs.Empty), 1);
            tile("tools", "Tools", () => CabDrop(toolStripDropDownButton4), 1);
            tile("view", "View", () => btnNavigationSettings_Click(this, EventArgs.Empty), 1);
            tile("agio", "AgIO", () => btnStartAgIO_Click(this, EventArgs.Empty), 1);
            tile("gear", "Settings", () => CabDrop(toolStripDropDownButton1), 1);
            if (isJobStarted) tile("bound", "Boundary", () => CabDrop(toolStripBtnFieldTools), 1);
        }

        private void PaintSpeed(Graphics g, CabIsland isl)
        {
            isl.Hit(new RectangleF(0, 0, isl.Width, isl.Height), EditTargetSpeed);
            double kmh = double.IsNaN(avgSpeed) ? 0 : avgSpeed, target = Settings.Default.setDisplay_targetSpeed;
            double k = isMetric ? 1 : 0.621371;
            double full = Math.Max(10, target * 2);
            var ring = new RectangleF(14 + 12, 78 - 52, 104, 104);
            using (var track = CabDraw.RoundPen(CabTheme.Ring, 11))
            using (var fill = CabDraw.RoundPen(CabTheme.Ink, 11))
            using (var tick = CabDraw.RoundPen(CabTheme.Accent, 4))
            {
                g.DrawArc(track, ring, 135, 270);
                float sweep = (float)(270 * Math.Max(0.004, Math.Min(1, kmh / full)));
                g.DrawArc(fill, ring, 135, sweep);
                if (target > 0)
                {
                    double a = (135 + 270 * Math.Min(1, target / full)) * Math.PI / 180;
                    float cx = ring.X + 52, cy = ring.Y + 52;
                    g.DrawLine(tick, cx + (float)(43 * Math.Cos(a)), cy + (float)(43 * Math.Sin(a)), cx + (float)(62 * Math.Cos(a)), cy + (float)(62 * Math.Sin(a)));
                }
            }
            CabDraw.TextCentre(g, (kmh * k).ToString("N1"), CabDraw.F40b, CabTheme.Ink, ring.X + 52, 70);
            CabDraw.TextCentre(g, isMetric ? "km/h" : "mph", CabDraw.F14s, CabTheme.Muted, ring.X + 52, 100);

            float tx = 156;
            if (target > 0)
            {
                double diff = (kmh - target) * k;
                CabDraw.Text(g, "Target speed", CabDraw.F14s, CabTheme.Muted, tx, 40);
                CabDraw.Text(g, (target * k).ToString("N1") + (isMetric ? " km/h" : " mph"), CabDraw.F26b, CabTheme.Ink, tx, 58);
                string chip = (diff >= 0 ? "+" : "−") + Math.Abs(diff).ToString("N1") + (Math.Abs(diff) < 0.05 ? "" : diff > 0 ? " fast" : " slow");
                float wc = CabDraw.Measure(g, chip, CabDraw.F15b);
                CabDraw.Fill(g, new RectangleF(tx, 96, wc + 20, 26), 10, CabTheme.AccentSoft);
                CabDraw.TextMid(g, chip, CabDraw.F15b, CabTheme.Accent, tx + 10, 109);
            }
            else
            {
                CabDraw.Text(g, "Target speed", CabDraw.F14s, CabTheme.Muted, tx, 52);
                CabDraw.Text(g, "Tap to set", CabDraw.F20b, CabTheme.Ink, tx, 72);
            }
        }

        private void PaintZoom(Graphics g, CabIsland isl)
        {
            CabDraw.Tile(g, isl, new RectangleF(4, 4, 56, 60), "plus", null, null, () => { camera.ZoomIn(); SetZoom(); }, 1, 18);
            CabDraw.Tile(g, isl, new RectangleF(4, 68, 56, 60), "minus", null, null, () => { camera.ZoomOut(); SetZoom(); }, 1, 18);
            using (var p = new Pen(CabTheme.Edge, 1.5f)) g.DrawLine(p, 14, 66, 50, 66);
        }

        private void PaintTrees(Graphics g, CabIsland isl)
        {
            var ring = new RectangleF(16 + 7, 14 + 7, 62, 62);
            double prog = aioTrees > 0 ? Math.Min(1.0, (double)aioHits / aioTrees) : 0;
            using (var track = new Pen(CabTheme.Ring, 8)) g.DrawEllipse(track, ring);
            using (var fill = CabDraw.RoundPen(CabTheme.Ok, 8)) g.DrawArc(fill, ring, -90, (float)Math.Max(6, 360 * prog));
            CabIcons.Draw(g, "tree", new RectangleF(ring.X + 18, ring.Y + 18, 26, 26), CabTheme.Ok, 2);

            float x = 124;
            Action<string, string, string, bool> col = (label, value, unit, last) =>
            {
                CabDraw.Text(g, label, CabDraw.F14s, CabTheme.Muted, x, 24);
                float wv = CabDraw.Text(g, value, CabDraw.F27b, CabTheme.Ink, x, 44);
                float wu = unit.Length == 0 ? 0 : CabDraw.Text(g, unit, CabDraw.F15, CabTheme.Muted, x + wv + 6, 57);
                x += Math.Max(CabDraw.Measure(g, label, CabDraw.F14s), wv + 6 + wu) + 28;
                if (!last) using (var p = new Pen(CabTheme.Edge, 1.5f)) g.DrawLine(p, x, 22, x, isl.Height - 22);
                x += 28;
            };
            col("Trees marked", aioHits.ToString("N0"), aioTrees > 0 ? "of " + aioTrees.ToString("N0") : "", false);
            if (aioNd < 0) col("Next tree", "–", "", false);
            else col("Next tree", Math.Abs(aioNa).ToString("N1") + " m",
                     (aioNa >= 0 ? "ahead" : "behind") + " · " + CabSmall(Math.Abs(aioNc)) + (aioNc >= 0 ? " right" : " left"), false);
            col("Last mark", lastMarkMiss < 0 ? "–" : CabSmall(lastMarkMiss), lastMarkMiss < 0 ? "" : "off", false);
            col("Today", markCount == 0 ? "–" : CabSmall(markMissSum / markCount),
                markCount == 0 ? "" : "average · " + markWithin10 + " of " + markCount + " within " + CabSmall(0.1), true);
        }

        private void PaintDock(Graphics g, CabIsland isl)
        {
            double snapCm = ToolSettings.Default.setAS_snapDistance;
            string step = CabSmall(snapCm * 0.01);
            float y = 12, h = 72;
            CabDraw.Tile(g, isl, new RectangleF(12, y, 206, h), "left", "Nudge left", step, () => CabDo(() => btnAdjLeft_Click(this, EventArgs.Empty)), 0, 20);
            CabDraw.Tile(g, isl, new RectangleF(228, y, 206, h), "snap", "Snap to line", null, () => CabDo(() => btnSnapToPivot_Click(this, EventArgs.Empty)), 0, 20);
            CabDraw.Tile(g, isl, new RectangleF(444, y, 206, h), "right", "Nudge right", step, () => CabDo(() => btnAdjRight_Click(this, EventArgs.Empty)), 0, 20, false, true);

            var steer = new RectangleF(isl.Width - 12 - 300, y, 300, h);
            CabDraw.Tile(g, isl, steer, "wheel", isBtnAutoSteerOn ? "Steer ON" : "Steer off",
                "angle " + mc.actualSteerAngleDegrees.ToString("N1") + "°",
                () => CabDo(() => btnAutoSteer_Click(this, EventArgs.Empty)), isBtnAutoSteerOn ? 3 : 0, 20, false, false, true);
            CabDraw.Tile(g, isl, new RectangleF(steer.X - 10 - 190, y, 190, h), "uturn", "U-turn",
                (yt.isYouTurnBtnOn ? "on" : "off") + (bnd.isHeadlandOn && bnd.HeadlandDistance.HasValue ? " · " + bnd.HeadlandDistance.Value.ToString("N0") + " m" : ""),
                () => CabDo(() => btnAutoYouTurn_Click(this, EventArgs.Empty)), yt.isYouTurnBtnOn ? 2 : 0, 20);
        }

        // ---- "Today" marker stats survive a restart (same calendar day) ----------------------
        private void LoadMarkDay()
        {
            var p = Settings.Default.setSandy_markDay.Split('|');
            if (p.Length != 4 || p[0] != DateTime.Now.ToString("yyyy-MM-dd")) return;
            int.TryParse(p[1], out markCount);
            int.TryParse(p[2], out markWithin10);
            double.TryParse(p[3], NumberStyles.Float, CultureInfo.InvariantCulture, out markMissSum);
        }

        private void SaveMarkDay()
        {
            markDayDirty = false;
            Settings.Default.setSandy_markDay = string.Join("|", DateTime.Now.ToString("yyyy-MM-dd"), markCount.ToString(),
                markWithin10.ToString(), markMissSum.ToString("R", CultureInfo.InvariantCulture));
            Settings.Default.Save();
        }
    }

    // One palette, day and night. Day is for sunlight (dark ink on white); night is dim.
    public static class CabTheme
    {
        public static bool Day = true;
        private static Color C(int day, int night) => Color.FromArgb(unchecked((int)0xFF000000) | (Day ? day : night));
        public static void Gl(Color c, int alpha = 255) => GL.Color4(c.R, c.G, c.B, (byte)alpha);
        public static Color Ground => C(0xDBD4BF, 0x25271F);
        public static Color Cover => C(0xBCCBA8, 0x34422F);
        public static Color Row => C(0x42684A, 0x86A878);
        public static Color VehHi => C(0x3A424A, 0xAEB6BE);
        public static Color Glass => C(0x8FB4C8, 0x5D8296);
        public static Color Island => C(0xFFFFFF, 0x1D2125);
        public static Color Tile => C(0xF0EDE4, 0x2A2F35);
        public static Color Ink => C(0x14181C, 0xECEEF0);
        public static Color Muted => C(0x636A72, 0x98A1AB);
        public static Color Edge => C(0xD6D1C2, 0x343A41);
        public static Color Ring => C(0xD6D1C2, 0x3A4047); // dial track: strong enough to read in sun
        public static Color Accent => C(0xC2470A, 0xF5A13A);
        public static Color AccentInk => C(0xFFFFFF, 0x1A1206);
        public static Color AccentSoft => C(0xFBE6D8, 0x3D2C14);
        public static Color Ok => C(0x1F7A3A, 0x7FD391);
        public static Color OkSoft => C(0xDFF1E0, 0x1D3524);
        public static Color Warn => C(0x8A5A00, 0xF5C76A);
        public static Color WarnSoft => C(0xFDEBC8, 0x3D2C14);
        public static Color Bad => C(0xB3261E, 0xFF8A80);
        public static Color BadSoft => C(0xFBDAD7, 0x3A1E1C);
    }

    // A floating rounded panel on the map. Its painter draws the content and registers hit areas.
    public class CabIsland : Control
    {
        public static float S = 1; // screen pixels per layout unit; 1 on a 1350 x 900 or larger map
        public float Radius = 24;
        // painters and hit areas work in layout units, whatever the scale
        public new float Width => base.Width / S;
        public new float Height => base.Height / S;
        public void Put(float x, float y, float w, float h)
        {
            var r = Rectangle.Round(new RectangleF(x * S, y * S, w * S, h * S));
            if (Bounds != r) Bounds = r; else OnSizeChanged(EventArgs.Empty); // same pixels, maybe a new scale
        }
        public Action<Graphics, CabIsland> Painter;
        private readonly List<KeyValuePair<RectangleF, Action>> hits = new List<KeyValuePair<RectangleF, Action>>();
        private int pressed = -1;

        public CabIsland()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        }

        public void Hit(RectangleF r, Action a) => hits.Add(new KeyValuePair<RectangleF, Action>(r, a));
        public bool IsPressed(RectangleF r) => pressed >= 0 && pressed < hits.Count && hits[pressed].Key == r;

        // clip to the rounded shape so the map shows in the corners
        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            if (base.Width < 8 || base.Height < 8) return; // collapsed while the window is minimised or still loading
            using (var p = CabDraw.Round(new RectangleF(0, 0, base.Width, base.Height), Radius * S)) Region = new Region(p);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            g.Clear(CabTheme.Island);
            hits.Clear();
            if (base.Width < 8 || base.Height < 8) return;
            g.ScaleTransform(S, S);
            using (var path = CabDraw.Round(new RectangleF(0.75f, 0.75f, Width - 2.5f, Height - 2.5f), Radius))
            using (var edge = new Pen(CabTheme.Edge, 1.5f)) g.DrawPath(edge, path);
            // a drawing fault must never take the guidance screen down: skip this frame and carry on
            try { Painter?.Invoke(g, this); }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            pressed = hits.FindLastIndex(h => h.Key.Contains(e.X / S, e.Y / S));
            Invalidate();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            Action a = pressed >= 0 && pressed < hits.Count && hits[pressed].Key.Contains(e.X / S, e.Y / S) ? hits[pressed].Value : null;
            pressed = -1;
            Invalidate();
            a?.Invoke();
        }
    }

    public static class CabDraw
    {
        private static Font Px(string family, float px, FontStyle style = FontStyle.Regular) => new Font(family, px, style, GraphicsUnit.Pixel);
        public static readonly Font F14 = Px("Segoe UI", 14), F14s = Px("Segoe UI Semibold", 14), F15 = Px("Segoe UI", 15),
            F15b = Px("Segoe UI", 15, FontStyle.Bold), F16s = Px("Segoe UI Semibold", 16), F17 = Px("Segoe UI", 17),
            F17b = Px("Segoe UI", 17, FontStyle.Bold), F19s = Px("Segoe UI Semibold", 19), F20b = Px("Segoe UI", 20, FontStyle.Bold),
            F25b = Px("Segoe UI", 25, FontStyle.Bold), F26b = Px("Segoe UI", 26, FontStyle.Bold), F27b = Px("Segoe UI", 27, FontStyle.Bold),
            F30b = Px("Segoe UI", 30, FontStyle.Bold), F40b = Px("Segoe UI", 40, FontStyle.Bold);
        private static readonly StringFormat Fmt = MakeFmt();

        private static StringFormat MakeFmt()
        {
            var f = new StringFormat(StringFormat.GenericTypographic);
            f.FormatFlags |= StringFormatFlags.MeasureTrailingSpaces | StringFormatFlags.NoWrap;
            return f;
        }

        public static GraphicsPath Round(RectangleF r, float rad)
        {
            var p = new GraphicsPath();
            float d = Math.Max(1, Math.Min(rad * 2, Math.Min(r.Width, r.Height)));
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        public static void Fill(Graphics g, RectangleF r, float rad, Color c)
        {
            using (var p = Round(r, rad)) using (var b = new SolidBrush(c)) g.FillPath(b, p);
        }

        public static Pen RoundPen(Color c, float width) => new Pen(c, width) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };

        public static float Measure(Graphics g, string s, Font f) => g.MeasureString(s, f, PointF.Empty, Fmt).Width;

        // draws at top-left x,y; returns the width
        public static float Text(Graphics g, string s, Font f, Color c, float x, float y)
        {
            using (var b = new SolidBrush(c)) g.DrawString(s, f, b, x, y, Fmt);
            return Measure(g, s, f);
        }

        // vertically centred on cy
        public static float TextMid(Graphics g, string s, Font f, Color c, float x, float cy) => Text(g, s, f, c, x, cy - f.GetHeight(g) / 2);

        // centred on cx, cy
        public static void TextCentre(Graphics g, string s, Font f, Color c, float cx, float cy) => TextMid(g, s, f, c, cx - Measure(g, s, f) / 2, cy);

        // A button inside an island. style: 0 soft tile, 1 plain, 2 inverted (active), 3 accent (Steer ON).
        public static void Tile(Graphics g, CabIsland isl, RectangleF r, string icon, string label, string sub, Action click,
            int style, float rad, bool vertical = false, bool iconRight = false, bool big = false)
        {
            isl.Hit(r, click);
            Color bg = style == 3 ? CabTheme.Accent : style == 2 ? CabTheme.Ink : style == 0 ? CabTheme.Tile : CabTheme.Island;
            Color fg = style == 3 ? CabTheme.AccentInk : style == 2 ? CabTheme.Island : CabTheme.Ink;
            Color subC = style == 3 || style == 2 ? fg : CabTheme.Muted;
            if (style != 1) Fill(g, r, rad, bg);
            if (isl.IsPressed(r)) Fill(g, r, rad, Color.FromArgb(style == 1 || style == 0 ? 36 : 60, CabTheme.Day == (style < 2) ? Color.Black : Color.White));

            float cx = r.X + r.Width / 2, cy = r.Y + r.Height / 2;
            if (label == null) { CabIcons.Draw(g, icon, new RectangleF(cx - 14, cy - 14, 28, 28), fg, 2); return; }
            if (vertical)
            {
                CabIcons.Draw(g, icon, new RectangleF(cx - 14, r.Y + 11, 28, 28), fg, 2);
                TextCentre(g, label, F14s, fg, cx, r.Y + 55);
                return;
            }

            Font lf = big ? F25b : F19s, sf = big ? F15b : F14;
            float ic = big ? 36 : 28, gap = big ? 14 : 12;
            float wt = Math.Max(Measure(g, label, lf), sub == null ? 0 : Measure(g, sub, sf));
            float x = cx - (ic + gap + wt) / 2;
            float tx = iconRight ? x : x + ic + gap;
            CabIcons.Draw(g, icon, new RectangleF(iconRight ? x + wt + gap : x, cy - ic / 2, ic, ic), fg, big ? 2.2f : 2);
            if (sub == null) TextMid(g, label, lf, fg, tx, cy);
            else
            {
                TextMid(g, label, lf, fg, tx, cy - (big ? 11 : 10));
                TextMid(g, sub, sf, subC, tx, cy + (big ? 17 : 14));
            }
        }
    }

    // One line-icon set: 24-unit grid, round 2 px stroke. SVG path data (M L H V A Z, circular arcs only).
    public static class CabIcons
    {
        private static readonly Dictionary<string, string> Data = new Dictionary<string, string>
        {
            ["menu"] = "M4 7h16M4 12h16M4 17h10",
            ["field"] = "M3 6l6-2 6 2 6-2v14l-6 2-6-2-6 2zM9 4v14M15 6v14",
            ["tracks"] = "M8 3L5 21M16 3l3 18M12 5v2M12 11v2M12 17v2",
            ["tools"] = "M14.7 6.3a4 4 0 0 0-5.4 5.2L3.5 17.3a1.8 1.8 0 0 0 2.6 2.6l5.8-5.8a4 4 0 0 0 5.2-5.4l-2.6 2.6-2.4-.4-.4-2.4z",
            ["view"] = "M2.5 12a10.5 10.5 0 0 1 19 0a10.5 10.5 0 0 1-19 0zM9 12a3 3 0 1 0 6 0a3 3 0 1 0-6 0",
            ["agio"] = "M10 12a2 2 0 1 0 4 0a2 2 0 1 0-4 0M8.5 15.5a5 5 0 0 1 0-7M15.5 8.5a5 5 0 0 1 0 7M5.6 18.4a9 9 0 0 1 0-12.8M18.4 5.6a9 9 0 0 1 0 12.8",
            ["gear"] = "M9 12a3 3 0 1 0 6 0a3 3 0 1 0-6 0M12 2v3M12 19v3M2 12h3M19 12h3M4.9 4.9l2.1 2.1M17 17l2.1 2.1M4.9 19.1L7 17M17 7l2.1-2.1",
            ["bound"] = "M4 7l7-3 9 3-2 12-11 1zM10.5 12a1.5 1.5 0 1 0 3 0a1.5 1.5 0 1 0-3 0",
            ["plus"] = "M12 5v14M5 12h14",
            ["minus"] = "M5 12h14",
            ["left"] = "M19 12H6M11 6l-6 6 6 6",
            ["right"] = "M5 12h13M13 6l6 6-6 6",
            ["snap"] = "M12 3v18M4 12h4M5.5 9.5L8 12l-2.5 2.5M20 12h-4M18.5 9.5L16 12l2.5 2.5",
            ["uturn"] = "M7 21V10a5 5 0 0 1 10 0v6M13.5 13l3.5 3.5 3.5-3.5",
            ["wheel"] = "M3 12a9 9 0 1 0 18 0a9 9 0 1 0-18 0M10 12a2 2 0 1 0 4 0a2 2 0 1 0-4 0M3.5 10.5l6.6 1M20.5 10.5l-6.6 1M12 14v7",
            ["tree"] = "M12 22v-7M12 15a5.5 5.5 0 0 1-4.6-8.5a4.8 4.8 0 0 1 9.2 0a5.5 5.5 0 0 1-4.6 8.5z",
            ["min"] = "M6 17h12",
        };
        private static readonly Dictionary<string, GraphicsPath> Cache = new Dictionary<string, GraphicsPath>();

        public static void Draw(Graphics g, string name, RectangleF box, Color c, float stroke)
        {
            if (!Cache.TryGetValue(name, out GraphicsPath path)) Cache[name] = path = Parse(Data[name]);
            var state = g.Save();
            g.TranslateTransform(box.X, box.Y);
            g.ScaleTransform(box.Width / 24f, box.Height / 24f);
            using (var pen = CabDraw.RoundPen(c, stroke * 24f / box.Width)) g.DrawPath(pen, path);
            g.Restore(state);
        }

        public static GraphicsPath Parse(string d)
        {
            var path = new GraphicsPath();
            var tok = Regex.Matches(d, @"[A-Za-z]|-?(?:\d+\.?\d*|\.\d+)");
            int i = 0;
            float x = 0, y = 0, sx = 0, sy = 0;
            Func<float> num = () => float.Parse(tok[i++].Value, CultureInfo.InvariantCulture);
            Func<bool> more = () => i < tok.Count && !char.IsLetter(tok[i].Value[0]);
            while (i < tok.Count)
            {
                char cmd = tok[i++].Value[0];
                bool rel = char.IsLower(cmd);
                switch (char.ToUpperInvariant(cmd))
                {
                    case 'M':
                        x = num() + (rel ? x : 0); y = num() + (rel ? y : 0); sx = x; sy = y; path.StartFigure();
                        while (more()) { float nx = num() + (rel ? x : 0), ny = num() + (rel ? y : 0); path.AddLine(x, y, nx, ny); x = nx; y = ny; }
                        break;
                    case 'L':
                        while (more()) { float nx = num() + (rel ? x : 0), ny = num() + (rel ? y : 0); path.AddLine(x, y, nx, ny); x = nx; y = ny; }
                        break;
                    case 'H':
                        while (more()) { float nx = num() + (rel ? x : 0); path.AddLine(x, y, nx, y); x = nx; }
                        break;
                    case 'V':
                        while (more()) { float ny = num() + (rel ? y : 0); path.AddLine(x, y, x, ny); y = ny; }
                        break;
                    case 'A':
                        while (more())
                        {
                            float r = num(); num(); num();
                            bool large = num() != 0, sweep = num() != 0;
                            float nx = num() + (rel ? x : 0), ny = num() + (rel ? y : 0);
                            Arc(path, x, y, nx, ny, r, large, sweep);
                            x = nx; y = ny;
                        }
                        break;
                    case 'Z':
                        path.CloseFigure(); x = sx; y = sy;
                        break;
                }
            }
            return path;
        }

        // SVG circular arc (rx = ry) from (x0,y0) to (x1,y1)
        private static void Arc(GraphicsPath path, float x0, float y0, float x1, float y1, float r, bool large, bool sweep)
        {
            double hx = (x0 - x1) / 2.0, hy = (y0 - y1) / 2.0, d2 = hx * hx + hy * hy;
            if (d2 < 1e-9) return;
            if (r * r < d2) r = (float)Math.Sqrt(d2);
            double coef = Math.Sqrt(Math.Max(0, (r * r - d2) / d2)) * (large != sweep ? 1 : -1);
            double cx = coef * hy + (x0 + x1) / 2.0, cy = -coef * hx + (y0 + y1) / 2.0;
            double a0 = Math.Atan2(y0 - cy, x0 - cx) * 180 / Math.PI, a1 = Math.Atan2(y1 - cy, x1 - cx) * 180 / Math.PI;
            double delta = a1 - a0;
            if (sweep && delta < 0) delta += 360;
            if (!sweep && delta > 0) delta -= 360;
            path.AddArc((float)(cx - r), (float)(cy - r), 2 * r, 2 * r, (float)a0, (float)delta);
        }
    }
}
