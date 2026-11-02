using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Pixelroom;

internal sealed class Handler : HttpMessageHandler
{
    public Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> Respond;
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) { return Respond(request, token); }
}
internal sealed class InlineProgress : IProgress<TransferProgress>
{
    public Action<TransferProgress> Action;
    public void Report(TransferProgress value) { Action(value); }
}
internal static partial class Tests
{
    private static int passed;
    private static string root;
    private static HttpResponseMessage Response(string json, HttpStatusCode status = HttpStatusCode.OK) { return new HttpResponseMessage(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") }; }
    private static void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static void Pass(string name) { passed++; Console.WriteLine("PASS " + name); }
    private static async Task Run()
    {
        string key = "dummy-test-only-never-real";
        string scope = FileRules.AccountScope(key);
        Assert(scope.Length == 64 && !scope.Contains(key), "Account scope must be a hash.");
        Assert(FileRules.SafeName("../../CON.txt") == "_CON.txt", "Filename traversal/reserved name.");
        Assert(FileRules.SafeName("bad:name?.txt") == "bad_name_.txt", "Invalid Windows filename characters.");
        bool invalid = false; try { FileRules.CheckId("../../secret"); } catch (ArgumentException) { invalid = true; } Assert(invalid, "Invalid file ID accepted.");
        Pass("safe filenames and account separation");

        MetadataStore store = new MetadataStore(Path.Combine(root, "metadata")); LocalData local = new LocalData(); local.Favorites.Add("abcd1234"); local.Aliases["abcd1234"] = "Friendly name"; local.Collections.Add(new Collection { Name = "Project", Files = new List<string> { "abcd1234" } }); store.Save(scope, local);
        string saved = File.ReadAllText(Path.Combine(root, "metadata", scope + ".json")); Assert(!saved.Contains(key), "Key persisted.");
        LocalData loaded = store.Load(scope); Assert(loaded.Favorites.Count == 1 && loaded.Collections[0].Files.Count == 1, "Metadata roundtrip failed.");
        loaded.Remove("abcd1234"); Assert(loaded.Favorites.Count == 0 && loaded.Collections[0].Files.Count == 0 && loaded.Aliases.Count == 0, "Metadata removal failed.");
        Assert(store.Load(FileRules.AccountScope("other-key")).Favorites.Count == 0, "Cross-account metadata.");
        store.Save(scope, loaded); Assert(store.Load(scope).Favorites.Count == 0, "Atomic replacement failed.");
        Pass("local metadata isolation, atomic save and no API key persistence");

        PreferencesStore preferences = new PreferencesStore(Path.Combine(root, "preferences"));
        Assert(preferences.Load().Theme == "Gray", "Default theme is not Gray.");
        preferences.Save("Dark"); Assert(preferences.Load().Theme == "Dark", "Dark theme was not saved.");
        preferences.Save("Gray"); Assert(preferences.Load().Theme == "Gray", "Gray theme was not saved.");
        preferences.Save("System"); Assert(preferences.Load().Theme == "Gray", "Unexpected theme was accepted.");
        string preferencesPath = Path.Combine(root, "preferences", "settings.json");
        string settingsJson = File.ReadAllText(preferencesPath);
        Assert(!settingsJson.Contains(key) && !settingsJson.Contains("API"), "Preferences contain credentials.");
        File.WriteAllText(preferencesPath, "invalid-json"); Assert(preferences.Load().Theme == "Gray", "Invalid preferences do not fall back safely.");
        Pass("exactly two persistent themes; preferences do not store credentials");

        CheckCredentials(key);

        Handler handler = new Handler();
        handler.Respond = delegate(HttpRequestMessage request, CancellationToken token)
        {
            Assert(request.RequestUri.GetLeftPart(UriPartial.Authority) == "https://pixeldrain.com", "Unexpected key destination.");
            Assert(request.RequestUri.AbsolutePath == "/api/user/files", "Wrong list endpoint.");
            Assert(request.Headers.Authorization.Scheme == "Basic" && request.Headers.Authorization.Parameter == Convert.ToBase64String(Encoding.UTF8.GetBytes(":" + key)), "Incorrect Basic auth.");
            return Task.FromResult(Response("{\"files\":[{\"id\":\"abcd1234\",\"name\":\"test.txt\",\"size\":4194304000,\"mime_type\":\"text/plain\",\"date_upload\":\"2026-10-16T00:00:00Z\"}]}"));
        };
        using (PixelDrainClient client = new PixelDrainClient(handler))
        {
            List<CloudFile> result = await client.ListAsync(key, CancellationToken.None);
            Assert(result.Count == 1 && result[0].Size == 4194304000L && result[0].Kind == "Document", "File listing / 64-bit sizes failed.");
        }
        Pass("authenticated file listing and files larger than 2 GB on this process architecture");

        handler = new Handler(); handler.Respond = delegate { return Task.FromResult(Response("{\"success\":false,\"value\":\"authentication_failed\"}", HttpStatusCode.Unauthorized)); };
        using (PixelDrainClient client = new PixelDrainClient(handler))
        { bool rejected = false; try { await client.ListAsync(key, CancellationToken.None); } catch (IOException ex) { rejected = ex.Message.IndexOf("API key", StringComparison.OrdinalIgnoreCase) >= 0 && !ex.Message.Contains(key); } Assert(rejected, "Invalid key handling."); }
        handler = new Handler(); handler.Respond = delegate { return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Redirect) { Content = new StringContent(""), Headers = { Location = new Uri("https://untrusted.test/") } }); };
        using (PixelDrainClient client = new PixelDrainClient(handler))
        { bool rejected = false; try { await client.ListAsync(key, CancellationToken.None); } catch (IOException ex) { rejected = ex.Message.Contains("redirect"); } Assert(rejected, "Redirect was not rejected."); }
        Pass("invalid keys and redirects are rejected without exposing credentials");

        string uploadPath = Path.Combine(root, "contoh unggah.txt"); File.WriteAllText(uploadPath, new String('x', 250000), new UTF8Encoding(false)); long uploadProgress = 0;
        handler = new Handler(); handler.Respond = async delegate(HttpRequestMessage request, CancellationToken token)
        {
            Assert(request.Method == HttpMethod.Put && request.RequestUri.AbsolutePath == "/api/file/contoh%20unggah.txt", "Upload URL incorrect.");
            using (MemoryStream stream = new MemoryStream()) { await request.Content.CopyToAsync(stream); Assert(stream.Length == 250000, "Upload truncated."); }
            return Response("{\"success\":true,\"id\":\"upload12\",\"name\":\"contoh unggah.txt\",\"size\":250000}", HttpStatusCode.Created);
        };
        using (PixelDrainClient client = new PixelDrainClient(handler))
        { CloudFile uploaded = await client.UploadAsync(uploadPath, key, new InlineProgress { Action = p => uploadProgress = p.Done }, CancellationToken.None); Assert(uploaded.Id == "upload12" && uploadProgress == 250000, "Upload progress missing."); }
        Pass("streamed upload with encoded filename and transfer progress");

        byte[] payload = Encoding.UTF8.GetBytes("complete download payload"); string target = Path.Combine(root, "download.txt");
        handler = new Handler(); handler.Respond = delegate(HttpRequestMessage request, CancellationToken token)
        {
            Assert(request.RequestUri.AbsolutePath == "/api/file/abcd1234,efgh5678" && request.RequestUri.Query == "?download", "ZIP download endpoint incorrect.");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(payload) });
        };
        using (PixelDrainClient client = new PixelDrainClient(handler)) await client.DownloadAsync(new List<string> { "abcd1234", "efgh5678" }, target, key, null, CancellationToken.None);
        Assert(File.ReadAllText(target) == "complete download payload", "Download contents incorrect.");
        Pass("authenticated multi-file download writes the final file");

        File.WriteAllText(target, "keep original");
        handler = new Handler(); handler.Respond = delegate
        {
            ByteArrayContent content = new ByteArrayContent(payload); content.Headers.ContentLength = payload.Length + 123;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        };
        using (PixelDrainClient client = new PixelDrainClient(handler))
        { bool rejected = false; try { await client.DownloadAsync(new List<string> { "abcd1234" }, target, key, null, CancellationToken.None); } catch (IOException) { rejected = true; } Assert(rejected && File.ReadAllText(target) == "keep original", "Truncated download overwrote existing file."); }
        Assert(Directory.GetFiles(root, "*.part").Length == 0, "Failed transfer left a partial file.");
        Pass("failed download preserves existing files and removes its own partial file");

        CancellationTokenSource cancel = new CancellationTokenSource();
        handler = new Handler(); handler.Respond = delegate { return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(new byte[300000]) }); };
        using (PixelDrainClient client = new PixelDrainClient(handler))
        { bool cancelled = false; try { await client.DownloadAsync(new List<string> { "abcd1234" }, target, key, new InlineProgress { Action = p => cancel.Cancel() }, cancel.Token); } catch (OperationCanceledException) { cancelled = true; } Assert(cancelled && File.ReadAllText(target) == "keep original", "Cancellation corrupted final file."); }
        Assert(Directory.GetFiles(root, "*.part").Length == 0, "Cancelled transfer left a partial file."); cancel.Dispose();
        Pass("download cancellation preserves existing files");

        int deletes = 0; handler = new Handler(); handler.Respond = delegate(HttpRequestMessage request, CancellationToken token) { Assert(request.Method == HttpMethod.Delete && request.RequestUri.AbsolutePath == "/api/file/abcd1234", "Wrong delete request."); deletes++; return Task.FromResult(Response("{\"success\":true}")); };
        using (PixelDrainClient client = new PixelDrainClient(handler)) await client.DeleteAsync("abcd1234", key, CancellationToken.None);
        Assert(deletes == 1, "Delete issued more than once."); Pass("single authenticated delete request");

        handler = new Handler(); handler.Respond = delegate { return Task.FromResult(Response("{\"success\":false,\"value\":\"ip_download_limited_captcha_required\"}", HttpStatusCode.Forbidden)); };
        using (PixelDrainClient client = new PixelDrainClient(handler))
        { bool rejected = false; try { await client.DownloadAsync(new List<string> { "abcd1234" }, target, key, null, CancellationToken.None); } catch (IOException ex) { rejected = ex.Message.Contains("captcha"); } Assert(rejected, "Captcha error was hidden."); }
        Pass("PixelDrain captcha/transfer restrictions are surfaced without a browser launch");

        await CheckFilesystem(key, uploadPath);
        await CheckExplorer();
        await CheckFoldersAndSharing();
        await CheckBrowsingAndTransfers();
        await CheckBatchOperations();
        await CheckStatistics();

        if (Environment.GetCommandLineArgs().Contains("--live"))
        {
            using (PixelDrainClient client = new PixelDrainClient())
            { bool rejected = false; try { await client.ListAsync("dummy-not-a-real-api-key", CancellationToken.None); } catch (IOException ex) { rejected = ex.Message.IndexOf("API key", StringComparison.OrdinalIgnoreCase) >= 0; } Assert(rejected, "Live service did not reject the dummy key as expected."); }
            Pass("live PixelDrain API rejects a dummy key over TLS");
        }
    }
    private static async Task CheckFilesystem(string key, string uploadPath)
    {
        Assert(FilesystemRules.Route("/me/A b/é#%.txt") == "/filesystem/me/A%20b/%C3%A9%23%25.txt", "Filesystem paths must encode each segment.");
        Assert(FilesystemRules.Parent("/me") == "/me" && FilesystemRules.Child("/me/A b", "new") == "/me/A b/new", "Folder navigation.");
        foreach (string bad in new string[] { "/me/../other", "/other", "https://untrusted.test/me", "/me//child", "/me/child\\escape" })
        { bool rejected = false; try { FilesystemRules.Normalize(bad); } catch (ArgumentException) { rejected = true; } Assert(rejected, "Unsafe filesystem path accepted."); }
        bool tooLong = false; try { FilesystemRules.CheckName(new String('é', 128)); } catch (ArgumentException) { tooLong = true; } Assert(tooLong, "Name limit must use UTF-8 bytes.");
        Pass("filesystem Unicode encoding, root confinement and UTF-8 name limits");

        string listing = "{\"path\":[{\"type\":\"dir\",\"path\":\"/\",\"name\":\"\"},{\"type\":\"dir\",\"path\":\"/me\",\"name\":\"me\"}],\"base_index\":1,\"children\":[{\"type\":\"dir\",\"path\":\"/me/A b\",\"name\":\"A b\"},{\"type\":\"file\",\"path\":\"/me/large.bin\",\"name\":\"large.bin\",\"file_size\":4294967297,\"modified\":\"2026-10-16T00:00:00Z\"}],\"permissions\":{\"read\":true,\"write\":true,\"delete\":false}}";
        Handler handler = new Handler(); handler.Respond = delegate(HttpRequestMessage request, CancellationToken token)
        {
            Assert(request.RequestUri.AbsoluteUri == "https://pixeldrain.com/api/filesystem/me?stat", "Wrong filesystem listing endpoint.");
            Assert(request.Headers.Authorization.Parameter == Convert.ToBase64String(Encoding.UTF8.GetBytes(":" + key)), "Filesystem authentication missing.");
            return Task.FromResult(Response(listing));
        };
        using (PixelDrainClient client = new PixelDrainClient(handler))
        { FilesystemDirectory result = await client.ListFilesystemAsync("/me", key, CancellationToken.None); Assert(result.Children.Count == 2 && result.Children[0].IsDirectory && result.Children[1].Size == 4294967297L, "Filesystem metadata parsing."); Assert(result.CanRead && result.CanWrite && !result.CanDelete, "Server permissions must be preserved."); }
        Pass("authenticated filesystem listing, ancestor metadata, 64-bit sizes and permissions");

        handler = new Handler(); handler.Respond = delegate { return Task.FromResult(Response(listing.Replace("/me/large.bin", "/me/outside/large.bin"))); };
        using (PixelDrainClient client = new PixelDrainClient(handler))
        { bool rejected = false; try { await client.ListFilesystemAsync("/me", key, CancellationToken.None); } catch (IOException) { rejected = true; } Assert(rejected, "Cross-directory response was accepted."); }
        handler = new Handler(); handler.Respond = delegate { return Task.FromResult(Response("{\"value\":\"permission_denied\",\"message\":\"Denied\"}", HttpStatusCode.Forbidden)); };
        using (PixelDrainClient client = new PixelDrainClient(handler))
        { bool rejected = false; try { await client.ListFilesystemAsync("/me", key, CancellationToken.None); } catch (IOException ex) { rejected = ex.Message.Contains("access") && !ex.Message.Contains(key); } Assert(rejected, "Filesystem access error hidden."); }
        Pass("filesystem malformed listings and account access failures are rejected");

        int count = 0; handler = new Handler(); handler.Respond = async delegate(HttpRequestMessage request, CancellationToken token)
        {
            Assert(request.RequestUri.Host == "pixeldrain.com" && request.Headers.Authorization != null, "Mutation destination/authentication.");
            string body = request.Content == null ? "" : await request.Content.ReadAsStringAsync();
            if (count == 0) Assert(request.Method == HttpMethod.Post && body.Contains("mkdir") && request.RequestUri.AbsolutePath.EndsWith("/new%20folder"), "Create folder protocol.");
            if (count == 1) Assert(request.Method == HttpMethod.Post && body.Contains("rename") && body.Contains("/me/new name"), "Rename target is not raw multipart text.");
            if (count == 2) Assert(request.Method == HttpMethod.Delete && request.RequestUri.Query == "?recursive", "Folder delete must be recursive only when requested.");
            if (count == 3) Assert(request.Method == HttpMethod.Delete && request.RequestUri.Query == "", "File delete must not add recursion.");
            count++; return new HttpResponseMessage(HttpStatusCode.NoContent);
        };
        using (PixelDrainClient client = new PixelDrainClient(handler))
        {
            await client.CreateFilesystemDirectoryAsync("/me/new folder", key, CancellationToken.None);
            await client.RenameFilesystemAsync("/me/new folder", "/me/new name", key, CancellationToken.None);
            await client.DeleteFilesystemAsync("/me/new name", true, key, CancellationToken.None);
            await client.DeleteFilesystemAsync("/me/file.txt", false, key, CancellationToken.None);
            bool blocked = false; try { await client.DeleteFilesystemAsync("/me", true, key, CancellationToken.None); } catch (ArgumentException) { blocked = true; }
            Assert(blocked && count == 4, "A root deletion request escaped to the API.");
            blocked = false; try { await client.RenameFilesystemAsync("/me/folder", "/me/folder/child", key, CancellationToken.None); } catch (ArgumentException) { blocked = true; }
            Assert(blocked && count == 4, "A folder move into itself escaped to the API.");
        }
        Pass("filesystem create, rename, recursive delete and root protection without retries");

        long uploaded = 0; handler = new Handler(); handler.Respond = async delegate(HttpRequestMessage request, CancellationToken token)
        {
            Assert(request.Method == HttpMethod.Put && request.RequestUri.AbsolutePath == "/api/filesystem/me/A%20b/contoh%20unggah.txt", "Wrong filesystem upload path.");
            using (MemoryStream stream = new MemoryStream()) { await request.Content.CopyToAsync(stream); Assert(stream.Length == new FileInfo(uploadPath).Length, "Filesystem upload truncated."); }
            return Response("{\"type\":\"file\",\"path\":\"/me/A b/contoh unggah.txt\"}");
        };
        using (PixelDrainClient client = new PixelDrainClient(handler)) await client.UploadFilesystemAsync(uploadPath, "/me/A b/contoh unggah.txt", key, new InlineProgress { Action = p => uploaded = p.Done }, CancellationToken.None);
        Assert(uploaded == new FileInfo(uploadPath).Length, "Filesystem upload progress."); Pass("filesystem streamed upload with encoded path and progress");

        byte[] payload = Encoding.UTF8.GetBytes("filesystem payload"); count = 0; handler = new Handler(); handler.Respond = delegate(HttpRequestMessage request, CancellationToken token)
        {
            Assert(request.RequestUri.Query == (count == 0 ? "?attach" : "?bulk_download"), "Wrong file/folder download protocol."); count++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(payload) });
        };
        string fileTarget = Path.Combine(root, "filesystem-file.txt"), zipTarget = Path.Combine(root, "filesystem-folder.zip");
        using (PixelDrainClient client = new PixelDrainClient(handler)) { await client.DownloadFilesystemAsync("/me/file.txt", false, fileTarget, key, null, CancellationToken.None); await client.DownloadFilesystemAsync("/me/A b", true, zipTarget, key, null, CancellationToken.None); }
        Assert(count == 2 && File.ReadAllBytes(fileTarget).SequenceEqual(payload) && File.ReadAllBytes(zipTarget).SequenceEqual(payload), "Filesystem downloaded data differs.");
        File.WriteAllText(fileTarget, "preserve"); handler = new Handler(); handler.Respond = delegate { ByteArrayContent content = new ByteArrayContent(payload); content.Headers.ContentLength = payload.Length + 1; return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content }); };
        using (PixelDrainClient client = new PixelDrainClient(handler)) { bool rejected = false; try { await client.DownloadFilesystemAsync("/me/file.txt", false, fileTarget, key, null, CancellationToken.None); } catch (IOException) { rejected = true; } Assert(rejected && File.ReadAllText(fileTarget) == "preserve" && Directory.GetFiles(root, "*.part").Length == 0, "Filesystem failed download replaced an existing file."); }
        Pass("filesystem file and folder ZIP downloads with atomic failure cleanup");

        count = 0; handler = new Handler(); handler.Respond = async delegate(HttpRequestMessage request, CancellationToken token) { string body = await request.Content.ReadAsStringAsync(); Assert(request.Method == HttpMethod.Post && body.Contains("import") && body.Contains("abcd1234") && body.Contains("efgh5678"), "Import protocol."); count++; return Response("{\"value\":\"success\"}"); };
        using (PixelDrainClient client = new PixelDrainClient(handler)) { await client.ImportFilesystemAsync("/me", new List<string> { "abcd1234", "efgh5678" }, key, CancellationToken.None); bool rejected = false; try { await client.ImportFilesystemAsync("/me", new List<string> { "../../unsafe" }, key, CancellationToken.None); } catch (ArgumentException) { rejected = true; } Assert(rejected && count == 1, "Invalid import sent to API."); }
        Pass("filesystem file import validates IDs before sending an authenticated request");
    }
    private static void CheckCredentials(string key)
    {
        string credentialRoot = Path.Combine(root, "credentials"), credentialPath = Path.Combine(credentialRoot, "api-key.dat");
        CredentialStore credentials = new CredentialStore(credentialRoot);
        Assert(credentials.Load() == null, "A new profile must not have a saved key.");
        credentials.Save(key);
        byte[] encrypted = File.ReadAllBytes(credentialPath);
        Assert(new CredentialStore(credentialRoot).Load() == key, "A new store instance must recover the saved key.");
        string diskText = Encoding.UTF8.GetString(encrypted);
        Assert(!diskText.Contains(key) && !diskText.Contains(Convert.ToBase64String(Encoding.UTF8.GetBytes(key))), "Saved credential is plaintext or base64.");
        byte[] plain = ProtectedData.Unprotect(encrypted, Encoding.UTF8.GetBytes("Pixelroom.PixelDrain.ApiKey.v1"), DataProtectionScope.CurrentUser);
        Assert(Encoding.UTF8.GetString(plain) == key, "Credential must be protected by Windows DPAPI."); Array.Clear(plain, 0, plain.Length);
        Pass("saved credentials use Windows DPAPI and recover without plaintext storage");

        string hash; using (SHA256 sha = SHA256.Create()) hash = Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(key)));
        ProcessStartInfo restart = new ProcessStartInfo(System.Reflection.Assembly.GetExecutingAssembly().Location, "--credential-read \"" + credentialRoot + "\" " + hash) { UseShellExecute = false, CreateNoWindow = true };
        using (Process child = Process.Start(restart))
        { Assert(child.WaitForExit(10000), "Credential reload process timed out."); Assert(child.ExitCode == 0, "A fresh application process could not reload the key."); }
        Pass("a fresh Windows process reloads the encrypted API key");

        string nextKey = key + "-updated"; credentials.Save(nextKey);
        Assert(credentials.Load() == nextKey && !encrypted.SequenceEqual(File.ReadAllBytes(credentialPath)), "Updating credentials failed.");
        bool invalid = false; try { credentials.Save("invalid\nkey"); } catch (ArgumentException) { invalid = true; }
        Assert(invalid && credentials.Load() == nextKey, "Invalid input replaced a saved key.");
        using (FileStream locked = new FileStream(credentialPath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            bool failed = false; try { credentials.Save(key); } catch (IOException ex) { failed = !ex.Message.Contains(key); }
            Assert(failed, "A locked credential must report a safe save failure.");
        }
        Assert(credentials.Load() == nextKey && Directory.GetFiles(credentialRoot, "*.tmp").Length == 0, "Failed save lost the previous credential or left temporary files.");
        Pass("credential replacement is atomic and failed saves preserve the previous key");

        encrypted = File.ReadAllBytes(credentialPath); encrypted[encrypted.Length / 2] ^= 0x5a; File.WriteAllBytes(credentialPath, encrypted);
        bool corrupt = false; try { credentials.Load(); } catch (IOException ex) { corrupt = !ex.Message.Contains(key); }
        Assert(corrupt, "Corrupt credentials must fail safely.");
        File.WriteAllBytes(credentialPath, new byte[20000]);
        corrupt = false; try { credentials.Load(); } catch (IOException) { corrupt = true; } Assert(corrupt, "Oversized credentials must be rejected.");
        credentials.Delete(); credentials.Delete(); Assert(credentials.Load() == null && !File.Exists(credentialPath), "Forgetting an account must remove the saved key.");
        Pass("corrupt keys are rejected and forgetting a key is permanent and repeatable");

        string settingsRoot = Path.Combine(root, "remember-preferences"); Directory.CreateDirectory(settingsRoot);
        File.WriteAllText(Path.Combine(settingsRoot, "settings.json"), "{\"Theme\":\"Dark\"}");
        PreferencesStore preferences = new PreferencesStore(settingsRoot);
        Assert(preferences.Load().Theme == "Dark" && preferences.Load().RememberApiKey, "Old preferences must migrate with the theme intact.");
        preferences.SaveRememberKey(false); preferences.Save("Gray");
        Assert(!preferences.Load().RememberApiKey && preferences.Load().Theme == "Gray", "Theme changes must preserve the remember choice.");
        preferences.SaveRememberKey(true); Assert(preferences.Load().RememberApiKey && preferences.Load().Theme == "Gray", "Remember changes must preserve the theme.");
        Pass("remember preference persists independently and upgrades old theme settings");
    }
    private static int Main(string[] args)
    {
        if (args.Length == 3 && args[0] == "--credential-read")
        {
            try
            {
                string loadedKey = new CredentialStore(args[1]).Load(); if (loadedKey == null) return 1;
                using (SHA256 sha = SHA256.Create()) return Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(loadedKey))) == args[2] ? 0 : 1;
            }
            catch { return 1; }
        }
        root = Path.GetFullPath(args.FirstOrDefault(a => !a.StartsWith("--")) ?? Path.Combine(Environment.CurrentDirectory, "test-work")); Directory.CreateDirectory(root);
        try { ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12; Run().GetAwaiter().GetResult(); Console.WriteLine("SUCCESS " + passed + " checks; process=" + (Environment.Is64BitProcess ? "64-bit" : "32-bit")); return 0; }
        catch (Exception ex) { Console.Error.WriteLine("FAIL " + ex); return 1; }
    }
}
