using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace Pixelroom
{
    internal sealed class ActionToolbar : Panel
    {
        private bool arranging;
        public int ColumnCount { get; private set; }
        public int RowCount { get; private set; }
        public int RequiredHeight { get; private set; }
        public ActionToolbar()
        {
            Dock = DockStyle.Fill; Margin = Padding.Empty; Padding = Padding.Empty;
            Layout += delegate { ArrangeButtons(); };
        }
        public Button AddAction(string text, bool primary = false)
        {
            Button button = Theme.Button(text, primary); button.Margin = Padding.Empty; Controls.Add(button); ArrangeButtons(); return button;
        }
        public int HeightForWidth(int width)
        {
            int columns = width >= 780 * (DeviceDpi / 96f) ? 6 : 4;
            return (Controls.Count + columns - 1) / columns * (int)(46 * (DeviceDpi / 96f));
        }
        private void ArrangeButtons()
        {
            if (Controls.Count == 0 || arranging) return; arranging = true;
            try
            {
                float scale = DeviceDpi / 96f; ColumnCount = ClientSize.Width >= 780 * scale ? 6 : 4; RowCount = (Controls.Count + ColumnCount - 1) / ColumnCount;
                int gap = (int)(8 * scale), rowHeight = (int)(46 * scale), height = RowCount * rowHeight;
                for (int i = 0; i < Controls.Count; i++)
                {
                    int col = i % ColumnCount, left = col * (ClientSize.Width + gap) / ColumnCount, right = (col + 1) * (ClientSize.Width + gap) / ColumnCount - gap;
                    Rectangle bounds = new Rectangle(left, i / ColumnCount * rowHeight, Math.Max(1, right - left), rowHeight - gap); if (Controls[i].Bounds != bounds) Controls[i].Bounds = bounds;
                }
                RequiredHeight = height;
            }
            finally { arranging = false; }
        }
    }
    internal static class SelectionPaint
    {
        public static void Header(DataGridView grid, DataGridViewCellPaintingEventArgs e, bool all, bool any)
        {
            e.Paint(e.ClipBounds, e.PaintParts & ~DataGridViewPaintParts.ContentForeground);
            float scale = grid.DeviceDpi / 96f; int size = (int)(15 * scale); Rectangle box = new Rectangle(e.CellBounds.X + (e.CellBounds.Width - size) / 2, e.CellBounds.Y + (e.CellBounds.Height - size) / 2, size, size);
            using (SolidBrush fill = new SolidBrush(all || any ? Theme.Pale : Theme.Input)) e.Graphics.FillRectangle(fill, box);
            using (Pen border = new Pen(Theme.Muted)) e.Graphics.DrawRectangle(border, box);
            if (all) Artwork.Icon(e.Graphics, "check", box, Theme.Ink);
            else if (any) using (Pen stroke = new Pen(Theme.Ink, 2 * scale)) e.Graphics.DrawLine(stroke, box.Left + 3 * scale, box.Top + size / 2, box.Right - 3 * scale, box.Top + size / 2);
            e.Handled = true;
        }
    }
}
