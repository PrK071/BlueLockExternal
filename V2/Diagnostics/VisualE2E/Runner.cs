// C# | Diagnostics/VisualE2E/Runner.cs | .NET Framework 4.8 x64 | Bounded native visual loop; truth receiver is write-only with respect to the controller.
using System;
using System.IO;
using System.Text;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Diagnostics;
using System.Linq;
using System.Web.Script.Serialization;
using OpenCvSharp;
using BlueLockV2.Core;
using BlueLockV2.Capture;
using BlueLockV2.Vision;
using BlueLockV2.Tracking;
using BlueLockV2.Prediction;
using BlueLockV2.Decision;
using BlueLockV2.Input;
using BlueLockV2.Configuration;
class VisualRunner
{
 static volatile bool emergency;
 static int Main(string[] args)
 {
  try{return Run(args);}catch(Exception error){Console.Error.WriteLine("RUNNER_START_OR_EXECUTION_ERROR: "+error);return 2;}
 }
 static int Run(string[] args)
 {
  if(args.Length<4)throw new ArgumentException("Runner.exe folder run-id width max-seconds [baseline] [relay]");
  if(!System.Text.RegularExpressions.Regex.IsMatch(args[1],"^[A-Za-z0-9_-]{1,80}$"))throw new ArgumentException("Invalid run id");
  string dir=Path.GetFullPath(args[0]),run=args[1];int width=int.Parse(args[2]);double limit=double.Parse(args[3],System.Globalization.CultureInfo.InvariantCulture);
  if(width<320||width>1280||limit<1||limit>60||Double.IsNaN(limit))throw new ArgumentException("Width 320..1280 and duration 1..60 seconds required");
  Directory.CreateDirectory(dir);var clock=new MonotonicClock();var json=new JavaScriptSerializer();Cv2.SetNumThreads(1);
  bool relay=args.Length>5&&args[5]=="relay";
  var listener=new TcpListener(IPAddress.Loopback,relay?60138:60137);listener.Start();
  var truthThread=new Thread(()=>TruthLoop(listener,dir,run,clock)){IsBackground=true};truthThread.Start();
  var process=Process.GetCurrentProcess();long frameCount=0,missingBall=0,missingActor=0,calibrated=0,suspended=0;string status="timeout";double lastSave=-10;int events=0,heldAfter=0;double visionMs=0,captureMs=0;
  var decision=new StudioVisualDecision();
  using(var source=new WindowSource(true,clock))using(var pool=new MatPool(3))using(var vision=new StudioLabVision())
  using(var frames=new StreamWriter(Path.Combine(dir,run+"-frames.jsonl"),false,new UTF8Encoding(false)))
  using(var input=new InputScheduler(new NativeTransport(true),clock,new Settings{Sprint=false,PilotDribble=false},true))
  using(var hotkeys=new HotkeyService(IntPtr.Zero,k=>{},()=>{emergency=true;input.EmergencyStop();decision.Stop();},()=>{emergency=true;input.EmergencyStop();decision.Stop();}))
  {
   var tracker=new TemporalTracker(6,12);var predictor=new PredictionEngine();frames.AutoFlush=true;double arrivedSince=-1;
   try
   {
    while(clock.Now<limit)
    {
     if(emergency||File.Exists(Path.Combine(dir,"STOP"))){status="emergency";break;}
     double began=clock.Now;using(var frame=source.Read(pool,width,null))
     {
      if(frame==null){suspended++;input.EmergencyStop();Thread.Sleep(50);continue;}
      captureMs+=(clock.Now-began)*1000;
      double perceive=clock.Now;var obs=vision.Detect(frame.Image);double elapsed=(clock.Now-perceive)*1000;visionMs+=elapsed;frameCount++;
      bool ball=obs.Detections.Any(d=>d.Type==EntityType.BALL),actor=obs.Detections.Any(d=>d.Type==EntityType.LOCAL_PLAYER);
      if(!ball)missingBall++;if(!actor)missingActor++;if(obs.Calibrated)calibrated++;
      var game=tracker.Update(obs.Detections,frame.Timestamp,frame.Id,100,100,CameraMotion.Stable);
      var forecast=predictor.Predict(game,.1);predictor.ObserveAndQueue(game,forecast);
      decision.Right=obs.Right;decision.Forward=obs.Forward;decision.CurrentBall=ball;decision.CurrentPlayer=actor;decision.Calibrated=obs.Calibrated;
      if(ball)decision.MeasuredBall=obs.Detections.First(d=>d.Type==EntityType.BALL).Position;
      if(actor)decision.MeasuredPlayer=obs.Detections.First(d=>d.Type==EntityType.LOCAL_PLAYER).Position;
      var action=decision.Decide(game,forecast,clock.Now);input.Execute(action);
      var tr=decision.Trace;
      frames.WriteLine(json.Serialize(new{run=run,t=frame.Timestamp,age=clock.Now-frame.Timestamp,width=frame.Image.Width,height=frame.Image.Height,calibrated=obs.Calibrated,ball=ball,actor=actor,visionMs=elapsed,kind=action.Kind.ToString(),keys=input.HeldKeys,state=tr==null?"NONE":tr.State,distance=tr==null?0:tr.Distance,reason=action.Reason,detections=obs.Detections.Select(d=>new{type=d.Type.ToString(),x=d.Position.X,z=d.Position.Y,score=d.Confidence}),predictionRmse=predictor.Rmse}));
      if(clock.Now-lastSave>=2){lastSave=clock.Now;Cv2.ImWrite(Path.Combine(dir,run+"-frame-"+frameCount+".png"),frame.Image);}
      if(tr!=null&&tr.State=="REACHED"&&ball&&actor&&Vec.Distance(decision.MeasuredBall,decision.MeasuredPlayer)<=1.2)
      {if(arrivedSince<0)arrivedSince=clock.Now;if(clock.Now-arrivedSince>=.4){status="visual-reached";break;}}
      else arrivedSince=-1;
      if(tr!=null&&tr.State=="RECALIBRATE"){status="navigation-fault";break;}
     }
     Thread.Sleep(Math.Max(1,(int)((.1-(clock.Now-began))*1000)));
    }
   }
   finally{input.EmergencyStop();events=input.Events;heldAfter=input.HeldCount;decision.Stop();Thread.Sleep(300);listener.Stop();truthThread.Join(1000);}
  }
  if(frameCount==0&&status=="timeout")status="blocked-focus";
  process.Refresh();var result=new{run=run,status=status,frames=frameCount,suspended=suspended,missingBall=missingBall,missingActor=missingActor,calibrated=calibrated,events=events,heldAfter=heldAfter,seconds=clock.Now,meanVisionMs=visionMs/Math.Max(1,frameCount),meanCaptureMs=captureMs/Math.Max(1,frameCount),cpuSeconds=process.TotalProcessorTime.TotalSeconds,workingSet=process.WorkingSet64,privateBytes=process.PrivateMemorySize64,input="C# NativeTransport/InputScheduler; no MCP input"};
  string summary=json.Serialize(result);File.WriteAllText(Path.Combine(dir,run+"-result.json"),summary);Console.WriteLine(summary);return 0;
 }
 static void TruthLoop(TcpListener listener,string dir,string run,IClock clock)
 {
  using(var log=new StreamWriter(Path.Combine(dir,run+"-truth.jsonl"),false,new UTF8Encoding(false)))
  {
   double lastTruthClock=Double.NegativeInfinity;
   log.AutoFlush=true;
   while(true)try{using(var client=listener.AcceptTcpClient())using(var stream=client.GetStream())
   {
    client.ReceiveTimeout=client.SendTimeout=500;var header=new StringBuilder();uint tail=0;
    while(header.Length<4096){int b=stream.ReadByte();if(b<0)throw new IOException();header.Append((char)b);tail=(tail<<8)|(byte)b;if(tail==0x0d0a0d0a)break;}
    if(!header.ToString().StartsWith("POST /truth-visual-e2e HTTP/1.1"))throw new IOException("route");int size=0;foreach(var line in header.ToString().Split(new[]{"\r\n"},StringSplitOptions.None))if(line.StartsWith("Content-Length:",StringComparison.OrdinalIgnoreCase))size=int.Parse(line.Substring(15).Trim());if(size<1||size>16384)throw new IOException("size");
    byte[] body=new byte[size];int offset=0;while(offset<size){int n=stream.Read(body,offset,size-offset);if(n==0)throw new IOException();offset+=n;}
    string raw=Encoding.UTF8.GetString(body);var parsed=new JavaScriptSerializer().Deserialize<System.Collections.Generic.Dictionary<string,object>>(raw);
    double truthClock=parsed.ContainsKey("clock")?Convert.ToDouble(parsed["clock"]):Double.NegativeInfinity;
    if(parsed.ContainsKey("run")&&(string)parsed["run"]==run&&truthClock>lastTruthClock){lastTruthClock=truthClock;log.WriteLine("{\"received\":"+clock.Now.ToString("R",System.Globalization.CultureInfo.InvariantCulture)+",\"truth\":"+raw+"}");}
    byte[] response=Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: 2\r\nConnection: close\r\n\r\n{}");stream.Write(response,0,response.Length);
   }}catch(SocketException){break;}catch(ObjectDisposedException){break;}catch(Exception){if(!listener.Server.IsBound)break;}
  }
 }
}
