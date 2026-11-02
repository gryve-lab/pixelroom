using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Pixelroom;

internal static partial class Tests
{
    private sealed class CopyProgress : IProgress<FilesystemCopyProgress>
    { public Action<FilesystemCopyProgress> Action; public void Report(FilesystemCopyProgress progress) { Action(progress); } }
    private static async Task CheckExplorer()
    {
        ExplorerFixture fixture = new ExplorerFixture(); fixture.Folder("/me/Source"); fixture.Folder("/me/Source/nested"); fixture.Folder("/me/Source/empty"); fixture.Folder("/me/Target");
        byte[] payload = Enumerable.Range(0, 300000).Select(i => (byte)(i % 251)).ToArray(); fixture.File("/me/Source/nested/é #%.bin", payload); fixture.File("/me/Source/zero.txt", new byte[0]);
        int completed = 0;
        using (PixelDrainClient client = new PixelDrainClient(fixture))
            await client.CopyFilesystemAsync(fixture.Nodes["/me/Source"], "/me/Target/Source", ExplorerFixture.Key, new CopyProgress { Action = p => completed = Math.Max(completed, p.Completed) }, CancellationToken.None);
        Assert(fixture.Bytes["/me/Target/Source/nested/é #%.bin"].SequenceEqual(payload) && fixture.Bytes["/me/Source/nested/é #%.bin"].SequenceEqual(payload), "Recursive copy changed source bytes or truncated the destination.");
        Assert(fixture.Nodes.ContainsKey("/me/Target/Source/empty") && fixture.Bytes["/me/Target/Source/zero.txt"].Length == 0 && completed == 5, "Copy lost empty folders/files or completion progress.");
        Assert(fixture.Mutations.All(r => r.StartsWith("POST ") || r.StartsWith("PUT ")), "Copy must never delete or move source items.");
        Pass("recursive copies preserve binary bytes, Unicode names, empty folders and source items");

        fixture = new ExplorerFixture(); fixture.Folder("/me/Source"); fixture.Folder("/me/Target"); fixture.Folder("/me/Target/Source");
        using (PixelDrainClient client = new PixelDrainClient(fixture))
        {
            bool rejected = false; try { await client.CopyFilesystemAsync(fixture.Nodes["/me/Source"], "/me/Target/Source", ExplorerFixture.Key, null, CancellationToken.None); } catch (IOException) { rejected = true; }
            Assert(rejected && fixture.Mutations.Count == 0, "Copy overwrote an existing destination."); int requests = fixture.Requests.Count;
            foreach (string target in new string[] { "/me/Source", "/me/Source/inside", "/me" })
            { rejected = false; try { await client.CopyFilesystemAsync(fixture.Nodes["/me/Source"], target, ExplorerFixture.Key, null, CancellationToken.None); } catch (ArgumentException) { rejected = true; } Assert(rejected && fixture.Requests.Count == requests, "Unsafe copy issued a request."); }
        }
        Pass("copy rejects existing destinations, root replacement and copying into itself");

        fixture = new ExplorerFixture(); fixture.Folder("/me/Source"); fixture.Folder("/me/Source/private"); fixture.Folder("/me/Target"); fixture.DenyRead.Add("/me/Source/private");
        using (PixelDrainClient client = new PixelDrainClient(fixture))
        { bool rejected = false; try { await client.CopyFilesystemAsync(fixture.Nodes["/me/Source"], "/me/Target/Copy", ExplorerFixture.Key, null, CancellationToken.None); } catch (IOException) { rejected = true; } Assert(rejected && fixture.Mutations.Count == 0, "Read permission failure must be found before creating the copy."); }
        Pass("copy checks the full folder tree before modifying the destination");

        fixture = new ExplorerFixture(); fixture.Folder("/me/Target"); fixture.File("/me/source.bin", payload); fixture.Truncate = true;
        using (PixelDrainClient client = new PixelDrainClient(fixture))
        { bool failed = false; try { await client.CopyFilesystemAsync(fixture.Nodes["/me/source.bin"], "/me/Target/copy.bin", ExplorerFixture.Key, null, CancellationToken.None); } catch (Exception) { failed = true; } Assert(failed && !fixture.Bytes.ContainsKey("/me/Target/copy.bin") && fixture.Bytes["/me/source.bin"].SequenceEqual(payload), "An interrupted source must not become a successful copy."); }
        fixture = new ExplorerFixture(); fixture.Folder("/me/Target"); fixture.File("/me/source.bin", payload);
        using (CancellationTokenSource cancel = new CancellationTokenSource())
        using (PixelDrainClient client = new PixelDrainClient(fixture))
        {
            bool failed = false; try { await client.CopyFilesystemAsync(fixture.Nodes["/me/source.bin"], "/me/Target/copy.bin", ExplorerFixture.Key, new CopyProgress { Action = p => { if (p.Bytes > 0) cancel.Cancel(); } }, cancel.Token); } catch (Exception) { failed = true; }
            Assert(failed && cancel.IsCancellationRequested && fixture.Bytes["/me/source.bin"].SequenceEqual(payload), "Copy cancellation must preserve the source.");
        }
        Pass("interrupted and cancelled copies never modify their source");

        string unicode = new String('é', 125) + ".txt";
        string renamed = FilesystemView.CopyName(unicode, false, new string[] { unicode });
        Assert(Encoding.UTF8.GetByteCount(renamed) <= 255 && renamed.EndsWith(" - Copy.txt"), "Copy suffix exceeds the API name limit.");
        Assert(FilesystemView.CopyName("file.txt", false, new string[] { "file.txt", "file - Copy.txt" }) == "file - Copy (2).txt", "Repeated copy must choose another name.");
        Assert(FilesystemView.CopyName("source", true, new string[0]) == "source", "Copies to another folder should preserve the name.");
        var items = new FilesystemNode[] { new FilesystemNode { Name = "Report.PDF", Type = "file" }, new FilesystemNode { Name = "reports", Type = "dir" }, new FilesystemNode { Name = "image.png", Type = "file" } };
        Assert(FilesystemView.Filter(items, " REPORT ").Count == 2 && FilesystemView.Filter(items, "report")[0].IsDirectory && FilesystemView.Filter(items, "missing").Count == 0 && items.Length == 3, "Search must be case insensitive, folder first and leave the source listing intact.");
        Pass("search is case insensitive and repeated copy names preserve extensions and Unicode limits");

        string dropped = Path.Combine(root, "dropped.txt"); File.WriteAllText(dropped, "test");
        Assert(UploadSelection.Files(new string[] { dropped, Path.Combine(root, ".", "dropped.txt") }).Length == 1, "Drop selection must normalize duplicates.");
        bool invalid = false; try { UploadSelection.Files(new string[] { root }); } catch (IOException) { invalid = true; } Assert(invalid, "A folder must not be silently ignored as an uploaded file.");
        invalid = false; try { UploadSelection.Files(new string[] { dropped, dropped + ".missing" }); } catch (IOException) { invalid = true; } Assert(invalid, "A missing dropped file must be reported before uploading.");
        Pass("drag-and-drop validates complete file selections before uploading");
    }
}
