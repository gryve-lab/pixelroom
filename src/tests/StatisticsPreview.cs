using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows.Forms;
using Pixelroom;
internal static class StatisticsPreview
{
    private static readonly BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private static object Field(object target, string name) { return target.GetType().GetField(name, Hidden).GetValue(target); }
    private static object Call(object target, string name, params object[] values) { return target.GetType().GetMethod(name, Hidden).Invoke(target, values); }
    private static void Capture(MainWindow main, string file) { main.PerformLayout(); main.Refresh(); using (Bitmap bitmap = new Bitmap(main.Width, main.Height)) { main.DrawToBitmap(bitmap, new Rectangle(Point.Empty, main.Size)); bitmap.Save(file, System.Drawing.Imaging.ImageFormat.Png); } Console.WriteLine(file); }
    [STAThread] private static int Main(string[] args)
    {
        Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false); System.Threading.Thread.CurrentThread.CurrentCulture = System.Globalization.CultureInfo.GetCultureInfo("en-US");
        string output = Path.GetFullPath(args[0]); Directory.CreateDirectory(output); int result = 1;
        using (Form runner = new Form { ShowInTaskbar = false, Opacity = 0, WindowState = FormWindowState.Minimized })
        {
            runner.Shown += async delegate
            {
                try
                {
                    foreach (string theme in new string[] { "Gray", "Dark" })
                    {
                        string profile = Path.Combine(output, "preview-profile-" + theme); new PreferencesStore(profile).Save(theme); var fixture = new StatisticsFixture { DailyPreview = true };
                        fixture.File("/me/Project overview.mp4", 120); fixture.File("/me/Design assets.zip", 58); fixture.File("/me/Product guide.pdf", 23); fixture.File("/me/Launch teaser.mp4", 91); fixture.File("/me/Read me.txt", 0);
                        using (var client = new PixelDrainClient(fixture))
                        using (var main = new MainWindow(client, profile))
                        {
                            main.StartPosition = FormStartPosition.Manual; main.Location = new Point(8, 8); main.Show(); Call(main, "ActivateAccount", ExplorerFixture.Key, new List<CloudFile>()); Call(main, "Navigate", "statistics"); var view = (StatisticsView)Field(main, "statisticsView"); await view.Initialization; await Task.Delay(60);
                            Capture(main, Path.Combine(output, "statistics-" + theme + ".png"));
                            ((ComboBox)Field(view, "metric")).SelectedIndex = 1; Capture(main, Path.Combine(output, "bandwidth-" + theme + ".png"));
                            main.Size = main.MinimumSize; await Task.Delay(30); Capture(main, Path.Combine(output, "minimum-" + theme + ".png"));
                            main.Size = new Size(1600, 950); await Task.Delay(30); Capture(main, Path.Combine(output, "wide-" + theme + ".png")); main.Close();
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
