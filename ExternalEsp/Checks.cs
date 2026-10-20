// C# | ExternalEsp/Checks.cs | .NET Framework 4.8 x64 / Windows | Real cross-process fixture reads and offscreen rendering; no Roblox writes or game input.
using System;using System.Collections.Generic;using System.Diagnostics;using System.Drawing;using System.Drawing.Imaging;using System.IO;using System.Reflection;using System.Runtime.InteropServices;using System.Text;using System.Web.Script.Serialization;
namespace BlueLockEsp
{
 public sealed class Arena:IDisposable
 {
  readonly List<IntPtr> blocks=new List<IntPtr>();
  public long Alloc(int count){var p=Marshal.AllocHGlobal(count);blocks.Add(p);Marshal.Copy(new byte[count],0,p,count);return p.ToInt64();}
  public void Ptr(long at,long value){Marshal.WriteInt64(new IntPtr(at),value);}
  public void Float(long at,float value){Marshal.Copy(BitConverter.GetBytes(value),0,new IntPtr(at),4);}
  public long Text(string text){byte[] bytes=Encoding.UTF8.GetBytes(text);long data=Alloc(32);if(bytes.Length<16){Marshal.Copy(bytes,0,new IntPtr(data),bytes.Length);Ptr(data+24,15);}else{long heap=Alloc(bytes.Length+1);Marshal.Copy(bytes,0,new IntPtr(heap),bytes.Length);Ptr(data,heap);Ptr(data+24,bytes.Length);}Ptr(data+16,bytes.Length);return data;}
  public long Node(string name){long p=Alloc(0xC00);Ptr(p+Offsets.Instance.NameContainer,Text(name));Ptr(p+Offsets.Instance.ChildrenStart,Alloc(16));return p;}
  public void Children(long parent,params long[] children){long list=Alloc(16);Ptr(parent+Offsets.Instance.ChildrenStart,list);if(children.Length==0)return;long storage=Alloc(children.Length*8);Ptr(list,storage);Ptr(list+8,storage+children.Length*8);for(int i=0;i<children.Length;i++){Ptr(storage+i*8,children[i]);Ptr(children[i]+Offsets.Instance.Parent,parent);}}
  public long Part(string name,V3 position){long p=Node(name),primitive=Alloc(0x300);Ptr(p+Offsets.BasePart.Primitive,primitive);Float(primitive+Offsets.Primitive.Position,position.X);Float(primitive+Offsets.Primitive.Position+4,position.Y);Float(primitive+Offsets.Primitive.Position+8,position.Z);return p;}
  public void Dispose(){foreach(var p in blocks)Marshal.FreeHGlobal(p);blocks.Clear();}
 }
 public static class Fixture
 {
  public static int Run()
  {
   using(var a=new Arena())
   {
    long block=a.Alloc(checked((int)(Offsets.FakeDataModel.Pointer-Offsets.VisualEngine.Pointer+4096))),module=block-Offsets.VisualEngine.Pointer;
    long root=a.Node("DataModel"),workspace=a.Node("Workspace"),players=a.Node("Players"),local=a.Node("LocalFixture"),remote=a.Node("RemoteFixture测试"),localModel=a.Node("LocalModel"),remoteModel=a.Node("RemoteModel"),ball=a.Part("Ball",new V3(0,-.5f,0));
    long localPart=a.Part("HumanoidRootPart",new V3(-1,0,0)),remotePart=a.Part("HumanoidRootPart",new V3(2,0,0)),head=a.Part("Head",new V3(2,2,0)),humanoid=a.Node("Humanoid");a.Float(humanoid+Offsets.Humanoid.Health,85);a.Children(localModel,localPart);a.Children(remoteModel,remotePart,head,humanoid);a.Children(workspace,localModel,remoteModel,ball);a.Children(root,workspace,players);a.Children(players,local,remote);a.Ptr(players+Offsets.Player.LocalPlayer,local);a.Ptr(local+Offsets.Player.ModelInstance,localModel);a.Ptr(remote+Offsets.Player.ModelInstance,remoteModel);a.Ptr(root+Offsets.DataModel.Workspace,workspace);
    long fake=a.Alloc(0x300),visual=a.Alloc(0xC00);a.Ptr(module+Offsets.FakeDataModel.Pointer,fake);a.Ptr(fake+Offsets.FakeDataModel.RealDataModel,root);a.Ptr(module+Offsets.VisualEngine.Pointer,visual);a.Float(visual+Offsets.VisualEngine.Dimensions,960);a.Float(visual+Offsets.VisualEngine.Dimensions+4,540);for(int i=0;i<16;i++)a.Float(visual+Offsets.VisualEngine.ViewMatrix+i*4,i==15?1:i==0||i==5||i==10?.15f:0);
    var json=new JavaScriptSerializer();Console.WriteLine(json.Serialize(new{pid=Process.GetCurrentProcess().Id,module,ball,root,visual}));Console.Out.Flush();string command;
    while((command=Console.ReadLine())!=null&&command!="stop") {if(command=="null-root")a.Ptr(module+Offsets.FakeDataModel.Pointer,0);else if(command=="bad-matrix")a.Float(visual+Offsets.VisualEngine.ViewMatrix,Single.NaN);else if(command=="restore"){a.Ptr(module+Offsets.FakeDataModel.Pointer,fake);a.Float(visual+Offsets.VisualEngine.ViewMatrix,.15f);}Console.WriteLine("ok");Console.Out.Flush();}
   }return 0;
  }
 }
 public static class Checks
 {
  static readonly List<object> results=new List<object>();static int failed;
  static void Check(bool ok,string label){results.Add(new{label,passed=ok});Console.WriteLine((ok?"PASS ":"FAIL ")+label);if(!ok)failed++;}
  public static int Run(string report)
  {
   Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(report)));var clock=Stopwatch.StartNew();string exe=Assembly.GetExecutingAssembly().Location;var info=new ProcessStartInfo(exe,"--fixture"){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardInput=true,RedirectStandardError=true};
   using(var child=Process.Start(info))
   {
    try{string line=child.StandardOutput.ReadLine();if(line==null)throw new Exception("Fixture não iniciou: "+child.StandardError.ReadToEnd());var data=new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(line);long module=Convert.ToInt64(data["module"]);using(var memory=new ProcessMemory(child.Id))
     {
      var scene=new SceneReader(memory,module,new[]{"Ball"});Snapshot s=scene.Read(0);Check(s.State=="READY","cross-process fixture snapshot ready");Check(s.Players==1,"local player excluded and one remote player observed");Check(s.Balls==1,"named physical ball observed");Check(s.Entities.Length==2,"bounded graph yields expected two entities");Entity player=Array.Find(s.Entities,x=>x.Kind=="player"),ball=Array.Find(s.Entities,x=>x.Kind=="ball");Check(player.Name=="RemoteFixture测试","long UTF8 MSVC string layout read across process");Check(player.Health==85,"health value decoded");Check(Math.Abs(player.Distance-3)<.001,"world-space player distance");Check(Math.Abs(ball.Distance-Math.Sqrt(1.25))<.001,"world-space ball distance");Check(ball.Path=="Workspace/Ball","ball provenance includes exact hierarchy path");PointF point;Check(Projection.Screen(new V3(0,0,0),s.Matrix,960,540,out point)&&point.X==480&&point.Y==270,"matrix projects origin to viewport center");var behind=(float[])s.Matrix.Clone();behind[15]=-1;Check(!Projection.Screen(new V3(0,0,0),behind,960,540,out point),"behind-camera projection rejected");Check(!Projection.Screen(new V3(1000,0,0),s.Matrix,960,540,out point),"offscreen projection rejected");
      bool invalid=false;try{memory.Read(0,8);}catch(MemoryFault){invalid=true;}Check(invalid,"invalid address rejected before native read");using(var bitmap=new Bitmap(960,540))using(var g=Graphics.FromImage(bitmap)){g.Clear(Color.FromArgb(25,40,55));int drawn=EspDrawing.Draw(g,s,960,540,true,true,2000);Check(drawn==2,"player box and ball marker rendered");Check(EspDrawing.Draw(g,s,960,540,false,false,2000)==0,"both overlay switches suppress entities");Check(EspDrawing.Draw(g,s,960,540,true,true,.5)==0,"distance filter suppresses out-of-range entities");bitmap.Save(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(report)),"fixture-overlay.png"),ImageFormat.Png);}
      child.StandardInput.WriteLine("null-root");child.StandardInput.Flush();child.StandardOutput.ReadLine();bool rootRejected=false;try{scene.Read(.1);}catch(MemoryFault){rootRejected=true;}Check(rootRejected,"null root never yields a fabricated snapshot");child.StandardInput.WriteLine("restore");child.StandardInput.Flush();child.StandardOutput.ReadLine();Check(scene.Read(.2).Players==1,"reader recovers after root becomes valid");child.StandardInput.WriteLine("bad-matrix");child.StandardInput.Flush();child.StandardOutput.ReadLine();bool matrixRejected=false;try{scene.Read(.3);}catch(MemoryFault){matrixRejected=true;}Check(matrixRejected,"non-finite matrix invalidates snapshot");Check(memory.BytesRead>0&&memory.ReadCalls>0,"native read counters contain actual transfers");
     }
    }finally{if(!child.HasExited){child.StandardInput.WriteLine("stop");child.StandardInput.Flush();if(!child.WaitForExit(3000))child.Kill();}}
   }
   var output=new{mode="controlled owned child process and offscreen drawing; not a Roblox gameplay test",passed=results.Count-failed,failed,seconds=clock.Elapsed.TotalSeconds,results};File.WriteAllText(report,new JavaScriptSerializer().Serialize(output));Console.WriteLine("TOTAL "+(results.Count-failed)+" passed / "+failed+" failed");return failed==0?0:1;
  }
 }
}
