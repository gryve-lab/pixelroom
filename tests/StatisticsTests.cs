using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Pixelroom;

internal static partial class Tests
{
    private static async Task CheckStatistics()
    {
        TimeZoneInfo zone = TimeZoneInfo.CreateCustomTimeZone("Statistics test", TimeSpan.FromHours(8), "Statistics test", "Statistics test");
        var period = StatisticsPeriod.FromDates(new DateTime(2026, 10, 1), new DateTime(2026, 10, 2), zone, new DateTime(2026, 10, 25, 1, 0, 0, DateTimeKind.Utc));
        Assert(period.StartUtc == new DateTime(2026, 9, 30, 16, 0, 0, DateTimeKind.Utc) && period.EndUtc == new DateTime(2026, 10, 2, 16, 0, 0, DateTimeKind.Utc), "Both chosen local dates must be fully included and translated to UTC.");
        bool invalid = false; try { StatisticsPeriod.FromDates(DateTime.Today, DateTime.Today.AddDays(-1), zone, DateTime.UtcNow); } catch (ArgumentException) { invalid = true; } Assert(invalid, "Reversed statistics ranges must fail before networking.");
        invalid = false; try { StatisticsPeriod.FromDates(new DateTime(2020, 1, 1), new DateTime(2022, 1, 1), zone, DateTime.UtcNow); } catch (ArgumentException) { invalid = true; } Assert(invalid, "Large ranges must be bounded for 32-bit memory safety.");
        Pass("statistics dates use inclusive local calendar days, UTC requests and bounded ranges");

        StatisticsFixture fixture = new StatisticsFixture(); fixture.File("/me/é #%.txt", 3); fixture.Explorer.Folder("/me/Nested"); fixture.File("/me/Nested/item.txt", 2);
        using (PixelDrainClient client = new PixelDrainClient(fixture))
        {
            var points = await client.GetFilesystemStatisticsAsync("/me/é #%.txt", period, ExplorerFixture.Key, CancellationToken.None); var row = StatisticsData.Summarize("Example", "/me/é #%.txt", points);
            Assert(row.Downloads == 12 && row.FreeBytes == 16106127360L && row.PaidBytes == 3072 && row.LastActivityUtc == period.StartUtc.AddHours(2), "Statistics must retain 64-bit transfer values and the last active bucket.");
            Assert(fixture.Queries.Single().Contains("interval=60") && fixture.Queries.Single().Contains("2026-09-30T16%3A00%3A00"), "Statistics must encode UTC parameters rather than local upload dates.");
            Assert(StatisticsData.Aggregate(points, true, zone).Single().Downloads == 12 && StatisticsData.Aggregate(points, false, zone).Count == 3, "Daily and hourly charts must preserve totals.");
            var all = await client.DiscoverStatisticsFilesAsync("/me", true, ExplorerFixture.Key, null, CancellationToken.None); var direct = await client.DiscoverStatisticsFilesAsync("/me", false, ExplorerFixture.Key, null, CancellationToken.None);
            Assert(all.Files.Count == 2 && direct.Files.Count == 1 && all.SkippedFolders == 0 && fixture.Explorer.Mutations.Count == 0, "Statistics discovery must read files recursively without modifying them or including folder totals twice.");
            var flat = await client.GetFileStatisticsAsync(new CloudFile { Id = "abcd1234", Name = "Flat" }, ExplorerFixture.Key, CancellationToken.None);
            Assert(flat.Lifetime && flat.Downloads == 27 && flat.FreeBytes == 5000000000L && flat.PaidBytes == 1000000000L, "My files lifetime counts must stay separate from dated history.");
        }
        Pass("authenticated per-file history, large transfers, recursive discovery and lifetime totals agree");

        var body = new Dictionary<string, object>();
        Func<string, object> series = amount => new { timestamps = new string[] { period.StartUtc.ToString("o"), period.EndUtc.ToString("o") }, amounts = new string[] { amount, "999" } };
        body = Json.Object(Json.NewSerializer().Serialize(new { downloads = series("5"), transfer_free = series("0"), transfer_paid = series("0") }));
        Assert(StatisticsData.Parse(body, period).Single().Downloads == 5, "The end boundary must be exclusive, so adjacent date ranges do not double count.");
        invalid = false; try { StatisticsData.Parse(Json.Object("{\"path\":[],\"children\":[]}"), period); } catch (IOException) { invalid = true; } Assert(invalid, "A folder stat response must not be mistaken for zero downloads.");
        body = Json.Object(Json.NewSerializer().Serialize(new { downloads = series("-1"), transfer_free = series("0"), transfer_paid = series("0") }));
        invalid = false; try { StatisticsData.Parse(body, period); } catch (IOException) { invalid = true; } Assert(invalid, "Malformed counts must remain unavailable, not become zero.");
        Pass("statistics reject missing or invalid series and prevent date-boundary double counting");

        fixture = new StatisticsFixture(); fixture.File("/me/item.txt", 3); fixture.DelayMilliseconds = 200;
        using (PixelDrainClient client = new PixelDrainClient(fixture))
        using (var cancel = new CancellationTokenSource(15))
        {
            bool cancelled = false; try { await client.GetFilesystemStatisticsAsync("/me/item.txt", period, ExplorerFixture.Key, cancel.Token); } catch (OperationCanceledException) { cancelled = true; } Assert(cancelled && fixture.Explorer.Mutations.Count == 0, "Cancelling statistics must stop reads without changing the account.");
            fixture.DelayMilliseconds = 0; fixture.Errors["/me/item.txt"] = 429; bool limited = false;
            try { await client.GetFilesystemStatisticsAsync("/me/item.txt", period, ExplorerFixture.Key, CancellationToken.None); } catch (StatisticsRequestException ex) { limited = ex.Status == 429; } Assert(limited, "Rate limits need a distinct signal to stop repeated scans.");
        }
        string csv = StatisticsData.Csv(new StatisticsRow[] { new StatisticsRow { Name = "=HYPERLINK(\"x\")", Path = "/me/name,with comma", Downloads = 4, Error = null }, new StatisticsRow { Name = "Unavailable", Error = "Missing" } }, "Dates");
        Assert(csv.Contains("' =") == false && csv.Contains("'" + "=HYPERLINK") && csv.Contains("\"/me/name,with comma\"") && !csv.Contains(ExplorerFixture.Key), "CSV exports must quote names, neutralize spreadsheet formulas and never contain the key.");
        Pass("statistics cancellation, rate-limit signals and CSV exports preserve user data");
    }
}
