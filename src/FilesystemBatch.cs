using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Pixelroom
{
    public sealed class FilesystemBatchEntry
    {
        public FilesystemNode Source;
        public string Target;
    }
    public static class FilesystemBatch
    {
        public static List<FilesystemNode> Selection(IEnumerable<FilesystemNode> selection)
        {
            List<FilesystemNode> items = selection.GroupBy(n => FilesystemRules.Normalize(n.Path), StringComparer.Ordinal).Select(g => g.First()).ToList();
            if (items.Count == 0 || items.Count > 50000) throw new IOException("Select between 1 and 50,000 items.");
            foreach (FilesystemNode node in items) { FilesystemRules.NotRoot(node.Path); FilesystemRules.CheckName(node.Name); if (node.Type != "file" && node.Type != "dir") throw new IOException("Select files or folders."); }
            HashSet<string> folders = new HashSet<string>(items.Where(n => n.IsDirectory).Select(n => FilesystemRules.Normalize(n.Path)), StringComparer.Ordinal);
            return items.Where(n => { string parent = FilesystemRules.Parent(n.Path); while (parent != "/me") { if (folders.Contains(parent)) return false; parent = FilesystemRules.Parent(parent); } return true; }).ToList();
        }
        public static List<FilesystemBatchEntry> Plan(IEnumerable<FilesystemNode> selected, string destination, IEnumerable<FilesystemNode> existing, bool copy)
        {
            destination = FilesystemRules.Normalize(destination); List<FilesystemNode> items = Selection(selected);
            HashSet<string> names = new HashSet<string>(existing.Select(n => n.Name), StringComparer.Ordinal); List<FilesystemBatchEntry> plan = new List<FilesystemBatchEntry>();
            foreach (FilesystemNode node in items)
            {
                if (node.IsDirectory && (destination == node.Path || destination.StartsWith(node.Path + "/", StringComparison.Ordinal))) throw new IOException("Choose a destination outside every selected folder.");
                if (!copy && FilesystemRules.Parent(node.Path) == destination) throw new IOException("Choose a different destination folder.");
                string name = copy ? FilesystemView.CopyName(node.Name, node.IsDirectory, names) : node.Name;
                if (!copy && names.Contains(name)) throw new IOException("The destination already contains \"" + name + "\". Rename it or choose another folder; existing items will not be replaced.");
                names.Add(name); plan.Add(new FilesystemBatchEntry { Source = node, Target = FilesystemRules.Child(destination, name) });
            }
            return plan;
        }
        public static List<string> DownloadTargets(IEnumerable<FilesystemNode> selected, string folder)
        {
            folder = Path.GetFullPath(folder); HashSet<string> names = new HashSet<string>(Directory.EnumerateFileSystemEntries(folder).Select(Path.GetFileName), StringComparer.OrdinalIgnoreCase); List<string> paths = new List<string>();
            foreach (FilesystemNode node in Selection(selected))
            {
                string name = FileRules.SafeName(node.Name + (node.IsDirectory ? ".zip" : "")), stem = Path.GetFileNameWithoutExtension(name), extension = Path.GetExtension(name), candidate = name;
                for (int suffix = 2; names.Contains(candidate); suffix++) candidate = stem + " (" + suffix + ")" + extension;
                names.Add(candidate); paths.Add(Path.Combine(folder, candidate));
            }
            return paths;
        }
    }
    internal sealed class InlineFilesystemProgress : IProgress<FilesystemCopyProgress>
    {
        private readonly Action<FilesystemCopyProgress> report;
        public InlineFilesystemProgress(Action<FilesystemCopyProgress> report) { this.report = report; }
        public void Report(FilesystemCopyProgress value) { report(value); }
    }
    public sealed partial class PixelDrainClient
    {
        public async Task CopyFilesystemBatchAsync(IEnumerable<FilesystemNode> selection, string destination, string key, IProgress<FilesystemCopyProgress> progress, CancellationToken token)
        {
            FilesystemDirectory folder = await ListFilesystemAsync(destination, key, token).ConfigureAwait(false);
            if (!folder.CanWrite) throw new IOException("You cannot paste into this folder.");
            List<FilesystemBatchEntry> plan = FilesystemBatch.Plan(selection, destination, folder.Children, true);
            for (int index = 0; index < plan.Count; index++)
            {
                token.ThrowIfCancellationRequested(); int current = index; FilesystemBatchEntry entry = plan[index];
                var relay = new InlineFilesystemProgress(p => { if (progress != null) progress.Report(new FilesystemCopyProgress { Phase = p.Phase, Name = p.Name, Completed = current, Total = plan.Count, Bytes = p.Bytes, BytesTotal = p.BytesTotal }); });
                await CopyFilesystemAsync(entry.Source, entry.Target, key, relay, token).ConfigureAwait(false);
            }
        }
        public async Task MoveFilesystemBatchAsync(IEnumerable<FilesystemNode> selection, string destination, string key, IProgress<FilesystemCopyProgress> progress, CancellationToken token)
        {
            FilesystemDirectory folder = await ListFilesystemAsync(destination, key, token).ConfigureAwait(false);
            if (!folder.CanWrite) throw new IOException("You cannot move items into this folder.");
            List<FilesystemBatchEntry> plan = FilesystemBatch.Plan(selection, destination, folder.Children, false);
            for (int index = 0; index < plan.Count; index++)
            {
                token.ThrowIfCancellationRequested(); FilesystemBatchEntry entry = plan[index];
                if (progress != null) progress.Report(new FilesystemCopyProgress { Phase = "Moving", Name = entry.Source.Name, Completed = index, Total = plan.Count });
                await RenameFilesystemAsync(entry.Source.Path, entry.Target, key, token).ConfigureAwait(false);
            }
        }
    }
}
