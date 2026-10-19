// C# | Capture/Frames.cs | .NET Framework 4.8 x64 | Pooled frames and replay/capture contracts.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Threading;
using OpenCvSharp;
using BlueLockV2.Core;

namespace BlueLockV2.Capture
{
    public sealed class MatPool : IDisposable
    {
        readonly Stack<Mat> free = new Stack<Mat>();
        readonly int limit;
        bool closed;
        public int Created, Outstanding, Peak;
        public MatPool(int capacity)
        {
            limit = capacity;
        }

        public Mat Rent()
        {
            lock (free)
            {
                if (closed)
                    throw new ObjectDisposedException("MatPool");
                Mat m = free.Count > 0 ? free.Pop() : new Mat();
                if (m.Empty())
                    Created++;
                Outstanding++;
                Peak = Math.Max(Peak, Outstanding);
                return m;
            }
        }

        public void Return(Mat mat)
        {
            lock (free)
            {
                Outstanding--;
                if (closed || free.Count >= limit)
                    mat.Dispose();
                else
                    free.Push(mat);
            }
        }

        public void Dispose()
        {
            lock (free)
            {
                closed = true;
                while (free.Count > 0)
                    free.Pop().Dispose();
            }
        }
    }

    public sealed class Frame : IDisposable
    {
        public readonly Mat Image;
        public readonly double Timestamp;
        public readonly long Id;
        public readonly int Width, Height;
        public readonly Rect Region;
        readonly MatPool pool;
        int disposed;
        public Frame(Mat image, MatPool owner, double time, long id, int width, int height, Rect region)
        {
            Image = image;
            pool = owner;
            Timestamp = time;
            Id = id;
            Width = width;
            Height = height;
            Region = region;
        }

        public Vec ToScreen(double x, double y)
        {
            return new Vec(Region.X + x * Region.Width / Image.Width, Region.Y + y * Region.Height / Image.Height);
        }

        public double Scale
        {
            get
            {
                return (double)Region.Width / Image.Width;
            }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) == 0)
            {
                if (pool == null)
                    Image.Dispose();
                else
                    pool.Return(Image);
            }
        }
    }

    public interface IFrameSource : IDisposable
    {
        Frame Read(MatPool pool, int width, Rect? roi);
        SourceKind Kind { get; }

        bool Completed { get; }

        string Description { get; }
    }

    public static class Pixels
    {
        public static Bitmap Bitmap(Mat source)
        {
            using (Mat bgr = new Mat())
            {
                if (source.Channels() == 4)
                    Cv2.CvtColor(source, bgr, ColorConversionCodes.BGRA2BGR);
                else if (source.Channels() == 1)
                    Cv2.CvtColor(source, bgr, ColorConversionCodes.GRAY2BGR);
                else
                    source.CopyTo(bgr);
                Bitmap result = new Bitmap(bgr.Width, bgr.Height, PixelFormat.Format24bppRgb);
                var data = result.LockBits(new Rectangle(0, 0, result.Width, result.Height), ImageLockMode.WriteOnly, result.PixelFormat);
                try
                {
                    byte[] row = new byte[bgr.Width * 3];
                    for (int y = 0; y < bgr.Height; y++)
                    {
                        Marshal.Copy(bgr.Ptr(y), row, 0, row.Length);
                        Marshal.Copy(row, 0, IntPtr.Add(data.Scan0, y * data.Stride), row.Length);
                    }
                }
                finally
                {
                    result.UnlockBits(data);
                }

                return result;
            }
        }

        public static void Copy(Bitmap bitmap, Mat destination)
        {
            var data = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height), ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
            try
            {
                using (Mat header = Mat.FromPixelData(bitmap.Height, bitmap.Width, MatType.CV_8UC3, data.Scan0, data.Stride))
                    header.CopyTo(destination);
            }
            finally
            {
                bitmap.UnlockBits(data);
            }
        }

        public static Frame Resize(Mat raw, MatPool pool, int width, Rect? requested, double timestamp, long id)
        {
            Rect area = requested ?? new Rect(0, 0, raw.Width, raw.Height);
            area = area.Intersect(new Rect(0, 0, raw.Width, raw.Height));
            if (area.Width < 4 || area.Height < 4)
                area = new Rect(0, 0, raw.Width, raw.Height);
            int w = Math.Min(width, area.Width), h = Math.Max(1, (int)Math.Round((double)area.Height * w / area.Width));
            Mat output = pool.Rent();
            try
            {
                using (Mat crop = new Mat(raw, area))
                    Cv2.Resize(crop, output, new OpenCvSharp.Size(w, h), 0, 0, InterpolationFlags.Area);
                return new Frame(output, pool, timestamp, id, raw.Width, raw.Height, area);
            }
            catch
            {
                pool.Return(output);
                throw;
            }
        }
    }

    public sealed class ImageSequenceSource : IFrameSource
    {
        readonly string[] files;
        readonly double[] times;
        readonly bool loop;
        readonly IClock clock;
        double start;
        int index;
        long id;
        public SourceKind Kind
        {
            get
            {
                return SourceKind.Images;
            }
        }

        public bool Completed { get; private set; }

        public string Description
        {
            get
            {
                return "Sequência de " + files.Length + " imagens / " + (times == null ? "timestamps de reprodução" : "timeline.csv");
            }
        }

        public ImageSequenceSource(string directory, IClock time, bool repeat)
        {
            clock = time;
            start = time.Now;
            loop = repeat;
            var list = new List<string>();
            string manifest = System.IO.Path.Combine(directory, "timeline.csv");
            if (System.IO.File.Exists(manifest))
            {
                var schedule = new List<double>();
                foreach (string line in System.IO.File.ReadAllLines(manifest))
                {
                    if (String.IsNullOrWhiteSpace(line) || line.StartsWith("file,"))
                        continue;
                    string[] pair = line.Split(',');
                    double seconds;
                    if (pair.Length != 2 || !double.TryParse(pair[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out seconds) || double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds < 0 || (schedule.Count > 0 && seconds <= schedule[schedule.Count - 1]))
                        throw new ArgumentException("timeline.csv inválido: file,seconds em ordem crescente.");
                    string file = System.IO.Path.GetFullPath(System.IO.Path.Combine(directory, pair[0]));
                    string root = System.IO.Path.GetFullPath(directory).TrimEnd('\\') + "\\";
                    if (!file.StartsWith(root, StringComparison.OrdinalIgnoreCase) || !System.IO.File.Exists(file))
                        throw new ArgumentException("Imagem da timeline ausente ou fora da pasta.");
                    list.Add(file);
                    schedule.Add(seconds);
                }

                times = schedule.ToArray();
            }
            else
            {
                foreach (string file in System.IO.Directory.GetFiles(directory))
                {
                    string e = System.IO.Path.GetExtension(file).ToLowerInvariant();
                    if (e == ".png" || e == ".jpg" || e == ".bmp")
                        list.Add(file);
                }

                list.Sort(StringComparer.OrdinalIgnoreCase);
            }

            files = list.ToArray();
            if (files.Length == 0)
                throw new ArgumentException("Pasta sem imagens.");
        }

        public Frame Read(MatPool pool, int width, Rect? roi)
        {
            if (Completed)
                return null;
            double timestamp = times == null ? clock.Now : start + times[index] - times[0];
            if (clock.Now < timestamp)
                return null;
            using (Mat raw = Cv2.ImRead(files[index++]))
            {
                if (raw.Empty())
                    throw new InvalidOperationException("Imagem inválida.");
                Frame frame = Pixels.Resize(raw, pool, width, roi, timestamp, ++id);
                if (index == files.Length)
                {
                    if (loop)
                    {
                        index = 0;
                        start = clock.Now;
                    }
                    else
                        Completed = true;
                }

                return frame;
            }
        }

        public void Dispose()
        {
        }
    }

    public sealed class VideoSource : IFrameSource
    {
        // CAP_PROP_N_THREADS=70 is supported by native OpenCV 4.13 but absent from this wrapper's enum.
        const int DecoderThreadsProperty = 70;
        readonly VideoCapture video;
        readonly Mat raw = new Mat();
        readonly IClock clock;
        readonly double start;
        double next;
        long id;
        public SourceKind Kind
        {
            get
            {
                return SourceKind.Video;
            }
        }

        public bool Completed { get; private set; }

        public string Description
        {
            get
            {
                return "Replay de vídeo, timestamps do arquivo";
            }
        }

        public VideoSource(string path, IClock time)
        {
            if (!System.IO.File.Exists(path))
                throw new ArgumentException("Vídeo local inexistente.");
            clock = time;
            video = new VideoCapture(path, VideoCaptureAPIs.FFMPEG, new[] { DecoderThreadsProperty, 1 });
            if (!video.IsOpened())
            {
                video.Dispose();
                raw.Dispose();
                throw new InvalidOperationException("Não foi possível abrir o vídeo. Verifique o pacote completo de videoio.");
            }

            start = time.Now;
        }

        public Frame Read(MatPool pool, int width, Rect? roi)
        {
            if (Completed)
                return null;
            double elapsed = clock.Now - start;
            if (elapsed < next)
                return null;
            double fps = video.Fps > 0 ? video.Fps : 30;
            int target = (int)Math.Floor(elapsed * fps);
            if (target - video.PosFrames > fps * 2)
                video.PosFrames = target;
            else
                while (video.PosFrames < target)
                {
                    if (!video.Grab())
                    {
                        Completed = true;
                        return null;
                    }
                }

            if (!video.Read(raw) || raw.Empty())
            {
                Completed = true;
                return null;
            }

            double media = video.PosMsec / 1000;
            if (media <= 0)
                media = Math.Max(0, video.PosFrames - 1) / fps;
            next = media + 1 / fps;
            return Pixels.Resize(raw, pool, width, roi, start + media, ++id);
        }

        public void Dispose()
        {
            video.Dispose();
            raw.Dispose();
        }
    }
}
