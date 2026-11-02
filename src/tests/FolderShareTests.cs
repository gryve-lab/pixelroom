using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Pixelroom;

internal static partial class Tests
{
    private static async Task CheckFoldersAndSharing()
    {
        string localRoot = Path.Combine(root, "folder-upload"), alpha = Path.Combine(localRoot, "Alpha"), beta = Path.Combine(localRoot, "Beta");
        Directory.CreateDirectory(Path.Combine(alpha, "nested")); Directory.CreateDirectory(Path.Combine(alpha, "empty")); Directory.CreateDirectory(beta);
        byte[] payload = Enumerable.Range(0, 200000).Select(i => (byte)(i % 251)).ToArray();
        File.WriteAllBytes(Path.Combine(alpha, "nested", "é #%.bin"), payload); File.WriteAllBytes(Path.Combine(beta, "zero.txt"), new byte[0]);
        ExplorerFixture fixture = new ExplorerFixture(); fixture.Folder("/me/Target"); fixture.Folder("/me/Target/Alpha");
        using (PixelDrainClient client = new PixelDrainClient(fixture))
        {
            FilesystemDirectory target = await client.ListFilesystemAsync("/me/Target", ExplorerFixture.Key, CancellationToken.None);
            FolderUploadPlan plan = FolderUploadPlan.Build(new string[] { alpha, beta, Path.Combine(alpha, "nested") }, "/me/Target", target.Children, CancellationToken.None);
            Assert(plan.Roots.Count == 2 && plan.Entries.Count == 6 && plan.Roots[0].RemotePath == "/me/Target/Alpha (2)", "Multi-folder selection must remove overlaps and preserve existing folders.");
            await client.UploadFilesystemPlanAsync(plan, false, ExplorerFixture.Key, null, CancellationToken.None);
        }
        Assert(fixture.Bytes["/me/Target/Alpha (2)/nested/é #%.bin"].SequenceEqual(payload) && fixture.Nodes.ContainsKey("/me/Target/Alpha (2)/empty") && fixture.Bytes["/me/Target/Beta/zero.txt"].Length == 0, "Multi-folder upload lost files, empty folders or Unicode names.");
        Assert(File.ReadAllBytes(Path.Combine(alpha, "nested", "é #%.bin")).SequenceEqual(payload), "Upload changed local files.");
        Pass("multiple folders preserve structure, empty folders, binary files and existing destinations");

        string first = Path.Combine(localRoot, "first"), second = Path.Combine(localRoot, "second"); Directory.CreateDirectory(first); Directory.CreateDirectory(second);
        File.WriteAllText(Path.Combine(first, "notes.txt"), "first"); File.WriteAllText(Path.Combine(second, "notes.txt"), "second");
        fixture = new ExplorerFixture(); fixture.File("/me/notes.txt", Encoding.UTF8.GetBytes("old"));
        using (PixelDrainClient client = new PixelDrainClient(fixture))
        {
            FilesystemDirectory target = await client.ListFilesystemAsync("/me", ExplorerFixture.Key, CancellationToken.None);
            FolderUploadPlan plan = FolderUploadPlan.Build(new string[] { Path.Combine(first, "notes.txt"), Path.Combine(second, "notes.txt") }, "/me", target.Children, CancellationToken.None);
            Assert(plan.Replacements.Count == 1 && plan.Roots[1].RemotePath == "/me/notes (2).txt", "Duplicate selected names must be kept separately.");
            bool blocked = false; try { await client.UploadFilesystemPlanAsync(plan, false, ExplorerFixture.Key, null, CancellationToken.None); } catch (IOException) { blocked = true; }
            Assert(blocked && fixture.Mutations.Count == 0 && Encoding.UTF8.GetString(fixture.Bytes["/me/notes.txt"]) == "old", "Unapproved replacements must not change any files.");
            await client.UploadFilesystemPlanAsync(plan, true, ExplorerFixture.Key, null, CancellationToken.None);
            Assert(Encoding.UTF8.GetString(fixture.Bytes["/me/notes.txt"]) == "first" && Encoding.UTF8.GetString(fixture.Bytes["/me/notes (2).txt"]) == "second", "Approved file replacement did not keep both selected files.");
        }
        Pass("folder batches require replacement approval and keep same-name local files separately");

        fixture = new ExplorerFixture();
        FolderUploadPlan stale = FolderUploadPlan.Build(new string[] { beta }, "/me", new FilesystemNode[0], CancellationToken.None); fixture.Folder("/me/Beta");
        using (PixelDrainClient client = new PixelDrainClient(fixture))
        { bool blocked = false; try { await client.UploadFilesystemPlanAsync(stale, false, ExplorerFixture.Key, null, CancellationToken.None); } catch (IOException) { blocked = true; } Assert(blocked && fixture.Mutations.Count == 0, "A new destination collision must abort before uploading."); }
        using (CancellationTokenSource cancel = new CancellationTokenSource())
        {
            cancel.Cancel(); bool cancelled = false; try { FolderUploadPlan.Build(new string[] { alpha }, "/me", new FilesystemNode[0], cancel.Token); } catch (OperationCanceledException) { cancelled = true; } Assert(cancelled, "Folder scanning must be cancellable.");
        }
        bool invalid = false; try { FolderUploadPlan.Build(new string[] { alpha, beta + ".missing" }, "/me", new FilesystemNode[0], CancellationToken.None); } catch (IOException) { invalid = true; } Assert(invalid, "Missing selections must fail during preflight.");
        Pass("folder upload preflight rejects stale destinations, missing items and cancellation");

        fixture = new ExplorerFixture(); fixture.Folder("/me/Private"); fixture.File("/me/Private/report.txt", payload);
        using (PixelDrainClient client = new PixelDrainClient(fixture))
        {
            FilesystemShareInfo info = await client.GetFilesystemShareAsync("/me/Private", ExplorerFixture.Key, CancellationToken.None);
            Assert(!info.LinkRead && !info.OwnShared && info.CanManage && fixture.Mutations.Count == 0, "Reading Share must not publish an item.");
            info = await client.SetFilesystemSharingAsync("/me/Private", true, ExplorerFixture.Key, CancellationToken.None);
            Assert(info.OwnShared && info.LinkRead && !info.LinkWrite && !info.LinkDelete && info.ShareUrl == "https://pixeldrain.com/d/shar0001" && info.Hotlink == "https://pixeldrain.com/api/filesystem/shar0001?bulk_download", "Folder links must use public IDs and ZIP download.");
            info = await client.SetFilesystemSharingAsync("/me/Private/report.txt", true, ExplorerFixture.Key, CancellationToken.None);
            Assert(info.ParentShared && info.OwnShared && info.Hotlink == "https://pixeldrain.com/api/filesystem/shar0002", "File hotlinks must not point at an authenticated private path.");
            info = await client.SetFilesystemSharingAsync("/me/Private/report.txt", false, ExplorerFixture.Key, CancellationToken.None);
            Assert(!info.OwnShared && info.ParentShared && info.ShareUrl == "https://pixeldrain.com/d/shar0001/report.txt", "Stopping an item share must still report inherited parent access.");
            info = await client.SetFilesystemSharingAsync("/me/Private", false, ExplorerFixture.Key, CancellationToken.None);
            Assert(!info.LinkRead && info.ShareUrl == "" && info.Hotlink == "", "Stopping a share must remove copied link candidates.");
            int requests = fixture.Requests.Count; bool blocked = false; try { await client.SetFilesystemSharingAsync("/me", true, ExplorerFixture.Key, CancellationToken.None); } catch (ArgumentException) { blocked = true; }
            Assert(blocked && requests == fixture.Requests.Count, "The account root must not be published.");
        }
        Pass("sharing creates read-only links, uses public IDs, stops sharing and reports parent access");

        fixture = new ExplorerFixture(); fixture.File("/me/locked.txt", payload); fixture.DenyOwner.Add("/me/locked.txt");
        using (PixelDrainClient client = new PixelDrainClient(fixture))
        {
            FilesystemShareInfo info = await client.GetFilesystemShareAsync("/me/locked.txt", ExplorerFixture.Key, CancellationToken.None); Assert(!info.CanManage, "Share controls must reflect server ownership permissions.");
            bool denied = false; try { await client.SetFilesystemSharingAsync("/me/locked.txt", true, ExplorerFixture.Key, CancellationToken.None); } catch (IOException) { denied = true; } Assert(denied && fixture.Mutations.Count == 0, "Unauthorized sharing was not rejected.");
        }
        Assert(ShareLinks.FileLink("abcd1234") == "https://pixeldrain.com/u/abcd1234" && ShareLinks.FileHotlink("abcd1234") == "https://pixeldrain.com/api/file/abcd1234", "My files links are incorrect.");
        Assert(ShareLinks.FilesystemLink("abcd1234/é #%.txt", true, false) == "https://pixeldrain.com/api/filesystem/abcd1234/%C3%A9%20%23%25.txt", "Shared path encoding is incorrect.");
        invalid = false; try { ShareLinks.FilesystemLink("abcd1234/../private", true, false); } catch (ArgumentException) { invalid = true; } Assert(invalid, "Shared links must reject traversal.");
        Pass("sharing respects account permissions and produces encoded links without API keys");
    }
}
