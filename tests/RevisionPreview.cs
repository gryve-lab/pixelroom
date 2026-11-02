using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Pixelroom;

internal static class RevisionPreview
{
    private static readonly BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private static object Field(object target, string name) { return target.GetType().GetField(name, Hidden).GetValue(target); }
    private static object Call(object target, string name, params object[] values) { return target.GetType().GetMethod(name, Hidden).Invoke(target, values); }
    private static void Capture(MainWindow main, string path)
    {
        main.PerformLayout(); main.Refresh();
        using (Bitmap image = new Bitmap(main.Width, main.Height)) { main.DrawToBitmap(image, new Rectangle(Point.Empty, main.Size)); image.Save(path, System.Drawing.Imaging.ImageFormat.Png); }
        Console.WriteLine(path);
    }
    [STAThread]
    private static int Main(string[] args)
    {
        Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
        System.Threading.Thread.CurrentThread.CurrentCulture = System.Globalization.CultureInfo.GetCultureInfo("en-US");
        string output = Path.GetFullPath(args[0]); Directory.CreateDirectory(output); int result = 1;
        using (Form runner = new Form { ShowInTaskbar = false, Opacity = 0, WindowState = FormWindowState.Minimized })
        {
            runner.Shown += async delegate
            {
                try
                {
                    foreach (string theme in new string[] { "Gray", "Dark" })
                    {
                        string profile = Path.Combine(output, "fixture-profile-" + theme); new PreferencesStore(profile).Save(theme);
                        ExplorerFixture fixture = new ExplorerFixture(); fixture.Folder("/me/Documents"); fixture.Folder("/me/Documents/Projects"); fixture.Folder("/me/Archive"); fixture.Folder("/me/Design assets");
                        fixture.File("/me/Notes.txt", Encoding.UTF8.GetBytes("Visual QA fixture only")); fixture.File("/me/Documents/Quarterly report.pdf", new byte[4096]); fixture.File("/me/Documents/Meeting notes.txt", new byte[1024]);
                        fixture.Nodes["/me/Documents"].Id = "preview1"; fixture.LinkPermissions["/me/Documents"] = new bool[] { true, false, false };
                        using (PixelDrainClient client = new PixelDrainClient(fixture))
                        using (MainWindow main = new MainWindow(client, profile))
                        {
                            main.StartPosition = FormStartPosition.Manual; main.Location = new Point(8, 8); main.Show();
                            Call(main, "ActivateAccount", ExplorerFixture.Key, new List<CloudFile> { new CloudFile { Id = "file1234", Name = "Quarterly report.pdf", Mime = "application/pdf", Size = 6400000, Uploaded = DateTime.Now }, new CloudFile { Id = "file5678", Name = "Project notes.txt", Mime = "text/plain", Size = 4800, Uploaded = DateTime.Now.AddDays(-3) } });
                            await Task.Delay(80); Capture(main, Path.Combine(output, "files-" + theme + ".png"));
                            Call(main, "SelectVisible"); Capture(main, Path.Combine(output, "marked-files-" + theme + ".png"));
                            Call(main, "Navigate", "filesystem"); FilesystemWindow fs = (FilesystemWindow)Field(main, "filesystemView"); await fs.InitializeAsync(); await Task.Delay(80);
                            Capture(main, Path.Combine(output, "folders-" + theme + ".png"));
                            Size originalSize = main.Size; main.Size = new Size(1800, 900); Capture(main, Path.Combine(output, "wide-folders-" + theme + ".png")); main.Size = originalSize;
                            Call(fs, "ToggleMarked", "/me/Documents"); Call(fs, "ToggleMarked", "/me/Notes.txt"); Capture(main, Path.Combine(output, "marked-folders-" + theme + ".png")); Call(fs, "ClearMarks");
                            await (Task)Call(fs, "LoadFolder", "/me/Documents", System.Threading.CancellationToken.None); Capture(main, Path.Combine(output, "inherited-" + theme + ".png"));
                            Call(main, "Navigate", "transfers"); var jobs = (List<TransferJob>)Field(main, "jobs");
                            TransferJob upload = new TransferJob { Name = "Project-assets.zip", Direction = "Upload", Status = "Uploading" }; upload.Metrics.UpdateAt(67108864, 268435456, 12); jobs.Add(upload);
                            TransferJob download = new TransferJob { Name = "Quarterly report.pdf", Direction = "Download", Status = "Downloading" }; download.Metrics.UpdateAt(8388608, 16777216, 5); jobs.Add(download);
                            Call(main, "RenderTransfers"); Capture(main, Path.Combine(output, "transfers-" + theme + ".png"));
                            foreach (TransferJob job in jobs) { job.Finished = true; job.Metrics.Finish(true); job.Status = "Completed"; } jobs.Clear(); Call(main, "RenderTransfers");
                            Call(main, "Connection"); await Task.Delay(60); Capture(main, Path.Combine(output, "settings-" + theme + ".png"));
                            Call(main, "Navigate", "filesystem"); main.Size = main.MinimumSize; Capture(main, Path.Combine(output, "minimum-" + theme + ".png"));
                            main.Close();
                        }
                    }
                    result = 0;
                }
                catch (Exception ex) { Console.Error.WriteLine(ex); }
                finally { runner.Close(); }
            };
            Application.Run(runner);
        }
        return result;
    }
}
