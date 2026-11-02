using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Pixelroom
{
    public sealed class ShareDialog : Form
    {
        private readonly PixelDrainClient client;
        private readonly string key, path;
        private readonly CancellationTokenSource cancellation = new CancellationTokenSource();
        private readonly TextBox shareLink, hotlink;
        private readonly Label description, feedback;
        private readonly Button copyShare, copyHotlink, publish, unshare;
        private FilesystemShareInfo info;
        private bool busy, disposed;
        private IList<FilesystemNode> batch;
        private readonly List<FilesystemShareInfo> batchInfo = new List<FilesystemShareInfo>();

        private ShareDialog(string name, bool multiple = false)
        {
            int extra = multiple ? 64 : 0;
            Theme.StyleDialog(this); Text = "Share — Pixelroom"; ClientSize = new Size(620, 550 + extra);
            Label title = Theme.Label("Share", 24, Theme.Ink, FontStyle.Bold); title.SetBounds(26, 21, 550, 43); Controls.Add(title);
            Label target = Theme.Label(name, 10, Theme.Ink, FontStyle.Bold); target.SetBounds(29, 73, 560, 26); target.AutoEllipsis = true; Controls.Add(target);
            if (multiple) { target.Visible = false; TextBox names = new TextBox { ReadOnly = true, Multiline = true, ScrollBars = ScrollBars.Vertical, Text = name, Font = Theme.Font(9), AccessibleName = "Selected items to share" }; names.SetBounds(29, 73, 560, 83); Controls.Add(names); }
            description = Theme.Label("Loading sharing details…", 9, Theme.Muted); description.SetBounds(29, 108 + extra, 560, 52); Controls.Add(description);
            Label shareLabel = Theme.Label("Share link", 10, Theme.Ink, FontStyle.Bold); shareLabel.SetBounds(29, 169 + extra, 420, 24); Controls.Add(shareLabel);
            shareLink = LinkField("Share link", 199 + extra); copyShare = CopyButton("Copy share link", 208 + extra, shareLink);
            Label hotLabel = Theme.Label("Hotlink", 10, Theme.Ink, FontStyle.Bold); hotLabel.SetBounds(29, 275 + extra, 420, 24); Controls.Add(hotLabel);
            hotlink = LinkField("Hotlink", 305 + extra); copyHotlink = CopyButton("Copy hotlink", 314 + extra, hotlink);
            publish = Theme.Button("Create read-only link", true); publish.SetBounds(29, 390 + extra, 190, 38); publish.Click += async delegate { if (batch != null) await LoadBatch(true); else await Work(() => client.SetFilesystemSharingAsync(path, true, key, cancellation.Token), true); }; Controls.Add(publish);
            unshare = Theme.Button("Stop sharing"); unshare.SetBounds(229, 390 + extra, 135, 38); unshare.Click += async delegate
            {
                if (Notice.Show(this, "Disable this item's own share link? Access through a shared parent folder will remain.", "Stop sharing", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) == DialogResult.Yes)
                    await Work(() => client.SetFilesystemSharingAsync(path, false, key, cancellation.Token), true);
            }; Controls.Add(unshare);
            feedback = Theme.Label("Hotlink availability follows your PixelDrain account settings.", 8.5f, Theme.Muted); feedback.SetBounds(29, 441 + extra, 560, 43); Controls.Add(feedback);
            Button done = Theme.Button("Done"); done.SetBounds(474, 495 + extra, 117, 37); done.Click += delegate { Close(); }; Controls.Add(done); CancelButton = done;
            FormClosing += delegate { cancellation.Cancel(); }; UpdateButtons();
        }
        public ShareDialog(IList<CloudFile> files) : this(files.Count == 1 ? files[0].Name : files.Count + " selected files")
        {
            shareLink.Text = String.Join(Environment.NewLine, files.Select(f => ShareLinks.FileLink(f.Id)));
            hotlink.Text = String.Join(Environment.NewLine, files.Select(f => ShareLinks.FileHotlink(f.Id)));
            description.Text = "Anyone with these links can view or download these files."; publish.Visible = unshare.Visible = false; UpdateButtons();
        }
        public ShareDialog(PixelDrainClient client, FilesystemNode node, string key) : this(node.Name)
        {
            this.client = client; this.key = key; path = node.Path;
            Shown += async delegate { await LoadDetails(); };
        }
        public ShareDialog(PixelDrainClient client, IList<FilesystemNode> nodes, string key) : this(String.Join(Environment.NewLine, nodes.Select(n => n.Path)), true)
        {
            this.client = client; this.key = key; batch = nodes.ToList(); unshare.Visible = false;
            Shown += async delegate { await LoadBatch(false); }; UpdateButtons();
        }
        private async Task LoadBatch(bool create)
        {
            if (busy || cancellation.IsCancellationRequested) return; busy = true; batchInfo.Clear(); shareLink.Clear(); hotlink.Clear(); UpdateButtons();
            try
            {
                foreach (FilesystemNode node in batch)
                {
                    feedback.Text = "Checking " + (batchInfo.Count + 1) + " of " + batch.Count + " · " + node.Name;
                    FilesystemShareInfo result = await client.GetFilesystemShareAsync(node.Path, key, cancellation.Token);
                    if (create && !result.LinkRead && result.CanManage) result = await client.SetFilesystemSharingAsync(node.Path, true, key, cancellation.Token);
                    if (IsDisposed || cancellation.IsCancellationRequested) return; batchInfo.Add(result);
                    shareLink.Text = String.Join(Environment.NewLine, batchInfo.Where(i => i.LinkRead).Select(i => i.ShareUrl));
                    hotlink.Text = String.Join(Environment.NewLine, batchInfo.Where(i => i.LinkRead).Select(i => i.Hotlink));
                }
                int available = batchInfo.Count(i => i.LinkRead); description.Text = available + " of " + batch.Count + " items have readable links. Anyone with a link can read that item; a folder link includes its contents.";
                feedback.Text = available == batch.Count ? "All selected links are ready. Existing sharing permissions were preserved." : "Create missing links shares private items read-only where your API key has permission.";
            }
            catch (OperationCanceledException) { if (!IsDisposed && !cancellation.IsCancellationRequested) feedback.Text = "The request timed out. Close and reopen Share to retry."; }
            catch (Exception ex) { if (!IsDisposed) feedback.Text = "Stopped: " + ex.Message + (create ? " Some earlier links may already have been created." : ""); }
            finally { busy = false; if (!IsDisposed) UpdateButtons(); }
        }
        private TextBox LinkField(string name, int y)
        {
            TextBox field = new TextBox { ReadOnly = true, Multiline = true, WordWrap = false, ScrollBars = ScrollBars.Both, BorderStyle = BorderStyle.FixedSingle, Font = Theme.Font(9), AccessibleName = name };
            field.SetBounds(29, y, 450, 66); Controls.Add(field); return field;
        }
        private Button CopyButton(string name, int y, TextBox field)
        {
            Button button = Theme.Button("Copy"); button.AccessibleName = name; button.SetBounds(491, y, 100, 38);
            button.Click += delegate { try { Clipboard.SetText(field.Text); feedback.Text = name == "Copy hotlink" ? "Hotlink copied." : "Share link copied."; } catch (Exception) { feedback.Text = "The clipboard is busy. Select the link and copy it manually."; } }; Controls.Add(button); return button;
        }
        private Task LoadDetails() { return Work(() => client.GetFilesystemShareAsync(path, key, cancellation.Token)); }
        private async Task Work(Func<Task<FilesystemShareInfo>> action, bool changing = false)
        {
            if (busy || cancellation.IsCancellationRequested) return; busy = true;
            if (changing) { info = null; shareLink.Clear(); hotlink.Clear(); }
            UpdateButtons(); feedback.Text = "Working…";
            try
            {
                FilesystemShareInfo result = await action(); if (IsDisposed || cancellation.IsCancellationRequested) return;
                info = result; shareLink.Text = info.ShareUrl; hotlink.Text = info.Hotlink;
                description.Text = !info.LinkRead ? "This item is private. Creating a read-only link lets anyone with the link view or download " + (info.Node.IsDirectory ? "this folder and its contents." : "this file.") : info.LinkWrite || info.LinkDelete ? "This link currently grants additional write or delete permissions. Use Make read-only to restrict it." : "Anyone with this link can view or download " + (info.Node.IsDirectory ? "this folder and its contents." : "this file.");
                if (info.ParentShared) description.Text += " Access is also granted by a shared parent folder.";
                feedback.Text = (info.Node.IsDirectory ? "The folder hotlink downloads a ZIP archive. " : "") + "Hotlink availability follows your PixelDrain account settings.";
                if (!info.CanManage) feedback.Text = "Your API key can copy available links but cannot change this item's sharing.";
            }
            catch (OperationCanceledException) { if (!IsDisposed && !cancellation.IsCancellationRequested) feedback.Text = "The request timed out. Close and reopen Share to try again."; }
            catch (Exception ex) { if (!IsDisposed && !cancellation.IsCancellationRequested) feedback.Text = ex.Message; }
            finally { busy = false; if (!IsDisposed) UpdateButtons(); }
        }
        private void UpdateButtons()
        {
            copyShare.Enabled = !busy && shareLink.TextLength > 0; copyHotlink.Enabled = !busy && hotlink.TextLength > 0;
            if (batch != null) { publish.Text = "Create missing links"; publish.Enabled = !busy && batchInfo.Count == batch.Count && batchInfo.Any(i => !i.LinkRead && i.CanManage); unshare.Enabled = false; return; }
            publish.Enabled = !busy && info != null && info.CanManage && (!info.OwnShared || !info.LinkRead || info.LinkWrite || info.LinkDelete);
            publish.Text = info != null && info.OwnShared ? "Make read-only" : "Create read-only link";
            unshare.Enabled = !busy && info != null && info.CanManage && info.OwnShared;
        }
        protected override void Dispose(bool disposing)
        { if (disposing && !disposed) { disposed = true; cancellation.Cancel(); cancellation.Dispose(); } base.Dispose(disposing); }
    }
}
