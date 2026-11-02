using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Pixelroom
{
    internal sealed class StatisticsView : UserControl
    {
        private readonly PixelDrainClient client;
        private readonly string key;
        private readonly Func<List<CloudFile>> cloudFiles;
        private readonly ComboBox source, preset, visibility, ordering, metric, interval;
        private readonly TextBox folder;
        private readonly CueTextBox search;
        private readonly DateTimePicker from, to;
        private readonly CheckBox recursive, autoRefresh;
        private readonly Button refresh, cancel, export;
        private readonly DataGridView grid;
        private readonly Label status, graphTitle;
        private readonly StatisticsProgress progress;
        private readonly StatisticsSummary summary;
        private readonly StatisticsChart chart;
        private readonly System.Windows.Forms.Timer timer;
        private readonly CancellationTokenSource lifetime = new CancellationTokenSource();
        private CancellationTokenSource operation, detail;
        private List<StatisticsRow> rows = new List<StatisticsRow>(), shown = new List<StatisticsRow>();
        private List<StatisticsPoint> selectedPoints;
        private StatisticsPeriod loadedPeriod;
        private bool busy, changing, initialized, disposed, loadedLifetime;
        private int generation, detailGeneration, folderErrors;
        private string loadedRange = "";
        private string chartKey;
        private DateTime lastRender;
        internal event Action<string> BrowseRequested;
        internal bool IsBusy { get { return busy; } }
        internal Task Initialization { get; private set; }
        public StatisticsView(PixelDrainClient client, string key, Func<List<CloudFile>> cloudFiles)
        {
            this.client = client; this.key = key; this.cloudFiles = cloudFiles; Dock = DockStyle.Fill; Margin = Padding.Empty; Font = Theme.Font(9); AutoScaleMode = AutoScaleMode.Dpi;
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 7, Margin = Padding.Empty };
            foreach (float h in new float[] { 46, 44, 72, 44 }) layout.RowStyles.Add(new RowStyle(SizeType.Absolute, h));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 40)); layout.RowStyles.Add(new RowStyle(SizeType.Percent, 60)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30)); Controls.Add(layout);
            var location = Row(new float[] { 132, -1, 142, 97, 80 });
            source = Combo("Statistics source", new string[] { "File System", "My files" }); location.Controls.Add(source, 0, 0);
            var folderFrame = new FieldFrame { IconName = "folder", Dock = DockStyle.Fill, Margin = new Padding(0, 0, 8, 8) };
            folder = new TextBox { Text = "/me", BorderStyle = BorderStyle.None, Font = Theme.Font(10), AccessibleName = "Statistics folder" }; folder.SetBounds(36, 9, 220, 22); folderFrame.Controls.Add(folder); folderFrame.Layout += delegate { folder.Width = Math.Max(25, folderFrame.Width - folder.Left - 12); }; location.Controls.Add(folderFrame, 1, 0);
            recursive = new CheckBox { Text = "Include subfolders", Checked = true, Dock = DockStyle.Fill, Margin = Padding.Empty }; location.Controls.Add(recursive, 2, 0);
            refresh = Theme.Button("Refresh", true); Fill(refresh); refresh.Click += async delegate { await RefreshAsync(); }; location.Controls.Add(refresh, 3, 0);
            cancel = Theme.Button("Cancel"); Fill(cancel); cancel.Click += delegate { if (operation != null) operation.Cancel(); }; location.Controls.Add(cancel, 4, 0); layout.Controls.Add(location, 0, 0);
            var dates = Row(new float[] { 142, 37, 136, 26, 136, -1, 94 });
            preset = Combo("Statistics period", new string[] { "Today", "Last 7 days", "Last 30 days", "Custom range" }); preset.SelectedIndex = 1; dates.Controls.Add(preset, 0, 0);
            dates.Controls.Add(Caption("From"), 1, 0); from = Picker("Statistics start date"); from.Value = DateTime.Today.AddDays(-6); dates.Controls.Add(from, 2, 0);
            dates.Controls.Add(Caption("To"), 3, 0); to = Picker("Statistics end date"); dates.Controls.Add(to, 4, 0);
            export = Theme.Button("Export CSV"); Fill(export); export.Click += delegate { Export(); }; dates.Controls.Add(export, 6, 0); layout.Controls.Add(dates, 0, 1);
            summary = new StatisticsSummary(); layout.Controls.Add(summary, 0, 2);
            var filters = Row(new float[] { -1, 148, 155, 152 });
            var searchFrame = new FieldFrame { IconName = "search", Dock = DockStyle.Fill, Margin = new Padding(0, 0, 8, 7) };
            search = new CueTextBox { Cue = "Search content…", AccessibleName = "Search statistics", BorderStyle = BorderStyle.None, Font = Theme.Font(10) }; search.SetBounds(36, 9, 220, 22); search.TextChanged += FilterChanged; searchFrame.Controls.Add(search); searchFrame.Layout += delegate { search.Width = Math.Max(25, searchFrame.Width - search.Left - 12); }; filters.Controls.Add(searchFrame, 0, 0);
            visibility = Combo("Statistics results", new string[] { "Downloaded only", "All results", "Unavailable" }); visibility.SelectedIndexChanged += FilterChanged; filters.Controls.Add(visibility, 1, 0);
            ordering = Combo("Sort statistics", new string[] { "Most downloaded", "Most bandwidth", "Latest activity", "Name A → Z" }); ordering.SelectedIndexChanged += FilterChanged; filters.Controls.Add(ordering, 2, 0);
            autoRefresh = new CheckBox { Text = "Auto-refresh (60s)", Dock = DockStyle.Fill, Margin = Padding.Empty }; filters.Controls.Add(autoRefresh, 3, 0); layout.Controls.Add(filters, 0, 3);
            var listCard = new SurfaceCard { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 7) }; layout.Controls.Add(listCard, 0, 4);
            grid = new FileGrid { Dock = DockStyle.Fill, VirtualMode = true, ReadOnly = true, MultiSelect = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect, RowHeadersVisible = false, AllowUserToAddRows = false, AllowUserToDeleteRows = false, AllowUserToResizeRows = false, BorderStyle = BorderStyle.None, EnableHeadersVisualStyles = false, CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal, ColumnHeadersHeight = 33, ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing };
            grid.DefaultCellStyle.Padding = new Padding(8, 0, 0, 0); grid.RowTemplate.Height = 37;
            grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Content", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, MinimumWidth = 145 });
            foreach (var col in new object[][] { new object[] { "Downloads", 92 }, new object[] { "Bandwidth", 103 }, new object[] { "Last active interval", 144 }, new object[] { "Status", 100 } }) grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = (string)col[0], Width = (int)col[1] });
            foreach (DataGridViewColumn col in grid.Columns) col.SortMode = DataGridViewColumnSortMode.NotSortable;
            grid.CellValueNeeded += ValueNeeded; grid.SelectionChanged += async delegate { if (!changing) await LoadChartAsync(); };
            grid.CellToolTipTextNeeded += delegate(object sender, DataGridViewCellToolTipTextNeededEventArgs e) { if (e.RowIndex >= 0 && e.RowIndex < shown.Count) e.ToolTipText = (shown[e.RowIndex].Path ?? shown[e.RowIndex].Id) + (shown[e.RowIndex].Error == null ? "" : "\n" + shown[e.RowIndex].Error); };
            var menu = new ContextMenuStrip(); var browse = menu.Items.Add("Open containing folder", null, delegate { StatisticsRow row = Selected; if (row != null && row.Path != null && BrowseRequested != null) BrowseRequested(FilesystemRules.Parent(row.Path)); }); menu.Opening += delegate { browse.Enabled = Selected != null && Selected.Path != null; }; grid.ContextMenuStrip = menu; listCard.Controls.Add(grid);
            var graphCard = new SurfaceCard { Dock = DockStyle.Fill, Margin = Padding.Empty }; layout.Controls.Add(graphCard, 0, 5);
            var graphOptions = Row(new float[] { -1, 135, 126 }); graphOptions.Dock = DockStyle.Top; graphOptions.Height = 33;
            graphTitle = Theme.Label("Selected content", 9, Theme.Ink, FontStyle.Bold); graphTitle.Dock = DockStyle.Fill; graphTitle.AutoEllipsis = true; graphTitle.TextAlign = ContentAlignment.MiddleLeft; graphOptions.Controls.Add(graphTitle, 0, 0); metric = Combo("Chart metric", new string[] { "Downloads", "Bandwidth" }); graphOptions.Controls.Add(metric, 1, 0); interval = Combo("Chart grouping", new string[] { "Daily", "Hourly" }); graphOptions.Controls.Add(interval, 2, 0);
            chart = new StatisticsChart { ShowTitle = false }; graphCard.Controls.Add(chart); graphCard.Controls.Add(graphOptions); metric.SelectedIndexChanged += delegate { UpdateChart(); }; interval.SelectedIndexChanged += delegate { UpdateChart(); };
            var footer = Row(new float[] { -1, 124 }); status = Caption("Connect your account in Settings to load statistics."); footer.Controls.Add(status, 0, 0); progress = new StatisticsProgress { Dock = DockStyle.Fill, Margin = new Padding(4, 9, 0, 5), Style = ProgressBarStyle.Continuous }; footer.Controls.Add(progress, 1, 0); layout.Controls.Add(footer, 0, 6);
            preset.SelectedIndexChanged += delegate { if (changing) return; changing = true; if (preset.SelectedIndex < 3) { int days = preset.SelectedIndex == 0 ? 1 : preset.SelectedIndex == 1 ? 7 : 30; from.Value = DateTime.Today.AddDays(1 - days); to.Value = DateTime.Today; } changing = false; MarkPending(); };
            from.ValueChanged += DateChanged; to.ValueChanged += DateChanged;
            source.SelectedIndexChanged += delegate { StopAndClear(); UpdateControls(); MarkPending(); };
            folder.TextChanged += delegate { if (!changing && initialized) MarkPending(); }; recursive.CheckedChanged += delegate { if (initialized) MarkPending(); };
            timer = new System.Windows.Forms.Timer { Interval = 60000 }; timer.Tick += async delegate { if (autoRefresh.Checked && Visible && !busy && !String.IsNullOrEmpty(key)) await RefreshAsync(); }; timer.Start();
            Theme.Apply(this); UpdateControls(); chart.Clear("Select Refresh to load download statistics.");
        }
        private static void Fill(Control c) { c.Dock = DockStyle.Fill; c.Margin = new Padding(0, 0, 7, 8); }
        private static TableLayoutPanel Row(float[] widths)
        { var row = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = widths.Length, RowCount = 1, Margin = Padding.Empty }; row.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); foreach (float w in widths) row.ColumnStyles.Add(new ColumnStyle(w < 0 ? SizeType.Percent : SizeType.Absolute, w < 0 ? 100 : w)); return row; }
        private static Label Caption(string text) { Label label = Theme.Label(text, 8, Theme.Muted); label.Dock = DockStyle.Fill; label.TextAlign = ContentAlignment.MiddleLeft; label.AutoEllipsis = true; label.Margin = Padding.Empty; return label; }
        private static ComboBox Combo(string name, string[] items) { var combo = new FilterCombo { AccessibleName = name, Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList, Margin = new Padding(0, 5, 8, 0) }; combo.Items.AddRange(items); combo.SelectedIndex = 0; return combo; }
        private static DateTimePicker Picker(string name) { return new DateTimePicker { AccessibleName = name, Dock = DockStyle.Fill, Format = DateTimePickerFormat.Custom, CustomFormat = "yyyy-MM-dd", MaxDate = DateTime.Today, Value = DateTime.Today, Margin = new Padding(0, 6, 6, 0) }; }
        private void DateChanged(object sender, EventArgs e) { if (changing) return; changing = true; preset.SelectedIndex = 3; changing = false; MarkPending(); }
        private async void FilterChanged(object sender, EventArgs e) { RenderRows(); if (grid != null && !busy) await LoadChartAsync(); }
        internal void HandleKey(KeyEventArgs e)
        {
            if (e.Control && e.KeyCode == Keys.F) { search.Focus(); search.SelectAll(); e.SuppressKeyPress = true; }
            else if (e.KeyCode == Keys.F5) { RefreshAsync().ContinueWith(delegate { }); e.SuppressKeyPress = true; }
            else if (e.KeyCode == Keys.Escape && busy) { if (operation != null) operation.Cancel(); e.SuppressKeyPress = true; }
        }
        private void MarkPending() { if (status != null) status.Text = rows.Count > 0 ? "Filters changed. Select Refresh; displayed results still use " + loadedRange + "." : "Select Refresh to load statistics."; }
        private void UpdateControls()
        {
            bool ready = !String.IsNullOrEmpty(key) && !busy, fs = source.SelectedIndex == 0;
            source.Enabled = folder.Enabled = recursive.Enabled = refresh.Enabled = ready; folder.Enabled = recursive.Enabled = ready && fs;
            preset.Enabled = from.Enabled = to.Enabled = ready && fs; cancel.Enabled = busy; export.Enabled = rows.Count > 0 && !busy;
            interval.Enabled = fs; autoRefresh.Enabled = !String.IsNullOrEmpty(key);
        }
        internal void Initialize() { if (!initialized) { initialized = true; Initialization = RefreshAsync(); } }
        private StatisticsRow Selected { get { return grid.CurrentCell != null && grid.CurrentCell.RowIndex >= 0 && grid.CurrentCell.RowIndex < shown.Count ? shown[grid.CurrentCell.RowIndex] : null; } }
        private void StopAndClear()
        {
            generation++; if (operation != null) operation.Cancel(); if (detail != null) detail.Cancel(); rows.Clear(); shown.Clear(); changing = true; grid.RowCount = 0; changing = false; loadedPeriod = null; loadedRange = ""; selectedPoints = null; chart.Clear("Select Refresh to load this source."); summary.Downloads = summary.Bandwidth = summary.ActiveFiles = "—"; summary.Invalidate();
        }
        internal async Task RefreshAsync()
        {
            if (busy || String.IsNullOrEmpty(key) || disposed) return;
            StatisticsRow previous = Selected; string preferred = previous == null ? null : previous.Path ?? previous.Id;
            changing = true; from.MaxDate = to.MaxDate = DateTime.Today;
            if (preset.SelectedIndex < 3) { from.Value = DateTime.Today.AddDays(preset.SelectedIndex == 0 ? 0 : preset.SelectedIndex == 1 ? -6 : -29); to.Value = DateTime.Today; }
            changing = false;
            StatisticsPeriod period = null; bool isLifetime = source.SelectedIndex == 1; string root = folder.Text;
            try { if (!isLifetime) { root = FilesystemRules.Normalize(root.Trim()); period = StatisticsPeriod.FromDates(from.Value, to.Value, TimeZoneInfo.Local, DateTime.UtcNow); } }
            catch (Exception ex) { status.Text = ex.Message; return; }
            if (detail != null) detail.Cancel(); int run = ++generation; busy = true; operation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token); var active = operation; CancellationToken token = active.Token;
            loadedPeriod = period; loadedLifetime = isLifetime; loadedRange = isLifetime ? "Lifetime · unique downloads per IP" : from.Value.ToString("dd MMM yyyy") + " – " + to.Value.ToString("dd MMM yyyy") + " · local time";
            rows.Clear(); folderErrors = 0; RenderRows(); chart.Clear("Loading statistics…"); selectedPoints = null; progress.Style = ProgressBarStyle.Marquee; status.Text = "Finding content…"; UpdateControls();
            bool discovering = true;
            try
            {
                List<FilesystemNode> nodes = null; List<CloudFile> flat = null;
                if (!isLifetime)
                {
                    var catalog = await client.DiscoverStatisticsFilesAsync(root, recursive.Checked, key, new Progress<StatisticsScanProgress>(p => { if (disposed || run != generation || !discovering) return; status.Text = "Found " + p.Discovered + " files · " + (p.Name ?? root); }), token);
                    nodes = catalog.Files; folderErrors = catalog.SkippedFolders;
                }
                else flat = cloudFiles().ToList();
                discovering = false;
                int total = isLifetime ? flat.Count : nodes.Count, next = -1, complete = 0; progress.Style = ProgressBarStyle.Continuous; progress.Maximum = Math.Max(1, total); progress.Value = 0;
                Func<Task> worker = async delegate
                {
                    while (true)
                    {
                        token.ThrowIfCancellationRequested(); int index = Interlocked.Increment(ref next); if (index >= total) break; StatisticsRow row;
                        string name = isLifetime ? flat[index].Name : nodes[index].Name;
                        try { row = isLifetime ? await client.GetFileStatisticsAsync(flat[index], key, token) : StatisticsData.Summarize(name, nodes[index].Path, await client.GetFilesystemStatisticsAsync(nodes[index].Path, period, key, token)); }
                        catch (OperationCanceledException) { throw; }
                        catch (StatisticsRequestException ex) { if (ex.Status == 401 || ex.Status == 403 || ex.Status == 429) { active.Cancel(); throw; } row = new StatisticsRow { Name = name, Path = isLifetime ? null : nodes[index].Path, Id = isLifetime ? flat[index].Id : null, Error = ex.Message, Lifetime = isLifetime }; }
                        catch (Exception ex) { row = new StatisticsRow { Name = name, Path = isLifetime ? null : nodes[index].Path, Id = isLifetime ? flat[index].Id : null, Error = ex.Message, Lifetime = isLifetime }; }
                        if (disposed || run != generation || token.IsCancellationRequested) return; rows.Add(row); complete++; progress.Value = Math.Min(total, complete); status.Text = "Reading statistics " + complete + " / " + total + " · " + name;
                        if ((DateTime.UtcNow - lastRender).TotalMilliseconds > 500) { RenderRows(); lastRender = DateTime.UtcNow; }
                    }
                };
                await Task.WhenAll(Enumerable.Range(0, Math.Min(3, Math.Max(1, total))).Select(i => worker()));
                if (disposed || run != generation) return;
                status.Text = rows.Count + " files checked · " + rows.Count(r => r.Downloads > 0 && r.Error == null) + " with downloads" + (rows.Any(r => r.Error != null) || folderErrors > 0 ? " · Some items unavailable; results are incomplete." : " · Updated " + DateTime.Now.ToString("HH:mm:ss"));
            }
            catch (OperationCanceledException) { if (!disposed && run == generation) status.Text = "Scan cancelled. Showing " + rows.Count + " checked files; totals are partial."; }
            catch (Exception ex) { if (!disposed && run == generation) status.Text = ex.Message + " Showing partial results."; }
            finally { if (!disposed && run == generation) { busy = false; progress.Style = ProgressBarStyle.Continuous; RenderRows(); UpdateControls(); } operation = null; active.Dispose(); }
            if (!disposed && run == generation) { if (shown.Count > 0) { int selectedIndex = Math.Max(0, shown.FindIndex(r => (r.Path ?? r.Id) == preferred)); changing = true; grid.CurrentCell = grid[0, selectedIndex]; grid.Rows[selectedIndex].Selected = true; changing = false; await LoadChartAsync(); } else chart.Clear(rows.Any(r => r.Error != null) ? "Some statistics could not be loaded. Choose All results to inspect them." : "No downloaded content matches these filters."); }
        }
        private void RenderRows()
        {
            if (grid == null || summary == null) return; string selected = Selected == null ? null : Selected.Path ?? Selected.Id; string term = search.Text.Trim(); IEnumerable<StatisticsRow> query = rows.Where(r => term.Length == 0 || ((r.Name ?? "") + " " + r.Path).IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0);
            if (visibility.SelectedIndex == 0) query = query.Where(r => r.Error == null && r.Downloads > 0); else if (visibility.SelectedIndex == 2) query = query.Where(r => r.Error != null);
            if (ordering.SelectedIndex == 1) query = query.OrderByDescending(r => r.Bytes); else if (ordering.SelectedIndex == 2) query = query.OrderByDescending(r => r.LastActivityUtc); else if (ordering.SelectedIndex == 3) query = query.OrderBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase); else query = query.OrderByDescending(r => r.Downloads).ThenBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase);
            changing = true; shown = query.ToList(); grid.RowCount = shown.Count; grid.ClearSelection(); int index = shown.FindIndex(r => (r.Path ?? r.Id) == selected); if (index >= 0) { grid.CurrentCell = grid[0, index]; grid.Rows[index].Selected = true; } changing = false; grid.Invalidate();
            var valid = rows.Where(r => r.Error == null).ToList(); summary.Downloads = valid.Sum(r => (decimal)r.Downloads).ToString("N0"); decimal bytes = valid.Sum(r => (decimal)r.Bytes); summary.Bandwidth = bytes > Int64.MaxValue ? "> 8 EB" : FileRules.SizeText((long)bytes); summary.ActiveFiles = valid.Count(r => r.Downloads > 0).ToString("N0"); summary.Invalidate();
            if (Selected == null || (Selected.Path ?? Selected.Id) != chartKey) { selectedPoints = null; graphTitle.Text = "Selected content"; chart.Clear(shown.Count == 0 ? "No content matches these filters." : "Select a file to view its chart."); }
        }
        private void ValueNeeded(object sender, DataGridViewCellValueEventArgs e)
        {
            if (e.RowIndex < 0 || e.RowIndex >= shown.Count) return; StatisticsRow row = shown[e.RowIndex];
            e.Value = e.ColumnIndex == 0 ? row.Name : e.ColumnIndex == 1 ? (row.Error == null ? row.Downloads.ToString("N0") : "—") : e.ColumnIndex == 2 ? (row.Error == null ? FileRules.SizeText(row.Bytes) : "—") : e.ColumnIndex == 3 ? (row.LastActivityUtc.HasValue ? row.LastActivityUtc.Value.ToLocalTime().ToString("dd MMM HH:mm") : "—") : row.Error != null ? "Unavailable" : row.Lifetime ? "Lifetime" : "Ready";
        }
        private async Task LoadChartAsync()
        {
            StatisticsRow row = Selected; if (row == null || disposed) return; if (detail != null) detail.Cancel(); int run = ++detailGeneration; detail = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token); var active = detail; int scan = generation; selectedPoints = null; chartKey = null; graphTitle.Text = row.Name;
            if (row.Error != null) { chart.Clear(row.Error); active.Dispose(); detail = null; return; }
            chart.Clear("Loading chart for " + row.Name + "…");
            try
            {
                List<StatisticsPoint> data = row.Lifetime ? new List<StatisticsPoint> { new StatisticsPoint { TimeUtc = DateTime.MinValue, Downloads = row.Downloads, FreeBytes = row.FreeBytes, PaidBytes = row.PaidBytes } } : await client.GetFilesystemStatisticsAsync(row.Path, loadedPeriod, key, active.Token);
                if (disposed || active.IsCancellationRequested || run != detailGeneration || scan != generation) return; selectedPoints = data; chartKey = row.Path ?? row.Id; UpdateChart();
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { if (!disposed && run == detailGeneration) chart.Clear(ex.Message); }
            finally { if (Object.ReferenceEquals(detail, active)) detail = null; active.Dispose(); }
        }
        private void UpdateChart()
        {
            StatisticsRow row = Selected; if (row == null || selectedPoints == null || (row.Path ?? row.Id) != chartKey) return;
            graphTitle.Text = row.Name;
            chart.Bandwidth = metric.SelectedIndex == 1; var data = row.Lifetime ? selectedPoints : StatisticsData.Aggregate(selectedPoints, interval.SelectedIndex == 0, TimeZoneInfo.Local);
            chart.SetData(row.Name, data, row.Lifetime, loadedRange + (row.Lifetime ? " · Total per file; dated history is unavailable" : " · " + (interval.SelectedIndex == 0 ? "Daily" : "Hourly")));
        }
        private void Export()
        {
            using (var picker = new SaveFileDialog { Title = "Export statistics", Filter = "CSV files (*.csv)|*.csv", FileName = "Pixelroom-statistics-" + DateTime.Now.ToString("yyyyMMdd") + ".csv", OverwritePrompt = true })
                if (picker.ShowDialog(this) == DialogResult.OK) try { File.WriteAllText(picker.FileName, StatisticsData.Csv(shown, loadedRange), new System.Text.UTF8Encoding(true)); status.Text = "Statistics exported."; } catch (Exception ex) { status.Text = ex.Message; }
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing && !disposed) { disposed = true; generation++; lifetime.Cancel(); if (operation != null) operation.Cancel(); if (detail != null) detail.Cancel(); timer.Dispose(); lifetime.Dispose(); if (grid.ContextMenuStrip != null) grid.ContextMenuStrip.Dispose(); } base.Dispose(disposing);
        }
    }
}
