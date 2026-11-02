using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace Pixelroom
{
    public sealed class CloudFile
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public long Size { get; set; }
        public string Mime { get; set; }
        public DateTime Uploaded { get; set; }
        public string Kind
        {
            get
            {
                string name = (Name ?? "").ToLowerInvariant(), mime = Mime ?? "";
                if (mime.StartsWith("image/") || Regex.IsMatch(name, @"\.(png|jpe?g|gif|webp|svg|heic|avif)$")) return "Image";
                if (mime.StartsWith("video/") || Regex.IsMatch(name, @"\.(mp4|mov|mkv|webm)$")) return "Video";
                if (mime.StartsWith("audio/") || Regex.IsMatch(name, @"\.(mp3|wav|flac|ogg)$")) return "Audio";
                if (mime.StartsWith("text/") || Regex.IsMatch(name, @"\.(pdf|docx?|xlsx?|pptx?|csv|txt|md|odt)$")) return "Document";
                return "Other";
            }
        }
    }

    public sealed class Collection
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public List<string> Files { get; set; }
        public Collection() { Id = Guid.NewGuid().ToString("N"); Name = ""; Files = new List<string>(); }
        public override string ToString() { return Name; }
    }

    public sealed class LocalData
    {
        public List<string> Favorites { get; set; }
        public List<Collection> Collections { get; set; }
        public Dictionary<string, string> Aliases { get; set; }
        public Dictionary<string, DateTime> Recent { get; set; }
        public LocalData()
        {
            Favorites = new List<string>(); Collections = new List<Collection>();
            Aliases = new Dictionary<string, string>(); Recent = new Dictionary<string, DateTime>();
        }
        public void Normalize()
        {
            Favorites = Favorites ?? new List<string>(); Collections = Collections ?? new List<Collection>();
            Aliases = Aliases ?? new Dictionary<string, string>(); Recent = Recent ?? new Dictionary<string, DateTime>();
            Collections = Collections.Where(c => c != null).ToList();
            foreach (Collection c in Collections) { c.Files = c.Files ?? new List<string>(); if (String.IsNullOrEmpty(c.Id)) c.Id = Guid.NewGuid().ToString("N"); }
        }
        public string DisplayName(CloudFile file) { string alias; return Aliases.TryGetValue(file.Id, out alias) ? alias : file.Name; }
        public void Remove(string id)
        {
            Favorites.RemoveAll(x => x == id); Aliases.Remove(id); Recent.Remove(id);
            foreach (Collection c in Collections) c.Files.RemoveAll(x => x == id);
        }
    }

    public static class FileRules
    {
        public static string AccountScope(string key)
        {
            using (SHA256 sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(key))).Replace("-", "").ToLowerInvariant();
        }
        public static void CheckId(string id)
        {
            if (!Regex.IsMatch(id ?? "", @"^[a-zA-Z0-9]{4,64}$")) throw new ArgumentException("Invalid PixelDrain file ID.");
        }
        public static string SafeName(string name)
        {
            name = (name ?? "file").Replace('\\', '/');
            name = name.Substring(name.LastIndexOf('/') + 1);
            foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            name = name.Trim().TrimEnd('.', ' ');
            if (String.IsNullOrWhiteSpace(name)) name = "file";
            if (Regex.IsMatch(name, @"^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(?:\.|$)", RegexOptions.IgnoreCase)) name = "_" + name;
            return name.Length > 160 ? name.Substring(0, 160) : name;
        }
        public static string SizeText(long size)
        {
            string[] units = { "B", "KB", "MB", "GB", "TB" }; double value = Math.Max(0, size); int unit = 0;
            while (value >= 1024 && unit < units.Length - 1) { value /= 1024; unit++; }
            return value.ToString(unit == 0 ? "0" : "0.#", CultureInfo.GetCultureInfo("en-US")) + " " + units[unit];
        }
    }

    public sealed class MetadataStore
    {
        private readonly string root;
        public MetadataStore(string root) { this.root = root; }
        public LocalData Load(string scope)
        {
            try
            {
                string path = GetPath(scope); if (!File.Exists(path)) return new LocalData();
                LocalData result = Json.NewSerializer().Deserialize<LocalData>(File.ReadAllText(path, Encoding.UTF8));
                if (result == null) return new LocalData(); result.Normalize(); return result;
            }
            catch (IOException) { return new LocalData(); }
            catch (UnauthorizedAccessException) { return new LocalData(); }
            catch (ArgumentException) { return new LocalData(); }
            catch (InvalidOperationException) { return new LocalData(); }
        }
        public void Save(string scope, LocalData data)
        {
            Directory.CreateDirectory(root);
            string target = GetPath(scope), temp = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temp, Json.NewSerializer().Serialize(data), new UTF8Encoding(false));
                if (File.Exists(target)) File.Replace(temp, target, null); else File.Move(temp, target);
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }
        private string GetPath(string scope)
        {
            if (!Regex.IsMatch(scope, @"^(demo|[a-f0-9]{64})$")) throw new ArgumentException("Invalid account scope.");
            return Path.Combine(root, scope + ".json");
        }
    }

    public static class Json
    {
        public static JavaScriptSerializer NewSerializer() { return new JavaScriptSerializer { MaxJsonLength = 64 * 1024 * 1024, RecursionLimit = 100 }; }
        public static Dictionary<string, object> Object(string input)
        {
            try { return NewSerializer().Deserialize<Dictionary<string, object>>(input) ?? new Dictionary<string, object>(); }
            catch (ArgumentException) { throw new IOException("Unable to read the PixelDrain response. Check your connection."); }
            catch (InvalidOperationException) { throw new IOException("Unable to read the PixelDrain response."); }
        }
        public static string Text(Dictionary<string, object> data, string key, string fallback = "")
        { object value; return data.TryGetValue(key, out value) && value != null ? Convert.ToString(value, CultureInfo.InvariantCulture) : fallback; }
        public static CloudFile FileInfo(Dictionary<string, object> data)
        {
            string id = Text(data, "id"); FileRules.CheckId(id); long size; DateTime uploaded;
            Int64.TryParse(Text(data, "size"), NumberStyles.Any, CultureInfo.InvariantCulture, out size);
            DateTime.TryParse(Text(data, "date_upload"), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out uploaded);
            return new CloudFile { Id = id, Name = Text(data, "name", id), Size = Math.Max(0, size), Mime = Text(data, "mime_type"), Uploaded = uploaded };
        }
    }

    public sealed class TransferProgress
    {
        public long Done { get; set; }
        public long Total { get; set; }
        public int Percent { get { return Total > 0 ? (int)Math.Min(100, Done * 100.0 / Total) : 0; } }
    }

    public sealed class ProgressFileContent : HttpContent
    {
        private readonly string path;
        private readonly IProgress<TransferProgress> progress;
        private readonly CancellationToken token;
        private readonly long length;
        public ProgressFileContent(string path, IProgress<TransferProgress> progress, CancellationToken token)
        {
            this.path = path; this.progress = progress; this.token = token; length = new FileInfo(path).Length;
            Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        }
        protected override bool TryComputeLength(out long result) { result = length; return true; }
        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext context)
        {
            using (FileStream input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true))
            {
                byte[] buffer = new byte[81920]; long total = 0; int read; DateTime last = DateTime.MinValue;
                if (progress != null) progress.Report(new TransferProgress { Done = 0, Total = length });
                while ((read = await input.ReadAsync(buffer, 0, buffer.Length, token).ConfigureAwait(false)) > 0)
                {
                    await stream.WriteAsync(buffer, 0, read, token).ConfigureAwait(false); total += read;
                    if ((DateTime.UtcNow - last).TotalMilliseconds >= 120 || total == length)
                    { if (progress != null) progress.Report(new TransferProgress { Done = total, Total = length }); last = DateTime.UtcNow; }
                }
                if (length == 0 && progress != null) progress.Report(new TransferProgress { Done = 0, Total = 0 });
            }
        }
    }

    public sealed partial class PixelDrainClient : IDisposable
    {
        public const string Origin = "https://pixeldrain.com";
        private readonly HttpClient http;
        public PixelDrainClient() : this(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false, AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate }) { }
        public PixelDrainClient(HttpMessageHandler handler)
        {
            http = new HttpClient(handler); http.Timeout = Timeout.InfiniteTimeSpan;
            http.DefaultRequestHeaders.UserAgent.ParseAdd("Pixelroom-Windows/1.8");
        }
        private HttpRequestMessage Request(HttpMethod method, string path, string key)
        {
            HttpRequestMessage request = new HttpRequestMessage(method, Origin + "/api" + path);
            if (!String.IsNullOrEmpty(key)) request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes(":" + key)));
            return request;
        }
        private static string ErrorMessage(Dictionary<string, object> body, int status)
        {
            string code = Json.Text(body, "value");
            switch (code)
            {
                case "authentication_failed": case "authentication_required": return "Invalid or expired API key. Check your key and try again.";
                case "file_too_large": return "This file exceeds your PixelDrain upload limit.";
                case "user_out_of_space": return "Your PixelDrain storage is full.";
                case "email_address_not_verified": return "Verify your PixelDrain email before uploading.";
                case "forbidden": return "Your API key does not have access to this file.";
                case "not_found": return "This file was not found on PixelDrain.";
                case "path_not_found": return "This folder or file was not found on PixelDrain.";
                case "permission_denied": return "Your account or API key does not have access to this path.";
                case "node_already_exists": return "A file or folder with that name already exists.";
                case "directory_not_empty": return "This folder is not empty.";
                case "ip_rate_limit_reached": return "Too many requests. Please try again later.";
                case "download_limit_exceeded": case "transfer_limit_exceeded": return "Your PixelDrain download or transfer limit has been reached.";
            }
            if (code.IndexOf("captcha", StringComparison.OrdinalIgnoreCase) >= 0 || code == "hotlink_detected") return "PixelDrain requires a captcha on its website before this file can be downloaded.";
            string message = Json.Text(body, "message"); return String.IsNullOrEmpty(message) ? "PixelDrain returned status " + status + "." : message;
        }
        private static async Task<Dictionary<string, object>> ReadJson(HttpResponseMessage response)
        {
            if ((int)response.StatusCode >= 300 && (int)response.StatusCode < 400) throw new IOException("PixelDrain returned an unexpected redirect. Please try again.");
            string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            Dictionary<string, object> parsed = Json.Object(body);
            if (!response.IsSuccessStatusCode || Json.Text(parsed, "success") == "False" || Json.Text(parsed, "success") == "false") throw new IOException(ErrorMessage(parsed, (int)response.StatusCode));
            return parsed;
        }
        public async Task<List<CloudFile>> ListAsync(string key, CancellationToken token)
        {
            using (HttpRequestMessage request = Request(HttpMethod.Get, "/user/files", key))
            using (CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                timeout.CancelAfter(TimeSpan.FromSeconds(45));
                using (HttpResponseMessage response = await http.SendAsync(request, HttpCompletionOption.ResponseContentRead, timeout.Token).ConfigureAwait(false))
                {
                    Dictionary<string, object> body = await ReadJson(response).ConfigureAwait(false); object raw;
                    if (!body.TryGetValue("files", out raw) || !(raw is IEnumerable)) throw new IOException("The PixelDrain file list is unavailable.");
                    List<CloudFile> files = new List<CloudFile>();
                    foreach (object item in (IEnumerable)raw) { Dictionary<string, object> info = item as Dictionary<string, object>; if (info != null) files.Add(Json.FileInfo(info)); }
                    return files;
                }
            }
        }
        public async Task<CloudFile> UploadAsync(string path, string key, IProgress<TransferProgress> progress, CancellationToken token)
        {
            string name = Path.GetFileName(path); if (name.Length > 255) throw new IOException("The filename is longer than 255 characters.");
            if (new FileInfo(path).Length == 0) throw new IOException("Empty files cannot be uploaded.");
            using (HttpRequestMessage request = Request(HttpMethod.Put, "/file/" + Uri.EscapeDataString(name), key))
            {
                request.Content = new ProgressFileContent(path, progress, token);
                using (HttpResponseMessage response = await http.SendAsync(request, HttpCompletionOption.ResponseContentRead, token).ConfigureAwait(false))
                    return Json.FileInfo(await ReadJson(response).ConfigureAwait(false));
            }
        }
        public async Task DeleteAsync(string id, string key, CancellationToken token)
        {
            FileRules.CheckId(id);
            using (CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(token))
            using (HttpRequestMessage request = Request(HttpMethod.Delete, "/file/" + id, key))
            {
                timeout.CancelAfter(TimeSpan.FromSeconds(45));
                using (HttpResponseMessage response = await http.SendAsync(request, timeout.Token).ConfigureAwait(false)) await ReadJson(response).ConfigureAwait(false);
            }
        }
        public Task DownloadAsync(IList<string> ids, string target, string key, IProgress<TransferProgress> progress, CancellationToken token)
        {
            if (ids.Count < 1 || ids.Count > 100) throw new IOException("Select between 1 and 100 files per ZIP download.");
            foreach (string id in ids) FileRules.CheckId(id);
            return DownloadToFileAsync("/file/" + String.Join(",", ids) + "?download", target, key, progress, token);
        }
        private async Task DownloadToFileAsync(string route, string target, string key, IProgress<TransferProgress> progress, CancellationToken token, bool overwrite = true)
        {
            target = Path.GetFullPath(target); string temp = target + ".pixelroom-" + Guid.NewGuid().ToString("N") + ".part";
            long received = 0;
            try
            {
                using (HttpRequestMessage request = Request(HttpMethod.Get, route, key))
                using (HttpResponseMessage response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false))
                {
                    if (!response.IsSuccessStatusCode) { await ReadJson(response).ConfigureAwait(false); throw new IOException("The download could not be started."); }
                    long expected = response.Content.Headers.ContentLength ?? -1;
                    if (progress != null) progress.Report(new TransferProgress { Total = expected });
                    token.ThrowIfCancellationRequested();
                    using (CancellationTokenRegistration registration = token.Register(response.Dispose))
                    using (Stream input = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                    using (FileStream output = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
                    {
                        byte[] buffer = new byte[81920]; long done = 0; int read; DateTime last = DateTime.MinValue;
                        while ((read = await input.ReadAsync(buffer, 0, buffer.Length, token).ConfigureAwait(false)) > 0)
                        {
                            await output.WriteAsync(buffer, 0, read, token).ConfigureAwait(false); done += read;
                            if ((DateTime.UtcNow - last).TotalMilliseconds >= 120 || done == expected)
                            { if (progress != null) progress.Report(new TransferProgress { Done = done, Total = expected }); last = DateTime.UtcNow; }
                        }
                        if (expected >= 0 && done != expected) throw new IOException("The download ended before the complete file was received.");
                        received = done;
                    }
                }
                token.ThrowIfCancellationRequested();
                if (overwrite && File.Exists(target)) File.Replace(temp, target, null); else File.Move(temp, target);
                if (progress != null) progress.Report(new TransferProgress { Done = received, Total = received });
            }
            catch (Exception) { token.ThrowIfCancellationRequested(); throw; }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }
        public void Dispose() { http.Dispose(); }
    }
}
