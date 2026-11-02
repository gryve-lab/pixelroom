using System;
using System.Text;
using System.Windows.Forms;
using Pixelroom;

internal static class ExplorerPreview
{
    [STAThread]
    private static void Main(string[] args)
    {
        Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false); Theme.Set(System.IO.Path.GetFileName(Application.ExecutablePath).Contains("Gray") ? "Gray" : "Dark");
        System.Threading.Thread.CurrentThread.CurrentCulture = System.Globalization.CultureInfo.GetCultureInfo("en-US");
        ExplorerFixture fixture = new ExplorerFixture(); fixture.Folder("/me/Documents"); fixture.Folder("/me/Documents/Project"); fixture.Folder("/me/Archive"); fixture.Folder("/me/Design assets");
        fixture.File("/me/Notes.txt", Encoding.UTF8.GetBytes("Local preview only")); fixture.File("/me/Documents/Project/Report.pdf", new byte[4096]);
        fixture.Nodes["/me/Documents"].Id = "preview1"; fixture.LinkPermissions["/me/Documents"] = new bool[] { true, false, false };
        using (PixelDrainClient client = new PixelDrainClient(fixture)) Application.Run(new FilesystemWindow(client, ExplorerFixture.Key));
    }
}
