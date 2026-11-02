using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Pixelroom;

internal static partial class ConnectionTests
{
    private sealed class Handler : HttpMessageHandler
    {
        public Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> Respond;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) { return Respond(request, token); }
    }
    private static readonly BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static object Field(object window, string name) { return window.GetType().GetField(name, Private).GetValue(window); }
    private static object Call(object window, string name, params object[] args) { return window.GetType().GetMethod(name, Private).Invoke(window, args); }
    private static void Assert(bool value, string message) { if (!value) throw new Exception(message); }
    private static HttpResponseMessage Listing()
    { return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"files\":[{\"id\":\"abcd1234\",\"name\":\"file.txt\",\"size\":42}]}", Encoding.UTF8, "application/json") }; }
    private static int passed, result = 1;
    private static void Pass(string name) { passed++; Console.WriteLine("PASS " + name); }

    private static async Task Run(string root)
    {
        const string key = "dummy-saved-session-key", replacement = "dummy-replacement-session-key";
        string profile = Path.Combine(root, "startup"); new CredentialStore(profile).Save(key);
        int requests = 0;
        Handler handler = new Handler(); handler.Respond = delegate(HttpRequestMessage request, CancellationToken token)
        {
            requests++; Assert(request.RequestUri.AbsoluteUri == "https://pixeldrain.com/api/user/files", "Startup sent credentials to an unexpected destination.");
            Assert(request.Headers.Authorization.Parameter == Convert.ToBase64String(Encoding.UTF8.GetBytes(":" + key)), "Startup did not use the saved API key.");
            return Task.FromResult(Listing());
        };
        using (PixelDrainClient client = new PixelDrainClient(handler))
        using (MainWindow window = new MainWindow(client, profile))
        {
            await (Task)Call(window, "RestoreSavedConnection");
            Assert(requests == 1 && (string)Field(window, "apiKey") == key && ((List<CloudFile>)Field(window, "files")).Count == 1, "Startup must validate and load the saved account.");
            Assert(((Label)Field(window, "accountState")).Text == "Connected", "Successful startup did not update the connection badge.");
        }
        Pass("startup validates the saved key and loads files without manual entry");

        profile = Path.Combine(root, "offline"); CredentialStore credentials = new CredentialStore(profile); credentials.Save(key);
        handler = new Handler(); handler.Respond = delegate { var failure = new TaskCompletionSource<HttpResponseMessage>(); failure.SetException(new HttpRequestException("Offline")); return failure.Task; };
        using (PixelDrainClient client = new PixelDrainClient(handler))
        using (MainWindow window = new MainWindow(client, profile))
        {
            await (Task)Call(window, "RestoreSavedConnection");
            Assert(Field(window, "apiKey") == null && credentials.Load() == key, "Offline startup must preserve the saved key without claiming a connection.");
            Assert(((Label)Field(window, "status")).Text.Contains("Refresh") && !(bool)Field(window, "restoringConnection"), "Offline startup did not offer a retry.");
            handler.Respond = delegate { return Task.FromResult(Listing()); };
            await (Task)Call(window, "RefreshAccount");
            Assert((string)Field(window, "apiKey") == key, "Refresh must retry a failed automatic connection.");
        }
        Pass("offline startup keeps the key and Refresh reconnects successfully");

        profile = Path.Combine(root, "cancelled"); credentials = new CredentialStore(profile); credentials.Save(key);
        var pending = new TaskCompletionSource<HttpResponseMessage>();
        handler = new Handler(); handler.Respond = delegate { return pending.Task; };
        using (PixelDrainClient client = new PixelDrainClient(handler))
        using (MainWindow window = new MainWindow(client, profile))
        {
            Task startup = (Task)Call(window, "RestoreSavedConnection");
            Assert((bool)Field(window, "restoringConnection") && Field(window, "apiKey") == null, "Pending startup must not claim a connection.");
            Call(window, "Disconnect"); pending.SetResult(Listing()); await startup;
            Assert(Field(window, "apiKey") == null && credentials.Load() == null, "A late startup response must not reconnect after Disconnect.");
        }
        Pass("Disconnect removes the key and blocks a late automatic connection");

        profile = Path.Combine(root, "changed"); credentials = new CredentialStore(profile); credentials.Save(key);
        pending = new TaskCompletionSource<HttpResponseMessage>(); handler = new Handler(); handler.Respond = delegate { return pending.Task; };
        using (PixelDrainClient client = new PixelDrainClient(handler))
        using (MainWindow window = new MainWindow(client, profile))
        {
            Task startup = (Task)Call(window, "RestoreSavedConnection");
            Call(window, "ConnectAccount", replacement, new List<CloudFile>()); pending.SetResult(Listing()); await startup;
            Assert((string)Field(window, "apiKey") == replacement && credentials.Load() == replacement, "A stale startup response replaced the new account.");
            Call(window, "ChangeRememberKey", false);
            Assert(credentials.Load() == null && !new PreferencesStore(profile).Load().RememberApiKey && (string)Field(window, "apiKey") == replacement, "Opt-out must forget the key while keeping the current session.");
            Call(window, "ChangeTheme", "Gray"); Assert(!new PreferencesStore(profile).Load().RememberApiKey, "Changing theme reset the remember choice.");
            Call(window, "ChangeRememberKey", true); Assert(credentials.Load() == replacement, "Opt-in must remember the current validated account.");
            Call(window, "Disconnect"); Assert(credentials.Load() == null && Field(window, "apiKey") == null, "Disconnect did not forget the key.");
        }
        Pass("changing accounts ignores stale responses; remember choice and Disconnect work together");

        profile = Path.Combine(root, "session-only"); credentials = new CredentialStore(profile); credentials.Save(key); new PreferencesStore(profile).SaveRememberKey(false);
        requests = 0; handler = new Handler(); handler.Respond = delegate { requests++; return Task.FromResult(Listing()); };
        using (PixelDrainClient client = new PixelDrainClient(handler))
        using (MainWindow window = new MainWindow(client, profile))
        {
            await (Task)Call(window, "RestoreSavedConnection"); Assert(requests == 0 && Field(window, "apiKey") == null, "Opt-out must suppress automatic login.");
            Call(window, "ConnectAccount", replacement, new List<CloudFile>());
            Assert(credentials.Load() == null && (string)Field(window, "apiKey") == replacement, "Session-only connections must not save credentials.");
        }
        Pass("session-only mode suppresses automatic login and does not save keys");
        await CheckExplorerWindow(root);
        await CheckRevisionWindow(root);
        await CheckMarkedSelection(root);
        await CheckStatisticsWindow(root);
    }

    private static async Task CheckMarkedSelection(string root)
    {
        ExplorerFixture fixture = new ExplorerFixture(); fixture.Folder("/me/Archive"); fixture.File("/me/Alpha.txt", Encoding.UTF8.GetBytes("alpha")); fixture.File("/me/Beta.txt", Encoding.UTF8.GetBytes("beta"));
        using (PixelDrainClient client = new PixelDrainClient(fixture))
        using (MainWindow main = new MainWindow(client, Path.Combine(root, "marking")))
        {
            main.Show();
            Call(main, "ActivateAccount", ExplorerFixture.Key, new List<CloudFile> { new CloudFile { Id = "file1234", Name = "Alpha.txt", Uploaded = DateTime.Today }, new CloudFile { Id = "file5678", Name = "Beta.txt", Uploaded = DateTime.Today } });
            DataGridView files = (DataGridView)Field(main, "grid");
            typeof(DataGridView).GetMethod("OnCellMouseClick", Private).Invoke(files, new object[] { new DataGridViewCellMouseEventArgs(files.Columns["mark"].Index, 0, 1, 1, new MouseEventArgs(MouseButtons.Left, 1, 1, 1, 0)) });
            Call(main, "ToggleMarked", "file5678");
            Assert(((List<CloudFile>)Call(main, "SelectedFiles")).Count == 2 && (bool)files["mark", 0].Value && ((Label)Field(main, "selection")).Text.Contains("2 marked"), "Checkbox clicks must mark files for existing organize actions.");
            ((TextBox)Field(main, "search")).Text = "Alpha"; Call(main, "RenderFiles");
            Assert(files.RowCount == 1 && ((List<CloudFile>)Call(main, "SelectedFiles")).Count == 2 && ((Label)Field(main, "status")).Text.Contains("1 hidden"), "Filtering must retain marked files and disclose hidden selections.");
            Call(main, "ClearMarks"); Assert(((List<CloudFile>)Call(main, "SelectedFiles")).Count == 0, "Clear selection must remove marks and focused-row selection.");
            Call(main, "Navigate", "filesystem"); FilesystemWindow fs = (FilesystemWindow)Field(main, "filesystemView"); await fs.InitializeAsync();
            ActionToolbar toolbar = (ActionToolbar)Field(fs, "actionRows");
            Assert(toolbar.Controls.Count == 12 && toolbar.ColumnCount == 6 && toolbar.Controls.Cast<Control>().Max(c => c.Width) - toolbar.Controls.Cast<Control>().Min(c => c.Width) <= 1, "Desktop toolbar buttons must have equal proportional widths.");
            main.Size = main.MinimumSize; main.PerformLayout(); fs.PerformLayout(); await Task.Delay(20);
            Assert(toolbar.ColumnCount == 4 && toolbar.RequiredHeight == (int)((TableLayoutPanel)Field(fs, "layout")).RowStyles[4].Height && toolbar.Controls.Cast<Control>().All(c => c.Right <= toolbar.ClientSize.Width && c.Bottom <= toolbar.ClientSize.Height), "Narrow windows must reflow all actions without clipped buttons. Toolbar " + toolbar.Size + ", required " + toolbar.RequiredHeight + ".");
            Pass("file marking survives filters, clears explicitly and keeps action buttons proportional at narrow widths");

            Call(fs, "ToggleMarked", "/me/Alpha.txt"); Call(fs, "ToggleMarked", "/me/Beta.txt");
            DataGridView folders = (DataGridView)Field(fs, "grid"); Assert(((List<FilesystemNode>)Call(fs, "SelectedNodes")).Count == 2 && !((Button)Field(fs, "rename")).Enabled && ((Button)Field(fs, "copy")).Enabled, "Multi-item marking must enable batch actions and disable single-item rename.");
            ((TextBox)Field(fs, "search")).Text = "Archive"; Assert(((Label)Field(fs, "itemCount")).Text.Contains("2 hidden"), "Hidden folder-view selections must be counted.");
            Call(fs, "CopyItem"); await (Task)Call(fs, "LoadFolder", "/me/Archive", CancellationToken.None);
            Assert(((HashSet<string>)Field(fs, "marked")).Count == 0 && ((Button)Field(fs, "paste")).Enabled, "Folder navigation must clear marks while retaining the copy basket.");
            await (Task)Call(fs, "PasteItem"); Assert(fixture.Bytes.ContainsKey("/me/Archive/Alpha.txt") && fixture.Bytes.ContainsKey("/me/Archive/Beta.txt"), "Paste must copy every marked item, including ones hidden by the previous filter.");
            Call(fs, "SelectVisible"); Assert(((List<FilesystemNode>)Call(fs, "SelectedNodes")).Count == 2, "Select all must mark the visible folder items.");
            string downloads = Path.Combine(root, "marked-downloads"); Directory.CreateDirectory(downloads); File.WriteAllText(Path.Combine(downloads, "Alpha.txt"), "keep old");
            await (Task)Call(fs, "DownloadItems", Call(fs, "SelectedNodes"), downloads);
            Assert(File.ReadAllText(Path.Combine(downloads, "Alpha.txt")) == "keep old" && File.ReadAllText(Path.Combine(downloads, "Alpha (2).txt")) == "alpha" && File.ReadAllText(Path.Combine(downloads, "Beta.txt")) == "beta", "Marked downloads must save all selected files without replacing existing local files.");
            Call(fs, "ClearMarks"); Assert(((List<FilesystemNode>)Call(fs, "SelectedNodes")).Count == 0, "Clear folder selection failed.");
            Pass("marked folder items copy across folders and download together without changing existing files");

            fixture.File("/me/private.txt", new byte[10]); fixture.File("/me/shared.txt", new byte[10]); fixture.File("/me/locked.txt", new byte[10]); fixture.DenyOwner.Add("/me/locked.txt");
            fixture.Nodes["/me/shared.txt"].Id = "share123"; fixture.LinkPermissions["/me/shared.txt"] = new bool[] { true, true, false };
            fixture.File("/me/legacy.txt", new byte[1]); fixture.Nodes["/me/legacy.txt"].Id = "oldlink1"; fixture.OmitLinkPermissions.Add("/me/legacy.txt");
            int before = fixture.Mutations.Count;
            using (ShareDialog dialog = new ShareDialog(client, new List<FilesystemNode> { fixture.Nodes["/me/private.txt"], fixture.Nodes["/me/shared.txt"], fixture.Nodes["/me/locked.txt"], fixture.Nodes["/me/legacy.txt"] }, ExplorerFixture.Key))
            {
                await (Task)Call(dialog, "LoadBatch", false);
                Assert(fixture.Mutations.Count == before && ((TextBox)Field(dialog, "shareLink")).Lines.Length == 2, "Opening multi-share must read existing and legacy web links without changes.");
                await (Task)Call(dialog, "LoadBatch", true);
                Assert(fixture.Mutations.Count == before + 1 && ((TextBox)Field(dialog, "shareLink")).Lines.Length == 3 && fixture.LinkPermissions["/me/shared.txt"][1] && !fixture.LinkPermissions["/me/private.txt"][1] && fixture.Nodes["/me/locked.txt"].Id == null && !fixture.LinkPermissions.ContainsKey("/me/legacy.txt") && fixture.Nodes["/me/legacy.txt"].Id == "oldlink1", "Creating missing links must respect permissions and preserve existing and legacy web shares.");
            }
            Pass("multi-item Share reads first, creates only missing read-only links and preserves account permissions");
            main.Close();
        }
    }

    private static async Task CheckRevisionWindow(string root)
    {
        ExplorerFixture fixture = new ExplorerFixture(); fixture.Folder("/me/Documents"); fixture.Folder("/me/Documents/Nested"); fixture.File("/me/Documents/report.txt", new byte[32]);
        fixture.Nodes["/me/Documents"].Id = "pub12345"; fixture.LinkPermissions["/me/Documents"] = new bool[] { true, false, false };
        using (PixelDrainClient client = new PixelDrainClient(fixture))
        using (MainWindow main = new MainWindow(client, Path.Combine(root, "revision")))
        {
            main.Show();
            Call(main, "ActivateAccount", ExplorerFixture.Key, new List<CloudFile> { new CloudFile { Id = "file1234", Name = "first.txt", Uploaded = DateTime.Today.AddDays(-10) }, new CloudFile { Id = "file5678", Name = "last.txt", Uploaded = DateTime.Today } });
            int topLevels = Application.OpenForms.Cast<Form>().Count(f => f.TopLevel && f.Visible);
            Call(main, "Navigate", "filesystem"); FilesystemWindow fs = (FilesystemWindow)Field(main, "filesystemView"); await fs.InitializeAsync();
            Assert(!fs.TopLevel && fs.FormBorderStyle == FormBorderStyle.None && fs.Parent == Field(main, "center"), "File System must be embedded in the main content area.");
            Assert(Application.OpenForms.Cast<Form>().Count(f => f.TopLevel && f.Visible) == topLevels, "Selecting File System created another top-level window.");
            await (Task)Call(fs, "LoadFolder", "/me/Documents", CancellationToken.None); ((TextBox)Field(fs, "search")).Text = "report";
            Assert(((DataGridView)Field(fs, "grid")).RowCount == 1 && ((List<FilesystemNode>)Field(fs, "nodes"))[0].SharingText == "Shared via parent", "Nested sharing badges did not reach the file grid.");
            Call(main, "Navigate", "favorites"); Call(main, "Navigate", "recent"); Call(main, "Navigate", "filesystem");
            Assert(Object.ReferenceEquals(fs, Field(main, "filesystemView")) && (string)Field(fs, "currentPath") == "/me/Documents" && ((TextBox)Field(fs, "search")).Text == "report", "Sidebar navigation must reuse the folder view and retain its folder/search.");
            KeyEventArgs find = new KeyEventArgs(Keys.Control | Keys.F); Call(main, "HandleKeys", main, find);
            Assert(find.SuppressKeyPress && ((TextBox)Field(fs, "search")).Focused && (string)Field(main, "page") == "filesystem", "Ctrl+F must search the embedded folder rather than a hidden My files field.");
            KeyEventArgs location = new KeyEventArgs(Keys.Control | Keys.L); Call(main, "HandleKeys", main, location); Assert(location.SuppressKeyPress && ((TextBox)Field(fs, "address")).Focused, "Ctrl+L must focus the current folder path.");
            Pass("sidebar uses one window and preserves the embedded folder location and search");

            BrowseFilterBar folderFilters = (BrowseFilterBar)Field(fs, "browseFilters"); ((TextBox)Field(fs, "search")).Clear();
            folderFilters.sharing.SelectedIndex = 2; Assert(((DataGridView)Field(fs, "grid")).RowCount == 0, "Private filter must exclude inherited folder shares.");
            folderFilters.ResetFilters(); Assert(((DataGridView)Field(fs, "grid")).RowCount == 2, "Reset must restore the current folder.");
            folderFilters.SetDates(new DateTime(2050, 1, 1), new DateTime(2050, 1, 2)); Assert(((DataGridView)Field(fs, "grid")).RowCount == 0, "Date filters did not update the live grid.");
            folderFilters.ResetFilters();
            Call(main, "Navigate", "all"); BrowseFilterBar fileFilters = (BrowseFilterBar)Field(main, "browseFilters"); fileFilters.SetDates(DateTime.Today, DateTime.Today);
            Assert(((List<CloudFile>)Field(main, "visible")).Single().Name == "last.txt", "My files date filtering failed.");
            fileFilters.sharing.SelectedIndex = 2; Assert(((DataGridView)Field(main, "grid")).RowCount == 0, "My files public entries must not be mislabeled private."); fileFilters.ResetFilters();
            Pass("folder and My files grids apply sharing/date filters and reset without network mutations");

            Call(main, "Connection"); SettingsDialog settings = (SettingsDialog)Field(main, "settingsView");
            Assert(!settings.TopLevel && settings.Parent == Field(main, "center") && (string)Field(main, "page") == "settings", "Settings must use the main window.");
            Call(main, "Navigate", "all"); Call(main, "Connection"); Assert(Object.ReferenceEquals(settings, Field(main, "settingsView")), "Settings must be reused rather than spawning windows.");
            settings.Close(); Assert((string)Field(main, "page") == "all", "Done in embedded Settings must return to the file view.");
            Pass("Settings stays embedded, reuses its panel and returns to files with Done");

            string upload = Path.Combine(root, "revision-upload.bin"); File.WriteAllBytes(upload, new byte[200000]);
            Call(main, "Navigate", "filesystem"); Task uploading = (Task)Call(fs, "UploadPaths", (object)new string[] { upload });
            Call(main, "Navigate", "transfers"); await uploading; await Task.Delay(30);
            var jobs = (List<TransferJob>)Field(main, "jobs"); TransferJob folderJob = jobs.Last();
            Assert(fixture.Bytes.ContainsKey("/me/Documents/revision-upload.bin") && folderJob.FileSystem && folderJob.Finished && folderJob.Status == "Completed", "A folder upload must continue across sidebar navigation and appear in Transfers.");
            Assert(folderJob.Metrics.Done == 200000 && folderJob.Metrics.Total == 200000 && ((DataGridView)Field(main, "transfers")).Columns.Contains("speed"), "Transfers must show actual byte totals and speed for folder uploads.");
            Pass("folder uploads continue across menus and share the main Transfers list with byte progress");

            int attempts = 0; TransferJob failed = new TransferJob { Name = "retry.txt", Direction = "Upload", Status = "Queued", LocalPath = upload };
            Func<CancellationToken, IProgress<TransferProgress>, Task> work = delegate(CancellationToken token, IProgress<TransferProgress> progress)
            {
                attempts++; if (attempts == 1) { var failure = new TaskCompletionSource<bool>(); failure.SetException(new IOException("Simulated interruption")); return failure.Task; }
                progress.Report(new TransferProgress { Done = 24, Total = 24 }); return Task.FromResult(true);
            };
            jobs.Add(failed); Call(main, "StartJob", failed, work); await Task.Delay(30);
            DataGridView transfers = (DataGridView)Field(main, "transfers"); transfers.ClearSelection(); transfers.Rows[jobs.IndexOf(failed)].Selected = true; Call(main, "UpdateTransferButtons");
            Assert(((Button)Field(main, "retryTransfer")).Enabled, "A failed current-account transfer must offer Retry.");
            Call(main, "RetrySelectedTransfer"); await Task.Delay(30); Assert(attempts == 2 && jobs.Last().Finished && jobs.Last().Status == "Completed", "Retry must run the selected transfer again.");
            Call(main, "Navigate", "filesystem"); Assert(Object.ReferenceEquals(fs, Field(main, "filesystemView")), "Transfers must not replace the folder browser.");
            Assert(Application.OpenForms.Cast<Form>().Count(f => f.TopLevel && f.Visible) == topLevels, "Sidebar actions created extra top-level windows.");
            Pass("failed transfers can be retried while the single-window folder browser remains intact");
            main.Close();
        }
    }

    private static async Task CheckExplorerWindow(string root)
    {
        ExplorerFixture fixture = new ExplorerFixture(); fixture.Folder("/me/Documents"); fixture.Folder("/me/Documents/empty"); fixture.Folder("/me/Archive"); fixture.File("/me/Documents/report.txt", Encoding.UTF8.GetBytes("report"));
        using (PixelDrainClient client = new PixelDrainClient(fixture))
        using (FilesystemWindow window = new FilesystemWindow(client, ExplorerFixture.Key))
        {
            await (Task)Call(window, "LoadFolder", "/me", CancellationToken.None);
            var search = (TextBox)Field(window, "search"); var grid = (DataGridView)Field(window, "grid");
            search.Text = "DOC"; Assert(grid.RowCount == 1 && ((List<FilesystemNode>)Field(window, "nodes"))[0].Path == "/me/Documents", "The File System search did not filter the active folder.");
            grid.Rows[0].Selected = true; Call(window, "CopyItem");
            await (Task)Call(window, "LoadFolder", "/me/Archive", CancellationToken.None);
            Assert(search.Text.Length == 0 && ((Button)Field(window, "paste")).Enabled, "Copy must survive navigation, and a new folder must start with a clear search.");
            await (Task)Call(window, "PasteItem");
            Assert(fixture.Bytes.ContainsKey("/me/Archive/Documents/report.txt") && fixture.Nodes.ContainsKey("/me/Archive/Documents/empty"), "Paste did not copy the complete directory into the active folder.");
            grid.Rows[0].Selected = true; Call(window, "CopyItem"); await (Task)Call(window, "PasteItem");
            Assert(fixture.Bytes.ContainsKey("/me/Archive/Documents - Copy/report.txt"), "Paste into the same folder did not choose a safe new name.");
            Pass("File System search, copy across folders and repeated paste work together");

            search.Text = "nothing-matches"; Assert(grid.RowCount == 0 && ((Label)Field(window, "emptyTitle")).Text == "No matching items", "No-match search must show an empty state.");
            string dropped = Path.Combine(root, "dragged-upload.bin"); File.WriteAllBytes(dropped, Enumerable.Range(0, 1000).Select(i => (byte)i).ToArray());
            DataObject data = new DataObject(); data.SetData(DataFormats.FileDrop, new string[] { dropped });
            DragEventArgs enter = new DragEventArgs(data, 0, 0, 0, DragDropEffects.Copy, DragDropEffects.None);
            typeof(Control).GetMethod("OnDragEnter", Private).Invoke(grid, new object[] { enter }); Assert(enter.Effect == DragDropEffects.Copy && grid.AllowDrop, "The file grid must accept file drops.");
            var empty = (Control)Field(window, "empty"); Assert(empty.AllowDrop, "The empty folder surface must accept file drops.");
            DragEventArgs drop = new DragEventArgs(data, 0, 0, 0, DragDropEffects.Copy, DragDropEffects.Copy);
            typeof(Control).GetMethod("OnDragDrop", Private).Invoke(empty, new object[] { drop });
            for (int i = 0; i < 100 && (bool)Field(window, "busy"); i++) await Task.Delay(10);
            Assert(fixture.Bytes.ContainsKey("/me/Archive/dragged-upload.bin") && fixture.Bytes["/me/Archive/dragged-upload.bin"].Length == 1000 && !(bool)Field(window, "busy"), "A drop must upload to the active folder even while search hides all items.");
            search.Clear(); Assert(grid.RowCount == 3, "Clearing search must restore the latest uploaded listing.");
            Pass("drag-and-drop uploads to the current File System folder even with an active search");

            string alpha = Path.Combine(root, "drop-Alpha"), beta = Path.Combine(root, "drop-Beta");
            Directory.CreateDirectory(Path.Combine(alpha, "empty")); Directory.CreateDirectory(beta); File.WriteAllText(Path.Combine(beta, "note.txt"), "folder drop");
            data = new DataObject(); data.SetData(DataFormats.FileDrop, new string[] { alpha, beta });
            drop = new DragEventArgs(data, 0, 0, 0, DragDropEffects.Copy, DragDropEffects.Copy);
            typeof(Control).GetMethod("OnDragDrop", Private).Invoke(grid, new object[] { drop });
            for (int i = 0; i < 500 && (bool)Field(window, "busy"); i++) await Task.Delay(10);
            Assert(!(bool)Field(window, "busy") && fixture.Nodes.ContainsKey("/me/Archive/drop-Alpha/empty") && fixture.Bytes.ContainsKey("/me/Archive/drop-Beta/note.txt"), "Dropping multiple folders must upload both complete trees.");
            Pass("File System accepts multiple dropped folders and preserves empty subfolders");

            using (ShareDialog dialog = new ShareDialog(client, fixture.Nodes["/me/Archive/Documents"], ExplorerFixture.Key))
            {
                await (Task)Call(dialog, "LoadDetails");
                Assert(((Button)Field(dialog, "publish")).Enabled && !((Button)Field(dialog, "copyHotlink")).Enabled, "A private item must require a deliberate sharing action.");
                await client.SetFilesystemSharingAsync("/me/Archive/Documents", true, ExplorerFixture.Key, CancellationToken.None);
                await (Task)Call(dialog, "LoadDetails");
                Assert(((Button)Field(dialog, "copyHotlink")).Enabled && ((TextBox)Field(dialog, "hotlink")).Text.EndsWith("?bulk_download") && ((Button)Field(dialog, "unshare")).Enabled, "Shared folders must expose usable ZIP hotlinks and Stop sharing.");
            }
            using (ShareDialog dialog = new ShareDialog(new List<CloudFile> { new CloudFile { Id = "abcd1234", Name = "one.txt" }, new CloudFile { Id = "efgh5678", Name = "two.txt" } }))
            { Assert(((TextBox)Field(dialog, "shareLink")).Lines.Length == 2 && ((TextBox)Field(dialog, "hotlink")).Lines.Length == 2 && ((Button)Field(dialog, "copyShare")).Enabled, "My files must provide share and hotlinks for multiple selections."); }
            Pass("Share dialogs distinguish private items, shared folders and multiple public files");
        }
        using (PixelDrainClient client = new PixelDrainClient(new Handler()))
        using (MainWindow main = new MainWindow(client, Path.Combine(root, "drop-surfaces")))
        { Assert(((Control)Field(main, "grid")).AllowDrop && ((Control)Field(main, "emptyPanel")).AllowDrop && ((Control)Field(main, "transfers")).AllowDrop, "Main file and transfer surfaces must accept file drops."); }
        Pass("My files and Transfers expose file drop surfaces");

        Type dialogType = typeof(FolderPicker).GetNestedType("FileOpenDialog", BindingFlags.NonPublic);
        Type dialogInterface = typeof(FolderPicker).GetNestedType("IFileOpenDialog", BindingFlags.NonPublic);
        object nativeDialog = Activator.CreateInstance(dialogType, true);
        try
        {
            object[] options = new object[] { 0u }; dialogInterface.GetMethod("GetOptions").Invoke(nativeDialog, options);
            dialogInterface.GetMethod("SetOptions").Invoke(nativeDialog, new object[] { (uint)options[0] | 0x20u | 0x200u | 0x40u });
            dialogInterface.GetMethod("GetOptions").Invoke(nativeDialog, options);
            Assert(((uint)options[0] & 0x220u) == 0x220u, "The native picker must support folders and multiple selection.");
        }
        finally { if (Marshal.IsComObject(nativeDialog)) Marshal.ReleaseComObject(nativeDialog); }
        Pass("native folder picker supports multi-selection on this process architecture");
    }

    [STAThread]
    private static int Main(string[] args)
    {
        Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
        using (Form runner = new Form { ShowInTaskbar = false, Opacity = 0, WindowState = FormWindowState.Minimized })
        {
            runner.Shown += async delegate
            {
                try { await Run(Path.GetFullPath(args[0])); Console.WriteLine("SUCCESS " + passed + " connection lifecycle checks; process=" + (Environment.Is64BitProcess ? "64-bit" : "32-bit")); result = 0; }
                catch (Exception ex) { Console.Error.WriteLine("FAIL " + ex); }
                finally { runner.Close(); }
            };
            Application.Run(runner);
        }
        return result;
    }
}
