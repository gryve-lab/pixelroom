using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Pixelroom
{
    internal sealed class FilesystemTransferEventArgs : EventArgs
    {
        public string Name, Status, Direction, LocalPath;
        public TransferMetrics Metrics;
        public CancellationTokenSource Cancellation;
        public bool Finished;
    }
    public sealed class FilesystemWindow : Form
    {
        private readonly PixelDrainClient client;
        private readonly string key;
        private readonly DataGridView grid;
        private readonly TextBox address;
        private readonly CueTextBox search;
        private readonly ComboBox sort;
        private readonly BrowseFilterBar browseFilters;
        private readonly TransferStatusPanel transferStatus;
        private readonly TableLayoutPanel layout;
        private readonly Label folderSharing;
        private readonly Label status, emptyTitle, emptyBody, itemCount;
        private readonly Panel empty;
        private readonly Button home, up, go, refresh, upload, uploadFolders, share, download, create, rename, move, delete, import, cancel, copy, paste, clearSearch;
        private readonly CancellationTokenSource lifetime = new CancellationTokenSource();
        private CancellationTokenSource operation;
        private FilesystemDirectory directory;
        private List<FilesystemNode> copied = new List<FilesystemNode>();
        private readonly HashSet<string> marked = new HashSet<string>(StringComparer.Ordinal);
        private readonly ActionToolbar actionRows;
        private readonly Button selectAll;
        private List<FilesystemNode> nodes = new List<FilesystemNode>();
        private string currentPath = "/me";
        private bool busy, closeAfter;
        private bool transferActive, disposed;
        private Task initialization;
        private FilesystemTransferEventArgs transferEvent;
        internal event EventHandler<FilesystemTransferEventArgs> TransferChanged;
        internal bool IsBusy { get { return busy; } }
        internal void CancelOperation() { if (operation != null) operation.Cancel(); }

        public FilesystemWindow(PixelDrainClient client, string key, string[] initialUploads = null, bool embedded = false)
        {
            this.client = client; this.key = key;
            Text = "File System — Pixelroom"; ClientSize = new Size(1080, 760); MinimumSize = new Size(900, 620); StartPosition = FormStartPosition.CenterParent;
            if (embedded) { TopLevel = false; FormBorderStyle = FormBorderStyle.None; MinimumSize = Size.Empty; Dock = DockStyle.Fill; }
            Font = Theme.Font(9); AutoScaleMode = AutoScaleMode.Dpi; KeyPreview = true; AllowDrop = true;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
            layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 8, Padding = embedded ? Padding.Empty : new Padding(24, 8, 24, 10) };
            foreach (float height in new float[] { embedded ? 30 : 94, 51, 48, 46, 94 }) layout.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 0)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48)); Controls.Add(layout);
            Panel heading = embedded ? new Panel() : new BackdropPanel(); heading.Dock = DockStyle.Fill; heading.Margin = Padding.Empty;
            Label title = Theme.Label("File System", 27, Theme.Ink, FontStyle.Bold); title.SetBounds(0, 22, 400, 50); heading.Controls.Add(title); layout.Controls.Add(heading, 0, 0);
            title.Visible = !embedded;
            folderSharing = Theme.Label("", 8, Theme.Muted); folderSharing.SetBounds(0, embedded ? 3 : 72, 1000, 22); folderSharing.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right; heading.Controls.Add(folderSharing);
            TableLayoutPanel location = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 5, RowCount = 1, Margin = Padding.Empty, Padding = new Padding(0, 5, 0, 8) };
            location.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 79)); location.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 65)); location.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); location.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 60)); location.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 105));
            home = Theme.Button("Root"); home.Dock = DockStyle.Fill; home.Margin = new Padding(0, 0, 8, 0); home.Click += async delegate { await Navigate("/me"); }; location.Controls.Add(home, 0, 0);
            up = Theme.Button("Up"); up.Dock = DockStyle.Fill; up.Margin = new Padding(0, 0, 8, 0); up.Click += async delegate { await Navigate(FilesystemRules.Parent(currentPath)); }; location.Controls.Add(up, 1, 0);
            address = new TextBox { Text = "/me", Font = Theme.Font(10), BorderStyle = BorderStyle.FixedSingle, Dock = DockStyle.Fill, AccessibleName = "File System path", Margin = new Padding(0, 6, 8, 0) };
            address.KeyDown += async delegate(object sender, KeyEventArgs e) { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; await Navigate(address.Text); } }; location.Controls.Add(address, 2, 0);
            go = Theme.Button("Go"); go.Dock = DockStyle.Fill; go.Margin = new Padding(0, 0, 8, 0); go.Click += async delegate { await Navigate(address.Text); }; location.Controls.Add(go, 3, 0);
            refresh = Theme.Button("Refresh"); refresh.Dock = DockStyle.Fill; refresh.Margin = Padding.Empty; refresh.Click += async delegate { await Navigate(currentPath); }; location.Controls.Add(refresh, 4, 0); layout.Controls.Add(location, 0, 1);
            TableLayoutPanel searchRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 1, Margin = Padding.Empty, Padding = new Padding(0, 0, 0, 10) };
            searchRow.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            searchRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); searchRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 78)); searchRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 178)); searchRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
            FieldFrame searchFrame = new FieldFrame { IconName = "search", Dock = DockStyle.Fill, Margin = new Padding(0, 0, 8, 0) };
            search = new CueTextBox { Cue = "Search this folder…", Font = Theme.Font(10), BorderStyle = BorderStyle.None, AccessibleName = "Search this folder" }; search.SetBounds(38, 9, 300, 22); searchFrame.Controls.Add(search);
            searchFrame.Layout += delegate { search.Width = Math.Max(30, searchFrame.Width - search.Left - 12); }; search.Enter += delegate { searchFrame.Invalidate(); }; search.Leave += delegate { searchFrame.Invalidate(); };
            search.TextChanged += delegate { ApplyFilter(); if (!busy && directory != null) status.Text = "Showing " + nodes.Count + " of " + directory.Children.Count + " items."; }; searchRow.Controls.Add(searchFrame, 0, 0);
            clearSearch = Theme.Button("Clear"); clearSearch.Dock = DockStyle.Fill; clearSearch.Margin = new Padding(0, 0, 8, 0); clearSearch.Click += delegate { search.Clear(); search.Focus(); }; searchRow.Controls.Add(clearSearch, 1, 0);
            sort = new FilterCombo { Dock = DockStyle.Fill, AccessibleName = "Sort folder items", DropDownStyle = ComboBoxStyle.DropDownList, Margin = new Padding(0, 0, 8, 0) }; sort.Items.AddRange(new object[] { "Name A → Z", "Name Z → A", "Newest first", "Oldest first", "Largest first", "Smallest first" }); sort.SelectedIndex = 0; searchRow.Controls.Add(sort, 2, 0); sort.SelectedIndexChanged += delegate { ApplyFilter(); };
            itemCount = Theme.Label("0 items", 8, Theme.Muted); itemCount.Dock = DockStyle.Fill; itemCount.TextAlign = ContentAlignment.MiddleRight; itemCount.Margin = Padding.Empty; searchRow.Controls.Add(itemCount, 3, 0); layout.Controls.Add(searchRow, 0, 2);
            browseFilters = new BrowseFilterBar(true); browseFilters.Changed += delegate { ApplyFilter(); }; browseFilters.ResetRequested += delegate { search.Clear(); sort.SelectedIndex = 0; ApplyFilter(); }; layout.Controls.Add(browseFilters, 0, 3);
            actionRows = new ActionToolbar(); layout.Controls.Add(actionRows, 0, 4);
            upload = actionRows.AddAction("Upload files", true); upload.Click += async delegate { await Upload(); };
            uploadFolders = actionRows.AddAction("Upload folders"); ((ThemedButton)uploadFolders).IconName = "folder"; uploadFolders.Click += async delegate { await ChooseFolders(); };
            download = actionRows.AddAction("Download"); download.Click += async delegate { await Download(); };
            create = actionRows.AddAction("New folder"); ((ThemedButton)create).IconName = "folder"; create.Click += async delegate { await Create(); };
            copy = actionRows.AddAction("Copy"); copy.Click += delegate { CopyItem(); };
            paste = actionRows.AddAction("Paste"); paste.Click += async delegate { await PasteItem(); };
            move = actionRows.AddAction("Move to…"); move.Click += async delegate { await MoveItem(); };
            rename = actionRows.AddAction("Rename"); rename.Click += async delegate { await Rename(); };
            share = actionRows.AddAction("Share…"); share.Click += async delegate { await ShareItem(); };
            import = actionRows.AddAction("Import files"); import.Click += async delegate { await Import(); };
            delete = actionRows.AddAction("Delete"); delete.Click += async delegate { await Delete(); };
            selectAll = actionRows.AddAction("Select all"); selectAll.Click += delegate { MarkVisible(); };
            layout.Layout += delegate { int required = actionRows.HeightForWidth(layout.ClientSize.Width - layout.Padding.Horizontal); if (layout.RowStyles[4].Height != required) layout.RowStyles[4].Height = required; }; layout.RowStyles[4].Height = actionRows.RequiredHeight;
            SurfaceCard container = new SurfaceCard { Dock = DockStyle.Fill, Margin = Padding.Empty }; layout.Controls.Add(container, 0, 5);
            grid = new FileGrid { Dock = DockStyle.Fill, VirtualMode = true, ReadOnly = true, RowHeadersVisible = false, MultiSelect = true, SelectionMode = DataGridViewSelectionMode.FullRowSelect, AllowUserToAddRows = false, AllowUserToDeleteRows = false, AllowUserToResizeRows = false, EnableHeadersVisualStyles = false, BorderStyle = BorderStyle.None, CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal, ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None, ColumnHeadersHeight = 42, ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing };
            grid.DefaultCellStyle.Padding = new Padding(9, 0, 0, 0); grid.RowTemplate.Height = 49;
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "name", HeaderText = "Name", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, MinimumWidth = 160, SortMode = DataGridViewColumnSortMode.NotSortable });
            grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Type", Width = 70 }); grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Size", Width = 82 }); grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Modified", Width = 142 }); grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "sharing", HeaderText = "Sharing", Width = 148 });
            grid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "mark", HeaderText = "Select all visible", Width = 40, DisplayIndex = 0, SortMode = DataGridViewColumnSortMode.NotSortable });
            foreach (DataGridViewColumn column in grid.Columns) column.SortMode = DataGridViewColumnSortMode.NotSortable;
            grid.CellValueNeeded += delegate(object sender, DataGridViewCellValueEventArgs e) { if (e.RowIndex < 0 || e.RowIndex >= nodes.Count) return; FilesystemNode node = nodes[e.RowIndex]; if (e.ColumnIndex == 5) { e.Value = marked.Contains(node.Path); return; } DateTime date = browseFilters.Query.DateField == BrowseDate.Created ? node.Created : node.Modified; e.Value = e.ColumnIndex == 0 ? node.Name : e.ColumnIndex == 1 ? (node.IsDirectory ? "Folder" : "File") : e.ColumnIndex == 2 ? (node.IsDirectory ? "—" : FileRules.SizeText(node.Size)) : e.ColumnIndex == 4 ? node.SharingText : date == DateTime.MinValue ? "—" : date.ToLocalTime().ToString("dd MMM yyyy  HH:mm"); };
            grid.CellToolTipTextNeeded += delegate(object sender, DataGridViewCellToolTipTextNeededEventArgs e) { if (e.RowIndex < 0 || e.RowIndex >= nodes.Count) return; FilesystemNode node = nodes[e.RowIndex]; e.ToolTipText = node.Path + "\n" + (node.OwnLinkReadable ? "Anyone with this item's link can read it." : node.InheritedShared ? "Anyone with the shared parent folder's link can read this item." : "This item has no active public read link. Individual children may have their own links."); };
            grid.ColumnHeaderMouseClick += delegate(object sender, DataGridViewCellMouseEventArgs e) { if (e.Button != MouseButtons.Left) return; if (e.ColumnIndex == 5) MarkVisible(); else if (e.ColumnIndex == 0) sort.SelectedIndex = sort.SelectedIndex == 0 ? 1 : 0; else if (e.ColumnIndex == 2) sort.SelectedIndex = sort.SelectedIndex == 4 ? 5 : 4; else if (e.ColumnIndex == 3) sort.SelectedIndex = sort.SelectedIndex == 2 ? 3 : 2; };
            grid.CellFormatting += delegate(object sender, DataGridViewCellFormattingEventArgs e) { bool check = e.RowIndex >= 0 && e.RowIndex < nodes.Count && marked.Contains(nodes[e.RowIndex].Path); e.CellStyle.BackColor = check ? Theme.Pale : Theme.Surface; e.CellStyle.ForeColor = Theme.Ink; e.CellStyle.SelectionBackColor = marked.Count > 0 && !check ? Theme.Surface : Theme.Pale; e.CellStyle.SelectionForeColor = Theme.Ink; };
            grid.CellPainting += PaintName;
            grid.CellPainting += delegate(object sender, DataGridViewCellPaintingEventArgs e) { if (e.ColumnIndex == 5) { bool all = e.RowIndex < 0 ? nodes.Count > 0 && nodes.All(n => marked.Contains(n.Path)) : e.RowIndex < nodes.Count && marked.Contains(nodes[e.RowIndex].Path); SelectionPaint.Header(grid, e, all, e.RowIndex < 0 && nodes.Any(n => marked.Contains(n.Path))); } };
            grid.CellMouseClick += delegate(object sender, DataGridViewCellMouseEventArgs e) { if (e.Button == MouseButtons.Left && e.ColumnIndex == 5 && e.RowIndex >= 0 && e.RowIndex < nodes.Count) ToggleMarked(nodes[e.RowIndex].Path); };
            grid.CellDoubleClick += async delegate(object sender, DataGridViewCellEventArgs e) { if (e.ColumnIndex != 5 && e.RowIndex >= 0 && e.RowIndex < nodes.Count && nodes[e.RowIndex].IsDirectory) await Navigate(nodes[e.RowIndex].Path); };
            grid.SelectionChanged += delegate { UpdateControls(); }; container.Controls.Add(grid);
            grid.CellMouseDown += delegate(object sender, DataGridViewCellMouseEventArgs e) { if (e.Button == MouseButtons.Right && e.RowIndex >= 0 && e.RowIndex < nodes.Count && !marked.Contains(nodes[e.RowIndex].Path) && !grid.Rows[e.RowIndex].Selected) { marked.Clear(); grid.ClearSelection(); grid.Rows[e.RowIndex].Selected = true; grid.Invalidate(); } };
            empty = new Panel { Dock = DockStyle.Fill, Tag = "surface" }; container.Controls.Add(empty);
            emptyTitle = Theme.Label("Connect your account", 19, Theme.Ink, FontStyle.Bold); emptyTitle.TextAlign = ContentAlignment.MiddleCenter; empty.Controls.Add(emptyTitle);
            emptyBody = Theme.Label("Add your API key in Settings to browse your PixelDrain folders.", 9, Theme.Muted); emptyBody.TextAlign = ContentAlignment.TopCenter; empty.Controls.Add(emptyBody);
            ContextMenuStrip menu = new ContextMenuStrip();
            ToolStripItem menuCopy = menu.Items.Add("Copy", null, delegate { CopyItem(); }); ((ToolStripMenuItem)menuCopy).ShortcutKeyDisplayString = "Ctrl+C";
            ToolStripItem menuPaste = menu.Items.Add("Paste", null, async delegate { await PasteItem(); }); ((ToolStripMenuItem)menuPaste).ShortcutKeyDisplayString = "Ctrl+V";
            menu.Items.Add(new ToolStripSeparator()); ToolStripItem menuShare = menu.Items.Add("Share…", null, async delegate { await ShareItem(); });
            menu.Items.Add("Select all visible", null, delegate { MarkVisible(); }); menu.Items.Add("Clear selection", null, delegate { ClearMarks(); });
            menu.Opening += delegate { menuCopy.Enabled = copy.Enabled; menuPaste.Enabled = paste.Enabled; menuShare.Enabled = share.Enabled; }; grid.ContextMenuStrip = empty.ContextMenuStrip = menu;
            empty.Layout += delegate { int y = Math.Max(8, empty.Height / 2 - 50); emptyTitle.SetBounds(15, y, Math.Max(0, empty.Width - 30), 45); emptyBody.SetBounds(25, y + 56, Math.Max(0, empty.Width - 50), 65); };
            transferStatus = new TransferStatusPanel { Visible = false }; layout.Controls.Add(transferStatus, 0, 6);
            FooterPanel footer = new FooterPanel { Dock = DockStyle.Fill, Margin = Padding.Empty }; layout.Controls.Add(footer, 0, 7);
            status = Theme.Label(String.IsNullOrEmpty(key) ? "Not connected" : "Ready", 8, Theme.Muted); status.SetBounds(0, 14, 600, 25); footer.Controls.Add(status);
            cancel = Theme.Button("Cancel"); cancel.Width = 88; cancel.Height = 31; cancel.Click += delegate { if (operation != null) operation.Cancel(); }; footer.Controls.Add(cancel);
            Label credit = Theme.Label("Disign By Gryve", 8, Theme.Muted); credit.Width = 145; credit.Height = 24; credit.TextAlign = ContentAlignment.MiddleRight; footer.Controls.Add(credit);
            credit.Visible = !embedded;
            footer.Layout += delegate { float s = footer.DeviceDpi / 96f; credit.Location = new Point(footer.Width - credit.Width, footer.Height - credit.Height); cancel.Location = new Point(credit.Left - cancel.Width - (int)(13 * s), (int)(11 * s)); status.Width = Math.Max(100, (busy ? cancel.Left : credit.Left) - (int)(12 * s)); };
            Shown += async delegate { Theme.Apply(this); UpdateControls(); empty.PerformLayout(); empty.BringToFront(); footer.PerformLayout(); Refresh(); if (!String.IsNullOrEmpty(key)) { await InitializeAsync(); if (directory != null && initialUploads != null) await UploadPaths(initialUploads); } };
            EnableFileDrop(this); EnableFileDrop(grid); EnableFileDrop(container); EnableFileDrop(empty); EnableFileDrop(emptyTitle); EnableFileDrop(emptyBody);
            KeyDown += async delegate(object sender, KeyEventArgs e)
            {
                if (e.Control && e.KeyCode == Keys.F) { e.SuppressKeyPress = true; if (search.Enabled) { search.Focus(); search.SelectAll(); } }
                else if (e.KeyCode == Keys.F5) { e.SuppressKeyPress = true; await Navigate(currentPath); }
                else if (e.KeyCode == Keys.Escape && search.Focused) { e.SuppressKeyPress = true; search.Clear(); }
                else if (!address.Focused && !search.Focused && e.Control && e.KeyCode == Keys.C) { e.SuppressKeyPress = true; CopyItem(); }
                else if (!address.Focused && !search.Focused && e.Control && e.KeyCode == Keys.V) { e.SuppressKeyPress = true; await PasteItem(); }
            };
            FormClosing += delegate(object sender, FormClosingEventArgs e)
            {
                if (!busy) return; e.Cancel = true;
                if (!closeAfter && Notice.Show(this, "Cancel the active operation and close File System?", "Close File System", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) == DialogResult.Yes) { closeAfter = true; if (operation != null) operation.Cancel(); }
            };
            FormClosed += delegate { menu.Dispose(); }; UpdateControls();
        }
        internal Task InitializeAsync() { if (initialization == null) initialization = Navigate("/me"); return initialization; }
        internal async Task NavigateToAsync(string path) { await InitializeAsync(); await Navigate(path); }
        internal bool HandleBrowserKey(KeyEventArgs e)
        {
            if (e.Control && e.Shift && e.KeyCode == Keys.A) { ClearMarks(); e.SuppressKeyPress = true; return true; }
            if (e.Control && e.KeyCode == Keys.F) { if (search.Enabled) { search.Focus(); search.SelectAll(); } }
            else if (e.Control && e.KeyCode == Keys.L) { if (address.Enabled) { address.Focus(); address.SelectAll(); } }
            else if (e.KeyCode == Keys.F5) Navigate(currentPath).ContinueWith(delegate { });
            else if (e.Control && e.KeyCode == Keys.U) { if (upload.Enabled) Upload().ContinueWith(delegate { }); }
            else if (!address.Focused && !search.Focused && e.Control && e.KeyCode == Keys.A) SelectVisible();
            else if (!address.Focused && !search.Focused && e.KeyCode == Keys.Space && grid.CurrentCell != null && grid.CurrentCell.RowIndex < nodes.Count) ToggleMarked(nodes[grid.CurrentCell.RowIndex].Path);
            else if (!address.Focused && !search.Focused && e.KeyCode == Keys.Escape) ClearMarks();
            else if (!address.Focused && !search.Focused && e.Control && e.KeyCode == Keys.C) CopyItem();
            else if (!address.Focused && !search.Focused && e.Control && e.KeyCode == Keys.V) PasteItem().ContinueWith(delegate { });
            else if (e.Alt && e.KeyCode == Keys.Up) Navigate(FilesystemRules.Parent(currentPath)).ContinueWith(delegate { });
            else return false;
            e.SuppressKeyPress = true; return true;
        }
        internal async Task UploadSelectionAsync(string[] paths)
        {
            await InitializeAsync();
            if (busy) { Notice.Show(this, "Wait for the current operation, or cancel it before starting another upload.", "File System", MessageBoxButtons.OK); return; }
            await UploadPaths(paths);
        }
        protected override void Dispose(bool disposing)
        { if (disposing && !disposed) { disposed = true; lifetime.Cancel(); lifetime.Dispose(); } base.Dispose(disposing); }
        private List<FilesystemNode> SelectedNodes()
        { return marked.Count > 0 && directory != null ? directory.Children.Where(n => marked.Contains(n.Path)).ToList() : grid.SelectedRows.Cast<DataGridViewRow>().Where(r => r.Index >= 0 && r.Index < nodes.Count).OrderBy(r => r.Index).Select(r => nodes[r.Index]).ToList(); }
        private FilesystemNode Selected { get { return SelectedNodes().FirstOrDefault(); } }
        private void ToggleMarked(string path) { if (busy) return; if (!marked.Add(path)) marked.Remove(path); grid.ClearSelection(); grid.Invalidate(); UpdateControls(); }
        private void ClearMarks() { marked.Clear(); grid.ClearSelection(); grid.Invalidate(); UpdateControls(); }
        private void MarkVisible()
        { if (busy) return; if (marked.Count > 0 && (nodes.Count == 0 || nodes.All(n => marked.Contains(n.Path)))) ClearMarks(); else SelectVisible(); }
        private void SelectVisible() { if (busy) return; foreach (FilesystemNode node in nodes) marked.Add(node.Path); grid.ClearSelection(); grid.Invalidate(); UpdateControls(); }
        private void UpdateControls()
        {
            if (cancel == null) return; bool connected = !String.IsNullOrEmpty(key), idle = connected && !busy; List<FilesystemNode> selected = SelectedNodes(); FilesystemNode node = selected.FirstOrDefault();
            home.Enabled = go.Enabled = refresh.Enabled = address.Enabled = idle; up.Enabled = idle && currentPath != "/me"; cancel.Enabled = busy; cancel.Visible = busy;
            upload.Enabled = uploadFolders.Enabled = create.Enabled = import.Enabled = idle && directory != null && directory.CanWrite;
            download.Enabled = idle && node != null && directory != null && directory.CanRead;
            move.Enabled = idle && node != null && directory != null && directory.CanWrite && directory.CanDelete; rename.Enabled = move.Enabled && selected.Count == 1;
            delete.Enabled = idle && node != null && directory != null && directory.CanDelete;
            copy.Enabled = idle && node != null && directory != null && directory.CanRead;
            share.Enabled = idle && node != null && directory != null && directory.CanRead;
            paste.Enabled = idle && copied.Count > 0 && directory != null && directory.CanWrite;
            selectAll.Enabled = idle && (nodes.Count > 0 || marked.Count > 0); selectAll.Text = marked.Count > 0 && (nodes.Count == 0 || nodes.All(n => marked.Contains(n.Path))) ? "Clear selection" : "Select all";
            HashSet<string> shown = new HashSet<string>(nodes.Select(n => n.Path), StringComparer.Ordinal); int hidden = marked.Count(n => !shown.Contains(n));
            itemCount.Text = selected.Count > 0 ? selected.Count + (marked.Count > 0 ? " marked" : " selected") + (hidden > 0 ? " · " + hidden + " hidden" : "") : nodes.Count + " of " + (directory == null ? 0 : directory.Children.Count) + " items";
            search.Enabled = clearSearch.Enabled = sort.Enabled = browseFilters.Enabled = idle && directory != null;
            grid.Enabled = !busy;
        }
        private async Task Run(Func<CancellationToken, Task> action, string completed = null, bool reload = false)
        {
            if (busy || String.IsNullOrEmpty(key)) return;
            busy = true; operation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token); CancellationTokenSource active = operation; UpdateControls(); status.Text = "Working…";
            bool changed = false, succeeded = false; string outcome = "Failed";
            try { await action(active.Token); succeeded = true; changed = reload; if (reload) await LoadFolder(currentPath, active.Token); outcome = "Completed"; if (!IsDisposed) status.Text = completed ?? nodes.Count + (nodes.Count == 1 ? " item" : " items"); }
            catch (OperationCanceledException) { outcome = "Cancelled"; if (!IsDisposed) status.Text = "Operation cancelled. Refresh to check the latest contents."; }
            catch (Exception ex) { if (!IsDisposed) { status.Text = changed ? "Change completed; refresh failed." : ex.Message; address.Text = currentPath; Notice.Show(this, (changed ? "The change completed, but the folder could not be refreshed.\n\n" : "") + ex.Message, "File System", MessageBoxButtons.OK); } }
            finally { if (transferActive) { transferStatus.End(succeeded ? "Completed" : outcome, succeeded); PublishTransfer(succeeded ? "Completed" : outcome, true); transferActive = false; } operation = null; active.Dispose(); busy = false; if (!IsDisposed) { UpdateControls(); if (closeAfter) BeginInvoke(new Action(Close)); } }
        }
        private Task Navigate(string path) { return Run(async delegate(CancellationToken token) { await LoadFolder(FilesystemRules.Normalize(path.Trim()), token); }); }
        private async Task LoadFolder(string path, CancellationToken token)
        {
            FilesystemDirectory loaded = await client.ListFilesystemAsync(path, key, token); if (IsDisposed || token.IsCancellationRequested) return;
            bool different = currentPath != loaded.Current.Path; directory = loaded; currentPath = loaded.Current.Path; address.Text = currentPath;
            if (different) marked.Clear(); else marked.IntersectWith(loaded.Children.Select(n => n.Path));
            folderSharing.Text = "Current folder: " + loaded.Current.SharingText + (loaded.Current.IsShared ? " · Its contents are accessible through a shared link." : " · Items inside may have their own shared links.");
            if (different) search.Clear(); ApplyFilter();
        }
        private void ApplyFilter()
        {
            if (directory == null || grid == null || empty == null || itemCount == null) return;
            string selectedPath = Selected == null ? null : Selected.Path;
            BrowseQuery query = browseFilters.Query; query.Search = search.Text; query.Sort = (BrowseSort)sort.SelectedIndex;
            nodes = query.Apply(directory.Children);
            grid.Columns[3].HeaderText = query.DateField == BrowseDate.Created ? "Created" : "Modified";
            grid.RowCount = nodes.Count; grid.ClearSelection(); int selectedIndex = nodes.FindIndex(n => n.Path == selectedPath); if (selectedIndex >= 0) grid.Rows[selectedIndex].Selected = true;
            grid.Invalidate(); itemCount.Text = nodes.Count + " of " + directory.Children.Count + " items";
            empty.Visible = nodes.Count == 0; emptyTitle.Text = query.Active ? "No matching items" : "This folder is empty";
            emptyBody.Text = query.Active ? "Change the filters or choose Reset filters to see this folder." : directory.CanWrite ? "Drop files or folders here to upload, or create a folder to get started." : "There are no files in this folder.";
            if (empty.Visible) empty.BringToFront(); else grid.BringToFront(); UpdateControls();
        }
        private void CopyItem()
        {
            if (!copy.Enabled || Selected == null) return;
            copied = SelectedNodes().Select(node => new FilesystemNode { Path = node.Path, Name = node.Name, Type = node.Type, Size = node.Size }).ToList();
            UpdateControls(); status.Text = copied.Count + " item(s) copied. Open a destination folder and choose Paste.";
        }
        private async Task PasteItem()
        {
            if (!paste.Enabled || copied.Count == 0) return; List<FilesystemNode> source = copied.ToList();
            await Run(async delegate(CancellationToken token)
            {
                BeginTransfer("Copying", source.Count + " selected items");
                CancellationTokenSource active = operation;
                var progress = BatchProgress(active);
                try { await client.CopyFilesystemBatchAsync(source, currentPath, key, progress, token); }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) { throw new IOException("Copy stopped. Completed items may remain in the destination. Refresh to check.\n\n" + ex.Message); }
            }, "Copy complete.", true);
        }
        private void EnableFileDrop(Control target)
        {
            target.AllowDrop = true;
            target.DragEnter += delegate(object sender, DragEventArgs e) { e.Effect = !busy && directory != null && directory.CanWrite && e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None; };
            target.DragDrop += async delegate(object sender, DragEventArgs e) { string[] paths = e.Data.GetData(DataFormats.FileDrop) as string[]; if (paths != null) await UploadPaths(paths); };
        }
        private async Task Create()
        {
            using (TextDialog dialog = new TextDialog("New folder", "Folder name", "", 255)) if (dialog.ShowDialog(this) == DialogResult.OK)
                await Run(token => client.CreateFilesystemDirectoryAsync(FilesystemRules.Child(currentPath, dialog.Result), key, token), "Folder created.", true);
        }
        private async Task Rename()
        {
            FilesystemNode node = Selected; if (node == null || SelectedNodes().Count != 1) return;
            using (TextDialog dialog = new TextDialog("Rename", "New name", node.Name, 255)) if (dialog.ShowDialog(this) == DialogResult.OK)
                await Run(token => client.RenameFilesystemAsync(node.Path, FilesystemRules.Child(currentPath, dialog.Result), key, token), "Item renamed.", true);
        }
        private async Task MoveItem()
        {
            List<FilesystemNode> selected = SelectedNodes(); if (selected.Count == 0) return;
            using (TextDialog dialog = new TextDialog("Move to folder", "Destination folder path, starting with /me", currentPath, 4095)) if (dialog.ShowDialog(this) == DialogResult.OK)
                await Run(async delegate(CancellationToken token) { CancellationTokenSource active = operation; status.Text = "Moving " + selected.Count + " item(s)…"; await client.MoveFilesystemBatchAsync(selected, dialog.Result, key, new Progress<FilesystemCopyProgress>(p => { if (!IsDisposed && busy && Object.ReferenceEquals(operation, active)) status.Text = "Moving " + (p.Completed + 1) + "/" + p.Total + " · " + p.Name; }), token); }, "Selected items moved.", true);
        }
        private async Task Delete()
        {
            List<FilesystemNode> selected = SelectedNodes(); if (selected.Count == 0) return;
            string message = "Permanently delete " + selected.Count + " selected item(s)? Selected folders include all files inside them. This cannot be undone.\n\n" + String.Join("\n", selected.Take(8).Select(n => n.Name)) + (selected.Count > 8 ? "\n…" : "");
            if (Notice.Show(this, message, "Delete from File System", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) == DialogResult.Yes)
                await Run(async delegate(CancellationToken token) { foreach (FilesystemNode node in selected) { token.ThrowIfCancellationRequested(); status.Text = "Deleting · " + node.Name; await client.DeleteFilesystemAsync(node.Path, node.IsDirectory, key, token); } }, "Selected items deleted.", true);
        }
        private async Task Upload()
        {
            using (OpenFileDialog picker = new OpenFileDialog { Title = "Upload to File System", Multiselect = true, CheckFileExists = true })
            {
                if (picker.ShowDialog(this) != DialogResult.OK) return;
                await UploadPaths(picker.FileNames);
            }
        }
        private async Task UploadPaths(string[] paths)
        {
            if (busy || directory == null || !directory.CanWrite || String.IsNullOrEmpty(key)) return;
            await Run(async delegate(CancellationToken token)
            {
                BeginTransfer("Uploading", currentPath);
                status.Text = "Reading selected files and folders…";
                FilesystemDirectory target = await client.ListFilesystemAsync(currentPath, key, token);
                FolderUploadPlan plan = await Task.Run(() => FolderUploadPlan.Build(paths, currentPath, target.Children, token), token);
                bool overwrite = plan.Replacements.Count > 0;
                if (overwrite && Notice.Show(this, plan.Replacements.Count + " existing file(s) will be replaced. Continue? Folders with matching names are uploaded under new names.", "Replace files", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes) throw new OperationCanceledException();
                CancellationTokenSource active = operation;
                var progress = BatchProgress(active);
                await client.UploadFilesystemPlanAsync(plan, overwrite, key, progress, token);
            }, "Upload complete.", true);
        }
        private async Task ChooseFolders()
        {
            if (!uploadFolders.Enabled) return;
            try { string[] paths = FolderPicker.SelectFolders(this); if (paths.Length > 0) await UploadPaths(paths); }
            catch (Exception ex) { Notice.Show(this, ex.Message, "Upload folders", MessageBoxButtons.OK); }
        }
        private async Task ShareItem()
        {
            if (!share.Enabled || Selected == null) return;
            List<FilesystemNode> selected = SelectedNodes();
            using (ShareDialog dialog = selected.Count == 1 ? new ShareDialog(client, selected[0], key) : new ShareDialog(client, selected, key)) dialog.ShowDialog(this);
            if (!IsDisposed) await Navigate(currentPath);
        }
        private async Task Download()
        {
            List<FilesystemNode> selected = SelectedNodes(); if (selected.Count == 0) return; FilesystemNode node = selected[0];
            if (selected.Count > 1)
            {
                using (FolderBrowserDialog picker = new FolderBrowserDialog { Description = "Choose a folder for the selected downloads", ShowNewFolderButton = true })
                    if (picker.ShowDialog(this) == DialogResult.OK) await DownloadItems(selected, picker.SelectedPath);
                return;
            }
            using (SaveFileDialog picker = new SaveFileDialog { Title = node.IsDirectory ? "Download folder as ZIP" : "Download file", FileName = FileRules.SafeName(node.Name + (node.IsDirectory ? ".zip" : "")), OverwritePrompt = true })
                if (picker.ShowDialog(this) == DialogResult.OK) await Run(token => { BeginTransfer("Downloading", node.Name, picker.FileName); return client.DownloadFilesystemAsync(node.Path, node.IsDirectory, picker.FileName, key, Progress(node.Name), token); }, "Download complete.");
        }
        private Task DownloadItems(List<FilesystemNode> selected, string folder)
        {
            return Run(async delegate(CancellationToken token)
            {
                List<FilesystemNode> items = FilesystemBatch.Selection(selected); List<string> targets = FilesystemBatch.DownloadTargets(items, folder);
                BeginTransfer("Downloading", items[0].Name, folder); CancellationTokenSource active = operation;
                for (int i = 0; i < items.Count; i++)
                {
                    token.ThrowIfCancellationRequested(); FilesystemNode item = items[i]; int index = i;
                    var progress = new Progress<TransferProgress>(p => { if (IsDisposed || !busy || !Object.ReferenceEquals(active, operation)) return; transferStatus.Report("Downloading", item.Name, p, index, items.Count); PublishTransfer("Downloading", false); });
                    await client.DownloadFilesystemAsync(item.Path, item.IsDirectory, targets[i], key, progress, token, false);
                }
            }, "Selected items downloaded.");
        }
        private void BeginTransfer(string action, string name, string localPath = null)
        {
            transferActive = true; transferStatus.Begin(action, name); transferStatus.Visible = true; layout.RowStyles[6].Height = 76 * DeviceDpi / 96f;
            transferEvent = new FilesystemTransferEventArgs { Name = name, Direction = action == "Downloading" ? "Download" : action == "Copying" ? "Copy" : "Upload", Cancellation = operation, LocalPath = localPath };
            PublishTransfer(action, false);
        }
        private void PublishTransfer(string phase, bool finished)
        {
            if (transferEvent == null) return; transferEvent.Name = transferStatus.CurrentName; transferEvent.Status = phase; transferEvent.Finished = finished; transferEvent.Metrics = transferStatus.Metrics;
            if (TransferChanged != null) TransferChanged(this, transferEvent);
        }
        private IProgress<FilesystemCopyProgress> BatchProgress(CancellationTokenSource active)
        {
            return new Progress<FilesystemCopyProgress>(p =>
            {
                if (IsDisposed || !busy || !Object.ReferenceEquals(active, operation)) return;
                status.Text = p.Phase + " · " + p.Name + " · " + p.Completed + "/" + p.Total;
                if (p.Phase == "Complete") return;
                transferStatus.Report(p.Phase, p.Name, new TransferProgress { Done = p.Bytes, Total = p.BytesTotal }, p.Completed, p.Total); PublishTransfer(p.Phase, false);
            });
        }
        private IProgress<TransferProgress> Progress(string name) { CancellationTokenSource active = operation; return new Progress<TransferProgress>(p => { if (!IsDisposed && busy && Object.ReferenceEquals(active, operation)) { transferStatus.Report("Downloading", name, p); PublishTransfer("Downloading", false); status.Text = "Downloading · " + name + " · " + p.Percent + "%"; } }); }
        private async Task Import()
        {
            using (TextDialog dialog = new TextDialog("Import files", "PixelDrain file IDs, separated by commas", "", 8192)) if (dialog.ShowDialog(this) == DialogResult.OK)
                await Run(token => client.ImportFilesystemAsync(currentPath, dialog.Result.Split(new char[] { ',', ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).ToList(), key, token), "Files imported.", true);
        }
        private void PaintName(object sender, DataGridViewCellPaintingEventArgs e)
        {
            if (e.RowIndex < 0 || e.RowIndex >= nodes.Count || e.ColumnIndex != 0) return;
            e.Paint(e.ClipBounds, e.PaintParts & ~DataGridViewPaintParts.ContentForeground); float s = grid.DeviceDpi / 96f;
            Artwork.Icon(e.Graphics, nodes[e.RowIndex].IsDirectory ? "folder" : "document", new RectangleF(e.CellBounds.X + 13 * s, e.CellBounds.Y + (e.CellBounds.Height - 20 * s) / 2, 20 * s, 20 * s), Theme.Muted);
            if (nodes[e.RowIndex].IsShared) Artwork.Icon(e.Graphics, "share", new RectangleF(e.CellBounds.X + 28 * s, e.CellBounds.Y + (e.CellBounds.Height - 4 * s) / 2, 12 * s, 12 * s), Theme.IsDark ? Color.FromArgb(67, 210, 216) : Color.FromArgb(12, 132, 142));
            TextRenderer.DrawText(e.Graphics, nodes[e.RowIndex].Name, Font, new Rectangle(e.CellBounds.X + (int)(45 * s), e.CellBounds.Y, Math.Max(0, e.CellBounds.Width - (int)(50 * s)), e.CellBounds.Height), Theme.Ink, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding); e.Handled = true;
        }
    }
}
