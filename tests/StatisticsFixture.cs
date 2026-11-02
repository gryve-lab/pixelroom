using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Pixelroom;

internal sealed class StatisticsFixture : HttpMessageHandler
{
    public readonly ExplorerFixture Explorer = new ExplorerFixture();
    private readonly HttpMessageInvoker inner;
    public readonly Dictionary<string, long> Downloads = new Dictionary<string, long>();
    public readonly Dictionary<string, int> Errors = new Dictionary<string, int>();
    public readonly List<string> Queries = new List<string>();
    public int DelayMilliseconds;
    public bool DailyPreview;
    public StatisticsFixture() { inner = new HttpMessageInvoker(Explorer); }
    public void File(string path, long downloads) { Explorer.File(path, new byte[1]); Downloads[path] = downloads; }
    private static HttpResponseMessage Response(object value, int code = 200) { return new HttpResponseMessage((HttpStatusCode)code) { Content = new StringContent(Json.NewSerializer().Serialize(value), Encoding.UTF8, "application/json") }; }
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
    {
        if (request.RequestUri.Query.StartsWith("?timeseries"))
        {
            if (request.Method != HttpMethod.Get || request.RequestUri.Host != "pixeldrain.com" || request.Headers.Authorization == null || request.Headers.Authorization.Parameter != Convert.ToBase64String(Encoding.UTF8.GetBytes(":" + ExplorerFixture.Key))) throw new Exception("Statistics must only use authenticated GET requests to PixelDrain.");
            string path = Uri.UnescapeDataString(request.RequestUri.AbsolutePath.Substring("/api/filesystem".Length)); Queries.Add(request.RequestUri.Query);
            if (DelayMilliseconds > 0) await Task.Delay(DelayMilliseconds, token);
            int error; if (Errors.TryGetValue(path, out error)) return Response(new { success = false, value = error == 429 ? "ip_rate_limit_reached" : error == 401 ? "authentication_failed" : "path_not_found" }, error);
            var query = request.RequestUri.Query.Trim('?').Split('&').Where(s => s.Contains("=")).Select(s => s.Split(new char[] { '=' }, 2)).ToDictionary(s => s[0], s => Uri.UnescapeDataString(s[1]));
            DateTime start = DateTime.Parse(query["start"], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind); long count; Downloads.TryGetValue(path, out count);
            string[] stamps = { start.ToString("o"), start.AddHours(1).ToString("o"), start.AddHours(2).ToString("o") };
            if (DailyPreview)
            {
                stamps = Enumerable.Range(0, 7).Select(i => start.AddDays(i).ToString("o")).ToArray();
                long[] weights = new long[] { count, count * 2, count / 2, count * 3, count * 2, count * 4, count * 2 };
                return Response(new { downloads = new { timestamps = stamps, amounts = weights }, transfer_free = new { timestamps = stamps, amounts = weights.Select(v => v * 83886080).ToArray() }, transfer_paid = new { timestamps = stamps, amounts = weights.Select(v => v * 41943040).ToArray() } });
            }
            return Response(new { downloads = new { timestamps = stamps, amounts = new long[] { count, count * 2, count } }, transfer_free = new { timestamps = stamps, amounts = new long[] { count * 5368709120L, 0, 0 } }, transfer_paid = new { timestamps = stamps, amounts = new long[] { 0, count * 1024, 0 } } });
        }
        if (request.RequestUri.AbsolutePath.StartsWith("/api/file/") && request.RequestUri.AbsolutePath.EndsWith("/info")) return Response(new { downloads = 27, bandwidth_used = 6000000000L, bandwidth_used_paid = 1000000000L });
        return await inner.SendAsync(request, token);
    }
    protected override void Dispose(bool disposing) { if (disposing) inner.Dispose(); base.Dispose(disposing); }
}
