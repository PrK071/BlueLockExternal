// C# | Diagnostics/VisualE2E/Probe.cs | .NET Framework 4.8 x64 | Native Studio capture or file replay; no input emission.
using System;
using System.Linq;
using System.Web.Script.Serialization;
using OpenCvSharp;
using BlueLockV2.Core;
using BlueLockV2.Capture;
using BlueLockV2.Vision;
using BlueLockV2.Input;
class VisualProbe
{
 static int Main(string[] args)
 {
  try
  {
   if(args.Length<1)throw new ArgumentException("Probe.exe output.png [input.png]");
   Cv2.SetNumThreads(1);
   using(var source=new WindowSource(true,new MonotonicClock()))using(var pool=new MatPool(2))using(var vision=new StudioLabVision())
   {
    Frame captured;
    if(args.Length>1){var image=Cv2.ImRead(args[1]);if(image.Empty()){image.Dispose();throw new ArgumentException("Empty replay image");}captured=new Frame(image,null,0,1,image.Width,image.Height,new Rect(0,0,image.Width,image.Height));}
    else captured=source.Read(pool,960,null);
    using(var frame=captured)
    {
     if(frame==null){Console.WriteLine("CAPTURE_SUSPENDED");return 2;}
     Cv2.ImWrite(args[0],frame.Image);var obs=vision.Detect(frame.Image);
     Console.WriteLine(new JavaScriptSerializer().Serialize(new{width=frame.Image.Width,height=frame.Image.Height,source=args.Length>1?"replay":"native-Studio",focused=new NativeTransport(true).Focused,calibrated=obs.Calibrated,references=obs.ReferenceCount,referencePixels=obs.ReferencePixels,detections=obs.Detections.Select(d=>new{type=d.Type.ToString(),x=d.Position.X,z=d.Position.Y,score=d.Confidence})}));
    }
   }
   return 0;
  }
  catch(Exception error){Console.Error.WriteLine(error);return 1;}
 }
}

