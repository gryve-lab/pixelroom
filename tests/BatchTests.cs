using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Pixelroom;

internal static partial class Tests
{
    private static async Task CheckBatchOperations()
    {
        ExplorerFixture fixture = new ExplorerFixture(); fixture.Folder("/me/Source"); fixture.Folder("/me/Source/Folder"); fixture.File("/me/Source/Folder/nested.txt", Encoding.UTF8.GetBytes("nested")); fixture.File("/me/Source/first.txt", Encoding.UTF8.GetBytes("first")); fixture.File("/me/Source/second.txt", Encoding.UTF8.GetBytes("second")); fixture.Folder("/me/Target");
        var selected = new[] { fixture.Nodes["/me/Source/Folder"], fixture.Nodes["/me/Source/Folder/nested.txt"], fixture.Nodes["/me/Source/first.txt"], fixture.Nodes["/me/Source/first.txt"] };
        Assert(FilesystemBatch.Selection(selected).Count == 2, "Selections must remove duplicates and descendants of a selected parent.");
        using (PixelDrainClient client = new PixelDrainClient(fixture))
        {
            await client.CopyFilesystemBatchAsync(selected, "/me/Target", ExplorerFixture.Key, null, CancellationToken.None);
            Assert(fixture.Bytes.ContainsKey("/me/Target/Folder/nested.txt") && fixture.Bytes.ContainsKey("/me/Target/first.txt") && !fixture.Bytes.ContainsKey("/me/Target/nested.txt"), "Batch copy must include exactly the selected roots.");
            await client.CopyFilesystemBatchAsync(selected, "/me/Target", ExplorerFixture.Key, null, CancellationToken.None);
            Assert(fixture.Bytes.ContainsKey("/me/Target/Folder - Copy/nested.txt") && fixture.Bytes.ContainsKey("/me/Target/first - Copy.txt") && fixture.Bytes.ContainsKey("/me/Source/first.txt"), "Repeated batch paste must preserve all sources and existing destinations.");
        }
        Pass("multi-item copy normalizes marked roots and safely repeats folder/file paste");

        int before = fixture.Mutations.Count;
        using (PixelDrainClient client = new PixelDrainClient(fixture))
        {
            bool blocked = false; try { await client.CopyFilesystemBatchAsync(selected, "/me/Source/Folder", ExplorerFixture.Key, null, CancellationToken.None); } catch (IOException) { blocked = true; }
            Assert(blocked && fixture.Mutations.Count == before, "A bad destination must block the complete copy batch before mutations.");
            blocked = false; try { await client.MoveFilesystemBatchAsync(selected, "/me/Target", ExplorerFixture.Key, null, CancellationToken.None); } catch (IOException) { blocked = true; }
            Assert(blocked && fixture.Mutations.Count == before, "A colliding move batch must fail before moving any selected item.");
            using (CancellationTokenSource cancel = new CancellationTokenSource()) { cancel.Cancel(); blocked = false; try { await client.CopyFilesystemBatchAsync(selected, "/me/Target", ExplorerFixture.Key, null, cancel.Token); } catch (OperationCanceledException) { blocked = true; } Assert(blocked && fixture.Mutations.Count == before, "Cancelled batch must not mutate files."); }
        }
        Pass("batch preflight rejects self-copy, move collisions and cancellation without partial mutations");

        fixture.Folder("/me/Moved");
        using (PixelDrainClient client = new PixelDrainClient(fixture)) await client.MoveFilesystemBatchAsync(selected, "/me/Moved", ExplorerFixture.Key, null, CancellationToken.None);
        Assert(fixture.Bytes.ContainsKey("/me/Moved/Folder/nested.txt") && fixture.Bytes.ContainsKey("/me/Moved/first.txt") && !fixture.Nodes.ContainsKey("/me/Source/Folder") && fixture.Bytes.ContainsKey("/me/Source/second.txt"), "Multi-item move must relocate selected trees while keeping unselected files.");
        Pass("multi-item moves preserve directory structure and leave unmarked items intact");

        string folder = Path.Combine(root, "batch-download"); Directory.CreateDirectory(folder); File.WriteAllText(Path.Combine(folder, "first.txt"), "keep");
        var download = new[] { fixture.Nodes["/me/Moved/first.txt"], fixture.Nodes["/me/Moved/Folder"] };
        var targets = FilesystemBatch.DownloadTargets(download, folder);
        Assert(targets[0].EndsWith("first (2).txt") && targets[1].EndsWith("Folder.zip"), "Batch downloads must avoid existing local names and save directories as ZIPs.");
        using (PixelDrainClient client = new PixelDrainClient(fixture))
        {
            for (int i = 0; i < download.Length; i++) await client.DownloadFilesystemAsync(download[i].Path, download[i].IsDirectory, targets[i], ExplorerFixture.Key, null, CancellationToken.None, false);
            Assert(File.ReadAllText(targets[0]) == "first" && File.ReadAllText(targets[1]).Contains("ZIP fixture"), "Marked files and folders did not download to their allocated names.");
            bool blocked = false; try { await client.DownloadFilesystemAsync(download[0].Path, false, Path.Combine(folder, "first.txt"), ExplorerFixture.Key, null, CancellationToken.None, false); } catch (IOException) { blocked = true; }
            Assert(blocked && File.ReadAllText(Path.Combine(folder, "first.txt")) == "keep" && Directory.GetFiles(folder, "*.part").Length == 0, "A batch download must not overwrite a file created after name allocation.");
        }
        Pass("multi-download names preserve existing files and use atomic no-replace writes");
    }
}
