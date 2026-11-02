using System;
using System.Diagnostics;
using System.Globalization;

namespace Pixelroom
{
    public sealed class TransferMetrics
    {
        private readonly Stopwatch watch = Stopwatch.StartNew();
        private long sampledBytes;
        private double sampledAt, lastActivity, speed;
        private bool completed;
        public long Done { get; private set; }
        public long Total { get; private set; }
        public bool Finished { get; private set; }
        public int Percent { get { return Total > 0 ? (int)Math.Max(0, Math.Min(100, Done * 100.0 / Total)) : completed ? 100 : 0; } }
        public bool HasLength { get { return Total >= 0; } }
        public TransferMetrics() { Total = -1; }
        public void Reset() { watch.Restart(); Done = sampledBytes = 0; Total = -1; sampledAt = lastActivity = speed = 0; Finished = completed = false; }
        public void Update(long done, long total) { UpdateAt(done, total, watch.Elapsed.TotalSeconds); }
        public void UpdateAt(long done, long total, double seconds)
        {
            if (Finished) return;
            done = Math.Max(0, done); seconds = Math.Max(sampledAt, seconds);
            if (done < Done) { sampledBytes = 0; sampledAt = seconds; speed = 0; }
            if (done != Done) lastActivity = seconds;
            Done = done; Total = total < 0 ? -1 : total;
            double elapsed = seconds - sampledAt;
            if (elapsed >= .25)
            {
                double rate = Math.Max(0, done - sampledBytes) / elapsed;
                speed = speed == 0 ? rate : speed * .3 + rate * .7;
                sampledBytes = done; sampledAt = seconds;
            }
        }
        public double SpeedAt(double seconds) { return Finished || seconds - lastActivity >= 3 ? 0 : speed; }
        public double BytesPerSecond { get { return SpeedAt(watch.Elapsed.TotalSeconds); } }
        public string EtaAt(double seconds)
        {
            double rate = SpeedAt(seconds);
            if (Finished || Total < 0 || rate <= 0 || Done >= Total) return "—";
            double remaining = Math.Ceiling((Total - Done) / rate);
            if (remaining > 86400 * 99) return "> 99 days";
            TimeSpan time = TimeSpan.FromSeconds(remaining);
            return time.TotalHours >= 1 ? ((int)time.TotalHours).ToString(CultureInfo.InvariantCulture) + "h " + time.Minutes + "m"
                : time.TotalMinutes >= 1 ? time.Minutes + "m " + time.Seconds + "s" : time.Seconds + "s";
        }
        public string Eta { get { return EtaAt(watch.Elapsed.TotalSeconds); } }
        public void Finish(bool complete) { completed = complete; if (complete && Total >= 0) Done = Total; Finished = true; speed = 0; }
        public string BytesText { get { return FileRules.SizeText(Done) + (Total >= 0 ? " / " + FileRules.SizeText(Total) : " transferred"); } }
        public string SpeedText { get { return FileRules.SizeText((long)Math.Min(Int64.MaxValue, BytesPerSecond)) + "/s"; } }
    }
}
