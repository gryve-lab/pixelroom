using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Pixelroom
{
    internal static class Atmosphere
    {
        public static Color Accent { get { return Theme.IsDark ? Color.FromArgb(104, 220, 232) : Color.FromArgb(21, 155, 178); } }
        private static float Clamp(float value) { return Math.Max(0, Math.Min(1, value)); }
        private static float Noise(int x, int y, int seed)
        {
            unchecked { uint n = (uint)(x * 374761393 + y * 668265263 + seed * 1447); n = (n ^ (n >> 13)) * 1274126177; return (n ^ (n >> 16)) / (float)UInt32.MaxValue; }
        }
        public static void Header(Graphics g, Rectangle bounds, float scale) { Draw(g, bounds, scale, 0); }
        public static void Sidebar(Graphics g, Rectangle bounds, float scale) { Draw(g, bounds, scale, 1); }
        public static void Corner(Graphics g, Rectangle bounds, float scale) { Draw(g, bounds, scale, 2); }
        private static void Draw(Graphics g, Rectangle bounds, float scale, int zone)
        {
            if (bounds.Width < 1 || bounds.Height < 1) return;
            GraphicsState state = g.Save(); g.IntersectClip(bounds); g.SmoothingMode = SmoothingMode.AntiAlias;
            float radius = 36 * scale, stepX = radius * 1.5f, stepY = radius * (float)Math.Sqrt(3);
            int columns = (int)Math.Ceiling(bounds.Width / stepX) + 2, rows = (int)Math.Ceiling(bounds.Height / stepY) + 2;
            Color lineColor = Theme.IsDark ? Color.FromArgb(175, 185, 187) : Color.FromArgb(80, 100, 105);
            for (int col = -1; col < columns; col++)
                for (int row = -1; row < rows; row++)
                {
                    float x = col * stepX, y = row * stepY + (Math.Abs(col) % 2) * stepY / 2;
                    float fade = zone == 1 ? (float)Math.Pow(Clamp((y / bounds.Height - .32f) / .68f), 1.3) * (1 - .85f * Clamp(x / bounds.Width))
                        : zone == 2 ? Clamp((x - (bounds.Width - 260 * scale)) / (260 * scale)) * Clamp((y + 15 * scale) / (bounds.Height + 15 * scale))
                        : Clamp((x - bounds.Width * .16f) / (bounds.Width * .84f));
                    if (fade <= .005f) continue;
                    PointF[] points = new PointF[6];
                    for (int i = 0; i < 6; i++)
                    {
                        double angle = i * Math.PI / 3; float px = x + (float)Math.Cos(angle) * radius, py = y + (float)Math.Sin(angle) * radius;
                        points[i] = new PointF(bounds.X + px, bounds.Y + py);
                    }
                    using (Pen line = new Pen(Color.FromArgb((int)(255 * .42f * fade), lineColor), 1.05f * scale))
                        for (int edgeIndex = 0; edgeIndex < 3; edgeIndex++) g.DrawLine(line, points[edgeIndex], points[edgeIndex + 1]);
                    if (Noise(col, row, 103) > .85f && fade > .2f)
                    {
                        using (SolidBrush fill = new SolidBrush(Color.FromArgb((int)(255 * .09f * fade), Accent))) g.FillPolygon(fill, points);
                        int edge = (int)(Noise(col, row, 151) * 2.99f); PointF from = points[edge], to = points[edge + 1];
                        using (Pen glow = new Pen(Color.FromArgb((int)(9 * fade), Accent), 9 * scale)) g.DrawLine(glow, from, to);
                        using (Pen glow = new Pen(Color.FromArgb((int)(20 * fade), Accent), 5 * scale)) g.DrawLine(glow, from, to);
                        using (Pen glow = new Pen(Color.FromArgb((int)(35 * fade), Accent), 2.7f * scale)) g.DrawLine(glow, from, to);
                        using (Pen neon = new Pen(Color.FromArgb((int)(255 * Math.Min(.9f, 1.15f * fade * .68f)), Accent), 1.6f * scale)) g.DrawLine(neon, from, to);
                    }
                }
            if (zone == 0)
            {
                Color clear = Color.FromArgb(0, Theme.Background);
                using (LinearGradientBrush fade = new LinearGradientBrush(bounds, Theme.Background, Theme.Background, LinearGradientMode.Vertical))
                {
                    fade.InterpolationColors = new ColorBlend { Positions = new float[] { 0, .12f, .60f, 1 }, Colors = new Color[] { Theme.Background, clear, clear, Theme.Background } };
                    g.FillRectangle(fade, bounds);
                }
                Rectangle end = new Rectangle(Math.Max(bounds.Left, bounds.Right - (int)(36 * scale)), bounds.Top, Math.Min(bounds.Width, (int)(36 * scale)), bounds.Height);
                using (LinearGradientBrush fade = new LinearGradientBrush(end, clear, Theme.Background, LinearGradientMode.Horizontal)) g.FillRectangle(fade, end);
            }
            g.Restore(state);
        }
    }
    internal sealed class HoneycombSidebar : TableLayoutPanel
    {
        public HoneycombSidebar() { DoubleBuffered = true; ResizeRedraw = true; }
        protected override void OnPaintBackground(PaintEventArgs e) { base.OnPaintBackground(e); Atmosphere.Sidebar(e.Graphics, ClientRectangle, DeviceDpi / 96f); }
    }
    internal sealed class PatternListBox : ListBox
    {
        protected override void OnResize(EventArgs e) { base.OnResize(e); Invalidate(); }
        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            if (m.Msg != 0xF || !IsHandleCreated || Parent == null) return;
            int occupied = Items.Count == 0 ? 0 : Math.Min(ClientSize.Height, Math.Max(0, Items.Count - TopIndex) * ItemHeight);
            if (occupied >= ClientSize.Height) return;
            using (Graphics g = Graphics.FromHwnd(Handle))
            {
                g.SetClip(new Rectangle(0, occupied, ClientSize.Width, ClientSize.Height - occupied));
                Atmosphere.Sidebar(g, new Rectangle(-Left, -Top, Parent.ClientSize.Width, Parent.ClientSize.Height), DeviceDpi / 96f);
            }
        }
    }
    internal sealed class FooterPanel : Panel
    {
        public FooterPanel() { DoubleBuffered = true; ResizeRedraw = true; }
        protected override void OnPaintBackground(PaintEventArgs e) { base.OnPaintBackground(e); Atmosphere.Corner(e.Graphics, ClientRectangle, DeviceDpi / 96f); }
    }
}
