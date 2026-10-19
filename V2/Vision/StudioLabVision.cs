// C# | Vision/StudioLabVision.cs | .NET Framework 4.8 x64 | Opt-in instrumented Studio lab: pixel fiducials, actual ball and actor marker; no telemetry input.
using System;
using System.Collections.Generic;
using OpenCvSharp;
using BlueLockV2.Core;
namespace BlueLockV2.Vision
{
    public sealed class StudioVisualObservation
    {
        public Detection[] Detections=new Detection[0];
        public Vec Right,Forward;
        public bool Calibrated;
        public int ReferenceCount;
        public Point2f[] ReferencePixels;
        public double ReprojectionError;
        public Vec BallPixel,PlayerPixel;
    }
    public sealed class StudioLabVision : IDisposable
    {
        readonly Mat hsv=new Mat(),mask=new Mat();
        Point2f[] domain;
        Mat projection;
        Vec? lastBall;
        static readonly Point2f[] Known={new Point2f(-20,-20),new Point2f(20,-20),new Point2f(20,20),new Point2f(-20,20)};
        Vec? Find(double hue,double tolerance,bool white,out double confidence)
        {
            confidence=0;
            Cv2.InRange(hsv,new Scalar(white?0:Math.Max(0,hue-tolerance),white?0:150,white?175:100),new Scalar(white?180:hue+tolerance,white?70:255,255),mask);
            Point[][] contours; HierarchyIndex[] hierarchy;
            // The selected viewport has a blue enclosing border; External hides blue markers inside it.
            Cv2.FindContours(mask,out contours,out hierarchy,RetrievalModes.List,ContourApproximationModes.ApproxSimple);
            Vec? best=null; double bestArea=0,bestBallDistance=Double.MaxValue;
            var actorPoints=new List<Point>(); bool actor=!white&&hue>140;
            foreach(var c in contours)
            {
                double area=Cv2.ContourArea(c); Rect box=Cv2.BoundingRect(c);
                if(area<(white?5:8)||area>hsv.Width*hsv.Height*.04||box.Width<3||box.Height<3)continue;
                double aspect=(double)box.Width/box.Height;
                if(domain!=null&&(white||actor)&&Cv2.PointPolygonTest(domain,new Point2f(box.X+box.Width*.5f,box.Y+box.Height*.5f),false)<0)continue;
                if(aspect<(actor?.2:white&&lastBall.HasValue?.25:.55)||aspect>(actor?5:white&&lastBall.HasValue?4:1.8))continue;
                if(!white&&!actor&&(aspect<.8||aspect>1.25||area/Math.Max(1,(box.Width-1)*(box.Height-1))<.82))continue;
                // Studio chrome contains the same hues. Require field pixels around every candidate.
                int green=0,total=0,margin=Math.Max(4,box.Width/2);
                for(int y=Math.Max(0,box.Y-margin);y<Math.Min(hsv.Height,box.Bottom+margin);y+=2)
                for(int x=Math.Max(0,box.X-margin);x<Math.Min(hsv.Width,box.Right+margin);x+=2)
                {if(x>=box.X&&x<box.Right&&y>=box.Y&&y<box.Bottom)continue;var px=hsv.Get<Vec3b>(y,x);total++;if(((px.Item0>=30&&px.Item0<=90)||(white&&px.Item0>=140&&px.Item0<=165))&&px.Item1>=60&&px.Item2>=35)green++;}
                if(total==0||(double)green/total<(white?.2:.45))continue;
                if(actor){actorPoints.AddRange(c);continue;}
                if(white){double perimeter=Cv2.ArcLength(c,true); if(4*Math.PI*area/(perimeter*perimeter)<(lastBall.HasValue?.4:.75))continue;
                    if(lastBall.HasValue&&projection!=null){var m=Cv2.Moments(c);if(m.M00<=0)continue;var candidate=new Vec(m.M10/m.M00,m.M01/m.M00);double distance=Vec.Distance(Project(candidate,projection),lastBall.Value);if(distance>3||distance>=bestBallDistance)continue;bestBallDistance=distance;best=candidate;confidence=.85;continue;}}
                if(area>bestArea){var m=Cv2.Moments(c);if(m.M00<=0)continue;bestArea=area;best=new Vec(m.M10/m.M00,m.M01/m.M00);confidence=.95;}
            }
            if(actorPoints.Count>=5){Point2f center;float radius;Cv2.MinEnclosingCircle(actorPoints,out center,out radius);confidence=.95;return new Vec(center.X,center.Y);}
            return best;
        }
        public StudioVisualObservation Detect(Mat image)
        {
            var result=new StudioVisualObservation();
            domain=null;
            Cv2.CvtColor(image,hsv,ColorConversionCodes.BGR2HSV);
            var pixels=new Point2f[4]; double conf;
            double[] hues={0,30,90,120};
            for(int i=0;i<4;i++){var p=Find(hues[i],12,false,out conf);if(!p.HasValue)return result;pixels[i]=new Point2f((float)p.Value.X,(float)p.Value.Y);result.ReferenceCount++;}
            result.ReferencePixels=pixels;
            domain=pixels;
            if(Math.Abs(Cv2.ContourArea(pixels))<500)return result;
            using(var transform=Cv2.GetPerspectiveTransform(pixels,Known))
            {
                projection=transform;
                var obs=new List<Detection>();
                var b=Find(0,0,true,out conf);double bc=conf;
                var p=Find(150,8,false,out conf);double pc=conf;
                if(b.HasValue){var q=Project(b.Value,transform);lastBall=q;result.BallPixel=b.Value;obs.Add(new Detection(EntityType.BALL,q,.8,bc,true));}
                if(p.HasValue){var q=Project(p.Value,transform);result.PlayerPixel=p.Value;obs.Add(new Detection(EntityType.LOCAL_PLAYER,q,2,pc,true));}
                Vec center=new Vec(image.Width*.5,image.Height*.5);
                result.Right=(Project(center+new Vec(10,0),transform)-Project(center,transform)).Normalized;
                result.Forward=(Project(center+new Vec(0,-10),transform)-Project(center,transform)).Normalized;
                result.Detections=obs.ToArray();result.Calibrated=true;
                projection=null;
            }
            return result;
        }
        static Vec Project(Vec p,Mat transform){var v=Cv2.PerspectiveTransform(new[]{new Point2f((float)p.X,(float)p.Y)},transform)[0];return new Vec(v.X,v.Y);}
        public void Dispose(){hsv.Dispose();mask.Dispose();}
    }
}
