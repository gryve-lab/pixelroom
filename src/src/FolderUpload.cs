using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Pixelroom
{
    public sealed class UploadEntry
    {
        public readonly string LocalPath, RemotePath;
        public readonly bool IsDirectory;
        internal UploadEntry(string local, string remote, bool folder) { LocalPath = local; RemotePath = remote; IsDirectory = folder; }
    }
    public sealed class FolderUploadPlan
    {
        public readonly string Destination;
        public readonly IList<UploadEntry> Entries, Roots;
        public readonly IList<string> Replacements;
        private FolderUploadPlan(string destination, List<UploadEntry> entries, List<UploadEntry> roots, List<string> replacements)
        { Destination = destination; Entries = entries.AsReadOnly(); Roots = roots.AsReadOnly(); Replacements = replacements.AsReadOnly(); }

        public static FolderUploadPlan Build(IEnumerable<string> selection, string destination, IEnumerable<FilesystemNode> existing, CancellationToken token)
        {
            destination = FilesystemRules.Normalize(destination);
            List<string> selected = (selection ?? new string[0]).Select(Path.GetFullPath).Select(p => p.Length > Path.GetPathRoot(p).Length ? p.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) : p).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (selected.Count == 0) throw new IOException("Select files or folders to upload.");
            if (selected.Count > 50000) throw new IOException("Upload fewer than 50,000 files and folders at a time.");
            Dictionary<string, bool> folders = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            foreach (string local in selected)
            {
                token.ThrowIfCancellationRequested(); FileAttributes attributes = File.GetAttributes(local);
                if ((attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("Linked files and folders cannot be uploaded. Select the original items instead.");
                folders[local] = (attributes & FileAttributes.Directory) != 0;
            }
            selected = selected.Where(p => !selected.Any(parent => parent != p && folders[parent] && p.StartsWith(parent.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))).ToList();
            List<FilesystemNode> current = existing.ToList(); HashSet<string> names = new HashSet<string>(current.Select(n => n.Name), StringComparer.Ordinal);
            List<UploadEntry> entries = new List<UploadEntry>(), roots = new List<UploadEntry>(); List<string> replacements = new List<string>();
            foreach (string local in selected)
            {
                token.ThrowIfCancellationRequested(); string name = Path.GetFileName(local);
                if (String.IsNullOrEmpty(name)) throw new IOException("Select a folder inside a drive rather than the drive itself.");
                FilesystemRules.CheckName(name); bool isFolder = folders[local]; string chosen = name;
                if (isFolder || roots.Any(r => r.RemotePath == FilesystemRules.Child(destination, chosen))) chosen = AvailableName(name, isFolder, names);
                else
                {
                    FilesystemNode conflict = current.FirstOrDefault(n => n.Name == chosen);
                    if (conflict != null && conflict.IsDirectory) throw new IOException("A folder already uses the name \"" + chosen + "\".");
                    if (conflict != null) replacements.Add(FilesystemRules.Child(destination, chosen));
                }
                names.Add(chosen); UploadEntry root = new UploadEntry(local, FilesystemRules.Child(destination, chosen), isFolder); roots.Add(root);
                Queue<UploadEntry> pending = new Queue<UploadEntry>(); pending.Enqueue(root);
                while (pending.Count > 0)
                {
                    token.ThrowIfCancellationRequested(); UploadEntry entry = pending.Dequeue();
                    if (entries.Count >= 50000) throw new IOException("Upload fewer than 50,000 files and folders at a time.");
                    FileAttributes attributes = File.GetAttributes(entry.LocalPath);
                    if ((attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("Linked files and folders cannot be uploaded. Select the original items instead.");
                    if (((attributes & FileAttributes.Directory) != 0) != entry.IsDirectory) throw new IOException("A selected item changed. Select the folders again.");
                    entries.Add(entry);
                    if (entry.IsDirectory)
                    {
                        foreach (FileSystemInfo child in new DirectoryInfo(entry.LocalPath).EnumerateFileSystemInfos())
                        {
                            token.ThrowIfCancellationRequested();
                            if ((child.Attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("Linked files and folders cannot be uploaded. Select the original items instead.");
                            pending.Enqueue(new UploadEntry(child.FullName, FilesystemRules.Child(entry.RemotePath, child.Name), (child.Attributes & FileAttributes.Directory) != 0));
                            if (pending.Count + entries.Count > 50000) throw new IOException("Upload fewer than 50,000 files and folders at a time.");
                        }
                    }
                    else { using (FileStream probe = new FileStream(entry.LocalPath, FileMode.Open, FileAccess.Read, FileShare.Read)) { } }
                }
            }
            return new FolderUploadPlan(destination, entries, roots, replacements);
        }
        private static string AvailableName(string name, bool folder, HashSet<string> names)
        {
            if (!names.Contains(name)) return name;
            int dot = folder ? -1 : name.LastIndexOf('.'); string extension = dot > 0 && Encoding.UTF8.GetByteCount(name.Substring(dot)) <= 64 ? name.Substring(dot) : "";
            string stem = extension.Length == 0 ? name : name.Substring(0, name.Length - extension.Length);
            for (int i = 2; i <= 50001; i++)
            {
                string suffix = " (" + i + ")", prefix = stem;
                while (Encoding.UTF8.GetByteCount(prefix + suffix + extension) > 255) { prefix = prefix.Substring(0, prefix.Length - 1); if (prefix.Length > 0 && Char.IsHighSurrogate(prefix[prefix.Length - 1])) prefix = prefix.Substring(0, prefix.Length - 1); }
                string candidate = prefix + suffix + extension; if (!names.Contains(candidate)) return candidate;
            }
            throw new IOException("Choose another destination folder.");
        }
    }
    internal sealed class InlineTransferProgress : IProgress<TransferProgress>
    {
        private readonly Action<TransferProgress> report;
        public InlineTransferProgress(Action<TransferProgress> report) { this.report = report; }
        public void Report(TransferProgress value) { report(value); }
    }
    public sealed partial class PixelDrainClient
    {
        public async Task UploadFilesystemPlanAsync(FolderUploadPlan plan, bool replaceConfirmed, string key, IProgress<FilesystemCopyProgress> progress, CancellationToken token)
        {
            FilesystemDirectory latest = await ListFilesystemAsync(plan.Destination, key, token).ConfigureAwait(false);
            if (!latest.CanWrite) throw new IOException("You do not have permission to upload into this folder.");
            foreach (UploadEntry root in plan.Roots)
            {
                FilesystemNode conflict = latest.Children.FirstOrDefault(n => n.Path == root.RemotePath);
                if (conflict != null && (root.IsDirectory || conflict.IsDirectory || !replaceConfirmed || !plan.Replacements.Contains(root.RemotePath)))
                    throw new IOException("A destination name changed or needs replacement approval. Select the upload again.");
            }
            int completed = 0;
            foreach (UploadEntry entry in plan.Entries)
            {
                token.ThrowIfCancellationRequested();
                FileAttributes attributes = File.GetAttributes(entry.LocalPath);
                if ((attributes & FileAttributes.ReparsePoint) != 0 || ((attributes & FileAttributes.Directory) != 0) != entry.IsDirectory)
                    throw new IOException("A selected item changed. Select the upload again.");
                if (progress != null) progress.Report(new FilesystemCopyProgress { Phase = entry.IsDirectory ? "Creating folder" : "Uploading", Name = entry.RemotePath, Completed = completed, Total = plan.Entries.Count });
                if (entry.IsDirectory) await CreateFilesystemDirectoryAsync(entry.RemotePath, key, token).ConfigureAwait(false);
                else
                {
                    int count = completed;
                    await UploadFilesystemAsync(entry.LocalPath, entry.RemotePath, key, new InlineTransferProgress(p => { if (progress != null) progress.Report(new FilesystemCopyProgress { Phase = "Uploading", Name = entry.RemotePath, Completed = count, Total = plan.Entries.Count, Bytes = p.Done, BytesTotal = p.Total }); }), token).ConfigureAwait(false);
                }
                completed++;
            }
            if (progress != null) progress.Report(new FilesystemCopyProgress { Phase = "Complete", Name = plan.Destination, Completed = completed, Total = plan.Entries.Count });
        }
    }
}
