using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;

namespace Pixelroom
{
    internal sealed class StatisticsProgress : Control
    {
        private int maximum = 1, value;
        private ProgressBarStyle style;
        public int Maximum { get { return maximum; } set { maximum = Math.Max(1, value); Invalidate(); } }
        public int Value { get { return value; } set { this.value = Math.Max(0, value); Invalidate(); } }
        public ProgressBarStyle Style { get { return style; } set { style = value; Invalidate(); } }
        public StatisticsProgress() { DoubleBuffered = true; AccessibleName = "Statistics loading progress"; }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e); float s = DeviceDpi / 96f; RectangleF track = new RectangleF(0, (Height - 5 * s) / 2, Width, 5 * s);
            Artwork.Card(e.Graphics, track, Theme.Line, Theme.Line, 2 * s);
            float amount = style == ProgressBarStyle.Marquee ? .33f : Math.Min(1, value / (float)maximum);
            if (amount > 0) Artwork.Card(e.Graphics, new RectangleF(track.X, track.Y, track.Width * amount, track.Height), Atmosphere.Accent, Atmosphere.Accent, 2 * s);
        }
    }
    internal sealed class StatisticsSummary : Control
    {
        public string Downloads = "—", Bandwidth = "—", ActiveFiles = "—";
        public StatisticsSummary() { DoubleBuffered = true; Dock = DockStyle.Fill; Margin = Padding.Empty; }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e); float s = DeviceDpi / 96f; int gap = (int)(10 * s), w = (Width - gap * 2) / 3;
            string[] labels = { "DOWNLOADS", "BANDWIDTH", "FILES WITH DOWNLOADS" }, values = { Downloads, Bandwidth, ActiveFiles };
            for (int i = 0; i < 3; i++)
            {
                Rectangle rect = new Rectangle(i * (w + gap), 4, i == 2 ? Width - i * (w + gap) : w, Height - 10);
                Artwork.Card(e.Graphics, rect, Theme.Surface, Theme.Line, 9 * s);
                using (Font small = Theme.Font(8)) TextRenderer.DrawText(e.Graphics, labels[i], small, new Rectangle(rect.Left + (int)(14 * s), rect.Top + (int)(9 * s), rect.Width - (int)(25 * s), (int)(18 * s)), Theme.Muted, TextFormatFlags.EndEllipsis);
                using (Font large = Theme.Font(21, FontStyle.Bold)) TextRenderer.DrawText(e.Graphics, values[i], large, new Rectangle(rect.Left + (int)(12 * s), rect.Top + (int)(25 * s), rect.Width - (int)(24 * s), (int)(37 * s)), Theme.Ink, TextFormatFlags.EndEllipsis);
            }
        }
    }
    internal sealed class StatisticsChart : Control
    {
        private List<StatisticsPoint> points = new List<StatisticsPoint>();
        private readonly ToolTip tip = new ToolTip();
        private readonly List<RectangleF> hits = new List<RectangleF>();
        private int hovered = -1;
        public string ContentName = "Select a file to view its chart", Message = "", RangeLabel = "";
        public bool Bandwidth, Lifetime, ShowTitle = true;
        public StatisticsChart() { DoubleBuffered = true; Dock = DockStyle.Fill; Margin = Padding.Empty; ResizeRedraw = true; AccessibleName = "Download statistics chart"; }
        public void SetData(string name, IEnumerable<StatisticsPoint> data, bool lifetime, string range)
        { ContentName = name; points = data.ToList(); Lifetime = lifetime; RangeLabel = range; Message = ""; hovered = -1; tip.Hide(this); Invalidate(); }
        public void Clear(string message) { ContentName = "Statistics"; Message = message; points.Clear(); tip.Hide(this); Invalidate(); }
        private string ValueText(long value) { return Bandwidth ? FileRules.SizeText(value) : value.ToString("N0", CultureInfo.CurrentCulture); }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e); hits.Clear(); Graphics g = e.Graphics; float s = DeviceDpi / 96f; g.SmoothingMode = SmoothingMode.AntiAlias;
            if (ShowTitle) using (Font font = Theme.Font(10, FontStyle.Bold)) TextRenderer.DrawText(g, ContentName, font, new Rectangle((int)(13 * s), (int)(4 * s), Width - (int)(26 * s), (int)(23 * s)), Theme.Ink, TextFormatFlags.EndEllipsis);
            using (Font font = Theme.Font(8)) TextRenderer.DrawText(g, RangeLabel, font, new Rectangle((int)(13 * s), (int)((ShowTitle ? 27 : 0) * s), Width - (int)(26 * s), (int)(19 * s)), Theme.Muted, TextFormatFlags.EndEllipsis);
            RectangleF plot = new RectangleF(71 * s, (ShowTitle ? 58 : 28) * s, Math.Max(1, Width - 91 * s), Math.Max(1, Height - (ShowTitle ? 91 : 61) * s));
            if (points.Count == 0 || plot.Height < 20 * s)
            {
                using (Font font = Theme.Font(9)) TextRenderer.DrawText(g, Message.Length > 0 ? Message : "No statistics are available for this period.", font, new Rectangle((int)(15 * s), (int)((ShowTitle ? 55 : 25) * s), Width - (int)(30 * s), Math.Max(1, Height - (int)((ShowTitle ? 60 : 30) * s))), Theme.Muted, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak); return;
            }
            long max = points.Max(p => Bandwidth ? p.Bytes : p.Downloads); double scaleMax = Math.Max(1, max);
            using (Font axis = Theme.Font(7.5f))
            using (Pen grid = new Pen(Theme.Line))
            {
                for (int i = 0; i <= 2; i++)
                {
                    float y = plot.Bottom - i * plot.Height / 2; g.DrawLine(grid, plot.Left, y, plot.Right, y);
                    TextRenderer.DrawText(g, ValueText((long)(scaleMax * i / 2)), axis, new Rectangle(0, (int)(y - 8 * s), (int)(plot.Left - 8 * s), (int)(18 * s)), Theme.Muted, TextFormatFlags.Right | TextFormatFlags.EndEllipsis);
                }
                int[] ticks = points.Count == 1 ? new int[] { 0 } : new int[] { 0, (points.Count - 1) / 2, points.Count - 1 };
                foreach (int i in ticks.Distinct())
                {
                    float x = plot.Left + plot.Width * (i + .5f) / points.Count;
                    string date = Lifetime ? "Lifetime" : points[i].TimeUtc.ToLocalTime().ToString(points.Count > 1 && (points.Last().TimeUtc - points[0].TimeUtc).TotalDays < 2 ? "dd MMM HH:mm" : "dd MMM");
                    TextRenderer.DrawText(g, date, axis, new Rectangle((int)(x - 53 * s), (int)(plot.Bottom + 9 * s), (int)(106 * s), (int)(18 * s)), Theme.Muted, TextFormatFlags.HorizontalCenter);
                }
            }
            Color accent = Atmosphere.Accent; float slot = plot.Width / points.Count;
            for (int i = 0; i < points.Count; i++)
            {
                long value = Bandwidth ? points[i].Bytes : points[i].Downloads; float h = (float)(plot.Height * value / scaleMax);
                RectangleF bar = new RectangleF(plot.Left + i * slot + Math.Min(2 * s, slot * .15f), plot.Bottom - h, Math.Max(.6f, slot - Math.Min(4 * s, slot * .30f)), h);
                if (h > 0) using (SolidBrush fill = new SolidBrush(Color.FromArgb(i == hovered ? 245 : 165, accent))) g.FillRectangle(fill, bar);
                hits.Add(new RectangleF(plot.Left + i * slot, plot.Top, slot, plot.Height));
            }
            if (max == 0) using (Font zero = Theme.Font(9)) TextRenderer.DrawText(g, Bandwidth ? "No bandwidth recorded in this period" : "No downloads recorded in this period", zero, Rectangle.Round(plot), Theme.Muted, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e); int index = hits.FindIndex(r => r.Contains(e.Location)); if (hovered == index) return; hovered = index; Invalidate();
            if (index < 0 || index >= points.Count) { tip.Hide(this); return; }
            StatisticsPoint p = points[index]; tip.Show((Lifetime ? "Lifetime" : p.TimeUtc.ToLocalTime().ToString("dd MMM yyyy HH:mm")) + "\nDownloads: " + p.Downloads.ToString("N0") + "\nBandwidth: " + FileRules.SizeText(p.Bytes) + "\nFree: " + FileRules.SizeText(p.FreeBytes) + " · Premium: " + FileRules.SizeText(p.PaidBytes), this, e.X + 12, e.Y + 12, 6000);
        }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hovered = -1; tip.Hide(this); Invalidate(); }
        protected override void Dispose(bool disposing) { if (disposing) tip.Dispose(); base.Dispose(disposing); }
    }
}
