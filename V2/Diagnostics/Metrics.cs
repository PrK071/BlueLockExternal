// C# | Diagnostics/Metrics.cs | .NET Framework 4.8 x64 | Bounded timings, process telemetry and rotating JSONL.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Globalization;

namespace BlueLockV2.Diagnostics
{
    public sealed class StageMetrics
    {
        readonly double[] values = new double[128];
        int index, count;
        readonly object gate = new object ();
        public void Add(double ms)
        {
            lock (gate)
            {
                values[index++ % values.Length] = ms;
                count = Math.Min(count + 1, values.Length);
            }
        }

        public double Percentile(double percentile)
        {
            lock (gate)
            {
                if (count == 0)
                    return 0;
                double[] data = new double[count];
                Array.Copy(values, data, count);
                Array.Sort(data);
                return data[(int)Math.Min(count - 1, Math.Floor(percentile * (count - 1)))];
            }
        }
    }

    public sealed class Telemetry : IDisposable
    {
        readonly Process process = Process.GetCurrentProcess();
        readonly object gate = new object ();
        readonly Stopwatch clock = Stopwatch.StartNew();
        double last;
        TimeSpan cpu;
        public readonly long BaselinePrivate;
        public double CpuPercent, PrivateMb, WorkingMb, PeakPrivateMb;
        public readonly StageMetrics Capture = new StageMetrics(), Vision = new StageMetrics(), Tracking = new StageMetrics(), Prediction = new StageMetrics(), Decision = new StageMetrics(), Latency = new StageMetrics();
        public long Captured, Processed, Stale, Decisions, Failures;
        public string LastError = "";
        public Telemetry()
        {
            process.Refresh();
            BaselinePrivate = process.PrivateMemorySize64;
            cpu = process.TotalProcessorTime;
        }

        public void Sample()
        {
            lock (gate)
            {
                double now = clock.Elapsed.TotalSeconds;
                if (now - last < .5)
                    return;
                process.Refresh();
                TimeSpan current = process.TotalProcessorTime;
                CpuPercent = (current - cpu).TotalSeconds / Math.Max(.001, now - last) / Environment.ProcessorCount * 100;
                last = now;
                cpu = current;
                PrivateMb = process.PrivateMemorySize64 / 1048576.0;
                WorkingMb = process.WorkingSet64 / 1048576.0;
                PeakPrivateMb = Math.Max(PeakPrivateMb, PrivateMb);
            }
        }

        public void Dispose()
        {
            process.Dispose();
        }
    }

    public sealed class StructuredLog : IDisposable
    {
        readonly object gate = new object ();
        readonly string path;
        StreamWriter writer;
        long bytes;
        int closed;
        public StructuredLog(string file)
        {
            path = file;
            Directory.CreateDirectory(Path.GetDirectoryName(file));
            Open();
        }

        void Open()
        {
            writer = new StreamWriter(path, true, System.Text.Encoding.UTF8);
            bytes = new FileInfo(path).Length;
        }

        public void Write(string module, string action, string detail)
        {
            lock (gate)
            {
                if (closed != 0)
                    return;
                try
                {
                    if (bytes > 2 * 1024 * 1024)
                    {
                        writer.Dispose();
                        string old = path + ".previous";
                        if (File.Exists(old))
                            File.Delete(old);
                        File.Move(path, old);
                        Open();
                    }

                    string line = "{\"utc\":\"" + DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture) + "\",\"module\":\"" + Escape(module) + "\",\"action\":\"" + Escape(action) + "\",\"detail\":\"" + Escape(detail) + "\"}";
                    writer.WriteLine(line);
                    writer.Flush();
                    bytes += line.Length * 2;
                }
                catch (IOException)
                {
                }
            }
        }

        static string Escape(string s)
        {
            return (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "\\r").Replace("\n", "\\n");
        }

        public void Dispose()
        {
            lock (gate)
            {
                closed = 1;
                writer.Dispose();
            }
        }
    }
}
