using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using Pixelroom;

internal static class MainPreview
{
    [STAThread]
    private static void Main()
    {
        Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
        System.Threading.Thread.CurrentThread.CurrentCulture = System.Globalization.CultureInfo.GetCultureInfo("en-US");
        string profile = Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), "preview-main-profile"); new PreferencesStore(profile).Save("Gray");
        using (PixelDrainClient client = new PixelDrainClient(new ExplorerFixture()))
        using (MainWindow window = new MainWindow(client, profile))
        {
            typeof(MainWindow).GetMethod("ActivateAccount", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(window, new object[] { ExplorerFixture.Key, new List<CloudFile> { new CloudFile { Id = "abcd1234", Name = "Report.pdf", Size = 4096, Uploaded = DateTime.UtcNow, Mime = "application/pdf" }, new CloudFile { Id = "efgh5678", Name = "Notes.txt", Size = 512, Uploaded = DateTime.UtcNow, Mime = "text/plain" } } });
            window.Text = "Preview — Pixelroom"; Application.Run(window);
        }
    }
}
