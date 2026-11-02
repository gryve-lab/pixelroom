using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Pixelroom;

internal sealed class ExplorerFixture : HttpMessageHandler
{
    public const string Key = "dummy-explorer-test-key";
    public readonly Dictionary<string, FilesystemNode> Nodes = new Dictionary<string, FilesystemNode>(StringComparer.Ordinal);
    public readonly Dictionary<string, byte[]> Bytes = new Dictionary<string, byte[]>(StringComparer.Ordinal);
    public readonly List<string> Requests = new List<string>(), Mutations = new List<string>();
    public readonly HashSet<string> DenyRead = new HashSet<string>(), DenyWrite = new HashSet<string>();
    public readonly HashSet<string> DenyOwner = new HashSet<string>();
    public readonly HashSet<string> OmitLinkPermissions = new HashSet<string>(), NullLinkPermissions = new HashSet<string>();
    public readonly Dictionary<string, bool[]> LinkPermissions = new Dictionary<string, bool[]>();
    public bool Truncate { get; set; }
    private int shareCounter;
    public ExplorerFixture() { Folder("/me"); }
    public void Folder(string path) { Nodes[path] = new FilesystemNode { Path = path, Name = path.Substring(path.LastIndexOf('/') + 1), Type = "dir" }; }
    public void File(string path, byte[] content) { Nodes[path] = new FilesystemNode { Path = path, Name = path.Substring(path.LastIndexOf('/') + 1), Type = "file", Size = content.Length }; Bytes[path] = content; }
    private object NodeJson(FilesystemNode node)
    {
        bool[] access; if (!LinkPermissions.TryGetValue(node.Path, out access)) access = new bool[3];
        var result = new Dictionary<string, object> { { "path", node.Path }, { "name", node.Name }, { "type", node.Type }, { "id", node.Id }, { "file_size", node.Size }, { "modified", "2026-10-17T00:00:00Z" } };
        if (!OmitLinkPermissions.Contains(node.Path)) result["link_permissions"] = NullLinkPermissions.Contains(node.Path) ? null : (object)new { read = access[0], write = access[1], delete = access[2] };
        return result;
    }
    private static HttpResponseMessage JsonResponse(object value, HttpStatusCode status = HttpStatusCode.OK)
    { return new HttpResponseMessage(status) { Content = new StringContent(Json.NewSerializer().Serialize(value), Encoding.UTF8, "application/json") }; }
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (request.RequestUri.Host != "pixeldrain.com" || request.Headers.Authorization == null || request.Headers.Authorization.Parameter != Convert.ToBase64String(Encoding.UTF8.GetBytes(":" + Key))) throw new Exception("Explorer request destination or authentication incorrect.");
        string path = Uri.UnescapeDataString(request.RequestUri.AbsolutePath.Substring("/api/filesystem".Length)); string route = request.Method + " " + path + request.RequestUri.Query; Requests.Add(route);
        if (request.Method == HttpMethod.Get && request.RequestUri.Query == "?stat")
        {
            if (!Nodes.ContainsKey(path)) return JsonResponse(new { value = "path_not_found" }, HttpStatusCode.NotFound);
            object[] chain = Nodes.Values.Where(n => n.Path == path || path.StartsWith(n.Path + "/", StringComparison.Ordinal)).OrderBy(n => n.Path.Length).Select(NodeJson).ToArray();
            return JsonResponse(new { path = chain, base_index = chain.Length - 1, children = Nodes.Values.Where(n => n.Path != path && FilesystemRules.Parent(n.Path) == path).Select(NodeJson).ToArray(), permissions = new { owner = !DenyOwner.Contains(path), read = !DenyRead.Contains(path), write = !DenyWrite.Contains(path), delete = true } });
        }
        if (request.Method == HttpMethod.Get && request.RequestUri.Query == "?attach")
        {
            ByteArrayContent content = new ByteArrayContent(Bytes[path]); if (Truncate) content.Headers.ContentLength = Bytes[path].Length + 12;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
        }
        if (request.Method == HttpMethod.Get && request.RequestUri.Query == "?bulk_download") return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Encoding.UTF8.GetBytes("ZIP fixture: " + path)) };
        if (request.Method == HttpMethod.Post)
        {
            Dictionary<string, string> fields = new Dictionary<string, string>();
            foreach (HttpContent part in (MultipartFormDataContent)request.Content) fields[part.Headers.ContentDisposition.Name.Trim('"')] = await part.ReadAsStringAsync();
            if (fields["action"] == "update")
            {
                if (DenyOwner.Contains(path)) return JsonResponse(new { value = "permission_denied" }, HttpStatusCode.Forbidden);
                if (fields["shared"] == "true")
                {
                    var permissions = Json.Object(fields["link_permissions"]);
                    if (Json.Text(permissions, "read") != "True" || Json.Text(permissions, "write") != "False" || Json.Text(permissions, "delete") != "False" || Json.Text(permissions, "owner") != "False") throw new Exception("New shared links must be read-only.");
                    Nodes[path].Id = Nodes[path].Id ?? "shar" + (++shareCounter).ToString("D4"); LinkPermissions[path] = new bool[] { true, false, false };
                }
                else if (fields["shared"] == "false") { Nodes[path].Id = null; LinkPermissions.Remove(path); }
                else throw new Exception("Invalid sharing flag.");
                Mutations.Add(route); return JsonResponse(NodeJson(Nodes[path]));
            }
            if (fields["action"] == "rename")
            {
                string target = fields["target"];
                if (Nodes.ContainsKey(target)) return JsonResponse(new { value = "node_already_exists" }, HttpStatusCode.Conflict);
                foreach (string old in Nodes.Keys.Where(p => p == path || p.StartsWith(path + "/", StringComparison.Ordinal)).ToArray())
                {
                    FilesystemNode node = Nodes[old]; Nodes.Remove(old); string next = target + old.Substring(path.Length); node.Path = next; node.Name = next.Substring(next.LastIndexOf('/') + 1); Nodes[next] = node;
                    byte[] bytes; if (Bytes.TryGetValue(old, out bytes)) { Bytes.Remove(old); Bytes[next] = bytes; }
                    bool[] access; if (LinkPermissions.TryGetValue(old, out access)) { LinkPermissions.Remove(old); LinkPermissions[next] = access; }
                }
                Mutations.Add(route); return JsonResponse(NodeJson(Nodes[target]));
            }
            if (fields["action"] != "mkdir") throw new Exception("Unexpected explorer mutation.");
            if (Nodes.ContainsKey(path)) return JsonResponse(new { value = "node_already_exists" }, HttpStatusCode.Conflict);
            Mutations.Add(route); Folder(path); return new HttpResponseMessage(HttpStatusCode.NoContent);
        }
        if (request.Method == HttpMethod.Delete)
        {
            foreach (string old in Nodes.Keys.Where(p => p == path || p.StartsWith(path + "/", StringComparison.Ordinal)).ToArray()) { Nodes.Remove(old); Bytes.Remove(old); LinkPermissions.Remove(old); }
            Mutations.Add(route); return JsonResponse(new { success = true });
        }
        if (request.Method == HttpMethod.Put)
        {
            Mutations.Add(route);
            using (MemoryStream output = new MemoryStream()) { await request.Content.CopyToAsync(output); File(path, output.ToArray()); }
            return JsonResponse(new { success = true });
        }
        throw new Exception("Unexpected explorer operation: " + route);
    }
}
