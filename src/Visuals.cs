using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Pixelroom
{
    internal static class Artwork
    {
        public static GraphicsPath Round(RectangleF r, float radius)
        {
            float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
            GraphicsPath path = new GraphicsPath();
            if (d <= 0) { path.AddRectangle(r); return path; }
            path.AddArc(r.X, r.Y, d, d, 180, 90); path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); path.AddArc(r.X, r.Bottom - d, d, d, 90, 90); path.CloseFigure(); return path;
        }
        public static void Card(Graphics g, RectangleF r, Color fill, Color line, float radius)
        {
            using (GraphicsPath path = Round(r, radius))
            using (SolidBrush brush = new SolidBrush(fill))
            using (Pen pen = new Pen(line)) { g.FillPath(brush, path); g.DrawPath(pen, path); }
        }
        public static void Icon(Graphics g, string name, RectangleF bounds, Color color, bool filled = false)
        {
            GraphicsState state = g.Save(); g.SmoothingMode = SmoothingMode.AntiAlias; g.TranslateTransform(bounds.X, bounds.Y); g.ScaleTransform(bounds.Width / 24, bounds.Height / 24);
            using (Pen p = new Pen(color, 1.6f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
            using (SolidBrush b = new SolidBrush(color))
            {
                switch (name)
                {
                    case "files":
                        g.DrawRectangle(p, 4, 4, 6, 6); g.DrawRectangle(p, 14, 4, 6, 6); g.DrawRectangle(p, 4, 14, 6, 6); g.DrawRectangle(p, 14, 14, 6, 6); break;
                    case "clock":
                        g.DrawEllipse(p, 3, 3, 18, 18); g.DrawLines(p, new PointF[] { new PointF(12, 7), new PointF(12, 12), new PointF(16, 14) }); break;
                    case "star":
                        PointF[] points = new PointF[10]; for (int i = 0; i < 10; i++) { double a = -Math.PI / 2 + i * Math.PI / 5; float radius = i % 2 == 0 ? 9 : 4.1f; points[i] = new PointF(12 + (float)Math.Cos(a) * radius, 12 + (float)Math.Sin(a) * radius); }
                        if (filled) g.FillPolygon(b, points); else g.DrawPolygon(p, points); break;
                    case "transfer":
                        g.DrawLine(p, 8, 20, 8, 4); g.DrawLines(p, new PointF[] { new PointF(4, 8), new PointF(8, 4), new PointF(12, 8) });
                        g.DrawLine(p, 17, 4, 17, 20); g.DrawLines(p, new PointF[] { new PointF(13, 16), new PointF(17, 20), new PointF(21, 16) }); break;
                    case "upload": case "download":
                        bool up = name == "upload"; g.DrawLines(p, new PointF[] { new PointF(4, 16), new PointF(4, 20), new PointF(20, 20), new PointF(20, 16) });
                        g.DrawLine(p, 12, 3, 12, 15); g.DrawLines(p, up ? new PointF[] { new PointF(7, 8), new PointF(12, 3), new PointF(17, 8) } : new PointF[] { new PointF(7, 10), new PointF(12, 15), new PointF(17, 10) }); break;
                    case "folder":
                        g.DrawLines(p, new PointF[] { new PointF(3, 7), new PointF(3, 5), new PointF(10, 5), new PointF(12, 8), new PointF(21, 8), new PointF(21, 20), new PointF(3, 20), new PointF(3, 7), new PointF(10, 7) }); break;
                    case "settings":
                        g.DrawEllipse(p, 8, 8, 8, 8); for (int i = 0; i < 8; i++) { double a = i * Math.PI / 4; g.DrawLine(p, 12 + (float)Math.Cos(a) * 8, 12 + (float)Math.Sin(a) * 8, 12 + (float)Math.Cos(a) * 10, 12 + (float)Math.Sin(a) * 10); } g.DrawEllipse(p, 4, 4, 16, 16); break;
                    case "search":
                        g.DrawEllipse(p, 3, 3, 13, 13); g.DrawLine(p, 15, 15, 21, 21); break;
                    case "refresh":
                        g.DrawArc(p, 4, 4, 16, 16, 40, 290); g.DrawLines(p, new PointF[] { new PointF(15, 3), new PointF(20, 4), new PointF(19, 9) }); break;
                    case "delete":
                        g.DrawLine(p, 4, 6, 20, 6); g.DrawLines(p, new PointF[] { new PointF(6, 6), new PointF(7, 21), new PointF(17, 21), new PointF(18, 6) }); g.DrawLines(p, new PointF[] { new PointF(9, 6), new PointF(9, 3), new PointF(15, 3), new PointF(15, 6) }); g.DrawLine(p, 10, 10, 10, 17); g.DrawLine(p, 14, 10, 14, 17); break;
                    case "plus": g.DrawLine(p, 5, 12, 19, 12); g.DrawLine(p, 12, 5, 12, 19); break;
                    case "drive":
                        using (GraphicsPath drive = Round(new RectangleF(3, 5, 18, 14), 3)) g.DrawPath(p, drive); g.DrawLine(p, 3, 13, 21, 13); g.FillEllipse(b, 16, 15, 2, 2); break;
                    case "image":
                        g.DrawRectangle(p, 3, 3, 18, 18); g.DrawEllipse(p, 7, 7, 3, 3); g.DrawLines(p, new PointF[] { new PointF(4, 18), new PointF(11, 12), new PointF(15, 16), new PointF(18, 13), new PointF(21, 16) }); break;
                    case "video":
                        using (GraphicsPath video = Round(new RectangleF(3, 4, 18, 16), 3)) g.DrawPath(p, video); g.DrawPolygon(p, new PointF[] { new PointF(10, 8), new PointF(16, 12), new PointF(10, 16) }); break;
                    case "audio":
                        g.DrawLines(p, new PointF[] { new PointF(9, 17), new PointF(9, 5), new PointF(19, 3), new PointF(19, 15) }); g.DrawEllipse(p, 3, 16, 6, 4); g.DrawEllipse(p, 13, 14, 6, 4); break;
                    case "share": g.DrawLine(p, 8, 11, 17, 6); g.DrawLine(p, 8, 13, 17, 18); g.DrawEllipse(p, 3, 9, 5, 5); g.DrawEllipse(p, 17, 3, 5, 5); g.DrawEllipse(p, 17, 16, 5, 5); break;
                    case "check": g.DrawLines(p, new PointF[] { new PointF(5, 12), new PointF(10, 17), new PointF(19, 7) }); break;
                    case "chevron": g.DrawLines(p, new PointF[] { new PointF(7, 9), new PointF(12, 14), new PointF(17, 9) }); break;
                    case "chart": g.DrawLines(p, new PointF[] { new PointF(4, 4), new PointF(4, 20), new PointF(21, 20) }); g.DrawLine(p, 8, 15, 8, 17); g.DrawLine(p, 13, 10, 13, 17); g.DrawLine(p, 18, 5, 18, 17); break;
                    default:
                        g.DrawLines(p, new PointF[] { new PointF(14, 3), new PointF(5, 3), new PointF(5, 21), new PointF(19, 21), new PointF(19, 8), new PointF(14, 3), new PointF(14, 8), new PointF(19, 8) }); g.DrawLine(p, 9, 12, 15, 12); g.DrawLine(p, 9, 16, 15, 16); break;
                }
            }
            g.Restore(state);
        }
        public static void Contours(Graphics g, Rectangle bounds)
        {
            GraphicsState state = g.Save(); g.SetClip(bounds); g.SmoothingMode = SmoothingMode.AntiAlias;
            using (Pen line = new Pen(Color.FromArgb(Theme.IsDark ? 12 : 16, Theme.Ink)))
                for (int i = 0; i < 9; i++) { float s = 100 + i * 28; g.DrawEllipse(line, bounds.Right - s * .72f - 35, bounds.Top - s * .62f, s * 1.5f, s); }
            g.Restore(state);
        }
    }

    internal class BackdropPanel : Panel
    {
        public BackdropPanel() { DoubleBuffered = true; ResizeRedraw = true; }
        protected override void OnPaint(PaintEventArgs e) { base.OnPaint(e); Atmosphere.Header(e.Graphics, ClientRectangle, DeviceDpi / 96f); }
    }
    internal sealed class SurfaceCard : Panel
    {
        public SurfaceCard() { DoubleBuffered = true; ResizeRedraw = true; Padding = new Padding(9); }
        protected override void OnPaint(PaintEventArgs e) { base.OnPaint(e); e.Graphics.SmoothingMode = SmoothingMode.AntiAlias; Artwork.Card(e.Graphics, new RectangleF(.5f, .5f, Width - 1, Height - 1), Theme.Surface, Theme.Line, 12); }
    }
    internal sealed class BrandMark : Control
    {
        public BrandMark() { DoubleBuffered = true; AccessibleName = "Pixelroom"; }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias; float s = DeviceDpi / 96f;
            RectangleF tile = new RectangleF(0, 7 * s, 36 * s, 36 * s); Artwork.Card(e.Graphics, tile, Theme.Primary, Theme.Primary, 10 * s);
            Artwork.Icon(e.Graphics, "files", new RectangleF(7 * s, 14 * s, 22 * s, 22 * s), Theme.PrimaryText);
            using (Font font = Theme.Font(18, FontStyle.Bold)) TextRenderer.DrawText(e.Graphics, "pixelroom", font, new Rectangle((int)(47 * s), (int)(5 * s), Width - (int)(47 * s), (int)(42 * s)), Theme.BrandInk, TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
    }
    internal sealed class AccountBadge : Label
    {
        public AccountBadge() { DoubleBuffered = true; }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias; Artwork.Card(e.Graphics, new RectangleF(.5f, .5f, Width - 1, Height - 1), Theme.Surface, Theme.Line, Height / 2);
            using (SolidBrush dot = new SolidBrush(Text == "Connected" ? Theme.Ink : Theme.Muted)) e.Graphics.FillEllipse(dot, 12, Height / 2 - 3, 6, 6);
            TextRenderer.DrawText(e.Graphics, Text, Font, new Rectangle(27, 0, Width - 33, Height), Theme.Muted, TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter);
        }
    }
    internal sealed class StatsStrip : Control
    {
        private string[] values = { "0", "0 B", "0" };
        public StatsStrip() { DoubleBuffered = true; ResizeRedraw = true; AccessibleName = "Library summary"; }
        public void UpdateValues(string count, string size, string favorites) { values = new string[] { count, size, favorites }; AccessibleDescription = count + " files, " + size + ", " + favorites + " favorites"; Invalidate(); }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e); Graphics g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias; float scale = DeviceDpi / 96f, gap = 12 * scale, width = (Width - gap * 2) / 3f;
            string[] labels = { "TOTAL FILES", "TOTAL SIZE", "FAVORITES" }, icons = { "files", "drive", "star" };
            using (Font small = Theme.Font(8, FontStyle.Bold)) using (Font big = Theme.Font(21, FontStyle.Bold))
                for (int i = 0; i < 3; i++)
                {
                    float x = i * (width + gap); Artwork.Card(g, new RectangleF(x + .5f, .5f, width - 1, Height - 2), Theme.Surface, Theme.Line, 11 * scale);
                    TextRenderer.DrawText(g, labels[i], small, new Rectangle((int)(x + 17 * scale), (int)(14 * scale), (int)(width - 55 * scale), (int)(20 * scale)), Theme.Muted, TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
                    TextRenderer.DrawText(g, values[i], big, new Rectangle((int)(x + 15 * scale), (int)(37 * scale), (int)(width - 30 * scale), (int)(40 * scale)), Theme.Ink, TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
                    Artwork.Icon(g, icons[i], new RectangleF(x + width - 38 * scale, 15 * scale, 19 * scale, 19 * scale), Theme.Muted);
                }
        }
    }
    internal sealed class FieldFrame : Panel
    {
        public string IconName;
        public FieldFrame() { DoubleBuffered = true; ResizeRedraw = true; Tag = "field"; }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e); float s = DeviceDpi / 96f; e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Artwork.Card(e.Graphics, new RectangleF(.5f, .5f, Width - 1, Height - 1), Theme.Input, ContainsFocus ? Theme.Accent : Theme.Line, 8 * s);
            if (IconName != null) Artwork.Icon(e.Graphics, IconName, new RectangleF(12 * s, (Height - 17 * s) / 2, 17 * s, 17 * s), Theme.Muted);
        }
    }
    internal sealed class FilterCombo : ComboBox
    {
        public FilterCombo() { DrawMode = DrawMode.OwnerDrawFixed; FlatStyle = FlatStyle.Flat; ItemHeight = 24; Font = Theme.Font(9); }
        protected override void OnDrawItem(DrawItemEventArgs e)
        {
            if (e.Index < 0) return; Color back = (e.State & DrawItemState.Selected) != 0 && (e.State & DrawItemState.ComboBoxEdit) == 0 ? Theme.Pale : Theme.Input;
            using (SolidBrush b = new SolidBrush(back)) e.Graphics.FillRectangle(b, e.Bounds);
            TextRenderer.DrawText(e.Graphics, GetItemText(Items[e.Index]), Font, new Rectangle(e.Bounds.X + 9, e.Bounds.Y, e.Bounds.Width - 15, e.Bounds.Height), Theme.Ink, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }
        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            if (m.Msg == 0xF)
                using (Graphics g = Graphics.FromHwnd(Handle)) using (SolidBrush b = new SolidBrush(Theme.Input)) using (Pen p = new Pen(Theme.Line))
                { float s = DeviceDpi / 96f; int arrow = (int)(28 * s); g.FillRectangle(b, Width - arrow, 1, arrow - 1, Height - 2); g.DrawRectangle(p, 0, 0, Width - 1, Height - 1); Artwork.Icon(g, "chevron", new RectangleF(Width - 23 * s, (Height - 16 * s) / 2, 16 * s, 16 * s), Theme.Muted); }
        }
    }
    internal sealed class FileGrid : DataGridView
    { public FileGrid() { DoubleBuffered = true; } }

    internal sealed class ThemeOption : RadioButton
    {
        public string ThemeName;
        public ThemeOption(string name)
        { ThemeName = name; Text = name; AccessibleName = name + " theme"; AutoSize = false; Appearance = Appearance.Button; FlatAppearance.BorderSize = 0; Cursor = Cursors.Hand; SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true); }
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias; float s = DeviceDpi / 96f;
            using (SolidBrush background = new SolidBrush(Parent != null ? Parent.BackColor : Theme.Background)) g.FillRectangle(background, ClientRectangle);
            Artwork.Card(g, new RectangleF(1, 1, Width - 3, Height - 3), Theme.Surface, Checked ? Theme.Accent : Theme.Line, 10 * s);
            bool dark = ThemeName == "Dark"; Color bg = dark ? Color.FromArgb(25, 25, 25) : Color.FromArgb(235, 235, 235), surface = dark ? Color.FromArgb(38, 38, 38) : Color.FromArgb(252, 252, 252), line = dark ? Color.FromArgb(66, 66, 66) : Color.FromArgb(202, 202, 202);
            RectangleF preview = new RectangleF(12 * s, 12 * s, Width - 24 * s, Height - 55 * s); Artwork.Card(g, preview, bg, line, 5 * s);
            using (SolidBrush brush = new SolidBrush(surface)) { g.FillRectangle(brush, preview.X + 1, preview.Y + 1, preview.Width * .23f, preview.Height - 2); g.FillRectangle(brush, preview.X + preview.Width * .29f, preview.Y + 13 * s, preview.Width * .64f, 13 * s); g.FillRectangle(brush, preview.X + preview.Width * .29f, preview.Y + 32 * s, preview.Width * .64f, preview.Height - 40 * s); }
            using (Pen pen = new Pen(line)) { g.DrawLine(pen, preview.X + 7 * s, preview.Y + 11 * s, preview.X + 23 * s, preview.Y + 11 * s); g.DrawLine(pen, preview.X + 7 * s, preview.Y + 22 * s, preview.X + 20 * s, preview.Y + 22 * s); }
            using (Font font = Theme.Font(10, FontStyle.Bold)) TextRenderer.DrawText(g, Text, font, new Rectangle((int)(15 * s), Height - (int)(36 * s), Width - (int)(54 * s), (int)(24 * s)), Theme.Ink, TextFormatFlags.VerticalCenter);
            RectangleF check = new RectangleF(Width - 34 * s, Height - 34 * s, 18 * s, 18 * s);
            using (Pen pen = new Pen(Checked ? Theme.Accent : Theme.Line, 1.5f)) g.DrawEllipse(pen, check);
            if (Checked) Artwork.Icon(g, "check", check, Theme.Ink);
            if (Focused) { using (Pen pen = new Pen(Theme.Muted) { DashStyle = DashStyle.Dot }) using (GraphicsPath path = Artwork.Round(new RectangleF(5, 5, Width - 11, Height - 11), 7 * s)) g.DrawPath(pen, path); }
        }
    }
}
