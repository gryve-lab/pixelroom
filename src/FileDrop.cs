using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Pixelroom
{
    public static class UploadSelection
    {
        public static string[] Files(IEnumerable<string> paths)
        {
            string[] result = (paths ?? new string[0]).Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            if (result.Length == 0) throw new IOException("Select files to upload.");
            foreach (string path in result)
            {
                if (Directory.Exists(path)) throw new IOException("Select individual files to upload.");
                if (!File.Exists(path)) throw new IOException("A selected file is no longer available. Select it again.");
            }
            return result;
        }
    }
}
