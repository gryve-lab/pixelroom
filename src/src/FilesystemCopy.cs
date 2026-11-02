using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Pixelroom
{
    public sealed class FilesystemCopyProgress
    {
        public string Name, Phase;
        public int Completed, Total;
        public long Bytes;
        public long BytesTotal = -1;
    }
    public static class FilesystemView
    {
        public static List<FilesystemNode> Filter(IEnumerable<FilesystemNode> nodes, string query)
        {
            query = (query ?? "").Trim();
            return nodes.Where(n => query.Length == 0 || n.Name.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
                .OrderByDescending(n => n.IsDirectory).ThenBy(n => n.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }
        public static string CopyName(string name, bool directory, IEnumerable<string> existing)
        {
            FilesystemRules.CheckName(name); HashSet<string> names = new HashSet<string>(existing, StringComparer.Ordinal);
            if (!names.Contains(name)) return name;
            int dot = directory ? -1 : name.LastIndexOf('.');
            string extension = dot > 0 && Encoding.UTF8.GetByteCount(name.Substring(dot)) <= 64 ? name.Substring(dot) : "";
            string stem = extension.Length == 0 ? name : name.Substring(0, name.Length - extension.Length);
            for (int i = 1; i <= 10000; i++)
            {
                string suffix = i == 1 ? " - Copy" : " - Copy (" + i + ")";
                string trimmed = stem;
                while (Encoding.UTF8.GetByteCount(trimmed + suffix + extension) > 255)
                { trimmed = trimmed.Substring(0, trimmed.Length - 1); if (trimmed.Length > 0 && Char.IsHighSurrogate(trimmed[trimmed.Length - 1])) trimmed = trimmed.Substring(0, trimmed.Length - 1); }
                string candidate = trimmed + suffix + extension; if (!names.Contains(candidate)) return candidate;
            }
            throw new IOException("Choose another destination or rename the copied item.");
        }
    }
    internal sealed class RelayContent : HttpContent
    {
        private readonly Stream input;
        private readonly long? length;
        private readonly CancellationToken token;
        private readonly Action<long> progress;
        public RelayContent(Stream input, long? length, CancellationToken token, Action<long> progress)
        { this.input = input; this.length = length; this.token = token; this.progress = progress; }
        protected override bool TryComputeLength(out long result) { result = length ?? 0; return length.HasValue; }
        protected override async Task SerializeToStreamAsync(Stream output, TransportContext context)
        {
            byte[] buffer = new byte[81920]; long done = 0; int read; DateTime last = DateTime.MinValue;
            while ((read = await input.ReadAsync(buffer, 0, buffer.Length, token).ConfigureAwait(false)) != 0)
            {
                token.ThrowIfCancellationRequested(); await output.WriteAsync(buffer, 0, read, token).ConfigureAwait(false); done += read;
                if (progress != null && ((DateTime.UtcNow - last).TotalMilliseconds >= 150 || done == length)) { progress(done); last = DateTime.UtcNow; }
            }
            if (length.HasValue && done != length.Value) throw new IOException("The source file changed or its transfer was interrupted. Refresh and try copying again.");
        }
    }
    public sealed partial class PixelDrainClient
    {
        public async Task CopyFilesystemAsync(FilesystemNode source, string destination, string key, IProgress<FilesystemCopyProgress> progress, CancellationToken token)
        {
            if (source == null) throw new ArgumentNullException("source");
            string from = FilesystemRules.Normalize(source.Path); destination = FilesystemRules.Normalize(destination);
            source = new FilesystemNode { Path = from, Name = source.Name, Type = source.Type, Size = source.Size };
            FilesystemRules.NotRoot(from); FilesystemRules.NotRoot(destination);
            if (destination == from || destination.StartsWith(from + "/", StringComparison.Ordinal)) throw new ArgumentException("Paste outside the copied folder.");
            if (source.Type != "dir" && source.Type != "file") throw new ArgumentException("Select a file or folder to copy.");
            token.ThrowIfCancellationRequested();
            FilesystemDirectory parent = await ListFilesystemAsync(FilesystemRules.Parent(destination), key, token).ConfigureAwait(false);
            if (!parent.CanWrite) throw new IOException("You do not have permission to paste into this folder.");
            if (parent.Children.Any(n => n.Path == destination)) throw new IOException("The destination already exists. Paste again to use a different name.");
            List<FilesystemNode> plan = new List<FilesystemNode> { source };
            for (int i = 0; i < plan.Count; i++)
            {
                token.ThrowIfCancellationRequested(); FilesystemNode node = plan[i];
                FilesystemRules.Normalize(destination + node.Path.Substring(from.Length));
                if (!node.IsDirectory) continue;
                if (progress != null) progress.Report(new FilesystemCopyProgress { Phase = "Reading", Name = node.Name, Total = plan.Count });
                FilesystemDirectory folder = await ListFilesystemAsync(node.Path, key, token).ConfigureAwait(false);
                if (!folder.CanRead) throw new IOException("You do not have permission to read every folder in this copy.");
                if (plan.Count + folder.Children.Count > 50000) throw new IOException("Select a smaller folder with fewer than 50,000 items per copy.");
                plan.AddRange(folder.Children);
            }
            int completed = 0;
            foreach (FilesystemNode node in plan)
            {
                token.ThrowIfCancellationRequested(); string target = destination + node.Path.Substring(from.Length);
                if (progress != null) progress.Report(new FilesystemCopyProgress { Phase = "Copying", Name = node.Name, Completed = completed, Total = plan.Count });
                if (node.IsDirectory) await CreateFilesystemDirectoryAsync(target, key, token).ConfigureAwait(false);
                else
                {
                    int count = completed;
                    await CopyFilesystemFileAsync(node.Path, target, key, token, p => { if (progress != null) progress.Report(new FilesystemCopyProgress { Phase = "Copying", Name = node.Path, Completed = count, Total = plan.Count, Bytes = p.Done, BytesTotal = p.Total }); }).ConfigureAwait(false);
                }
                completed++;
            }
            if (progress != null) progress.Report(new FilesystemCopyProgress { Phase = "Complete", Name = source.Name, Completed = completed, Total = plan.Count });
        }
        private async Task CopyFilesystemFileAsync(string source, string destination, string key, CancellationToken token, Action<TransferProgress> progress)
        {
            using (HttpRequestMessage read = Request(HttpMethod.Get, FilesystemRules.Route(source) + "?attach", key))
            using (HttpResponseMessage response = await http.SendAsync(read, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false))
            {
                if (!response.IsSuccessStatusCode) { await ReadJson(response).ConfigureAwait(false); throw new IOException("The source file could not be copied."); }
                using (Stream input = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                using (HttpRequestMessage write = Request(HttpMethod.Put, FilesystemRules.Route(destination), key))
                {
                    long total = response.Content.Headers.ContentLength ?? -1;
                    if (progress != null) progress(new TransferProgress { Total = total });
                    write.Content = new RelayContent(input, response.Content.Headers.ContentLength, token, bytes => { if (progress != null) progress(new TransferProgress { Done = bytes, Total = total }); });
                    write.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
                    using (HttpResponseMessage result = await http.SendAsync(write, token).ConfigureAwait(false)) await ReadJson(result).ConfigureAwait(false);
                }
            }
        }
    }
}
