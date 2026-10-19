// C# | Capture/WindowSource.cs | .NET Framework 4.8 x64 | Foreground-only client-area pixel capture.
using System;
using System.Drawing;
using System.Drawing.Imaging;
using OpenCvSharp;
using BlueLockV2.Core;
using BlueLockV2.Input;

namespace BlueLockV2.Capture
{
    public sealed class WindowSource : IFrameSource
    {
        readonly bool studio;
        readonly IClock clock;
        IntPtr window;
        Bitmap bitmap;
        Graphics graphics;
        readonly Mat raw = new Mat();
        long id;
        public int SuspendedFrames;
        public SourceKind Kind
        {
            get
            {
                return studio ? SourceKind.StudioWindow : SourceKind.RobloxWindow;
            }
        }

        public bool Completed
        {
            get
            {
                return false;
            }
        }

        public string Description
        {
            get
            {
                return "Captura de pixels da área cliente; suspensa fora de foco";
            }
        }

        public WindowSource(bool useStudio, IClock time)
        {
            studio = useStudio;
            clock = time;
        }

        public bool Focused
        {
            get
            {
                return window != IntPtr.Zero && Platform.IsTarget(window, studio) && Platform.GameFocused(window);
            }
        }

        public Frame Read(MatPool pool, int width, Rect? roi)
        {
            if (window == IntPtr.Zero || !Platform.IsTarget(window, studio))
                window = Platform.FindTarget(studio);
            if (window == IntPtr.Zero || !Platform.GameFocused(window))
            {
                SuspendedFrames++;
                return null;
            }

            Rectangle bounds = Platform.ClientBounds(window);
            Rect region = roi ?? new Rect(0, 0, bounds.Width, bounds.Height);
            region = region.Intersect(new Rect(0, 0, bounds.Width, bounds.Height));
            if (region.Width < 4 || region.Height < 4)
                region = new Rect(0, 0, bounds.Width, bounds.Height);
            if (bitmap == null || bitmap.Width != region.Width || bitmap.Height != region.Height)
            {
                if (graphics != null)
                    graphics.Dispose();
                if (bitmap != null)
                    bitmap.Dispose();
                bitmap = new Bitmap(region.Width, region.Height, PixelFormat.Format24bppRgb);
                graphics = Graphics.FromImage(bitmap);
            }

            double time = clock.Now;
            graphics.CopyFromScreen(bounds.X + region.X, bounds.Y + region.Y, 0, 0, bitmap.Size, CopyPixelOperation.SourceCopy);
            if (!Platform.GameFocused(window))
                return null;
            Pixels.Copy(bitmap, raw);
            Frame temporary = Pixels.Resize(raw, pool, width, null, time, ++id);
            return new Frame(temporary.Image, pool, time, id, bounds.Width, bounds.Height, region);
        }

        public bool TryGetFocusedBounds(out Rectangle bounds)
        {
            bounds = Rectangle.Empty;
            if (!Focused)
                return false;
            bounds = Platform.ClientBounds(window);
            return bounds.Width > 0 && bounds.Height > 0;
        }

        public void Dispose()
        {
            if (graphics != null)
                graphics.Dispose();
            if (bitmap != null)
                bitmap.Dispose();
            raw.Dispose();
        }
    }
}
