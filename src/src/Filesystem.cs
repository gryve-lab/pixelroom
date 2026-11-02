using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Pixelroom
{
    public sealed class FilesystemNode
    {
        public string Path, Name, Type, Mime, Id;
        public long Size;
        public DateTime Modified, Created;
        public bool OwnLinkReadable, InheritedShared;
        public bool IsDirectory { get { return Type == "dir"; } }
        public bool IsShared { get { return OwnLinkReadable || InheritedShared; } }
        public string SharingText { get { return OwnLinkReadable ? "Shared" : InheritedShared ? "Shared via parent" : "Not shared"; } }
    }
    public sealed class FilesystemDirectory
    {
        public FilesystemNode Current;
        public List<FilesystemNode> Children = new List<FilesystemNode>();
        public bool CanRead, CanWrite, CanDelete;
    }
    public static class FilesystemRules
    {
        public static string Normalize(string path)
        {
            path = "/" + (path ?? "").Trim('/');
            string[] parts = path.Substring(1).Split('/');
            if (parts.Length > 65 || parts[0] != "me" || Encoding.UTF8.GetByteCount(path) > 4095) throw new ArgumentException("Use a path inside /me.");
            foreach (string part in parts) CheckName(part);
            return path;
        }
        public static void CheckName(string name)
        {
            if (String.IsNullOrEmpty(name) || name == "." || name == ".." || name.IndexOfAny(new char[] { '/', '\\', '\0' }) >= 0 || Encoding.UTF8.GetByteCount(name) > 255)
                throw new ArgumentException("Enter a valid name of up to 255 bytes, without slashes.");
        }
        public static string Child(string parent, string name) { CheckName(name); return Normalize(Normalize(parent) + "/" + name); }
        public static string Parent(string path) { path = Normalize(path); return path == "/me" ? path : path.Substring(0, path.LastIndexOf('/')); }
        public static string Route(string path) { return "/filesystem/" + String.Join("/", Normalize(path).Substring(1).Split('/').Select(Uri.EscapeDataString)); }
        public static void NotRoot(string path) { if (Normalize(path) == "/me") throw new ArgumentException("The File System root cannot be replaced, renamed, or deleted."); }
    }
    public sealed partial class PixelDrainClient
    {
        private static FilesystemNode ParseFilesystemNode(Dictionary<string, object> data)
        {
            string type = Json.Text(data, "type"), path = FilesystemRules.Normalize(Json.Text(data, "path"));
            if (type != "file" && type != "dir") throw new IOException("PixelDrain returned an unsupported File System item.");
            long size; DateTime modified, created; Int64.TryParse(Json.Text(data, "file_size"), NumberStyles.Integer, CultureInfo.InvariantCulture, out size);
            DateTime.TryParse(Json.Text(data, "modified"), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out modified);
            DateTime.TryParse(Json.Text(data, "created"), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out created);
            string id = Json.Text(data, "id"); if (id.Length > 0 && id != "me") FileRules.CheckId(id);
            return new FilesystemNode { Path = path, Name = Json.Text(data, "name", path.Substring(path.LastIndexOf('/') + 1)), Type = type, Mime = Json.Text(data, "file_type"), Id = id, Size = Math.Max(0, size), Modified = modified, Created = created, OwnLinkReadable = LinkPermission(data, "read") };
        }
        private async Task<Dictionary<string, object>> FilesystemRequestAsync(HttpMethod method, string route, string key, Dictionary<string, string> fields, CancellationToken token)
        {
            using (HttpRequestMessage request = Request(method, route, key))
            using (CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                timeout.CancelAfter(TimeSpan.FromSeconds(45));
                if (fields != null)
                {
                    MultipartFormDataContent form = new MultipartFormDataContent();
                    foreach (KeyValuePair<string, string> field in fields) form.Add(new StringContent(field.Value, Encoding.UTF8), field.Key);
                    request.Content = form;
                }
                using (HttpResponseMessage response = await http.SendAsync(request, timeout.Token).ConfigureAwait(false))
                    return response.StatusCode == HttpStatusCode.NoContent ? new Dictionary<string, object>() : await ReadJson(response).ConfigureAwait(false);
            }
        }
        public async Task<FilesystemDirectory> ListFilesystemAsync(string path, string key, CancellationToken token)
        {
            path = FilesystemRules.Normalize(path);
            Dictionary<string, object> data = await FilesystemRequestAsync(HttpMethod.Get, FilesystemRules.Route(path) + "?stat", key, null, token).ConfigureAwait(false);
            object raw; List<Dictionary<string, object>> nodes = new List<Dictionary<string, object>>();
            if (data.TryGetValue("path", out raw) && raw is IEnumerable)
                foreach (object value in (IEnumerable)raw) { Dictionary<string, object> item = value as Dictionary<string, object>; if (item != null) nodes.Add(item); }
            int index; if (!Int32.TryParse(Json.Text(data, "base_index"), out index) || index < 0 || index >= nodes.Count) throw new IOException("The PixelDrain folder response is incomplete.");
            FilesystemDirectory result = new FilesystemDirectory { Current = ParseFilesystemNode(nodes[index]) };
            if (!result.Current.IsDirectory || result.Current.Path != path) throw new IOException("The selected path is not a folder.");
            for (int ancestorIndex = 0; ancestorIndex < index; ancestorIndex++)
            {
                string ancestor = Json.Text(nodes[ancestorIndex], "path"); if (ancestor == "/") continue;
                ancestor = FilesystemRules.Normalize(ancestor);
                if (!path.StartsWith(ancestor + "/", StringComparison.Ordinal)) throw new IOException("PixelDrain returned an invalid parent folder.");
                FilesystemNode parent = ParseFilesystemNode(nodes[ancestorIndex]);
                if (parent.OwnLinkReadable) result.Current.InheritedShared = true;
            }
            if (!data.TryGetValue("children", out raw) || !(raw is IEnumerable)) throw new IOException("The PixelDrain folder listing is unavailable.");
            foreach (object value in (IEnumerable)raw)
            {
                Dictionary<string, object> item = value as Dictionary<string, object>; if (item == null) continue;
                FilesystemNode node = ParseFilesystemNode(item); if (FilesystemRules.Parent(node.Path) != path || node.Path == path) throw new IOException("PixelDrain returned an item outside the selected folder.");
                node.InheritedShared = result.Current.IsShared;
                result.Children.Add(node);
            }
            if (data.TryGetValue("permissions", out raw))
            {
                Dictionary<string, object> permissions = raw as Dictionary<string, object>;
                if (permissions != null) { result.CanRead = Json.Text(permissions, "read").Equals("true", StringComparison.OrdinalIgnoreCase); result.CanWrite = Json.Text(permissions, "write").Equals("true", StringComparison.OrdinalIgnoreCase); result.CanDelete = Json.Text(permissions, "delete").Equals("true", StringComparison.OrdinalIgnoreCase); }
            }
            return result;
        }
        public Task CreateFilesystemDirectoryAsync(string path, string key, CancellationToken token)
        { FilesystemRules.NotRoot(path); return FilesystemRequestAsync(HttpMethod.Post, FilesystemRules.Route(path), key, new Dictionary<string, string> { { "action", "mkdir" } }, token); }
        public Task RenameFilesystemAsync(string source, string destination, string key, CancellationToken token)
        {
            source = FilesystemRules.Normalize(source); destination = FilesystemRules.Normalize(destination); FilesystemRules.NotRoot(source); FilesystemRules.NotRoot(destination);
            if (source == destination || destination.StartsWith(source + "/", StringComparison.Ordinal)) throw new ArgumentException("Choose a different destination outside the selected item.");
            return FilesystemRequestAsync(HttpMethod.Post, FilesystemRules.Route(source), key, new Dictionary<string, string> { { "action", "rename" }, { "target", destination } }, token);
        }
        public Task DeleteFilesystemAsync(string path, bool recursive, string key, CancellationToken token)
        { FilesystemRules.NotRoot(path); return FilesystemRequestAsync(HttpMethod.Delete, FilesystemRules.Route(path) + (recursive ? "?recursive" : ""), key, null, token); }
        public async Task UploadFilesystemAsync(string localPath, string destination, string key, IProgress<TransferProgress> progress, CancellationToken token)
        {
            FilesystemRules.NotRoot(destination);
            using (HttpRequestMessage request = Request(HttpMethod.Put, FilesystemRules.Route(destination), key))
            {
                request.Content = new ProgressFileContent(localPath, progress, token);
                using (HttpResponseMessage response = await http.SendAsync(request, token).ConfigureAwait(false)) await ReadJson(response).ConfigureAwait(false);
            }
        }
        public Task DownloadFilesystemAsync(string path, bool directory, string target, string key, IProgress<TransferProgress> progress, CancellationToken token, bool overwrite = true)
        { return DownloadToFileAsync(FilesystemRules.Route(path) + (directory ? "?bulk_download" : "?attach"), target, key, progress, token, overwrite); }
        public Task ImportFilesystemAsync(string folder, IList<string> ids, string key, CancellationToken token)
        {
            if (ids.Count < 1 || ids.Count > 100) throw new ArgumentException("Enter between 1 and 100 file IDs.");
            foreach (string id in ids) FileRules.CheckId(id);
            return FilesystemRequestAsync(HttpMethod.Post, FilesystemRules.Route(folder), key, new Dictionary<string, string> { { "action", "import" }, { "files", Json.NewSerializer().Serialize(ids) } }, token);
        }
    }
}
