using System.Diagnostics;

namespace BetterJoyForCemu {
    // Per-controller timing counters, summarised and reset every window when DebugType is Latency (7).
    public class LatencyStats {
        private static readonly double msPerTick = 1000.0 / Stopwatch.Frequency;
        private static readonly long windowTicks = 5 * Stopwatch.Frequency;
        // A read that returns data faster than this found a report already waiting in the HID buffer.
        private static readonly long bufferedReadTicks = Stopwatch.Frequency / 2000;

        private struct Sample {
            public int count;
            public long sum, max;

            public void Add(long ticks) {
                count++;
                sum += ticks;
                if (ticks > max) max = ticks;
            }

            public override string ToString() {
                return count == 0 ? "n/a" : string.Format("avg {0:F1} max {1:F1} ms", sum * msPerTick / count, max * msPerTick);
            }
        }

        private readonly object sync = new object();
        private long windowStart = Stopwatch.GetTimestamp();
        private long lastReport;
        private int bufferedReads, bufferedRun, maxBufferedRun, duplicates;
        private Sample interval, process, toVpad, rumble;

        public bool SummaryDue {
            get { return Stopwatch.GetTimestamp() - windowStart >= windowTicks; }
        }

        public void AddReport(long readStart, long readEnd) {
            lock (sync) {
                if (lastReport != 0) interval.Add(readEnd - lastReport);
                lastReport = readEnd;

                if (readEnd - readStart < bufferedReadTicks) {
                    bufferedReads++;
                    if (++bufferedRun > maxBufferedRun) maxBufferedRun = bufferedRun;
                } else {
                    bufferedRun = 0;
                }
            }
        }

        public void AddProcessing(long ticks) {
            lock (sync) process.Add(ticks);
        }

        public void AddReportToVpad(long ticks) {
            lock (sync) toVpad.Add(ticks);
        }

        public void AddRumbleWrite(long ticks) {
            lock (sync) rumble.Add(ticks);
        }

        public void AddDuplicateTimestamp() {
            lock (sync) duplicates++;
        }

        public string TakeSummary() {
            lock (sync) {
                long now = Stopwatch.GetTimestamp();
                double seconds = (now - windowStart) * msPerTick / 1000;
                int reports = process.count;
                string summary = string.Format(
                    "{0:F1} reports/s | interval {1} | buffered reads {2}% (max run {3}) | process {4} | report->vpad {5} | rumble writes {6}: {7} | dup ts {8}",
                    reports / seconds, interval, reports == 0 ? 0 : 100 * bufferedReads / reports, maxBufferedRun,
                    process, toVpad, rumble.count, rumble, duplicates);

                windowStart = now;
                bufferedReads = maxBufferedRun = duplicates = 0;
                interval = process = toVpad = rumble = new Sample();
                return summary;
            }
        }
    }
}
