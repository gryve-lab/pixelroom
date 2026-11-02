using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Pixelroom
{
    public sealed class StatisticsRequestException : IOException
    {
        public int Status { get; private set; }
        public StatisticsRequestException(int status, string message) : base(message) { Status = status; }
    }
    public sealed class StatisticsPeriod
    {
        public DateTime StartUtc, EndUtc;
        public static StatisticsPeriod FromDates(DateTime first, DateTime last, TimeZoneInfo zone, DateTime nowUtc)
        {
            first = DateTime.SpecifyKind(first.Date, DateTimeKind.Unspecified); last = DateTime.SpecifyKind(last.Date, DateTimeKind.Unspecified);
            if (last < first || (last - first).TotalDays >= 366) throw new ArgumentException("Choose a date range of 1 to 366 days.");
            DateTime start = TimeZoneInfo.ConvertTimeToUtc(first, zone), end = TimeZoneInfo.ConvertTimeToUtc(last.AddDays(1), zone);
            end = end < nowUtc ? end : nowUtc;
            if (end <= start) throw new ArgumentException("Choose dates up to today.");
            return new StatisticsPeriod { StartUtc = start, EndUtc = end };
        }
        internal string Query
        {
            get
            {
                if (StartUtc.Kind != DateTimeKind.Utc || EndUtc.Kind != DateTimeKind.Utc || EndUtc <= StartUtc || (EndUtc - StartUtc).TotalDays > 367) throw new ArgumentException("Invalid statistics period.");
                return "?timeseries&start=" + Uri.EscapeDataString(StartUtc.ToString("o", CultureInfo.InvariantCulture)) + "&end=" + Uri.EscapeDataString(EndUtc.ToString("o", CultureInfo.InvariantCulture)) + "&interval=60";
            }
        }
    }
    public sealed class StatisticsPoint
    {
        public DateTime TimeUtc;
        public long Downloads, FreeBytes, PaidBytes;
        public long Bytes { get { return checked(FreeBytes + PaidBytes); } }
    }
    public sealed class StatisticsRow
    {
        public string Name, Path, Id, Error;
        public bool Lifetime;
        public long Downloads, FreeBytes, PaidBytes;
        public long Bytes { get { return checked(FreeBytes + PaidBytes); } }
        public DateTime? LastActivityUtc;
    }
    public sealed class StatisticsScanProgress
    {
        public int Discovered, FolderErrors;
        public string Name;
    }
    public sealed class StatisticsCatalog
    {
        public List<FilesystemNode> Files = new List<FilesystemNode>();
        public int SkippedFolders;
    }
    public static class StatisticsData
    {
        internal static long Number(object value)
        {
            long result; if (value == null || !Int64.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), NumberStyles.Integer, CultureInfo.InvariantCulture, out result) || result < 0) throw new IOException("PixelDrain returned invalid statistics."); return result;
        }
        private static Dictionary<DateTime, long> Series(Dictionary<string, object> data, string name)
        {
            object raw, stamps, amounts;
            if (!data.TryGetValue(name, out raw) || !(raw is Dictionary<string, object>)) throw new IOException("Statistics are unavailable for this item.");
            var series = (Dictionary<string, object>)raw;
            if (!series.TryGetValue("timestamps", out stamps) || !(stamps is IEnumerable) || stamps is string || !series.TryGetValue("amounts", out amounts) || !(amounts is IEnumerable) || amounts is string) throw new IOException("The statistics response is incomplete.");
            object[] dates = ((IEnumerable)stamps).Cast<object>().ToArray(), values = ((IEnumerable)amounts).Cast<object>().ToArray();
            if (dates.Length != values.Length || dates.Length > 10000) throw new IOException("The statistics response is incomplete or too large.");
            var result = new Dictionary<DateTime, long>();
            for (int i = 0; i < dates.Length; i++)
            {
                DateTimeOffset date; if (!DateTimeOffset.TryParse(Convert.ToString(dates[i], CultureInfo.InvariantCulture), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out date) || result.ContainsKey(date.UtcDateTime)) throw new IOException("PixelDrain returned invalid statistics timestamps.");
                result.Add(date.UtcDateTime, Number(values[i]));
            }
            return result;
        }
        public static List<StatisticsPoint> Parse(Dictionary<string, object> data, StatisticsPeriod period)
        {
            var downloads = Series(data, "downloads"); var free = Series(data, "transfer_free"); var paid = Series(data, "transfer_paid");
            var times = new SortedSet<DateTime>(downloads.Keys.Concat(free.Keys).Concat(paid.Keys)); var points = new List<StatisticsPoint>();
            foreach (DateTime time in times)
            {
                if (time < period.StartUtc || time >= period.EndUtc) continue;
                long d, f, p; downloads.TryGetValue(time, out d); free.TryGetValue(time, out f); paid.TryGetValue(time, out p);
                try { checked { long total = f + p; } } catch (OverflowException) { throw new IOException("Statistics exceed the supported range."); }
                points.Add(new StatisticsPoint { TimeUtc = time, Downloads = d, FreeBytes = f, PaidBytes = p });
            }
            return points;
        }
        public static StatisticsRow Summarize(string name, string path, IList<StatisticsPoint> points)
        {
            var row = new StatisticsRow { Name = name, Path = path };
            try { foreach (StatisticsPoint point in points) { row.Downloads = checked(row.Downloads + point.Downloads); row.FreeBytes = checked(row.FreeBytes + point.FreeBytes); row.PaidBytes = checked(row.PaidBytes + point.PaidBytes); if (point.Downloads > 0 || point.Bytes > 0) row.LastActivityUtc = point.TimeUtc; } long bytes = row.Bytes; }
            catch (OverflowException) { throw new IOException("Statistics exceed the supported range."); }
            return row;
        }
        public static List<StatisticsPoint> Aggregate(IEnumerable<StatisticsPoint> points, bool daily, TimeZoneInfo zone)
        {
            return points.GroupBy(p => { DateTime local = TimeZoneInfo.ConvertTimeFromUtc(p.TimeUtc, zone); return daily ? local.Date : new DateTime(local.Year, local.Month, local.Day, local.Hour, 0, 0); }).OrderBy(g => g.Key).Select(g => new StatisticsPoint { TimeUtc = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(g.Key, DateTimeKind.Unspecified), zone), Downloads = g.Sum(p => p.Downloads), FreeBytes = g.Sum(p => p.FreeBytes), PaidBytes = g.Sum(p => p.PaidBytes) }).ToList();
        }
        public static string Csv(IEnumerable<StatisticsRow> rows, string period)
        {
            Func<string, string> cell = value => { value = value ?? ""; if (value.Length > 0 && "=+-@\t\r".IndexOf(value[0]) >= 0) value = "'" + value; return "\"" + value.Replace("\"", "\"\"") + "\""; };
            var text = new StringBuilder("Name,Path,Period,Downloads,Bandwidth bytes,Free bytes,Premium bytes,Last activity UTC,Status\r\n");
            foreach (StatisticsRow row in rows) text.AppendLine(String.Join(",", new string[] { cell(row.Name), cell(row.Path ?? row.Id), cell(period), row.Error == null ? row.Downloads.ToString(CultureInfo.InvariantCulture) : "", row.Error == null ? row.Bytes.ToString(CultureInfo.InvariantCulture) : "", row.Error == null ? row.FreeBytes.ToString(CultureInfo.InvariantCulture) : "", row.Error == null ? row.PaidBytes.ToString(CultureInfo.InvariantCulture) : "", cell(row.LastActivityUtc.HasValue ? row.LastActivityUtc.Value.ToString("o") : ""), cell(row.Error ?? "Ready") }));
            return text.ToString();
        }
    }
    public sealed partial class PixelDrainClient
    {
        private async Task<Dictionary<string, object>> StatisticsRequestAsync(string route, string key, CancellationToken token)
        {
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(token))
            using (HttpRequestMessage request = Request(HttpMethod.Get, route, key))
            {
                timeout.CancelAfter(TimeSpan.FromSeconds(45));
                using (HttpResponseMessage response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false))
                {
                    if ((int)response.StatusCode >= 300 && (int)response.StatusCode < 400) throw new IOException("PixelDrain returned an unexpected redirect.");
                    if (response.Content.Headers.ContentType == null || response.Content.Headers.ContentType.MediaType.IndexOf("json", StringComparison.OrdinalIgnoreCase) < 0) throw new IOException("Statistics are unavailable for this item.");
                    using (Stream input = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                    using (var memory = new MemoryStream())
                    {
                        byte[] buffer = new byte[16384]; int read;
                        while ((read = await input.ReadAsync(buffer, 0, buffer.Length, timeout.Token).ConfigureAwait(false)) > 0) { if (memory.Length + read > 8 * 1024 * 1024) throw new IOException("The statistics response is too large. Choose a shorter date range."); memory.Write(buffer, 0, read); }
                        var data = Json.Object(Encoding.UTF8.GetString(memory.ToArray()));
                        if (!response.IsSuccessStatusCode || Json.Text(data, "success").Equals("false", StringComparison.OrdinalIgnoreCase)) throw new StatisticsRequestException((int)response.StatusCode, ErrorMessage(data, (int)response.StatusCode));
                        return data;
                    }
                }
            }
        }
        public async Task<List<StatisticsPoint>> GetFilesystemStatisticsAsync(string path, StatisticsPeriod period, string key, CancellationToken token)
        {
            return StatisticsData.Parse(await StatisticsRequestAsync(FilesystemRules.Route(path) + period.Query, key, token).ConfigureAwait(false), period);
        }
        public async Task<StatisticsRow> GetFileStatisticsAsync(CloudFile file, string key, CancellationToken token)
        {
            FileRules.CheckId(file.Id); var data = await StatisticsRequestAsync("/file/" + Uri.EscapeDataString(file.Id) + "/info", key, token).ConfigureAwait(false);
            object d, bytes, paid; if (!data.TryGetValue("downloads", out d) || !data.TryGetValue("bandwidth_used", out bytes)) throw new IOException("Statistics are unavailable for this item.");
            long total = StatisticsData.Number(bytes), premium = data.TryGetValue("bandwidth_used_paid", out paid) ? StatisticsData.Number(paid) : 0;
            if (premium > total) throw new IOException("PixelDrain returned inconsistent transfer statistics.");
            return new StatisticsRow { Name = file.Name, Id = file.Id, Lifetime = true, Downloads = StatisticsData.Number(d), FreeBytes = total - premium, PaidBytes = premium };
        }
        public async Task<StatisticsCatalog> DiscoverStatisticsFilesAsync(string root, bool recursive, string key, IProgress<StatisticsScanProgress> progress, CancellationToken token)
        {
            var queue = new Queue<string>(); queue.Enqueue(FilesystemRules.Normalize(root)); var files = new List<FilesystemNode>(); var seen = new HashSet<string>(StringComparer.Ordinal); int errors = 0;
            while (queue.Count > 0)
            {
                token.ThrowIfCancellationRequested(); string path = queue.Dequeue(); if (!seen.Add(path)) continue;
                FilesystemDirectory folder;
                try { folder = await ListFilesystemAsync(path, key, token).ConfigureAwait(false); if (!folder.CanRead) throw new IOException("You cannot read this folder."); }
                catch (IOException) { if (path == FilesystemRules.Normalize(root)) throw; errors++; continue; }
                foreach (FilesystemNode node in folder.Children) { if (node.IsDirectory) { if (recursive) queue.Enqueue(node.Path); } else files.Add(node); }
                if (files.Count + queue.Count + seen.Count > 50000) throw new IOException("This scan exceeds 50,000 items. Choose a smaller folder.");
                if (progress != null) progress.Report(new StatisticsScanProgress { Name = path, Discovered = files.Count, FolderErrors = errors });
            }
            if (progress != null) progress.Report(new StatisticsScanProgress { Discovered = files.Count, FolderErrors = errors });
            return new StatisticsCatalog { Files = files, SkippedFolders = errors };
        }
    }
}
