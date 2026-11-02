using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Pixelroom;

internal static partial class ConnectionTests
{
    private static async Task CheckStatisticsWindow(string root)
    {
        StatisticsFixture fixture = new StatisticsFixture(); fixture.File("/me/Alpha.mp4", 3); fixture.File("/me/Zero.txt", 0); fixture.Explorer.Folder("/me/Nested"); fixture.File("/me/Nested/Beta.mp4", 2); fixture.File("/me/Missing.txt", 1); fixture.Errors["/me/Missing.txt"] = 404;
        using (PixelDrainClient client = new PixelDrainClient(fixture))
        using (MainWindow main = new MainWindow(client, Path.Combine(root, "statistics")))
        {
            main.Show(); Call(main, "ActivateAccount", ExplorerFixture.Key, new List<CloudFile> { new CloudFile { Id = "abcd1234", Name = "Flat.txt" } });
            int topLevels = Application.OpenForms.Cast<Form>().Count(f => f.TopLevel && f.Visible);
            Call(main, "Navigate", "statistics"); StatisticsView view = (StatisticsView)Field(main, "statisticsView"); await view.Initialization;
            Assert(view.Parent == Field(main, "center") && Application.OpenForms.Cast<Form>().Count(f => f.TopLevel && f.Visible) == topLevels, "Statistics must open inside the existing window.");
            List<StatisticsRow> rows = (List<StatisticsRow>)Field(view, "rows"), shown = (List<StatisticsRow>)Field(view, "shown");
            Assert(rows.Count == 4 && shown.Count == 2 && shown[0].Name == "Alpha.mp4" && shown[0].Downloads == 12 && ((StatisticsSummary)Field(view, "summary")).Downloads == "20", "Downloaded-only results must sort verified period totals and leave missing statistics unavailable.");
            Assert(((Label)Field(view, "status")).Text.Contains("incomplete") && fixture.Explorer.Mutations.Count == 0, "Failed item statistics must disclose partial results without changing files.");
            Assert(((StatisticsChart)Field(view, "chart")).ContentName == "Alpha.mp4" && ((List<StatisticsPoint>)Field(view, "selectedPoints")).Count == 3, "Selecting a file must show its own time series.");
            Pass("Statistics stays in one window and shows correct per-file charts, ranked results and partial-data status");

            ((ComboBox)Field(view, "ordering")).SelectedIndex = 3; ((TextBox)Field(view, "search")).Text = "Beta"; await Task.Delay(40);
            Assert(((List<StatisticsRow>)Field(view, "shown")).Single().Name == "Beta.mp4" && ((StatisticsChart)Field(view, "chart")).ContentName == "Beta.mp4", "Filtering must replace the chart instead of leaving another file's data visible.");
            Call(main, "Navigate", "filesystem"); await ((FilesystemWindow)Field(main, "filesystemView")).InitializeAsync(); Call(main, "Navigate", "statistics");
            Assert(Object.ReferenceEquals(view, Field(main, "statisticsView")) && ((TextBox)Field(view, "search")).Text == "Beta", "Sidebar navigation must preserve the Statistics view and its filters.");
            int before = fixture.Queries.Count; ((DateTimePicker)Field(view, "from")).Value = DateTime.Today.AddDays(-2);
            Assert(fixture.Queries.Count == before && ((Label)Field(view, "status")).Text.Contains("Filters changed"), "Date changes must mark old results as pending until Refresh.");
            await view.RefreshAsync(); Assert(Uri.UnescapeDataString(fixture.Queries.Last()).Contains(DateTime.Today.AddDays(-2).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture)), "Refresh must apply the chosen date range to the API query. " + fixture.Queries.Last());
            Assert(((StatisticsChart)Field(view, "chart")).ContentName == "Beta.mp4", "Refreshing must preserve the selected content chart.");
            KeyEventArgs find = new KeyEventArgs(Keys.Control | Keys.F); Call(main, "HandleKeys", main, find); Assert(find.SuppressKeyPress && ((TextBox)Field(view, "search")).Focused, "Ctrl+F must search Statistics rather than hidden file lists.");
            main.Size = main.MinimumSize; main.PerformLayout(); await Task.Delay(20);
            Assert(((StatisticsChart)Field(view, "chart")).Height >= 80 && ((Button)Field(view, "refresh")).Right <= ((Button)Field(view, "refresh")).Parent.ClientSize.Width, "Statistics charts and controls must remain usable at minimum window size.");
            foreach (TableLayoutPanel toolbar in view.Controls[0].Controls.OfType<TableLayoutPanel>()) Assert(toolbar.Controls.Cast<Control>().All(c => c.Bottom <= toolbar.ClientSize.Height && c.Right <= toolbar.ClientSize.Width), "Statistics toolbar controls must fit their row without overlapping the next row.");
            Pass("Statistics filters, date refresh, keyboard search and minimum-size charts remain consistent");

            ((TextBox)Field(view, "search")).Clear(); ((ComboBox)Field(view, "source")).SelectedIndex = 1; await view.RefreshAsync();
            shown = (List<StatisticsRow>)Field(view, "shown"); Assert(shown.Count == 1 && shown[0].Lifetime && shown[0].Downloads == 27 && !((DateTimePicker)Field(view, "from")).Enabled && ((StatisticsChart)Field(view, "chart")).Lifetime, "My files must show lifetime counters without presenting them as date-filtered history.");
            Assert(((StatisticsChart)Field(view, "chart")).RangeLabel.Contains("history is unavailable"), "Lifetime charts must explain the available scope.");
            Pass("My files Statistics clearly separates lifetime totals from File System date history");
            main.Close();
        }
        fixture = new StatisticsFixture(); fixture.File("/me/Slow.txt", 3); fixture.DelayMilliseconds = 200;
        using (PixelDrainClient client = new PixelDrainClient(fixture))
        using (MainWindow main = new MainWindow(client, Path.Combine(root, "statistics-cancellation")))
        {
            main.Show(); Call(main, "ActivateAccount", ExplorerFixture.Key, new List<CloudFile>()); Call(main, "Navigate", "statistics"); var view = (StatisticsView)Field(main, "statisticsView"); Task pending = view.Initialization;
            await Task.Delay(20); Call(main, "Disconnect"); await pending;
            Assert(view.IsDisposed && Field(main, "statisticsView") == null && Field(main, "apiKey") == null && fixture.Explorer.Mutations.Count == 0, "Disconnect must dispose Statistics and suppress late responses from the old account."); main.Close();
        }
        Pass("disconnect cancels statistics reads and suppresses late results from the previous account");
    }
}
