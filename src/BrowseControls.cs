using System;
using System.Drawing;
using System.Windows.Forms;

namespace Pixelroom
{
    internal sealed class BrowseFilterBar : FlowLayoutPanel
    {
        internal readonly ComboBox sharing, dates, basis;
        internal readonly CheckBox foldersFirst;
        private readonly Button reset;
        private bool changing;
        private int previousDate;
        private DateTime? from, to;
        public event EventHandler Changed, ResetRequested;
        public BrowseFilterBar(bool filesystem)
        {
            Dock = DockStyle.Fill; WrapContents = false; Margin = Padding.Empty; Padding = new Padding(0, 3, 0, 7);
            sharing = Combo("Sharing filter", 144, new object[] { "All sharing", "Shared", "Not shared", "Direct link", "Shared via parent" });
            dates = Combo("Date range", 161, new object[] { "Any date", "Today", "Last 7 days", "Last 30 days", "Custom range…" }); dates.DropDownWidth = 240;
            basis = Combo("Date field", 134, filesystem ? new object[] { "Modified date", "Created date" } : new object[] { "Uploaded date" });
            foldersFirst = new CheckBox { Text = "Folders first", Width = 110, Height = 34, Checked = true, Visible = filesystem, Margin = new Padding(0, 0, 8, 0), AccessibleName = "Folders first" }; Controls.Add(foldersFirst);
            reset = Theme.Button("Reset filters"); reset.Width = 100; reset.Height = 34; reset.Margin = Padding.Empty; Controls.Add(reset);
            sharing.SelectedIndexChanged += delegate { Notify(); }; basis.SelectedIndexChanged += delegate { Notify(); }; foldersFirst.CheckedChanged += delegate { Notify(); };
            dates.SelectedIndexChanged += delegate(object sender, EventArgs e) { if (dates.SelectedIndex != 4) ChangeDates(sender, e); };
            dates.SelectionChangeCommitted += delegate(object sender, EventArgs e) { if (dates.SelectedIndex == 4) ChangeDates(sender, e); };
            reset.Click += delegate { ResetFilters(); if (ResetRequested != null) ResetRequested(this, EventArgs.Empty); };
        }
        private ComboBox Combo(string name, int width, object[] items)
        {
            ComboBox combo = new FilterCombo { AccessibleName = name, DropDownStyle = ComboBoxStyle.DropDownList, Width = width, Margin = new Padding(0, 0, 8, 0) };
            combo.Items.AddRange(items); combo.SelectedIndex = 0; Controls.Add(combo); return combo;
        }
        private void Notify() { if (!changing && Changed != null) Changed(this, EventArgs.Empty); }
        private void ChangeDates(object sender, EventArgs e)
        {
            if (changing) return;
            if (dates.SelectedIndex == 4)
            {
                using (DateRangeDialog dialog = new DateRangeDialog(from ?? DateTime.Today.AddDays(-30), to ?? DateTime.Today))
                {
                    if (dialog.ShowDialog(FindForm()) != DialogResult.OK) { changing = true; dates.SelectedIndex = previousDate; changing = false; return; }
                    from = dialog.From; to = dialog.To;
                    changing = true; dates.Items[4] = from.Value.ToString("dd MMM yy") + " – " + to.Value.ToString("dd MMM yy"); dates.SelectedIndex = 4; changing = false;
                }
            }
            else
            {
                to = dates.SelectedIndex == 0 ? (DateTime?)null : DateTime.Today;
                from = dates.SelectedIndex == 1 ? DateTime.Today : dates.SelectedIndex == 2 ? DateTime.Today.AddDays(-6) : dates.SelectedIndex == 3 ? DateTime.Today.AddDays(-29) : (DateTime?)null;
            }
            previousDate = dates.SelectedIndex; Notify();
        }
        public BrowseQuery Query
        {
            get { return new BrowseQuery { Sharing = (SharingFilter)sharing.SelectedIndex, From = from, To = to, DateField = basis.Items.Count == 1 ? BrowseDate.Uploaded : basis.SelectedIndex == 1 ? BrowseDate.Created : BrowseDate.Modified, FoldersFirst = foldersFirst.Checked }; }
        }
        public void ResetFilters()
        {
            changing = true; from = to = null; previousDate = 0; sharing.SelectedIndex = dates.SelectedIndex = basis.SelectedIndex = 0;
            dates.Items[4] = "Custom range…"; foldersFirst.Checked = true; changing = false; Notify();
        }
        internal void SetDates(DateTime? start, DateTime? end)
        {
            if (start.HasValue && end.HasValue && start.Value.Date > end.Value.Date) throw new ArgumentException("The start date must not be after the end date.");
            from = start; to = end; Notify();
        }
    }

    internal sealed class DateRangeDialog : Form
    {
        private readonly DateTimePicker start, end;
        public DateTime From { get { return start.Value.Date; } }
        public DateTime To { get { return end.Value.Date; } }
        public DateRangeDialog(DateTime from, DateTime to)
        {
            Text = "Filter by date"; ClientSize = new Size(460, 255); Theme.StyleDialog(this);
            Label heading = Theme.Label("Choose a date range", 17, Theme.Ink, FontStyle.Bold); heading.SetBounds(24, 20, 410, 35); Controls.Add(heading);
            Label a = Theme.Label("From", 9, Theme.Muted), b = Theme.Label("To (inclusive)", 9, Theme.Muted); a.SetBounds(26, 72, 180, 24); b.SetBounds(245, 72, 180, 24); Controls.Add(a); Controls.Add(b);
            start = Picker(from, "Start date", 26); end = Picker(to, "End date", 245);
            Button apply = Theme.Button("Apply", true); apply.SetBounds(320, 193, 114, 38); apply.Click += delegate { if (From > To) { Notice.Show(this, "Choose an end date on or after the start date.", Text, MessageBoxButtons.OK); return; } DialogResult = DialogResult.OK; }; Controls.Add(apply);
            Button cancel = Theme.Button("Cancel"); cancel.SetBounds(193, 193, 114, 38); cancel.DialogResult = DialogResult.Cancel; Controls.Add(cancel); AcceptButton = apply; CancelButton = cancel;
        }
        private DateTimePicker Picker(DateTime value, string name, int x)
        { DateTimePicker picker = new DateTimePicker { Format = DateTimePickerFormat.Custom, CustomFormat = "dd MMM yyyy", AccessibleName = name }; picker.SetBounds(x, 105, 188, 30); picker.Value = value < picker.MinDate ? picker.MinDate : value > picker.MaxDate ? picker.MaxDate : value; Controls.Add(picker); return picker; }
    }

    internal sealed class TransferStatusPanel : Panel
    {
        private readonly Label heading, detail;
        private readonly TransferBar bar;
        private readonly System.Windows.Forms.Timer tick;
        private TransferMetrics metrics = new TransferMetrics();
        private string phase = "Ready", name = "", batch = "";
        public bool HasTransfer { get; private set; }
        internal TransferMetrics Metrics { get { return metrics; } }
        internal string CurrentName { get { return name; } }
        public TransferStatusPanel()
        {
            Dock = DockStyle.Fill; Margin = Padding.Empty;
            heading = Theme.Label("Ready", 9, Theme.Ink, FontStyle.Bold); heading.AutoEllipsis = true; heading.AccessibleName = "Current transfer"; Controls.Add(heading);
            detail = Theme.Label("", 8, Theme.Muted); detail.AutoEllipsis = true; detail.AccessibleName = "Transfer details"; Controls.Add(detail);
            bar = new TransferBar { AccessibleName = "File transfer progress" }; Controls.Add(bar);
            Layout += delegate { float s = DeviceDpi / 96f; heading.SetBounds(0, (int)(3 * s), Width, (int)(22 * s)); bar.SetBounds(0, (int)(29 * s), Width, (int)(7 * s)); detail.SetBounds(0, (int)(41 * s), Width, (int)(22 * s)); };
            tick = new System.Windows.Forms.Timer { Interval = 500 }; tick.Tick += delegate { RefreshText(); };
        }
        public void Begin(string action, string filename) { metrics = new TransferMetrics(); HasTransfer = true; phase = action; name = filename; batch = ""; tick.Start(); RefreshText(); }
        public void Report(string action, string filename, TransferProgress progress, int completed = 0, int total = 0)
        {
            if (!HasTransfer || name != filename) Begin(action, filename);
            phase = action; name = filename; batch = total > 0 ? Math.Min(total, completed + 1) + " of " + total + " items · " : "";
            metrics.Update(progress.Done, progress.Total); RefreshText();
        }
        public void Display(string action, string filename, TransferMetrics value, string summary)
        { HasTransfer = true; phase = action; name = filename; metrics = value; batch = summary ?? ""; if (value.Finished) tick.Stop(); else tick.Start(); RefreshText(); }
        public void End(string action, bool completed)
        { if (!HasTransfer) return; phase = action; metrics.Finish(completed); tick.Stop(); RefreshText(); }
        private void RefreshText()
        {
            heading.Text = phase + (name.Length > 0 ? " · " + name : "");
            string amount = metrics.HasLength ? metrics.Percent + "% · " + metrics.BytesText : metrics.BytesText + (metrics.Finished ? "" : " · total size unavailable");
            detail.Text = batch + amount + (metrics.Finished ? "" : " · " + metrics.SpeedText + " · ETA " + metrics.Eta);
            bar.Percent = metrics.Percent; bar.Known = metrics.HasLength || metrics.Finished; bar.AccessibleDescription = detail.Text; bar.Invalidate();
        }
        protected override void Dispose(bool disposing) { if (disposing) tick.Dispose(); base.Dispose(disposing); }
    }
    internal sealed class TransferBar : Control
    {
        public int Percent;
        public bool Known;
        public TransferBar() { DoubleBuffered = true; SetStyle(ControlStyles.ResizeRedraw, true); }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(Theme.Inset); int width = Known ? (int)(Width * Math.Max(0, Math.Min(100, Percent)) / 100.0) : Width / 5;
            using (SolidBrush fill = new SolidBrush(Theme.IsDark ? Color.FromArgb(67, 210, 216) : Color.FromArgb(12, 132, 142))) e.Graphics.FillRectangle(fill, 0, 0, width, Height);
        }
    }
}
