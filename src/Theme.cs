using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Pixelroom
{
    public static class Theme
    {
        private static string name = "Gray";
        public static string Name { get { return name; } }
        public static bool IsDark { get { return Name == "Dark"; } }
        private static Color Gray(int value) { return Color.FromArgb(value, value, value); }
        public static Color Background { get { return Gray(IsDark ? 22 : 237); } }
        public static Color Surface { get { return Gray(IsDark ? 30 : 250); } }
        public static Color Inset { get { return Gray(IsDark ? 38 : 242); } }
        public static Color Input { get { return Gray(IsDark ? 35 : 252); } }
        public static Color Ink { get { return Gray(IsDark ? 235 : 35); } }
        public static Color BrandInk { get { return Gray(IsDark ? 250 : 8); } }
        public static Color Muted { get { return Gray(IsDark ? 172 : 98); } }
        public static Color Line { get { return Gray(IsDark ? 53 : 216); } }
        public static Color Accent { get { return Gray(IsDark ? 221 : 61); } }
        public static Color Pale { get { return Gray(IsDark ? 52 : 226); } }
        public static Color Primary { get { return Gray(IsDark ? 223 : 48); } }
        public static Color PrimaryText { get { return Gray(IsDark ? 24 : 255); } }
        public static Color Hover { get { return Gray(IsDark ? 58 : 225); } }
        public static Color Error { get { return Gray(IsDark ? 221 : 49); } }
        public static void Set(string value) { name = value == "Dark" ? "Dark" : "Gray"; }
        public static Font Font(float size, FontStyle style = FontStyle.Regular) { return new Font("Segoe UI", size, style); }
        public static Button Button(string text, bool primary = false)
        {
            ThemedButton button = new ThemedButton { Text = text, AutoSize = false, Height = 38, Width = 130, FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand, Font = Font(9), BackColor = primary ? Primary : Surface, ForeColor = primary ? PrimaryText : Ink, Margin = new Padding(5, 0, 0, 0), Tag = primary ? "primary" : "button", UseVisualStyleBackColor = false };
            button.IconName = text == "Upload files" ? "upload" : text == "Download" ? "download" : text == "Settings" ? "settings" : text == "Refresh" ? "refresh" : text == "Delete" ? "delete" : text == "New collection" || text == "+" ? "plus" : text == "Add to collection" ? "folder" : null;
            button.FlatAppearance.BorderColor = primary ? Primary : Line;
            button.FlatAppearance.MouseOverBackColor = primary ? (IsDark ? Gray(235) : Gray(88)) : Hover;
            button.FlatAppearance.MouseDownBackColor = primary ? (IsDark ? Gray(188) : Gray(48)) : Pale;
            return button;
        }
        public static Label Label(string text, float size, Color color, FontStyle style = FontStyle.Regular)
        { return new Label { Text = text, AutoSize = false, Font = Font(size, style), ForeColor = color, BackColor = Color.Transparent, Tag = color == Muted ? "muted" : "text", UseMnemonic = false }; }
        public static void StyleDialog(Form form)
        {
            form.BackColor = Background; form.ForeColor = Ink; form.Font = Font(9); form.FormBorderStyle = FormBorderStyle.FixedDialog;
            form.StartPosition = FormStartPosition.CenterParent; form.MaximizeBox = false; form.MinimizeBox = false; form.ShowInTaskbar = false; form.AutoScaleMode = AutoScaleMode.Dpi;
            form.Shown += delegate { Apply(form); };
        }
        [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr handle, int attribute, ref int value, int size);
        private static void TitleBar(Form form)
        {
            if (!form.IsHandleCreated) return;
            try
            {
                int dark = IsDark ? 1 : 0; DwmSetWindowAttribute(form.Handle, 20, ref dark, 4);
                Color back = Background, text = Ink, border = Line;
                int caption = back.R | (back.G << 8) | (back.B << 16), foreground = text.R | (text.G << 8) | (text.B << 16), outline = border.R | (border.G << 8) | (border.B << 16);
                DwmSetWindowAttribute(form.Handle, 35, ref caption, 4); DwmSetWindowAttribute(form.Handle, 36, ref foreground, 4); DwmSetWindowAttribute(form.Handle, 34, ref outline, 4);
            }
            catch (DllNotFoundException) { }
            catch (EntryPointNotFoundException) { }
        }
        public static void Apply(Control control)
        {
            string role = control.Tag as string;
            Form form = control as Form;
            Button button = control as Button;
            DataGridView grid = control as DataGridView;
            TextBoxBase input = control as TextBoxBase;
            ComboBox combo = control as ComboBox;
            ListBox list = control as ListBox;
            if (form != null) { form.BackColor = Background; form.ForeColor = Ink; TitleBar(form); }
            else if (button != null)
            {
                bool primary = role == "primary"; button.BackColor = primary ? Primary : Surface; button.ForeColor = primary ? PrimaryText : Ink;
                button.FlatAppearance.BorderColor = primary ? Primary : Line; button.FlatAppearance.MouseOverBackColor = primary ? (IsDark ? Gray(235) : Gray(88)) : Hover;
                button.FlatAppearance.MouseDownBackColor = primary ? (IsDark ? Gray(188) : Gray(48)) : Pale;
            }
            else if (grid != null)
            {
                grid.BackgroundColor = Surface; grid.GridColor = Line; grid.BorderStyle = BorderStyle.None;
                grid.ColumnHeadersDefaultCellStyle.BackColor = Inset; grid.ColumnHeadersDefaultCellStyle.ForeColor = Muted;
                grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = Inset; grid.ColumnHeadersDefaultCellStyle.SelectionForeColor = Ink;
                grid.DefaultCellStyle.BackColor = Surface; grid.DefaultCellStyle.ForeColor = Ink; grid.DefaultCellStyle.SelectionBackColor = Pale; grid.DefaultCellStyle.SelectionForeColor = Ink;
                grid.RowsDefaultCellStyle.BackColor = Surface; grid.RowsDefaultCellStyle.ForeColor = Ink; grid.RowsDefaultCellStyle.SelectionBackColor = Pale; grid.RowsDefaultCellStyle.SelectionForeColor = Ink;
                grid.AlternatingRowsDefaultCellStyle.BackColor = Surface; grid.AlternatingRowsDefaultCellStyle.ForeColor = Ink; grid.AlternatingRowsDefaultCellStyle.SelectionBackColor = Pale; grid.AlternatingRowsDefaultCellStyle.SelectionForeColor = Ink;
            }
            else if (input != null) { input.BackColor = role == "readout" ? Background : Input; input.ForeColor = Ink; }
            else if (combo != null) { combo.BackColor = Input; combo.ForeColor = Ink; combo.FlatStyle = FlatStyle.Flat; }
            else if (list != null) { list.BackColor = Surface; list.ForeColor = Ink; }
            else if (control is Label) { control.BackColor = Color.Transparent; control.ForeColor = role == "muted" ? Muted : Ink; }
            else if (control is CheckBox || control is RadioButton) { control.BackColor = control.Parent != null ? control.Parent.BackColor : Background; control.ForeColor = Ink; ((ButtonBase)control).FlatStyle = FlatStyle.Flat; }
            else { control.BackColor = role == "surface" ? Surface : role == "inset" ? Inset : control.Parent != null ? control.Parent.BackColor : Background; control.ForeColor = Ink; }
            if (control.ContextMenuStrip != null) ApplyMenu(control.ContextMenuStrip);
            foreach (Control child in control.Controls) Apply(child);
            control.Invalidate();
        }
        private static void ApplyMenu(ContextMenuStrip menu)
        {
            menu.Renderer = new ToolStripProfessionalRenderer(new MenuColors()); menu.BackColor = Surface; menu.ForeColor = Ink;
            foreach (ToolStripItem item in menu.Items) { item.ForeColor = Ink; item.BackColor = Surface; }
        }
        private sealed class MenuColors : ProfessionalColorTable
        {
            public override Color ToolStripDropDownBackground { get { return Surface; } }
            public override Color ImageMarginGradientBegin { get { return Surface; } }
            public override Color ImageMarginGradientMiddle { get { return Surface; } }
            public override Color ImageMarginGradientEnd { get { return Surface; } }
            public override Color MenuBorder { get { return Line; } }
            public override Color MenuItemBorder { get { return Line; } }
            public override Color MenuItemSelected { get { return Pale; } }
            public override Color MenuItemSelectedGradientBegin { get { return Pale; } }
            public override Color MenuItemSelectedGradientEnd { get { return Pale; } }
            public override Color SeparatorDark { get { return Line; } }
            public override Color SeparatorLight { get { return Surface; } }
        }
    }

    public sealed class ThemedButton : Button
    {
        public string IconName { get; set; }
        public bool IsNavigation { get; set; }
        public bool IsSelected { get; set; }
        private bool hover, pressed;
        public ThemedButton() { DoubleBuffered = true; SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true); }
        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; pressed = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { pressed = true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { pressed = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias; float s = DeviceDpi / 96f;
            using (SolidBrush parent = new SolidBrush(Parent != null ? Parent.BackColor : Theme.Background)) g.FillRectangle(parent, ClientRectangle);
            bool primary = (Tag as string) == "primary";
            Color fill = !Enabled ? Theme.Surface : IsNavigation ? (IsSelected ? Theme.Pale : hover ? Theme.Hover : Theme.Surface) : pressed ? Theme.Pale : hover ? (primary ? Theme.Accent : Theme.Hover) : primary ? Theme.Primary : Theme.Surface;
            Color ink = !Enabled ? Theme.Muted : IsNavigation ? (IsSelected ? Theme.Ink : Theme.Muted) : primary ? Theme.PrimaryText : Theme.Ink;
            Artwork.Card(g, new RectangleF(.5f, .5f, Width - 1, Height - 1), fill, IsNavigation || FlatAppearance.BorderSize == 0 || primary && Enabled ? fill : Theme.Line, 7 * s);
            bool hasIcon = !String.IsNullOrEmpty(IconName); int iconSize = (int)(17 * s), gap = (int)(8 * s);
            int textWidth = TextRenderer.MeasureText(g, Text, Font, Size.Empty, TextFormatFlags.NoPadding).Width;
            int totalWidth = textWidth + (hasIcon ? iconSize + gap : 0), left = IsNavigation ? (int)(13 * s) : Math.Max((int)(7 * s), (Width - totalWidth) / 2);
            if (hasIcon) { Artwork.Icon(g, IconName, new RectangleF(left, (Height - iconSize) / 2, iconSize, iconSize), ink); left += iconSize + gap; }
            TextRenderer.DrawText(g, Text, Font, new Rectangle(left, 0, Math.Max(0, Width - left - (int)(7 * s)), Height), ink, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
            if (IsNavigation && IsSelected) using (SolidBrush dot = new SolidBrush(Atmosphere.Accent)) g.FillEllipse(dot, Width - 16 * s, Height / 2f - 2 * s, 4 * s, 4 * s);
            if (Focused && ShowFocusCues) using (Pen p = new Pen(ink) { DashStyle = DashStyle.Dot }) using (GraphicsPath path = Artwork.Round(new RectangleF(4, 4, Width - 9, Height - 9), 4 * s)) g.DrawPath(p, path);
        }
    }

    public static class Notice
    {
        public static DialogResult Show(IWin32Window owner, string message, string title, MessageBoxButtons buttons, MessageBoxIcon icon = MessageBoxIcon.None, MessageBoxDefaultButton defaultButton = MessageBoxDefaultButton.Button1)
        {
            using (Form dialog = new Form())
            {
                Theme.StyleDialog(dialog); dialog.Text = title; dialog.ClientSize = new Size(490, 262);
                Label heading = Theme.Label(title, 18, Theme.Ink); heading.SetBounds(25, 22, 440, 37); dialog.Controls.Add(heading);
                TextBox text = new TextBox { Text = message, Multiline = true, ReadOnly = true, BorderStyle = BorderStyle.None, ScrollBars = ScrollBars.Vertical, TabStop = false, Tag = "readout" }; text.SetBounds(27, 72, 436, 112); dialog.Controls.Add(text);
                Button confirm = Theme.Button(buttons == MessageBoxButtons.YesNo ? "Yes" : "OK", true); confirm.SetBounds(350, 202, 113, 37); confirm.DialogResult = buttons == MessageBoxButtons.YesNo ? DialogResult.Yes : DialogResult.OK; dialog.Controls.Add(confirm);
                if (buttons == MessageBoxButtons.YesNo || buttons == MessageBoxButtons.OKCancel)
                {
                    Button cancel = Theme.Button(buttons == MessageBoxButtons.YesNo ? "No" : "Cancel"); cancel.SetBounds(225, 202, 113, 37); cancel.DialogResult = buttons == MessageBoxButtons.YesNo ? DialogResult.No : DialogResult.Cancel; dialog.Controls.Add(cancel);
                    dialog.CancelButton = cancel; dialog.AcceptButton = defaultButton == MessageBoxDefaultButton.Button2 ? cancel : confirm;
                }
                else { dialog.AcceptButton = confirm; dialog.CancelButton = confirm; }
                return owner != null ? dialog.ShowDialog(owner) : dialog.ShowDialog();
            }
        }
    }
}
