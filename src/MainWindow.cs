using System;
using System.Collections.Generic;
using System.Drawing;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Pixelroom
{
    internal sealed class TransferJob
    {
        public string Name, Direction, Status, LocalPath;
        public long Size, Done;
        public int Percent;
        public bool Finished;
        public int Generation;
        public bool FileSystem;
        public TransferMetrics Metrics = new TransferMetrics();
        public Func<CancellationToken, IProgress<TransferProgress>, Task> Work;
        public CancellationTokenSource Cancel = new CancellationTokenSource();
    }

    public sealed class MainWindow : Form
    {
        private readonly PixelDrainClient client;
        private readonly MetadataStore store;
        private readonly PreferencesStore preferences;
        private readonly CredentialStore credentials;
        private readonly SemaphoreSlim slots = new SemaphoreSlim(2, 2);
        private CancellationTokenSource session = new CancellationTokenSource();
        private string apiKey, savedKey, scope = "demo", page = "all";
        private int generation;
        private bool demoMode, loading, changingNav, allowClose, deleting, rememberKey, restoringConnection;
        private List<CloudFile> files = new List<CloudFile>(), visible = new List<CloudFile>();
        private LocalData meta = new LocalData();
        private readonly List<TransferJob> jobs = new List<TransferJob>();
        private readonly HashSet<string> marked = new HashSet<string>(StringComparer.Ordinal);
        private readonly Dictionary<string, Button> nav = new Dictionary<string, Button>();
        private readonly Label title, status, collectionNote, selection, accountState;
        private readonly StatsStrip stats;
        private readonly Button connection, uploadButton, downloadButton, deleteButton, shareButton, cancelTransfer, clearTransfer;
        private readonly CueTextBox search;
        private readonly ComboBox type, sort;
        private readonly ListBox collections;
        private readonly DataGridView grid, transfers;
        private readonly Panel filePanel, emptyPanel, transferPanel;
        private readonly FlowLayoutPanel filters, fileActions;
        private readonly System.Windows.Forms.Timer searchDelay;
        private readonly System.Windows.Forms.Timer transferTicker;
        private readonly BrowseFilterBar browseFilters;
        private readonly TableLayoutPanel content;
        private readonly Panel center;
        private readonly TransferStatusPanel transferSummary;
        private readonly Button cancelAll, retryTransfer, showFolder;
        private FilesystemWindow filesystemView;
        private SettingsDialog settingsView;
        private StatisticsView statisticsView;
        private TransferJob filesystemJob;

        public MainWindow() : this(new PixelDrainClient(), Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Pixelroom")) { }
        internal MainWindow(PixelDrainClient client, string profileRoot)
        {
            this.client = client; store = new MetadataStore(Path.Combine(profileRoot, "metadata")); preferences = new PreferencesStore(profileRoot); credentials = new CredentialStore(profileRoot);
            Preferences settings = preferences.Load(); Theme.Set(settings.Theme); rememberKey = settings.RememberApiKey;
            Text = "Pixelroom — PixelDrain Explorer"; ClientSize = new Size(1190, 780); MinimumSize = new Size(980, 720); StartPosition = FormStartPosition.CenterScreen;
            Font = Theme.Font(9); ForeColor = Theme.Ink; BackColor = Theme.Background; AutoScaleMode = AutoScaleMode.Dpi; KeyPreview = true; AllowDrop = true;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
            TableLayoutPanel shell = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty, Padding = Padding.Empty };
            shell.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 225)); shell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); Controls.Add(shell);
            TableLayoutPanel sidebar = new HoneycombSidebar { Dock = DockStyle.Fill, BackColor = Theme.Surface, Tag = "surface", ColumnCount = 1, RowCount = 7, Padding = new Padding(18, 22, 18, 18), Margin = Padding.Empty };
            foreach (float h in new float[] { 60, 43, 272, 41 }) sidebar.RowStyles.Add(new RowStyle(SizeType.Absolute, h));
            sidebar.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); sidebar.RowStyles.Add(new RowStyle(SizeType.Absolute, 43)); sidebar.RowStyles.Add(new RowStyle(SizeType.Absolute, 53)); shell.Controls.Add(sidebar, 0, 0);
            BrandMark brand = new BrandMark { Dock = DockStyle.Fill, Margin = Padding.Empty }; sidebar.Controls.Add(brand, 0, 0);
            Label workspace = Theme.Label("Manage your PixelDrain files.", 8.5f, Theme.Muted); workspace.Dock = DockStyle.Fill; workspace.Padding = new Padding(7, 0, 0, 0); sidebar.Controls.Add(workspace, 0, 1);
            FlowLayoutPanel navPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = Padding.Empty };
            AddNav(navPanel, "all", "My files"); AddNav(navPanel, "filesystem", "File System"); AddNav(navPanel, "recent", "Recent"); AddNav(navPanel, "favorites", "Favorites"); AddNav(navPanel, "transfers", "Transfers"); AddNav(navPanel, "statistics", "Statistics"); sidebar.Controls.Add(navPanel, 0, 2);
            Panel collectionHeading = new Panel { Dock = DockStyle.Fill };
            Label localLabel = Theme.Label("COLLECTIONS", 8, Theme.Muted, FontStyle.Bold); localLabel.SetBounds(7, 13, 140, 20); collectionHeading.Controls.Add(localLabel);
            Button add = Theme.Button(""); ((ThemedButton)add).IconName = "plus"; add.AccessibleName = "New collection"; add.SetBounds(151, 3, 32, 29); add.FlatAppearance.BorderSize = 0; add.Click += delegate { NewCollection(null); }; collectionHeading.Controls.Add(add); sidebar.Controls.Add(collectionHeading, 0, 3);
            collections = new PatternListBox { Dock = DockStyle.Fill, BorderStyle = BorderStyle.None, Font = Theme.Font(10), IntegralHeight = false, BackColor = Theme.Surface, ForeColor = Theme.Muted, ItemHeight = 35, DrawMode = DrawMode.OwnerDrawFixed, HorizontalScrollbar = true };
            collections.DrawItem += DrawCollection; collections.SelectedIndexChanged += delegate { if (!changingNav && collections.SelectedItem is Collection) Navigate("collection:" + ((Collection)collections.SelectedItem).Id); };
            ContextMenuStrip collectionMenu = new ContextMenuStrip(); collectionMenu.Items.Add("Rename collection", null, delegate { NewCollection(CurrentCollection()); }); collectionMenu.Items.Add("Delete collection", null, delegate { RemoveCollection(); }); collections.ContextMenuStrip = collectionMenu; sidebar.Controls.Add(collections, 0, 4);
            Button newCollection = Theme.Button("New collection"); newCollection.Dock = DockStyle.Fill; newCollection.Margin = new Padding(0, 0, 0, 5); newCollection.Click += delegate { NewCollection(null); }; sidebar.Controls.Add(newCollection, 0, 5);
            connection = Theme.Button("Settings"); connection.Dock = DockStyle.Bottom; connection.Height = 40; connection.Margin = new Padding(0, 10, 0, 0); connection.Click += delegate { Connection(); }; sidebar.Controls.Add(connection, 0, 6);

            content = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 8, Padding = new Padding(28, 0, 28, 13), Margin = Padding.Empty };
            content.RowStyles.Add(new RowStyle(SizeType.Absolute, 62)); content.RowStyles.Add(new RowStyle(SizeType.Absolute, 89)); content.RowStyles.Add(new RowStyle(SizeType.Absolute, 108)); content.RowStyles.Add(new RowStyle(SizeType.Absolute, 101)); content.RowStyles.Add(new RowStyle(SizeType.Absolute, 50)); content.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); content.RowStyles.Add(new RowStyle(SizeType.Absolute, 0)); content.RowStyles.Add(new RowStyle(SizeType.Absolute, 45)); shell.Controls.Add(content, 1, 0);
            Panel header = new Panel { Dock = DockStyle.Fill };
            Label breadcrumb = Theme.Label("WORKSPACE  /  PIXELDRAIN", 8, Theme.Muted); breadcrumb.SetBounds(0, 24, 400, 22); header.Controls.Add(breadcrumb);
            accountState = new AccountBadge { Text = "Not connected", Font = Theme.Font(8), Width = 139, Height = 29 }; header.Resize += delegate { accountState.Location = new Point(header.ClientSize.Width - accountState.Width, 17); }; header.Controls.Add(accountState); content.Controls.Add(header, 0, 0);
            Panel heading = new BackdropPanel { Dock = DockStyle.Fill };
            title = Theme.Label("My files", 29, Theme.Ink, FontStyle.Bold); title.SetBounds(0, 5, 500, 52); heading.Controls.Add(title);

            heading.Resize += delegate { title.Width = Math.Max(180, heading.ClientSize.Width - 20); }; content.Controls.Add(heading, 0, 1);
            stats = new StatsStrip { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 17) }; content.Controls.Add(stats, 0, 2);
            filters = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, FlowDirection = FlowDirection.LeftToRight, Margin = Padding.Empty, Padding = new Padding(0, 7, 0, 0) };
            FieldFrame searchFrame = new FieldFrame { IconName = "search", Width = 260, Height = 34, Margin = new Padding(0, 0, 10, 0) };
            search = new CueTextBox { Cue = "Search files…", Font = Theme.Font(9.5f), BorderStyle = BorderStyle.None, AccessibleName = "Search files" }; search.SetBounds(38, 7, 208, 22); search.Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right; search.Enter += delegate { searchFrame.Invalidate(); }; search.Leave += delegate { searchFrame.Invalidate(); }; searchFrame.Controls.Add(search); filters.Controls.Add(searchFrame);
            type = new FilterCombo { DropDownStyle = ComboBoxStyle.DropDownList, Width = 122, Margin = new Padding(0, 0, 10, 0), AccessibleName = "File type" }; type.Items.AddRange(new object[] { "All types", "Image", "Video", "Document", "Audio", "Other" }); type.SelectedIndex = 0; filters.Controls.Add(type);
            sort = new FilterCombo { DropDownStyle = ComboBoxStyle.DropDownList, Width = 155, Margin = new Padding(0, 0, 10, 0), AccessibleName = "Sort files" }; sort.Items.AddRange(new object[] { "Newest first", "Oldest first", "Name A → Z", "Name Z → A", "Largest first", "Smallest first" }); sort.SelectedIndex = 0; filters.Controls.Add(sort);
            Button refresh = Theme.Button("Refresh"); refresh.Width = 105; refresh.Height = 34; refresh.Margin = Padding.Empty; refresh.Click += async delegate { await RefreshAccount(); }; filters.Controls.Add(refresh);
            Panel filterArea = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty }; filters.Dock = DockStyle.Top; filters.Height = 53; filterArea.Controls.Add(filters);
            browseFilters = new BrowseFilterBar(false) { Dock = DockStyle.Bottom, Height = 46 }; browseFilters.Changed += delegate { RenderFiles(); }; browseFilters.ResetRequested += delegate { search.Clear(); type.SelectedIndex = 0; sort.SelectedIndex = 0; RenderFiles(); }; filterArea.Controls.Add(browseFilters); content.Controls.Add(filterArea, 0, 3);
            filters.Layout += delegate { float scale = filters.DeviceDpi / 96f; int width = Math.Max((int)(185 * scale), filters.ClientSize.Width - type.Width - sort.Width - refresh.Width - (int)(30 * scale)); if (searchFrame.Width != width) searchFrame.Width = width; };
            searchDelay = new System.Windows.Forms.Timer { Interval = 180 }; searchDelay.Tick += delegate { searchDelay.Stop(); RenderFiles(); }; search.TextChanged += delegate { searchDelay.Stop(); searchDelay.Start(); }; type.SelectedIndexChanged += delegate { RenderFiles(); }; sort.SelectedIndexChanged += delegate { RenderFiles(); };
            fileActions = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = Padding.Empty };
            uploadButton = Theme.Button("Upload files", true); uploadButton.Text = "Upload ▾"; uploadButton.Width = 129; uploadButton.Margin = new Padding(0, 0, 8, 0);
            ContextMenuStrip uploadMenu = new ContextMenuStrip(); uploadMenu.Items.Add("Upload files…", null, delegate { ChooseUpload(); }); uploadMenu.Items.Add("Upload folders to File System…", null, delegate { ChooseFolderUpload(); });
            uploadButton.Click += delegate { uploadMenu.Show(uploadButton, new Point(0, uploadButton.Height)); }; fileActions.Controls.Add(uploadButton);
            downloadButton = Theme.Button("Download"); downloadButton.Width = 116; downloadButton.Margin = new Padding(0, 0, 8, 0); downloadButton.Click += delegate { ChooseDownload(); }; fileActions.Controls.Add(downloadButton);
            Button organize = Theme.Button("Add to collection"); organize.Width = 153; organize.Margin = new Padding(0, 0, 8, 0); organize.Click += delegate { AddToCollection(); }; fileActions.Controls.Add(organize);
            deleteButton = Theme.Button("Delete"); deleteButton.Width = 87; deleteButton.ForeColor = Theme.Ink; deleteButton.Margin = new Padding(0, 0, 8, 0); deleteButton.Click += async delegate { await DeleteSelected(); }; fileActions.Controls.Add(deleteButton);
            shareButton = Theme.Button("Share…"); shareButton.Width = 84; shareButton.Margin = new Padding(0, 0, 8, 0); shareButton.Click += delegate { ShareSelected(); }; fileActions.Controls.Add(shareButton);
            selection = new LinkLabel { Font = Theme.Font(8), LinkColor = Theme.Ink, ActiveLinkColor = Atmosphere.Accent, VisitedLinkColor = Theme.Ink, LinkBehavior = LinkBehavior.HoverUnderline, AutoEllipsis = true, TabStop = true, BackColor = Color.Transparent }; ((LinkLabel)selection).LinkClicked += delegate { ClearMarks(); }; selection.Width = 115; selection.Height = 38; selection.TextAlign = ContentAlignment.MiddleLeft; fileActions.Controls.Add(selection); content.Controls.Add(fileActions, 0, 4);
            fileActions.Layout += delegate { int width = Math.Max(30, fileActions.ClientSize.Width - shareButton.Right - shareButton.Margin.Right - selection.Margin.Horizontal); if (selection.Width != width) selection.Width = width; };

            center = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty }; content.Controls.Add(center, 0, 5);
            filePanel = new SurfaceCard { Dock = DockStyle.Fill }; center.Controls.Add(filePanel);
            grid = CreateGrid(); grid.VirtualMode = true; grid.MultiSelect = true; grid.AllowUserToResizeColumns = true;
            AddColumn(grid, "favorite", "", 39, false); AddColumn(grid, "name", "Filename", 230, true); AddColumn(grid, "size", "Size", 85, false); AddColumn(grid, "kind", "Type", 83, false); AddColumn(grid, "date", "Uploaded", 110, false); AddColumn(grid, "sharing", "Sharing", 128, false);
            grid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "mark", HeaderText = "Select all visible", Width = 38, DisplayIndex = 0, SortMode = DataGridViewColumnSortMode.NotSortable });
            grid.CellFormatting += delegate(object sender, DataGridViewCellFormattingEventArgs e) { bool check = e.RowIndex >= 0 && e.RowIndex < visible.Count && marked.Contains(visible[e.RowIndex].Id); e.CellStyle.BackColor = check ? Theme.Pale : Theme.Surface; e.CellStyle.SelectionBackColor = marked.Count > 0 && !check ? Theme.Surface : Theme.Pale; };
            grid.CellPainting += delegate(object sender, DataGridViewCellPaintingEventArgs e) { if (e.ColumnIndex == 6) { bool all = e.RowIndex < 0 ? visible.Count > 0 && visible.All(f => marked.Contains(f.Id)) : e.RowIndex < visible.Count && marked.Contains(visible[e.RowIndex].Id); SelectionPaint.Header(grid, e, all, e.RowIndex < 0 && visible.Any(f => marked.Contains(f.Id))); } };
            grid.CellToolTipTextNeeded += delegate(object sender, DataGridViewCellToolTipTextNeededEventArgs e) { if (e.RowIndex >= 0 && e.RowIndex < visible.Count) e.ToolTipText = visible[e.RowIndex].Name + "\nShared by link. My files entries are accessible to anyone with their links."; };
            grid.CellPainting += PaintFileCell;
            grid.CellValueNeeded += FileValue; grid.CellDoubleClick += delegate(object sender, DataGridViewCellEventArgs e) { if (e.RowIndex >= 0 && e.ColumnIndex > 0 && e.ColumnIndex != 6) ShowDetails(); };
            grid.CellClick += delegate(object sender, DataGridViewCellEventArgs e) { if (e.RowIndex >= 0 && e.ColumnIndex == 0) ToggleFavorite(visible[e.RowIndex]); };
            grid.CellMouseClick += delegate(object sender, DataGridViewCellMouseEventArgs e) { if (e.Button == MouseButtons.Left && e.RowIndex >= 0 && e.RowIndex < visible.Count && e.ColumnIndex == 6) ToggleMarked(visible[e.RowIndex].Id); };
            grid.ColumnHeaderMouseClick += delegate(object sender, DataGridViewCellMouseEventArgs e) { if (e.ColumnIndex == 1) sort.SelectedIndex = sort.SelectedIndex == 2 ? 3 : 2; };
            grid.ColumnHeaderMouseClick += delegate(object sender, DataGridViewCellMouseEventArgs e) { if (e.Button == MouseButtons.Left && e.ColumnIndex == 6) MarkVisible(); };
            grid.SelectionChanged += delegate { UpdateSelection(); };
            grid.CellMouseDown += delegate(object sender, DataGridViewCellMouseEventArgs e) { if (e.Button == MouseButtons.Right && e.RowIndex >= 0 && e.RowIndex < visible.Count && !marked.Contains(visible[e.RowIndex].Id) && !grid.Rows[e.RowIndex].Selected) { marked.Clear(); grid.ClearSelection(); grid.Rows[e.RowIndex].Selected = true; grid.Invalidate(); } };
            grid.ContextMenuStrip = BuildFileMenu(); filePanel.Controls.Add(grid);
            collectionNote = Theme.Label("", 8, Theme.Muted); collectionNote.Height = 30; collectionNote.Dock = DockStyle.Top; filePanel.Controls.Add(collectionNote);
            emptyPanel = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Surface, Tag = "surface" }; emptyPanel.Paint += PaintEmpty; filePanel.Controls.Add(emptyPanel);
            transferPanel = new SurfaceCard { Dock = DockStyle.Fill, Visible = false }; center.Controls.Add(transferPanel);
            transfers = CreateGrid(); transfers.VirtualMode = true; transfers.MultiSelect = false;
            AddColumn(transfers, "name", "Filename", 220, true); AddColumn(transfers, "direction", "Direction", 79, false); AddColumn(transfers, "progress", "Progress", 122, false); AddColumn(transfers, "size", "Transferred", 114, false); AddColumn(transfers, "status", "Status", 180, true); AddColumn(transfers, "speed", "Speed", 98, false); AddColumn(transfers, "eta", "ETA", 83, false);
            transfers.CellPainting += PaintTransferCell; transfers.SelectionChanged += delegate { UpdateTransferButtons(); };
            transfers.CellValueNeeded += TransferValue; transferPanel.Controls.Add(transfers);
            FlowLayoutPanel transferActions = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 48, Padding = new Padding(0, 10, 0, 0) };
            cancelTransfer = Theme.Button("Cancel selected"); cancelTransfer.Width = 135; cancelTransfer.Click += delegate { TransferJob selected = SelectedJob(); if (selected != null && !selected.Finished) selected.Cancel.Cancel(); }; transferActions.Controls.Add(cancelTransfer);
            cancelAll = Theme.Button("Cancel all"); cancelAll.Width = 96; cancelAll.Click += delegate { foreach (TransferJob job in jobs.Where(j => !j.Finished).ToArray()) job.Cancel.Cancel(); }; transferActions.Controls.Add(cancelAll);
            retryTransfer = Theme.Button("Retry"); retryTransfer.Width = 76; retryTransfer.Click += delegate { RetrySelectedTransfer(); }; transferActions.Controls.Add(retryTransfer);
            showFolder = Theme.Button("Show in folder"); showFolder.Width = 127; showFolder.Click += delegate { ShowDownloadFolder(); }; transferActions.Controls.Add(showFolder);
            clearTransfer = Theme.Button("Clear completed"); clearTransfer.Width = 140; clearTransfer.Click += delegate { jobs.RemoveAll(j => j.Finished); RenderTransfers(); }; transferActions.Controls.Add(clearTransfer); transferPanel.Controls.Add(transferActions);
            transferSummary = new TransferStatusPanel { Visible = false }; content.Controls.Add(transferSummary, 0, 6);
            Panel bottom = new FooterPanel { Dock = DockStyle.Fill }; status = Theme.Label("Ready", 8, Theme.Muted); status.SetBounds(0, 15, 600, 22); status.Anchor = AnchorStyles.Left | AnchorStyles.Top; bottom.Controls.Add(status);
            Label signature = Theme.Label("Disign By Gryve", 8, Theme.Muted); signature.Width = 155; signature.Height = 22; signature.TextAlign = ContentAlignment.MiddleRight; bottom.Controls.Add(signature);
            bottom.Resize += delegate { float scale = bottom.DeviceDpi / 96f; status.Width = Math.Max(100, bottom.ClientSize.Width - signature.Width - (int)(16 * scale)); signature.Location = new Point(bottom.ClientSize.Width - signature.Width, Math.Max(0, bottom.ClientSize.Height - signature.Height - (int)(3 * scale))); }; content.Controls.Add(bottom, 0, 7);
            transferTicker = new System.Windows.Forms.Timer { Interval = 500 }; transferTicker.Tick += delegate { RenderTransfers(); };
            foreach (Control dropTarget in new Control[] { this, grid, emptyPanel, filePanel, center, transferPanel, transfers }) EnableFileDrop(dropTarget);
            FormClosing += ClosingWindow; FormClosed += delegate { savedKey = null; ResetStatisticsView(); uploadMenu.Dispose(); searchDelay.Dispose(); transferTicker.Dispose(); session.Cancel(); client.Dispose(); session.Dispose(); };
            KeyDown += HandleKeys; Shown += async delegate { ApplyCurrentTheme(); RenderAll(); BeginInvoke(new Action(delegate { if (!IsDisposed) Refresh(); })); await RestoreSavedConnection(); }; RenderAll();
        }

        private void AddNav(FlowLayoutPanel panel, string key, string text)
        {
            ThemedButton button = (ThemedButton)Theme.Button(text); button.IsNavigation = true; button.IconName = key == "all" ? "files" : key == "filesystem" ? "folder" : key == "recent" ? "clock" : key == "favorites" ? "star" : key == "statistics" ? "chart" : "transfer"; button.Width = 187; button.Height = 40; button.Margin = new Padding(0, 0, 0, 4); button.TextAlign = ContentAlignment.MiddleLeft; button.Padding = new Padding(8, 0, 0, 0); button.FlatAppearance.BorderSize = 0;
            button.Click += delegate { Navigate(key); }; panel.Controls.Add(button); nav[key] = button;
        }
        private static DataGridView CreateGrid()
        {
            DataGridView view = new FileGrid { Dock = DockStyle.Fill, BorderStyle = BorderStyle.None, BackgroundColor = Theme.Surface, GridColor = Theme.Line, RowHeadersVisible = false, ReadOnly = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false, AllowUserToOrderColumns = false, AllowUserToResizeRows = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect, AutoGenerateColumns = false, EnableHeadersVisualStyles = false, CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal, ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None, ColumnHeadersHeight = 42, ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing, Font = Theme.Font(9), ShowCellToolTips = true };
            view.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle { BackColor = Theme.Inset, ForeColor = Theme.Muted, Font = Theme.Font(8), Padding = new Padding(8, 0, 0, 0), SelectionBackColor = Theme.Inset, SelectionForeColor = Theme.Muted };
            view.DefaultCellStyle = new DataGridViewCellStyle { BackColor = Theme.Surface, ForeColor = Theme.Ink, SelectionBackColor = Theme.Pale, SelectionForeColor = Theme.Ink, Padding = new Padding(8, 0, 0, 0) }; view.RowTemplate.Height = 53;
            view.CellFormatting += delegate(object sender, DataGridViewCellFormattingEventArgs e) { e.CellStyle.BackColor = Theme.Surface; e.CellStyle.ForeColor = Theme.Ink; e.CellStyle.SelectionBackColor = Theme.Pale; e.CellStyle.SelectionForeColor = Theme.Ink; };
            return view;
        }
        private static void AddColumn(DataGridView view, string name, string label, int width, bool fill)
        { view.Columns.Add(new DataGridViewTextBoxColumn { Name = name, HeaderText = label, Width = width, MinimumWidth = Math.Min(width, 70), AutoSizeMode = fill ? DataGridViewAutoSizeColumnMode.Fill : DataGridViewAutoSizeColumnMode.None, FillWeight = width, SortMode = DataGridViewColumnSortMode.Programmatic }); }
        private void DrawCollection(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0) return; Collection c = collections.Items[e.Index] as Collection; bool selected = (e.State & DrawItemState.Selected) != 0;
            using (SolidBrush background = new SolidBrush(selected ? Theme.Pale : Theme.Surface)) e.Graphics.FillRectangle(background, e.Bounds);
            Artwork.Icon(e.Graphics, "folder", new RectangleF(e.Bounds.X + 12, e.Bounds.Y + (e.Bounds.Height - 16) / 2, 16, 16), selected ? Theme.Ink : Theme.Muted);
            TextRenderer.DrawText(e.Graphics, c.Name, collections.Font, new Rectangle(e.Bounds.X + 38, e.Bounds.Y, e.Bounds.Width - 42, e.Bounds.Height), selected ? Theme.Ink : Theme.Muted, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }
        private Collection CurrentCollection() { return page.StartsWith("collection:") ? meta.Collections.FirstOrDefault(c => c.Id == page.Substring(11)) : collections.SelectedItem as Collection; }
        private void Navigate(string target)
        {
            if (target != page && marked.Count > 0) ClearMarks();
            page = target; search.Text = ""; type.SelectedIndex = 0; RenderAll();
        }
        private void RenderAll()
        {
            Collection c = page.StartsWith("collection:") ? meta.Collections.FirstOrDefault(x => x.Id == page.Substring(11)) : null;
            string name = c != null ? c.Name : page == "favorites" ? "Favorites" : page == "recent" ? "Recent" : page == "transfers" ? "Transfers" : page == "filesystem" ? "File System" : page == "statistics" ? "Statistics" : page == "settings" ? "Settings" : "My files";
            title.Text = name;
            accountState.Text = restoringConnection ? "Connecting…" : apiKey == null ? "Not connected" : "Connected";
            nav["filesystem"].Enabled = nav["statistics"].Enabled = !restoringConnection;
            stats.UpdateValues(files.Count.ToString("N0"), FileRules.SizeText(files.Sum(f => f.Size)), files.Count(f => meta.Favorites.Contains(f.Id)).ToString("N0"));
            foreach (KeyValuePair<string, Button> item in nav) { ((ThemedButton)item.Value).IsSelected = item.Key == page; item.Value.Invalidate(); }
            changingNav = true; collections.BeginUpdate(); collections.Items.Clear(); foreach (Collection item in meta.Collections) collections.Items.Add(item); if (c != null) collections.SelectedItem = c; collections.EndUpdate(); changingNav = false;
            bool transferPage = page == "transfers", filesystemPage = page == "filesystem", settingsPage = page == "settings", statisticsPage = page == "statistics", browserPage = !transferPage && !filesystemPage && !settingsPage && !statisticsPage;
            content.SuspendLayout(); float scale = DeviceDpi / 96f;
            stats.Visible = !filesystemPage && !settingsPage && !statisticsPage; content.RowStyles[2].Height = stats.Visible ? 108 * scale : 0;
            filters.Parent.Visible = browserPage; content.RowStyles[3].Height = browserPage ? 101 * scale : 0;
            fileActions.Visible = browserPage; content.RowStyles[4].Height = browserPage ? 50 * scale : 0;
            filePanel.Visible = browserPage; transferPanel.Visible = transferPage;
            if (filesystemView != null) filesystemView.Visible = filesystemPage;
            if (settingsView != null) settingsView.Visible = settingsPage;
            if (statisticsView != null) statisticsView.Visible = statisticsPage;
            if (filesystemPage) { EnsureFilesystemView(); filesystemView.Show(); filesystemView.BringToFront(); }
            else if (settingsPage) { EnsureSettingsView(); settingsView.Show(); settingsView.BringToFront(); }
            else if (statisticsPage) { EnsureStatisticsView(); statisticsView.Show(); statisticsView.BringToFront(); statisticsView.Initialize(); }
            else if (transferPage) transferPanel.BringToFront(); else { filePanel.BringToFront(); RenderFiles(); }
            content.ResumeLayout(); RenderTransfers();
        }
        private void RenderFiles()
        {
            if (grid == null) return; IEnumerable<CloudFile> result = files;
            if (page == "favorites") result = result.Where(f => meta.Favorites.Contains(f.Id));
            else if (page == "recent") result = result.Where(f => meta.Recent.ContainsKey(f.Id));
            else if (page.StartsWith("collection:")) { Collection c = meta.Collections.FirstOrDefault(x => x.Id == page.Substring(11)); result = result.Where(f => c != null && c.Files.Contains(f.Id)); }
            string query = search.Text.Trim(); if (query.Length > 0) result = result.Where(f => (meta.DisplayName(f) + " " + f.Name + " " + f.Id).IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0);
            if (type.SelectedIndex > 0) result = result.Where(f => f.Kind == (string)type.SelectedItem);
            BrowseQuery viewQuery = browseFilters.Query; result = result.Where(f => viewQuery.MatchesSharing(true, true) && viewQuery.MatchesDate(f.Uploaded));
            switch (sort.SelectedIndex)
            {
                case 1: result = result.OrderBy(f => f.Uploaded); break;
                case 2: result = result.OrderBy(f => meta.DisplayName(f), StringComparer.CurrentCultureIgnoreCase); break;
                case 3: result = result.OrderByDescending(f => meta.DisplayName(f), StringComparer.CurrentCultureIgnoreCase); break;
                case 4: result = result.OrderByDescending(f => f.Size); break;
                case 5: result = result.OrderBy(f => f.Size); break;
                default: result = page == "recent" ? result.OrderByDescending(f => meta.Recent[f.Id]) : result.OrderByDescending(f => f.Uploaded); break;
            }
            marked.IntersectWith(files.Select(f => f.Id)); visible = result.ToList(); grid.RowCount = visible.Count; grid.ClearSelection(); grid.Invalidate();
            collectionNote.Visible = false; collectionNote.Text = "";
            bool empty = visible.Count == 0; emptyPanel.Visible = empty; if (empty) { emptyPanel.BringToFront(); emptyPanel.Invalidate(); } else { grid.BringToFront();  }
            UpdateSelection();
        }
        private void FileValue(object sender, DataGridViewCellValueEventArgs e)
        {
            if (e.RowIndex < 0 || e.RowIndex >= visible.Count) return; CloudFile f = visible[e.RowIndex];
            if (e.ColumnIndex == 6) { e.Value = marked.Contains(f.Id); return; }
            switch (e.ColumnIndex) { case 0: e.Value = meta.Favorites.Contains(f.Id) ? "★" : "☆"; break; case 1: e.Value = meta.DisplayName(f); break; case 2: e.Value = FileRules.SizeText(f.Size); break; case 3: e.Value = f.Kind; break; case 4: e.Value = f.Uploaded == DateTime.MinValue ? "—" : f.Uploaded.ToLocalTime().ToString("dd MMM yyyy"); break; case 5: e.Value = "Shared by link"; break; }
        }
        private void PaintFileCell(object sender, DataGridViewCellPaintingEventArgs e)
        {
            if (e.RowIndex < 0 || e.RowIndex >= visible.Count || (e.ColumnIndex != 0 && e.ColumnIndex != 1)) return;
            e.Paint(e.ClipBounds, e.PaintParts & ~DataGridViewPaintParts.ContentForeground); CloudFile file = visible[e.RowIndex]; float s = grid.DeviceDpi / 96f;
            if (e.ColumnIndex == 0)
                Artwork.Icon(e.Graphics, "star", new RectangleF(e.CellBounds.X + (e.CellBounds.Width - 16 * s) / 2, e.CellBounds.Y + (e.CellBounds.Height - 16 * s) / 2, 16 * s, 16 * s), meta.Favorites.Contains(file.Id) ? Theme.Ink : Theme.Muted, meta.Favorites.Contains(file.Id));
            else
            {
                string icon = file.Kind == "Image" ? "image" : file.Kind == "Video" ? "video" : file.Kind == "Audio" ? "audio" : "document";
                PaintFileName(e, file, icon, s);
            }
            e.Handled = true;
        }
        private void PaintFileName(DataGridViewCellPaintingEventArgs e, CloudFile file, string icon, float s)
        {
            System.Drawing.Drawing2D.GraphicsState state = e.Graphics.Save(); e.Graphics.SetClip(e.CellBounds); e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            RectangleF tile = new RectangleF(e.CellBounds.X + 5 * s, e.CellBounds.Y + (e.CellBounds.Height - 32 * s) / 2, 30 * s, 32 * s);
            Artwork.Card(e.Graphics, tile, Theme.Inset, Theme.Inset, 6 * s); Artwork.Icon(e.Graphics, icon, new RectangleF(tile.X + 5 * s, tile.Y + 6 * s, 20 * s, 20 * s), Theme.Muted);
            using (Font font = Theme.Font(9, FontStyle.Bold)) TextRenderer.DrawText(e.Graphics, meta.DisplayName(file), font, new Rectangle(e.CellBounds.X + (int)(46 * s), e.CellBounds.Y, Math.Max(0, e.CellBounds.Width - (int)(52 * s)), e.CellBounds.Height), Theme.Ink, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
            e.Graphics.Restore(state);
        }
        private List<CloudFile> SelectedFiles()
        { return marked.Count > 0 ? files.Where(f => marked.Contains(f.Id)).ToList() : grid.SelectedRows.Cast<DataGridViewRow>().Where(r => r.Index >= 0 && r.Index < visible.Count).OrderBy(r => r.Index).Select(r => visible[r.Index]).ToList(); }
        private void ToggleMarked(string id) { if (!marked.Add(id)) marked.Remove(id); grid.ClearSelection(); grid.Invalidate(); UpdateSelection(); }
        private void ClearMarks() { marked.Clear(); grid.ClearSelection(); grid.Invalidate(); UpdateSelection(); SetStatus("Selection cleared."); }
        private void SelectVisible() { foreach (CloudFile file in visible) marked.Add(file.Id); grid.ClearSelection(); grid.Invalidate(); UpdateSelection(); }
        private void MarkVisible() { if (marked.Count > 0 && (visible.Count == 0 || visible.All(f => marked.Contains(f.Id)))) ClearMarks(); else SelectVisible(); }
        private void UpdateSelection()
        {
            int count = grid == null ? 0 : SelectedFiles().Count; selection.Text = count > 0 ? count + (marked.Count > 0 ? " marked" : " selected") : visible.Count + (visible.Count == 1 ? " file" : " files");
            if (count > 0) selection.Text += " ×"; ((LinkLabel)selection).LinkArea = count > 0 ? new LinkArea(0, selection.Text.Length) : new LinkArea(0, 0); selection.AccessibleName = count > 0 ? "Clear selection" : "File count";
            if (marked.Count > 0) { HashSet<string> shown = new HashSet<string>(visible.Select(f => f.Id), StringComparer.Ordinal); int hidden = marked.Count(id => !shown.Contains(id)); SetStatus(marked.Count + " marked" + (hidden > 0 ? " · " + hidden + " hidden by filters" : "") + ". Right-click for Clear selection."); }
            downloadButton.Enabled = count > 0 && apiKey != null; deleteButton.Enabled = count > 0 && !deleting && (apiKey != null || demoMode); uploadButton.Enabled = !deleting;
            shareButton.Enabled = count > 0 && apiKey != null;
        }
        private void PaintEmpty(object sender, PaintEventArgs e)
        {
            float scale = Math.Min(emptyPanel.DeviceDpi / 96f, Math.Max(.6f, emptyPanel.ClientSize.Height / 255f)); int mid = emptyPanel.ClientSize.Width / 2, y = Math.Max(7, (emptyPanel.ClientSize.Height - (int)(240 * scale)) / 2); Graphics g = e.Graphics; g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using (Pen line = new Pen(Color.FromArgb(Theme.IsDark ? 22 : 30, Theme.Muted)))
            {
                g.DrawEllipse(line, mid - 79 * scale, y - 12 * scale, 158 * scale, 158 * scale);
                g.DrawEllipse(line, mid - 97 * scale, y - 30 * scale, 194 * scale, 194 * scale);
                g.DrawLine(line, mid - 130 * scale, y + 67 * scale, mid - 111 * scale, y + 67 * scale); g.DrawLine(line, mid + 111 * scale, y + 67 * scale, mid + 130 * scale, y + 67 * scale);
            }
            Artwork.Card(g, new RectangleF(mid - 50 * scale, y + 18 * scale, 100 * scale, 100 * scale), Theme.Inset, Theme.Line, 24 * scale);
            Artwork.Icon(g, "folder", new RectangleF(mid - 26 * scale, y + 42 * scale, 52 * scale, 52 * scale), Theme.Ink);
            Artwork.Card(g, new RectangleF(mid + 30 * scale, y + 2 * scale, 31 * scale, 35 * scale), Theme.Surface, Theme.Line, 7 * scale);
            Artwork.Icon(g, "document", new RectangleF(mid + 36 * scale, y + 9 * scale, 19 * scale, 21 * scale), Theme.Muted);
            string heading = search.Text.Length > 0 || type.SelectedIndex > 0 || browseFilters.Query.Active ? "No matching files" : page == "favorites" ? "No favorites yet" : "No files yet";
            string body = apiKey == null && !demoMode ? "Add your API key in Settings to get started." : page == "favorites" ? "Star a file to keep it here." : page.StartsWith("collection:") ? "Add files to this collection." : "Upload files or change your filters.";
            if (apiKey != null && browseFilters.Query.Sharing == SharingFilter.NotShared) body = "My files are shared by link. Use File System to manage private items.";
            using (Font big = Theme.Font(18, FontStyle.Bold)) TextRenderer.DrawText(g, heading, big, new Rectangle(15, y + (int)(151 * scale), emptyPanel.Width - 30, (int)(36 * scale)), Theme.Ink, TextFormatFlags.HorizontalCenter | TextFormatFlags.EndEllipsis);
            using (Font small = Theme.Font(9)) TextRenderer.DrawText(g, body, small, new Rectangle(20, y + (int)(193 * scale), emptyPanel.Width - 40, (int)(38 * scale)), Theme.Muted, TextFormatFlags.HorizontalCenter | TextFormatFlags.WordBreak);
        }
        private void SetStatus(string text) { if (!IsDisposed) status.Text = text; }
        private void SaveMeta()
        {
            try { store.Save(scope, meta); } catch (Exception) { SetStatus("Your changes could not be saved."); }
        }
        private void ApplyCurrentTheme()
        {
            Theme.Apply(this);
            ((LinkLabel)selection).LinkColor = Theme.Ink; ((LinkLabel)selection).ActiveLinkColor = Atmosphere.Accent; ((LinkLabel)selection).VisitedLinkColor = Theme.Ink;
            foreach (KeyValuePair<string, Button> item in nav) { ((ThemedButton)item.Value).IsSelected = item.Key == page; item.Value.Invalidate(); }
            collections.Invalidate(); emptyPanel.Invalidate(); if (IsHandleCreated) Refresh();
        }
        private void ChangeTheme(string name)
        {
            Theme.Set(name); ApplyCurrentTheme(); preferences.Save(Theme.Name);
        }
        private bool CanChangeAccount()
        {
            if (filesystemView != null && filesystemView.IsBusy) { Notice.Show(this, "Finish or cancel the File System operation before changing accounts.", "Pixelroom", MessageBoxButtons.OK); return false; }
            if (deleting) { Notice.Show(this, "Please wait for file deletion to finish.", "Pixelroom", MessageBoxButtons.OK); return false; }
            return ActiveJobs() == 0 || Notice.Show(this, "Changing your connection will cancel active transfers. Continue?", "Change connection", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) == DialogResult.Yes;
        }
        private void ConnectAccount(string key, List<CloudFile> result)
        {
            if (rememberKey) credentials.Save(key); else credentials.Delete();
            savedKey = rememberKey ? key : null; ActivateAccount(key, result);
        }
        private void ActivateAccount(string key, List<CloudFile> result)
        {
            marked.Clear();
            ResetFilesystemView();
            ResetStatisticsView();
            restoringConnection = false;
            generation++; session.Cancel(); session.Dispose(); session = new CancellationTokenSource();
            apiKey = key; scope = FileRules.AccountScope(key); meta = store.Load(scope); files = result; demoMode = false; if (page != "settings") page = "all";
            search.Text = ""; type.SelectedIndex = 0; jobs.RemoveAll(j => j.Finished); RenderAll(); SetStatus("Connected.");
        }
        private void Connection()
        {
            if (restoringConnection)
            { generation++; session.Cancel(); session.Dispose(); session = new CancellationTokenSource(); restoringConnection = false; RenderAll(); SetStatus("Open Settings to connect your account."); }
            Navigate("settings");
        }
        private void EnsureFilesystemView()
        {
            if (filesystemView != null && !filesystemView.IsDisposed) return;
            filesystemView = new FilesystemWindow(client, apiKey, null, true);
            filesystemView.TransferChanged += FilesystemTransferChanged;
            center.Controls.Add(filesystemView); Theme.Apply(filesystemView);
        }
        private void ResetFilesystemView()
        {
            if (filesystemView != null) { filesystemView.CancelOperation(); center.Controls.Remove(filesystemView); filesystemView.Dispose(); filesystemView = null; }
            filesystemJob = null;
        }
        private void EnsureSettingsView()
        {
            if (settingsView != null && !settingsView.IsDisposed) return;
            settingsView = new SettingsDialog(client, apiKey ?? savedKey, apiKey != null, rememberKey, ChangeTheme, ChangeRememberKey, ConnectAccount, Disconnect, CanChangeAccount, true);
            settingsView.FormClosed += delegate { settingsView = null; if (!IsDisposed && !Disposing && !allowClose && page == "settings") Navigate("all"); };
            center.Controls.Add(settingsView); Theme.Apply(settingsView);
        }
        private void EnsureStatisticsView()
        {
            if (statisticsView != null && !statisticsView.IsDisposed) return;
            statisticsView = new StatisticsView(client, apiKey, () => files.ToList());
            statisticsView.BrowseRequested += async delegate(string path) { Navigate("filesystem"); await filesystemView.NavigateToAsync(path); };
            center.Controls.Add(statisticsView); Theme.Apply(statisticsView);
        }
        private void ResetStatisticsView()
        { if (statisticsView != null) { center.Controls.Remove(statisticsView); statisticsView.Dispose(); statisticsView = null; } }
        private void FilesystemTransferChanged(object sender, FilesystemTransferEventArgs e)
        {
            if (IsDisposed || Disposing) return;
            if (filesystemJob == null || filesystemJob.Finished)
            {
                filesystemJob = new TransferJob { FileSystem = true, Generation = generation, Direction = e.Direction, LocalPath = e.LocalPath };
                filesystemJob.Cancel.Dispose(); filesystemJob.Cancel = e.Cancellation; jobs.Add(filesystemJob);
            }
            filesystemJob.Name = e.Name; filesystemJob.Status = e.Status; filesystemJob.Metrics = e.Metrics; filesystemJob.Finished = e.Finished;
            filesystemJob.Done = e.Metrics.Done; filesystemJob.Size = e.Metrics.Total; filesystemJob.Percent = e.Metrics.Percent; RenderTransfers();
        }
        private void ChangeRememberKey(bool remember)
        {
            if (remember && apiKey != null) credentials.Save(apiKey);
            else if (!remember) credentials.Delete();
            try { preferences.SaveRememberKey(remember); }
            catch (Exception) { throw new IOException("Your preference could not be saved. Please try again."); }
            rememberKey = remember; savedKey = remember ? apiKey ?? savedKey : null;
        }
        private async Task RestoreSavedConnection()
        {
            if (!rememberKey || restoringConnection || apiKey != null) return;
            if (savedKey == null)
            {
                try { savedKey = credentials.Load(); }
                catch (IOException ex) { SetStatus(ex.Message); return; }
            }
            if (savedKey == null) return;
            string key = savedKey; int gen = generation; CancellationToken token = session.Token;
            restoringConnection = true; RenderAll(); SetStatus("Connecting with your saved API key…");
            try
            {
                List<CloudFile> result = await client.ListAsync(key, token);
                if (IsDisposed || token.IsCancellationRequested || gen != generation) return;
                ActivateAccount(key, result);
            }
            catch (OperationCanceledException)
            { if (!IsDisposed && gen == generation && !token.IsCancellationRequested) SetStatus("Connection timed out. Select Refresh or open Settings to try again."); }
            catch (Exception)
            { if (!IsDisposed && gen == generation && !token.IsCancellationRequested) SetStatus("Could not reconnect. Select Refresh or check your API key in Settings."); }
            finally { if (!IsDisposed && gen == generation) { restoringConnection = false; RenderAll(); } }
        }
        private void Disconnect()
        {
            marked.Clear();
            ResetFilesystemView();
            ResetStatisticsView();
            credentials.Delete(); savedKey = null; restoringConnection = false;
            generation++; session.Cancel(); session.Dispose(); session = new CancellationTokenSource(); apiKey = null; scope = "demo"; files.Clear(); meta = new LocalData(); demoMode = false; if (page != "settings") page = "all"; RenderAll(); SetStatus("Disconnected.");
        }
        private async Task RefreshAccount()
        {
            if (loading || restoringConnection) return; if (apiKey == null) { if (rememberKey && savedKey != null) await RestoreSavedConnection(); else SetStatus("Open Settings to connect your account."); return; }
            loading = true; string key = apiKey; int gen = generation; SetStatus("Loading files…");
            try { List<CloudFile> result = await client.ListAsync(key, session.Token); if (gen != generation) return; files = result; RenderAll(); SetStatus("Files updated."); }
            catch (OperationCanceledException) { if (gen == generation) SetStatus("The request was cancelled or timed out."); }
            catch (Exception ex) { if (gen == generation) ShowError(ex.Message); }
            finally { loading = false; }
        }
        private ContextMenuStrip BuildFileMenu()
        {
            ContextMenuStrip menu = new ContextMenuStrip(); menu.Items.Add("Download", null, delegate { ChooseDownload(); }); menu.Items.Add("Copy link", null, delegate { CopyLinks(); }); menu.Items.Add("Toggle favorite", null, delegate { List<CloudFile> selected = SelectedFiles(); foreach (CloudFile f in selected) { if (meta.Favorites.Contains(f.Id)) meta.Favorites.Remove(f.Id); else meta.Favorites.Add(f.Id); } SaveMeta(); RenderAll(); });
            menu.Items.Add("Select all visible", null, delegate { SelectVisible(); }); menu.Items.Add("Clear selection", null, delegate { ClearMarks(); });
            menu.Items.Add("Copy hotlink", null, delegate { CopyHotlinks(); }); menu.Items.Add("Share…", null, delegate { ShareSelected(); });
            menu.Items.Add(new ToolStripSeparator()); menu.Items.Add("Add to collection…", null, delegate { AddToCollection(); }); menu.Items.Add("Edit display name…", null, delegate { RenameFile(); }); menu.Items.Add("Remove from collection", null, delegate { RemoveFromCollection(); }); menu.Items.Add("File details", null, delegate { ShowDetails(); }); menu.Items.Add(new ToolStripSeparator()); menu.Items.Add("Delete file…", null, async delegate { await DeleteSelected(); });
            menu.Opening += delegate(object sender, System.ComponentModel.CancelEventArgs e) { if (SelectedFiles().Count == 0) e.Cancel = true; }; return menu;
        }
        private void ToggleFavorite(CloudFile file) { if (meta.Favorites.Contains(file.Id)) meta.Favorites.Remove(file.Id); else meta.Favorites.Add(file.Id); SaveMeta(); RenderAll(); }
        private void NewCollection(Collection existing)
        {
            using (TextDialog dialog = new TextDialog(existing == null ? "New collection" : "Rename collection", "Collection name", existing == null ? "" : existing.Name, 60))
            if (dialog.ShowDialog(this) == DialogResult.OK) { if (existing == null) meta.Collections.Add(new Collection { Name = dialog.Result }); else existing.Name = dialog.Result; SaveMeta(); RenderAll(); SetStatus("Collection saved."); }
        }
        private void RemoveCollection()
        {
            Collection c = CurrentCollection(); if (c == null) return;
            if (Notice.Show(this, "Delete collection \"" + c.Name + "\"? Your files will stay in PixelDrain.", "Delete collection", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            meta.Collections.Remove(c); page = "all"; SaveMeta(); RenderAll();
        }
        private void AddToCollection()
        {
            List<CloudFile> selected = SelectedFiles(); if (selected.Count == 0) { SetStatus("Select a file first."); return; }
            if (meta.Collections.Count == 0) NewCollection(null); if (meta.Collections.Count == 0) return;
            using (CollectionDialog dialog = new CollectionDialog(meta.Collections)) if (dialog.ShowDialog(this) == DialogResult.OK)
            { Collection c = dialog.SelectedCollection; foreach (CloudFile f in selected) if (!c.Files.Contains(f.Id)) c.Files.Add(f.Id); SaveMeta(); RenderAll(); SetStatus(selected.Count + " files added to " + c.Name + "."); }
        }
        private void RemoveFromCollection()
        { Collection c = CurrentCollection(); if (c == null) return; foreach (CloudFile f in SelectedFiles()) c.Files.Remove(f.Id); SaveMeta(); RenderAll(); }
        private void RenameFile()
        {
            List<CloudFile> selected = SelectedFiles(); if (selected.Count != 1) { SetStatus("Select one file to edit its display name."); return; } CloudFile f = selected[0];
            using (TextDialog dialog = new TextDialog("Display name", "Original filename: " + f.Name, meta.DisplayName(f), 255))
            if (dialog.ShowDialog(this) == DialogResult.OK) { if (dialog.Result == f.Name) meta.Aliases.Remove(f.Id); else meta.Aliases[f.Id] = dialog.Result; SaveMeta(); RenderAll(); SetStatus("Display name saved."); }
        }
        private void CopyLinks()
        {
            List<CloudFile> selected = SelectedFiles(); if (selected.Count == 0) return; if (apiKey == null) { SetStatus("Links are unavailable for demo files."); return; }
            Clipboard.SetText(String.Join(Environment.NewLine, selected.Select(f => PixelDrainClient.Origin + "/u/" + f.Id))); SetStatus("Link copied.");
        }
        private void CopyHotlinks()
        {
            List<CloudFile> selected = SelectedFiles(); if (selected.Count == 0 || apiKey == null) return;
            try { Clipboard.SetText(String.Join(Environment.NewLine, selected.Select(f => ShareLinks.FileHotlink(f.Id)))); SetStatus("Hotlinks copied."); }
            catch (Exception) { SetStatus("The clipboard is busy. Open Share to copy the links manually."); }
        }
        private void ShareSelected()
        {
            List<CloudFile> selected = SelectedFiles(); if (selected.Count == 0 || apiKey == null) return;
            using (ShareDialog dialog = new ShareDialog(selected)) dialog.ShowDialog(this);
        }
        private void ShowDetails()
        {
            CloudFile f = SelectedFiles().FirstOrDefault(); if (f == null) return; meta.Recent[f.Id] = DateTime.UtcNow; SaveMeta();
            Notice.Show(this, meta.DisplayName(f) + "\n\nOriginal filename: " + f.Name + "\nSize: " + FileRules.SizeText(f.Size) + "\nType: " + f.Kind + "\nFile ID: " + f.Id + "\nUploaded: " + f.Uploaded.ToLocalTime().ToString("dd MMM yyyy HH:mm") + (demoMode ? "\n\nDemo file." : ""), "File details — Pixelroom", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        private void ChooseUpload()
        {
            if (apiKey == null) { Connection(); if (apiKey == null) return; }
            using (OpenFileDialog dialog = new OpenFileDialog { Title = "Select files to upload", Multiselect = true, CheckFileExists = true }) if (dialog.ShowDialog(this) == DialogResult.OK) UploadPaths(dialog.FileNames);
        }
        private void UploadPaths(string[] paths)
        {
            if (paths != null && paths.Any(Directory.Exists)) { OpenFolderUpload(paths); return; }
            try { paths = UploadSelection.Files(paths); } catch (Exception ex) { ShowError(ex.Message); return; }
            if (apiKey == null) { Connection(); if (apiKey == null) return; }
            string key = apiKey; int gen = generation; Collection collection = page.StartsWith("collection:") ? CurrentCollection() : null; int added = 0;
            foreach (string path in paths)
            {
                if (!File.Exists(path)) continue;
                FileInfo info; try { info = new FileInfo(path); if (info.Length == 0) { SetStatus("Skipped empty file: " + info.Name); continue; } } catch (Exception ex) { ShowError(ex.Message); continue; }
                TransferJob job = new TransferJob { Name = info.Name, Direction = "Upload", Status = "Queued", Size = info.Length, LocalPath = path }; jobs.Add(job); added++;
                StartJob(job, async delegate(CancellationToken token, IProgress<TransferProgress> progress)
                {
                    CloudFile result = await client.UploadAsync(job.LocalPath, key, progress, token);
                    if (gen == generation) { files.RemoveAll(f => f.Id == result.Id); files.Add(result); if (collection != null && meta.Collections.Contains(collection) && !collection.Files.Contains(result.Id)) collection.Files.Add(result.Id); SaveMeta(); RenderAll(); }
                });
            }
            if (added > 0) { Navigate("transfers"); SetStatus(added + " files queued."); }
        }
        private void ChooseFolderUpload()
        {
            if (apiKey == null) { Connection(); if (apiKey == null) return; }
            try { string[] paths = FolderPicker.SelectFolders(this); if (paths.Length > 0) OpenFolderUpload(paths); }
            catch (Exception ex) { ShowError(ex.Message); }
        }
        private async void OpenFolderUpload(string[] paths)
        {
            if (apiKey == null) { Connection(); if (apiKey == null) return; }
            Navigate("filesystem"); await filesystemView.UploadSelectionAsync(paths.ToArray());
        }
        private void EnableFileDrop(Control target)
        {
            target.AllowDrop = true;
            target.DragEnter += delegate(object sender, DragEventArgs e) { e.Effect = !deleting && e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None; };
            target.DragDrop += delegate(object sender, DragEventArgs e) { string[] paths = e.Data.GetData(DataFormats.FileDrop) as string[]; if (!deleting && paths != null) UploadPaths(paths); };
        }
        private void ChooseDownload()
        {
            List<CloudFile> selected = SelectedFiles(); if (selected.Count == 0) { SetStatus("Select a file to download."); return; } if (apiKey == null) { SetStatus("Open Settings to connect your account."); return; }
            if (selected.Count > 100) { ShowError("Select up to 100 files per ZIP download."); return; }
            using (SaveFileDialog dialog = new SaveFileDialog { Title = "Save download", FileName = selected.Count == 1 ? FileRules.SafeName(selected[0].Name) : "Pixelroom-files.zip", Filter = selected.Count > 1 ? "ZIP archive (*.zip)|*.zip" : "All files (*.*)|*.*", OverwritePrompt = true, AddExtension = selected.Count > 1 })
            if (dialog.ShowDialog(this) == DialogResult.OK)
            {
                string key = apiKey; List<string> ids = selected.Select(f => f.Id).ToList(); TransferJob job = new TransferJob { Name = Path.GetFileName(dialog.FileName), Direction = "Download", Status = "Queued", Size = selected.Sum(f => f.Size), LocalPath = dialog.FileName }; jobs.Add(job);
                StartJob(job, async delegate(CancellationToken token, IProgress<TransferProgress> progress) { await client.DownloadAsync(ids, job.LocalPath, key, progress, token); }); Navigate("transfers");
            }
        }
        private int ActiveJobs() { return jobs.Count(j => !j.Finished); }
        private async void StartJob(TransferJob job, Func<CancellationToken, IProgress<TransferProgress>, Task> work)
        {
            job.Work = work; job.Generation = generation;
            CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(session.Token, job.Cancel.Token); bool entered = false;
            try
            {
                await slots.WaitAsync(linked.Token); entered = true; job.Metrics.Reset(); job.Status = job.Direction == "Upload" ? "Uploading" : "Downloading"; RenderTransfers();
                Progress<TransferProgress> progress = new Progress<TransferProgress>(delegate(TransferProgress p) { if (IsDisposed || job.Finished) return; job.Done = p.Done; if (p.Total >= 0) job.Size = p.Total; job.Metrics.Update(p.Done, p.Total); job.Percent = p.Percent; if (p.Percent == 100 && job.Direction == "Upload") job.Status = "Processing…"; RenderTransfers(); });
                await work(linked.Token, progress); job.Percent = 100; job.Status = "Completed";
            }
            catch (OperationCanceledException) { job.Status = "Cancelled"; }
            catch (Exception ex) { job.Status = linked.IsCancellationRequested ? "Cancelled" : "Failed: " + ex.Message; }
            finally
            {
                job.Finished = true; job.Metrics.Finish(job.Status == "Completed"); if (entered) slots.Release(); linked.Dispose();
                if (!IsDisposed) { RenderTransfers(); SetStatus(job.Name + " — " + job.Status); }
            }
        }
        private void RenderTransfers()
        {
            if (transfers == null || transferSummary == null || IsDisposed) return; transfers.RowCount = jobs.Count; transfers.Invalidate();
            nav["transfers"].Text = "Transfers" + (ActiveJobs() > 0 ? " (" + ActiveJobs() + ")" : ""); UpdateTransferButtons();
            TransferJob current = SelectedJob(); if (current == null || current.Finished) current = jobs.FirstOrDefault(j => !j.Finished) ?? current ?? jobs.LastOrDefault();
            bool show = current != null && page != "filesystem"; transferSummary.Visible = show; content.RowStyles[6].Height = show ? 73 * DeviceDpi / 96f : 0;
            if (show) transferSummary.Display(current.Status, current.Name, current.Metrics, ActiveJobs() > 1 ? ActiveJobs() + " active · " : "");
            if (transferTicker != null) transferTicker.Enabled = ActiveJobs() > 0;
        }
        private void TransferValue(object sender, DataGridViewCellValueEventArgs e)
        {
            if (e.RowIndex < 0 || e.RowIndex >= jobs.Count) return; TransferJob job = jobs[e.RowIndex];
            switch (e.ColumnIndex) { case 0: e.Value = job.Name; break; case 1: e.Value = job.Direction; break; case 2: e.Value = job.Metrics.HasLength || job.Finished ? job.Metrics.Percent + "%" : "Unknown"; break; case 3: e.Value = job.Metrics.BytesText; break; case 4: e.Value = job.Status; break; case 5: e.Value = job.Finished || job.Status == "Queued" ? "—" : job.Metrics.SpeedText; break; case 6: e.Value = job.Finished ? "—" : job.Metrics.Eta; break; }
        }
        private TransferJob SelectedJob() { return transfers == null || transfers.SelectedRows.Count == 0 || transfers.SelectedRows[0].Index >= jobs.Count ? null : jobs[transfers.SelectedRows[0].Index]; }
        private void UpdateTransferButtons()
        {
            if (cancelTransfer == null || cancelAll == null) return; TransferJob job = SelectedJob();
            cancelTransfer.Enabled = job != null && !job.Finished; cancelAll.Enabled = ActiveJobs() > 0; clearTransfer.Enabled = jobs.Any(j => j.Finished);
            retryTransfer.Enabled = job != null && job.Finished && job.Status != "Completed" && !job.FileSystem && job.Work != null && job.Generation == generation && apiKey != null;
            showFolder.Enabled = job != null && job.Finished && job.Status == "Completed" && job.Direction == "Download" && !String.IsNullOrEmpty(job.LocalPath) && (File.Exists(job.LocalPath) || Directory.Exists(job.LocalPath));
        }
        private void RetrySelectedTransfer()
        {
            if (!retryTransfer.Enabled) return; TransferJob old = SelectedJob();
            TransferJob retry = new TransferJob { Name = old.Name, Direction = old.Direction, Status = "Queued", Size = old.Size, LocalPath = old.LocalPath }; jobs.Add(retry); StartJob(retry, old.Work); RenderTransfers();
        }
        private void ShowDownloadFolder()
        {
            if (!showFolder.Enabled) return; TransferJob job = SelectedJob();
            try { Process.Start(new ProcessStartInfo("explorer.exe", (Directory.Exists(job.LocalPath) ? "\"" : "/select,\"") + Path.GetFullPath(job.LocalPath) + "\"") { UseShellExecute = true }); }
            catch (Exception ex) { ShowError("The download folder could not be opened. " + ex.Message); }
        }
        private void PaintTransferCell(object sender, DataGridViewCellPaintingEventArgs e)
        {
            if (e.RowIndex < 0 || e.RowIndex >= jobs.Count || e.ColumnIndex != 2) return;
            e.Paint(e.ClipBounds, e.PaintParts & ~DataGridViewPaintParts.ContentForeground); TransferJob job = jobs[e.RowIndex]; float s = transfers.DeviceDpi / 96f;
            Rectangle track = new Rectangle(e.CellBounds.X + (int)(9 * s), e.CellBounds.Y + e.CellBounds.Height / 2 + (int)(8 * s), Math.Max(0, e.CellBounds.Width - (int)(18 * s)), (int)(5 * s));
            using (SolidBrush background = new SolidBrush(Theme.Line)) e.Graphics.FillRectangle(background, track);
            using (SolidBrush fill = new SolidBrush(Theme.IsDark ? Color.FromArgb(67, 210, 216) : Color.FromArgb(12, 132, 142))) e.Graphics.FillRectangle(fill, track.X, track.Y, (int)(track.Width * job.Metrics.Percent / 100.0), track.Height);
            string text = job.Metrics.HasLength || job.Finished ? job.Metrics.Percent + "%" : "Size unknown";
            TextRenderer.DrawText(e.Graphics, text, Font, new Rectangle(e.CellBounds.X + (int)(9 * s), e.CellBounds.Y + (int)(4 * s), e.CellBounds.Width - (int)(14 * s), e.CellBounds.Height / 2), Theme.Ink, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis); e.Handled = true;
        }
        private async Task DeleteSelected()
        {
            if (deleting) return; List<CloudFile> selected = SelectedFiles(); if (selected.Count == 0) return;
            if (apiKey == null && !demoMode) return;
            string warning = demoMode ? "Remove " + selected.Count + " demo files?" : "Permanently delete " + selected.Count + " files from PixelDrain?\n\nThis cannot be undone.";
            if (Notice.Show(this, warning, "Delete files", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
            deleting = true; UpdateSelection(); string key = apiKey; int gen = generation; int count = 0; List<string> failures = new List<string>();
            try
            {
                foreach (CloudFile f in selected)
                {
                    if (gen != generation) break;
                    try { if (key != null) await client.DeleteAsync(f.Id, key, session.Token); if (gen != generation) break; files.RemoveAll(x => x.Id == f.Id); meta.Remove(f.Id); count++; SetStatus("Deleting files: " + count + "/" + selected.Count); }
                    catch (OperationCanceledException) { if (gen == generation) failures.Add(f.Name + ": request cancelled or timed out."); break; }
                    catch (Exception ex) { failures.Add(f.Name + ": " + ex.Message); }
                }
                if (gen == generation) { SaveMeta(); RenderAll(); SetStatus(count + " files deleted" + (failures.Count > 0 ? "; " + failures.Count + " failed." : ".")); if (failures.Count > 0) ShowError(String.Join("\n", failures.Take(8))); }
            }
            finally { deleting = false; if (!IsDisposed) UpdateSelection(); }
        }
        private void HandleKeys(object sender, KeyEventArgs e)
        {
            if (page == "statistics" && statisticsView != null) { statisticsView.HandleKey(e); return; }
            if (page == "filesystem" && filesystemView != null && filesystemView.HandleBrowserKey(e)) return;
            if (e.Control && e.Shift && e.KeyCode == Keys.A) { ClearMarks(); e.SuppressKeyPress = true; return; }
            if (grid.Focused && e.Control && e.KeyCode == Keys.A) { SelectVisible(); e.SuppressKeyPress = true; return; }
            if (grid.Focused && e.KeyCode == Keys.Space && grid.CurrentCell != null && grid.CurrentCell.RowIndex < visible.Count) { ToggleMarked(visible[grid.CurrentCell.RowIndex].Id); e.SuppressKeyPress = true; return; }
            if (grid.Focused && e.KeyCode == Keys.Escape) { ClearMarks(); e.SuppressKeyPress = true; return; }
            if (e.Control && e.KeyCode == Keys.F) { if (page == "transfers" || page == "settings") Navigate("all"); search.Focus(); search.SelectAll(); e.SuppressKeyPress = true; }
            else if (e.KeyCode == Keys.F5) { RefreshAccount().ContinueWith(delegate { }); e.SuppressKeyPress = true; }
            else if (e.Control && e.KeyCode == Keys.U) { ChooseUpload(); e.SuppressKeyPress = true; }
            else if (e.KeyCode == Keys.Delete && grid.Focused) { DeleteSelected().ContinueWith(delegate { }); e.SuppressKeyPress = true; }
        }
        private async void ClosingWindow(object sender, FormClosingEventArgs e)
        {
            if (!allowClose && (ActiveJobs() > 0 || deleting || (filesystemView != null && filesystemView.IsBusy)))
            {
                if (Notice.Show(this, "Transfers or file deletion are still running. Closing Pixelroom will cancel unfinished work. Close anyway?", "Close Pixelroom", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes) { e.Cancel = true; return; }
                e.Cancel = true; allowClose = true; generation++; apiKey = null; session.Cancel(); if (filesystemView != null) filesystemView.CancelOperation(); Enabled = false; SetStatus("Cancelling transfers…");
                while (ActiveJobs() > 0 || deleting || (filesystemView != null && filesystemView.IsBusy)) await Task.Delay(100);
                if (!IsDisposed) Close();
                return;
            }
            allowClose = true; apiKey = null; session.Cancel();
        }
        private void ShowError(string message) { if (!IsDisposed) { SetStatus(message); Notice.Show(this, message, "Pixelroom", MessageBoxButtons.OK, MessageBoxIcon.Warning); } }
    }
}
