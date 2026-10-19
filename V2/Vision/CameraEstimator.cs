// C# | Vision/CameraEstimator.cs | .NET Framework 4.8 x64 | Sparse background flow and optional planar projection.
using System;
using System.Collections.Generic;
using OpenCvSharp;
using BlueLockV2.Core;
using BlueLockV2.Capture;

namespace BlueLockV2.Vision
{
    public sealed class CameraEstimator : IDisposable
    {
        readonly Mat previous = new Mat(), gray = new Mat();
        Rect priorRegion;
        bool ready;
        double priorScale;
        public CameraMotion Estimate(Frame frame, bool enabled)
        {
            if (!enabled)
            {
                ready = false;
                return CameraMotion.Stable;
            }

            Cv2.CvtColor(frame.Image, gray, ColorConversionCodes.BGR2GRAY);
            CameraMotion result = CameraMotion.Stable;
            if (ready && previous.Size() == gray.Size() && Math.Abs(priorScale - frame.Scale) < .001)
            {
                Point2f[] points = Cv2.GoodFeaturesToTrack(previous, 70, .03, 12, null, 3, false, .04);
                if (points.Length >= 8)
                {
                    Point2f[] moved = (Point2f[])points.Clone();
                    byte[] status;
                    float[] error;
                    Cv2.CalcOpticalFlowPyrLK(previous, gray, points, ref moved, out status, out error, new Size(15, 15), 2);
                    var dx = new List<double>();
                    var dy = new List<double>();
                    for (int i = 0; i < points.Length; i++)
                        if (status[i] != 0 && error[i] < 30)
                        {
                            dx.Add(moved[i].X - points[i].X);
                            dy.Add(moved[i].Y - points[i].Y);
                        }

                    double x = Numeric.Median(dx), y = Numeric.Median(dy);
                    var deviations = new List<double>();
                    for (int i = 0; i < dx.Count; i++)
                        deviations.Add(Math.Sqrt((dx[i] - x) * (dx[i] - x) + (dy[i] - y) * (dy[i] - y)));
                    double mad = Numeric.Median(deviations), confidence = (double)dx.Count / points.Length * Math.Exp(-mad / 5);
                    bool jump = Math.Abs(x) > gray.Width * .18 || Math.Abs(y) > gray.Height * .18 || mad > 6 || confidence < .3;
                    result = new CameraMotion(new Vec(x * frame.Scale + frame.Region.X - priorRegion.X, y * frame.Scale + frame.Region.Y - priorRegion.Y), confidence, jump);
                }
                else
                    result = new CameraMotion(new Vec(), .3, true);
            }
            else if (ready)
                result = new CameraMotion(new Vec(), .3, true);
            gray.CopyTo(previous);
            priorRegion = frame.Region;
            priorScale = frame.Scale;
            ready = true;
            return result;
        }

        public void Dispose()
        {
            previous.Dispose();
            gray.Dispose();
        }
    }

    public sealed class FieldProjector : IDisposable
    {
        readonly Mat transform;
        public FieldProjector(double[] corners, double width, double height)
        {
            if (corners == null || corners.Length != 8 || width <= 0 || height <= 0)
                throw new ArgumentException("Quatro cantos ordenados e dimensões positivas são necessários.");
            Point2f[] from = new Point2f[4];
            for (int i = 0; i < 4; i++)
                from[i] = new Point2f((float)corners[i * 2], (float)corners[i * 2 + 1]);
            if (Math.Abs(Cv2.ContourArea(from)) < 100)
                throw new ArgumentException("Calibração degenerada.");
            transform = Cv2.GetPerspectiveTransform(from, new[] { new Point2f(0, 0), new Point2f((float)width, 0), new Point2f((float)width, (float)height), new Point2f(0, (float)height) });
        }

        public Vec Project(Vec screen)
        {
            Point2f[] result = Cv2.PerspectiveTransform(new[] { new Point2f((float)screen.X, (float)screen.Y) }, transform);
            return new Vec(result[0].X, result[0].Y);
        }

        public void Dispose()
        {
            transform.Dispose();
        }
    }
}
