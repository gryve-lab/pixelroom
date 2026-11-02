using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Pixelroom
{
    public sealed class FilesystemShareInfo
    {
        public FilesystemNode Node;
        public string ShareUrl = "", Hotlink = "";
        public bool OwnShared, ParentShared, CanManage, LinkRead, LinkWrite, LinkDelete;
    }
    public static class ShareLinks
    {
        public static string FileLink(string id) { FileRules.CheckId(id); return PixelDrainClient.Origin + "/u/" + Uri.EscapeDataString(id); }
        public static string FileHotlink(string id) { FileRules.CheckId(id); return PixelDrainClient.Origin + "/api/file/" + Uri.EscapeDataString(id); }
        public static string FilesystemLink(string publicPath, bool hotlink, bool folder)
        {
            string[] parts = publicPath.Split('/'); FileRules.CheckId(parts[0]);
            foreach (string part in parts.Skip(1)) FilesystemRules.CheckName(part);
            string encoded = String.Join("/", parts.Select(Uri.EscapeDataString));
            return PixelDrainClient.Origin + (hotlink ? "/api/filesystem/" : "/d/") + encoded + (hotlink && folder ? "?bulk_download" : "");
        }
    }
    public sealed partial class PixelDrainClient
    {
        private static bool Permission(Dictionary<string, object> data, string field, string name)
        { object value; Dictionary<string, object> permissions = data.TryGetValue(field, out value) ? value as Dictionary<string, object> : null; return permissions != null && Json.Text(permissions, name).Equals("true", StringComparison.OrdinalIgnoreCase); }
        private static bool LinkPermission(Dictionary<string, object> data, string name)
        {
            string id = Json.Text(data, "id"); if (id.Length == 0 || id == "me") return false;
            object value;
            // PixelDrain's web client defaults an omitted link_permissions object to read-only.
            if (!data.TryGetValue("link_permissions", out value) || value == null) return name == "read";
            return Permission(data, "link_permissions", name);
        }
        public async Task<FilesystemShareInfo> GetFilesystemShareAsync(string path, string key, CancellationToken token)
        {
            path = FilesystemRules.Normalize(path);
            Dictionary<string, object> data = await FilesystemRequestAsync(HttpMethod.Get, FilesystemRules.Route(path) + "?stat", key, null, token).ConfigureAwait(false);
            object raw; List<Dictionary<string, object>> chain = new List<Dictionary<string, object>>();
            if (data.TryGetValue("path", out raw) && raw is IEnumerable) foreach (object item in (IEnumerable)raw) { var node = item as Dictionary<string, object>; if (node != null) chain.Add(node); }
            int index; if (!Int32.TryParse(Json.Text(data, "base_index"), out index) || index < 0 || index >= chain.Count) throw new IOException("The sharing response is incomplete.");
            FilesystemShareInfo result = new FilesystemShareInfo { Node = ParseFilesystemNode(chain[index]), CanManage = Permission(data, "permissions", "owner") };
            if (result.Node.Path != path) throw new IOException("PixelDrain returned sharing details for another item.");
            string ownId = Json.Text(chain[index], "id"); result.OwnShared = ownId.Length > 0 && ownId != "me";
            int sharedIndex = -1; string publicPath = "";
            for (int i = 0; i <= index; i++)
            {
                string ancestor = Json.Text(chain[i], "path"); if (ancestor == "/") continue;
                ancestor = FilesystemRules.Normalize(ancestor);
                if (ancestor != path && !path.StartsWith(ancestor + "/", StringComparison.Ordinal)) throw new IOException("PixelDrain returned an invalid shared path.");
                string id = Json.Text(chain[i], "id"); if (id.Length == 0 || id == "me") continue;
                FileRules.CheckId(id);
                if (i < index && LinkPermission(chain[i], "read")) result.ParentShared = true;
                if (!LinkPermission(chain[i], "read")) continue;
                sharedIndex = i; publicPath = id + path.Substring(ancestor.Length);
            }
            if (sharedIndex >= 0)
            {
                result.LinkRead = true; result.LinkWrite = LinkPermission(chain[sharedIndex], "write"); result.LinkDelete = LinkPermission(chain[sharedIndex], "delete");
                result.ShareUrl = ShareLinks.FilesystemLink(publicPath, false, result.Node.IsDirectory); result.Hotlink = ShareLinks.FilesystemLink(publicPath, true, result.Node.IsDirectory);
            }
            return result;
        }
        public async Task<FilesystemShareInfo> SetFilesystemSharingAsync(string path, bool shared, string key, CancellationToken token)
        {
            FilesystemRules.NotRoot(path);
            Dictionary<string, string> fields = new Dictionary<string, string> { { "action", "update" }, { "shared", shared ? "true" : "false" } };
            if (shared) fields.Add("link_permissions", Json.NewSerializer().Serialize(new { owner = false, read = true, write = false, delete = false }));
            await FilesystemRequestAsync(HttpMethod.Post, FilesystemRules.Route(path), key, fields, token).ConfigureAwait(false);
            return await GetFilesystemShareAsync(path, key, token).ConfigureAwait(false);
        }
    }
}
