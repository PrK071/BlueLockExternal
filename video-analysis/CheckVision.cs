// C# | CheckVision.cs | .NET Framework / Windows x64 | Offline checks against supplied recording/images; no live input.
using System;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;

internal static class CheckVision
{
    private static int failures;
    private static void Check(string path,Bitmap template,double expectedX,double expectedY)
    {
        using(Bitmap source=new Bitmap(path)){
            Stopwatch watch=Stopwatch.StartNew();
            Match result=Vision.AnalyzeBitmap(source,template,Rectangle.Empty,.85);
            double x=(result.Box.X+result.Box.Width/2.0)*source.Width/result.FrameSize.Width;
            double y=(result.Box.Y+result.Box.Height/2.0)*source.Height/result.FrameSize.Height;
            bool correct=!result.Box.IsEmpty && Math.Abs(x-expectedX)<20 && Math.Abs(y-expectedY)<20;
            Console.WriteLine(String.Format(CultureInfo.InvariantCulture,"{0}: match={1}, score={2:0.000}, center={3:0.0}/{4:0.0}, candidates={5}, ms={6}, expected_match={7}",Path.GetFileName(path),!result.Box.IsEmpty,result.Confidence,x,y,result.CandidateCount,watch.ElapsedMilliseconds,correct));
            result.Preview.Save(Path.Combine(Path.GetDirectoryName(typeof(CheckVision).Assembly.Location),Path.GetFileNameWithoutExtension(path)+"-detected.png"));
            result.Preview.Dispose();if(!correct)failures++;
        }
    }
    public static int Main(string[] args)
    {
        using(Bitmap template=new Bitmap(args[0])){
            Check(args[1],template,1097,525);
            Check(args[2],template,622,262);
            Check(args[3],template,625,308);
            using(Bitmap source=new Bitmap(args[1]))
            using(Bitmap tile=source.Clone(new Rectangle(100,200,160,100),source.PixelFormat))
            using(Bitmap field=new Bitmap(800,400)){
                using(Graphics graphics=Graphics.FromImage(field))using(TextureBrush texture=new TextureBrush(tile))graphics.FillRectangle(texture,0,0,field.Width,field.Height);
                Match negative=Vision.AnalyzeBitmap(field,template,Rectangle.Empty,.85);
                Console.WriteLine("grass_only: match="+!negative.Box.IsEmpty+", candidates="+negative.CandidateCount);
                if(!negative.Box.IsEmpty)failures++;
                negative.Preview.Dispose();
            }
            using(Bitmap scene=new Bitmap(args[1]))
            using(Bitmap grass=scene.Clone(new Rectangle(1150,500,60,60),scene.PixelFormat)){
                using(Graphics g=Graphics.FromImage(scene))g.DrawImage(grass,new Rectangle(1075,500,50,50));
                Match noBall=Vision.AnalyzeBitmap(scene,template,Rectangle.Empty,.85);
                Console.WriteLine("original_scene_ball_removed: match="+!noBall.Box.IsEmpty+", candidates="+noBall.CandidateCount+", score="+noBall.Confidence.ToString("0.000",CultureInfo.InvariantCulture));
                if(!noBall.Box.IsEmpty)failures++;
                noBall.Preview.Dispose();
            }
        }
        return failures==0?0:1;
    }
}
