// C# | ExternalEsp/Program.cs | .NET Framework 4.8 x64 / Windows | ESP entry points, bounded live diagnostics and local evidence.
using System;using System.Collections.Generic;using System.Diagnostics;using System.IO;using System.Linq;using System.Threading;using System.Web.Script.Serialization;using System.Windows.Forms;
namespace BlueLockEsp
{
 public sealed class Journal:IDisposable
 {
  readonly StreamWriter writer;readonly Stopwatch clock=Stopwatch.StartNew();readonly object sync=new object();long next;
  public Journal(string file){Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(file)));writer=new StreamWriter(file,false,new System.Text.UTF8Encoding(false));writer.AutoFlush=true;}
  public void Write(string kind,object data){lock(sync){writer.WriteLine(new JavaScriptSerializer().Serialize(new{schemaVersion=1,eventId=++next,mono=clock.Elapsed.TotalSeconds,kind,data}));}}
  public void Dispose(){lock(sync){writer.Dispose();}}
 }
 public static class Program
 {
  [STAThread]public static int Main(string[] args)
  {
   try{if(args.Contains("--fixture"))return Fixture.Run();if(args.Contains("--self-test"))return Checks.Run(Option(args,"--out",DefaultPath("tests.json")));int pid=Int32.Parse(Option(args,"--pid","0"));var names=new List<string>();for(int i=0;i<args.Length;i++)if(args[i]=="--ball-name"){if(i+1>=args.Length||args[i+1].Length<1||args[i+1].Length>128)throw new ArgumentException("--ball-name exige nome");names.Add(args[++i]);}if(names.Count==0)names.AddRange(new[]{"Ball","Football","SoccerBall"});if(args.Contains("--probe"))return Probe(pid,names.ToArray(),Double.Parse(Option(args,"--seconds","3"),System.Globalization.CultureInfo.InvariantCulture),Option(args,"--out",DefaultPath("probe.json")));WindowApi.SetProcessDPIAware();Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);using(var journal=new Journal(DefaultPath("events.jsonl")))using(var panel=new EspPanel(pid,names.ToArray(),journal))Application.Run(panel);return 0;
   }catch(Exception e){Console.Error.WriteLine(e.Message);return 2;}
  }
  static string Option(string[] args,string name,string fallback){int n=Array.IndexOf(args,name);if(n<0)return fallback;if(n+1>=args.Length)throw new ArgumentException("Valor ausente para "+name);return args[n+1];}
  static string DefaultPath(string name){return Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"..","reports",DateTime.Now.ToString("yyyyMMdd-HHmmss")+"-"+Process.GetCurrentProcess().Id,name);}
  static int Probe(int pid,string[] names,double seconds,string file)
  {
   if(Double.IsNaN(seconds)||seconds<.1||seconds>10)throw new ArgumentException("--seconds entre 0.1 e 10");var clock=Stopwatch.StartNew();var rows=new List<object>();int valid=0,failed=0;var proc=Process.GetCurrentProcess();double cpu=proc.TotalProcessorTime.TotalSeconds;long peak=0;object attach=null;
   try{using(var target=Target.Find(pid)){attach=new{pid=target.Process.Id,version=target.Version,path=target.Path,window=target.Window.ToInt64(),access="PROCESS_VM_READ | PROCESS_QUERY_LIMITED_INFORMATION"};var reader=new SceneReader(target.Memory,target.Base,names);while(clock.Elapsed.TotalSeconds<seconds){var start=clock.Elapsed.TotalSeconds;try{var frame=reader.Read(start);valid++;rows.Add(new{time=start,valid=true,frame.Players,frame.Balls,frame.Visited,frame.Skipped,milliseconds=(clock.Elapsed.TotalSeconds-start)*1000});}catch(Exception e){failed++;rows.Add(new{time=start,valid=false,error=e.Message,milliseconds=(clock.Elapsed.TotalSeconds-start)*1000});}proc.Refresh();peak=Math.Max(peak,proc.WorkingSet64);Thread.Sleep(100);}}}catch(Exception e){failed++;rows.Add(new{time=clock.Elapsed.TotalSeconds,valid=false,error=e.Message});}
   var result=new{mode="bounded read-only live probe; no overlay or inputs",expectedVersion=Offsets.Info.ClientVersion,attach,validFrames=valid,failedFrames=failed,seconds=clock.Elapsed.TotalSeconds,cpuSeconds=proc.TotalProcessorTime.TotalSeconds-cpu,peakWorkingSet=peak,supported=valid>0,observations=rows};Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(file)));File.WriteAllText(file,new JavaScriptSerializer{MaxJsonLength=10000000}.Serialize(result));Console.WriteLine(new JavaScriptSerializer().Serialize(new{validFrames=valid,failedFrames=failed,supported=valid>0,report=Path.GetFullPath(file)}));return valid>0?0:3;
  }
 }
}
