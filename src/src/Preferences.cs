using System;
using System.IO;
using System.Text;

namespace Pixelroom
{
    public sealed class Preferences
    {
        public string Theme { get; set; }
        public bool RememberApiKey { get; set; }
        public Preferences() { Theme = "Gray"; RememberApiKey = true; }
    }
    public sealed class PreferencesStore
    {
        private readonly string root;
        public PreferencesStore(string root) { this.root = root; }
        public Preferences Load()
        {
            try
            {
                string path = Path.Combine(root, "settings.json");
                Preferences result = File.Exists(path) ? Json.NewSerializer().Deserialize<Preferences>(File.ReadAllText(path, Encoding.UTF8)) : null;
                return new Preferences { Theme = result != null && result.Theme == "Dark" ? "Dark" : "Gray", RememberApiKey = result == null || result.RememberApiKey };
            }
            catch (IOException) { return new Preferences(); }
            catch (UnauthorizedAccessException) { return new Preferences(); }
            catch (ArgumentException) { return new Preferences(); }
            catch (InvalidOperationException) { return new Preferences(); }
        }
        public void Save(string theme)
        {
            Preferences value = Load(); value.Theme = theme == "Dark" ? "Dark" : "Gray"; Save(value);
        }
        public void SaveRememberKey(bool remember)
        {
            Preferences value = Load(); value.RememberApiKey = remember; Save(value);
        }
        private void Save(Preferences value)
        {
            Directory.CreateDirectory(root); string target = Path.Combine(root, "settings.json"), temp = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temp, Json.NewSerializer().Serialize(value), new UTF8Encoding(false));
                if (File.Exists(target)) File.Replace(temp, target, null); else File.Move(temp, target);
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }
    }
}
