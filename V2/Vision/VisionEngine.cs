// C# | Vision/VisionEngine.cs | .NET Framework 4.8 x64 | OpenCV segmentation, shape, templates, calibrated markers.
using System;
using System.Collections.Generic;
using OpenCvSharp;
using BlueLockV2.Core;
using BlueLockV2.Capture;
using BlueLockV2.Configuration;

namespace BlueLockV2.Vision
{
    public interface IVisionEngine : IDisposable
    {
        Detection[] Detect(Frame frame, Settings settings, bool synthetic);
    }

    public sealed class VisionEngine : IVisionEngine
    {
        readonly Mat hsv = new Mat(), mask = new Mat(), work = new Mat(), gray = new Mat(), match = new Mat(), template = new Mat(), lines = new Mat();
        readonly Mat horizontal = Cv2.GetStructuringElement(MorphShapes.Rect, new Size(35, 1)), vertical = Cv2.GetStructuringElement(MorphShapes.Rect, new Size(1, 35));
        bool hasTemplate;
        public int Candidates { get; private set; }

        public void SetTemplate(string path)
        {
            using (Mat image = Cv2.ImRead(path, ImreadModes.Grayscale))
            {
                if (image.Empty())
                    throw new ArgumentException("Template vazio.");
                image.CopyTo(template);
                hasTemplate = true;
            }
        }

        public Detection[] Detect(Frame frame, Settings settings, bool synthetic)
        {
            var found = new List<Detection>();
            Mat image = frame.Image;
            Cv2.CvtColor(image, hsv, ColorConversionCodes.BGR2HSV);
            Cv2.CvtColor(image, gray, ColorConversionCodes.BGR2GRAY);
            Cv2.InRange(hsv, new Scalar(0, 0, 155), new Scalar(180, 90, 255), mask);
            Cv2.MorphologyEx(mask, lines, MorphTypes.Open, horizontal);
            Cv2.Subtract(mask, lines, mask);
            Cv2.MorphologyEx(mask, lines, MorphTypes.Open, vertical);
            Cv2.Subtract(mask, lines, mask);
            Cv2.Dilate(mask, work, null, iterations: 1);
            OpenCvSharp.Point[][] contours;
            HierarchyIndex[] hierarchy;
            Cv2.FindContours(work, out contours, out hierarchy, RetrievalModes.List, ContourApproximationModes.ApproxSimple);
            Candidates = 0;
            foreach (var contour in contours)
            {
                Rect box = Cv2.BoundingRect(contour);
                double area = Cv2.ContourArea(contour), perimeter = Cv2.ArcLength(contour, true), aspect = (double)box.Width / box.Height;
                if (box.Width < 3 || box.Height < 3 || box.Width > image.Width * .13 || box.Height > image.Height * .18 || aspect < .55 || aspect > 1.8 || area < 4)
                    continue;
                Vec center = frame.ToScreen(box.X + box.Width * .5, box.Y + box.Height * .5);
                if (!synthetic && settings.GameHudMask && !Playable(center.X / frame.Width, center.Y / frame.Height))
                    continue;
                double circular = perimeter > 0 ? 4 * Math.PI * area / (perimeter * perimeter) : 0;
                if (circular < .4)
                    continue;
                Candidates++;
                double green = GreenRing(box), fill = area / (box.Width * box.Height), texture = Texture(box), score = .28 + .2 * Math.Min(1, circular / .8) + .2 * Math.Min(1, green / .6) + .12 * Math.Min(1, fill / .65) + .12 * texture;
                if (green < .25 || texture < .2)
                    continue;
                // A template is supportive only; zoom and ball rotation must not force a match.
                if (hasTemplate && box.Width >= 8)
                {
                    Rect expanded = new Rect(Math.Max(0, box.X - 2), Math.Max(0, box.Y - 2), Math.Min(image.Width - Math.Max(0, box.X - 2), box.Width + 4), Math.Min(image.Height - Math.Max(0, box.Y - 2), box.Height + 4));
                    using (Mat crop = new Mat(gray, expanded))
                    using (Mat resized = new Mat())
                    {
                        Cv2.Resize(template, resized, crop.Size());
                        Cv2.MatchTemplate(crop, resized, match, TemplateMatchModes.CCoeffNormed);
                        double min, max;
                        Cv2.MinMaxLoc(match, out min, out max);
                        score += .06 * Math.Max(0, max);
                    }
                }

                score = Numeric.Clamp(score, 0, .99);
                if (score >= settings.Confidence)
                {
                    double radius = Math.Max(box.Width, box.Height) * frame.Scale * .5;
                    bool duplicate = false;
                    for (int i = 0; i < found.Count; i++)
                        if (found[i].Type == EntityType.BALL && Vec.Distance(found[i].Position, center) < Math.Max(radius, found[i].Radius))
                        {
                            if (score > found[i].Confidence)
                                found[i] = new Detection(EntityType.BALL, center, radius, score, true, box.Width * frame.Scale, box.Height * (double)frame.Region.Height / image.Height);
                            duplicate = true;
                            break;
                        }

                    if (!duplicate)
                        found.Add(new Detection(EntityType.BALL, center, radius, score, true, box.Width * frame.Scale, box.Height * (double)frame.Region.Height / image.Height));
                }
            }

            if (synthetic || settings.ColorsCalibrated)
            {
                Color(found, frame, EntityType.LOCAL_PLAYER, synthetic ? 112 : settings.LocalHue);
                Color(found, frame, EntityType.ALLY, synthetic ? 90 : settings.AllyHue);
                Color(found, frame, EntityType.OPPONENT, synthetic ? 0 : settings.OpponentHue);
                Color(found, frame, EntityType.GOAL, synthetic ? 150 : settings.GoalHue);
                Color(found, frame, EntityType.GOALKEEPER, synthetic ? 18 : settings.KeeperHue);
            }
            else if (settings.CalibratedLocal)
                found.Add(new Detection(EntityType.LOCAL_PLAYER, new Vec(settings.AnchorX * frame.Width, settings.AnchorY * frame.Height), 16, .7, false));
            if (settings.ImageCorners != null && settings.ImageCorners.Length == 8)
                for (int i = 0; i < 4; i++)
                    found.Add(new Detection(EntityType.FIELD_REFERENCE, new Vec(settings.ImageCorners[i * 2], settings.ImageCorners[i * 2 + 1]), 3, .7, false));
            return found.ToArray();
        }

        static bool Playable(double x, double y)
        {
            return y >= .14 && y <= .90 && !(x > .85 && y > .54) && !(x > .30 && x < .67 && y > .70);
        }

        double Texture(Rect box)
        {
            int dark = 0, light = 0, count = 0;
            double cx = box.X + box.Width * .5, cy = box.Y + box.Height * .5, r = Math.Max(box.Width, box.Height) * .38;
            for (int y = box.Y; y < box.Bottom; y++)
                for (int x = box.X; x < box.Right; x++)
                {
                    if ((x - cx) * (x - cx) + (y - cy) * (y - cy) > r * r)
                        continue;
                    Vec3b p = hsv.Get<Vec3b>(y, x);
                    count++;
                    if (p.Item1 < 140 && p.Item2 < 150)
                        dark++;
                    if (p.Item1 < 100 && p.Item2 > 155)
                        light++;
                }

            return count == 0 ? 0 : Math.Min(1, dark / (count * .1)) * Math.Min(1, light / (count * .25));
        }

        double GreenRing(Rect box)
        {
            int margin = Math.Max(3, box.Width / 2);
            Rect ring = new Rect(Math.Max(0, box.X - margin), Math.Max(0, box.Y - margin), 0, 0);
            ring.Width = Math.Min(hsv.Width - ring.X, box.Width + margin * 2);
            ring.Height = Math.Min(hsv.Height - ring.Y, box.Height + margin * 2);
            int green = 0, total = 0;
            for (int y = ring.Y; y < ring.Bottom; y += 2)
                for (int x = ring.X; x < ring.Right; x += 2)
                {
                    if (x >= box.X && x < box.Right && y >= box.Y && y < box.Bottom)
                        continue;
                    Vec3b p = hsv.Get<Vec3b>(y, x);
                    total++;
                    if (p.Item0 >= 30 && p.Item0 <= 90 && p.Item1 >= 60 && p.Item2 >= 40)
                        green++;
                }

            return total == 0 ? 0 : (double)green / total;
        }

        void Color(List<Detection> result, Frame frame, EntityType type, double hue)
        {
            if (hue < 8)
            {
                Cv2.InRange(hsv, new Scalar(0, 125, 100), new Scalar(hue + 8, 255, 255), mask);
            }
            else
                Cv2.InRange(hsv, new Scalar(hue - 8, 125, 100), new Scalar(hue + 8, 255, 255), mask);
            OpenCvSharp.Point[][] contours;
            HierarchyIndex[] h;
            Cv2.FindContours(mask, out contours, out h, RetrievalModes.External, ContourApproximationModes.ApproxSimple);
            foreach (var contour in contours)
            {
                double area = Cv2.ContourArea(contour);
                if (area < 25 || area > frame.Image.Width * frame.Image.Height * .06)
                    continue;
                Rect box = Cv2.BoundingRect(contour);
                result.Add(new Detection(type, frame.ToScreen(box.X + box.Width * .5, box.Y + box.Height * .5), Math.Max(box.Width, box.Height) * frame.Scale * .5, .94));
            }
        }

        public void Dispose()
        {
            hsv.Dispose();
            mask.Dispose();
            work.Dispose();
            gray.Dispose();
            match.Dispose();
            template.Dispose();
            lines.Dispose();
            horizontal.Dispose();
            vertical.Dispose();
        }
    }
}
