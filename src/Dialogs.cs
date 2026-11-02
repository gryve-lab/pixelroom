using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace Pixelroom
{
    public sealed class CueTextBox : TextBox
    {
        public string Cue { get; set; }
        protected override void WndProc(ref Message message)
        {
            base.WndProc(ref message);
            if (message.Msg == 0x000F && TextLength == 0 && !String.IsNullOrEmpty(Cue))
                using (Graphics graphics = Graphics.FromHwnd(Handle)) TextRenderer.DrawText(graphics, Cue, Font, new Rectangle(2, 0, Math.Max(0, ClientSize.Width - 4), ClientSize.Height), Theme.Muted, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
        }
        protected override void OnTextChanged(EventArgs e) { base.OnTextChanged(e); Invalidate(); }
    }

    public sealed class TextDialog : Form
    {
        private readonly TextBox input;
        public string Result { get { return input.Text.Trim(); } }
        public TextDialog(string title, string description, string value, int maxLength)
        {
            Theme.StyleDialog(this); Text = title + " — Pixelroom"; ClientSize = new Size(460, 228);
            Label heading = Theme.Label(title, 18, Theme.Ink); heading.SetBounds(24, 21, 413, 37); Controls.Add(heading);
            Label info = Theme.Label(description, 9, Theme.Muted); info.SetBounds(26, 65, 408, 43); Controls.Add(info);
            input = new TextBox { Text = value, Font = Theme.Font(10), MaxLength = maxLength, BorderStyle = BorderStyle.FixedSingle }; input.SetBounds(27, 118, 405, 29); Controls.Add(input);
            Button cancel = Theme.Button("Cancel"); cancel.SetBounds(224, 168, 95, 37); cancel.DialogResult = DialogResult.Cancel; Controls.Add(cancel);
            Button save = Theme.Button("Save", true); save.SetBounds(329, 168, 103, 37); save.Click += delegate { if (!String.IsNullOrWhiteSpace(input.Text)) { DialogResult = DialogResult.OK; Close(); } else input.Focus(); }; Controls.Add(save);
            AcceptButton = save; CancelButton = cancel; Shown += delegate { input.Focus(); input.SelectAll(); };
        }
    }

    public sealed class CollectionDialog : Form
    {
        private readonly ListBox list;
        public Collection SelectedCollection { get { return list.SelectedItem as Collection; } }
        public CollectionDialog(List<Collection> collections)
        {
            Theme.StyleDialog(this); Text = "Choose collection — Pixelroom"; ClientSize = new Size(420, 340);
            Label heading = Theme.Label("Add to collection", 18, Theme.Ink); heading.SetBounds(24, 22, 373, 35); Controls.Add(heading);
            Label info = Theme.Label("Choose a collection.", 9, Theme.Muted); info.SetBounds(26, 68, 365, 28); Controls.Add(info);
            list = new ListBox { Font = Theme.Font(10), BorderStyle = BorderStyle.FixedSingle, IntegralHeight = false }; list.SetBounds(27, 105, 366, 160); foreach (Collection c in collections) list.Items.Add(c); if (list.Items.Count > 0) list.SelectedIndex = 0; Controls.Add(list);
            Button cancel = Theme.Button("Cancel"); cancel.SetBounds(181, 283, 95, 37); cancel.DialogResult = DialogResult.Cancel; Controls.Add(cancel);
            Button save = Theme.Button("Add", true); save.SetBounds(285, 283, 108, 37); save.Click += delegate { if (SelectedCollection != null) { DialogResult = DialogResult.OK; Close(); } }; Controls.Add(save);
            AcceptButton = save; CancelButton = cancel;
        }
    }
}
