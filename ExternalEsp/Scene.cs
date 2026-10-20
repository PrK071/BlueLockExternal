// C# | ExternalEsp/Scene.cs | .NET Framework 4.8 x64 | Snapshot reader with bounded traversal; no writes, injection or input generation.
using System;using System.Collections.Generic;using System.Diagnostics;using System.Linq;using System.Drawing;
namespace BlueLockEsp
{
 public struct V3 {public float X,Y,Z;public V3(float x,float y,float z){X=x;Y=y;Z=z;}public bool Valid {get{return Finite(X)&&Finite(Y)&&Finite(Z)&&Math.Abs(X)<1000000&&Math.Abs(Y)<1000000&&Math.Abs(Z)<1000000;}}static bool Finite(float f){return !Single.IsNaN(f)&&!Single.IsInfinity(f);}public static double Distance(V3 a,V3 b){return Math.Sqrt(Math.Pow(a.X-b.X,2)+Math.Pow(a.Y-b.Y,2)+Math.Pow(a.Z-b.Z,2));}}
 public sealed class Entity {public string Kind,Name,Path;public long Address;public V3 Position,Head;public float Health;public double Distance;}
 public sealed class Snapshot {public double Time;public float[] Matrix;public float Width,Height;public string State;public int Players,Balls,Skipped,Visited;public Entity[] Entities=new Entity[0];}
 public static class Projection
 {
  public static bool Screen(V3 p,float[] m,int width,int height,out PointF screen){screen=new PointF();if(m==null||m.Length!=16||width<1||height<1||!p.Valid)return false;double w=m[12]*p.X+m[13]*p.Y+m[14]*p.Z+m[15];if(Double.IsNaN(w)||Double.IsInfinity(w)||w<=.01)return false;double x=(m[0]*p.X+m[1]*p.Y+m[2]*p.Z+m[3])/w,y=(m[4]*p.X+m[5]*p.Y+m[6]*p.Z+m[7])/w;if(Double.IsNaN(x)||Double.IsNaN(y)||Math.Abs(x)>1.2||Math.Abs(y)>1.2)return false;screen=new PointF((float)((x+1)*width*.5),(float)((1-y)*height*.5));return true;}
 }
 public sealed class SceneReader
 {
  readonly IMemory memory;readonly long module;readonly HashSet<string> ballNames;readonly Stopwatch budget=new Stopwatch();long workspace,players,localPlayer,visual,dataModel;double refreshAt=-1;long[] playerNodes=new long[0];List<Entity> ballNodes=new List<Entity>();int visited;
  public SceneReader(IMemory source,long moduleBase,IEnumerable<string> names){memory=source;module=moduleBase;ballNames=new HashSet<string>(names,StringComparer.OrdinalIgnoreCase);}
  void Budget(){if(budget.Elapsed.TotalMilliseconds>75)throw new MemoryFault("Snapshot excedeu orçamento de 75 ms");}
  public string Name(long instance){return memory.StringAt(memory.PointerAt(instance+Offsets.Instance.NameContainer)+Offsets.Instance.Name);}
  public long[] Children(long parent)
  {
   Budget();long list=memory.PointerAt(parent+Offsets.Instance.ChildrenStart);var ends=memory.Read(list,16);long start=BitConverter.ToInt64(ends,0),end=BitConverter.ToInt64(ends,8);if(start==0&&end==0)return new long[0];if(!MemoryValues.Pointer(start)||end<start||(end-start)%8!=0||end-start>131072)throw new MemoryFault("Vetor de filhos inválido ou excede 16384 slots");if(start==end)return new long[0];var bytes=memory.Read(start,(int)(end-start));var result=new List<long>();var seen=new HashSet<long>();for(int at=0;at<bytes.Length;at+=8){long p=BitConverter.ToInt64(bytes,at);if(!MemoryValues.Pointer(p)||!seen.Add(p))continue;try{if(memory.PointerAt(p+Offsets.Instance.Parent)==parent)result.Add(p);}catch(MemoryFault){}}return result.ToArray();
  }
  long Child(long parent,string wanted){foreach(long p in Children(parent)){try{if(Name(p)==wanted)return p;}catch(MemoryFault){}}throw new MemoryFault("Filho ausente: "+wanted);}
  V3 Position(long part){long primitive=memory.PointerAt(part+Offsets.BasePart.Primitive);var b=memory.Read(primitive+Offsets.Primitive.Position,12);var v=new V3(BitConverter.ToSingle(b,0),BitConverter.ToSingle(b,4),BitConverter.ToSingle(b,8));if(!v.Valid)throw new MemoryFault("Posição inválida");return v;}
  void Discover(double now)
  {
   players=Child(dataModel,"Players");workspace=memory.PointerAt(dataModel+Offsets.DataModel.Workspace);if(Name(workspace)!="Workspace")throw new MemoryFault("Workspace não validado");localPlayer=memory.PointerAt(players+Offsets.Player.LocalPlayer);playerNodes=Children(players);var found=new List<Entity>();var pending=new Queue<Tuple<long,string,int>>();pending.Enqueue(Tuple.Create(workspace,"Workspace",0));visited=0;
   while(pending.Count>0){Budget();var item=pending.Dequeue();if(++visited>3000)throw new MemoryFault("Árvore excede limite de 3000 nós; configure nomes/caminho");long[] children;try{children=Children(item.Item1);}catch(MemoryFault){continue;}foreach(long child in children){string name;try{name=Name(child);}catch(MemoryFault){continue;}string path=item.Item2+"/"+name;if(ballNames.Contains(name)){try{V3 pos=Position(child);found.Add(new Entity{Kind="ball",Name=name,Path=path,Address=child,Position=pos,Head=pos,Health=0});}catch(MemoryFault){}}if(item.Item3<5)pending.Enqueue(Tuple.Create(child,path,item.Item3+1));}}
   ballNodes=found;refreshAt=now+1;
  }
  public Snapshot Read(double now)
  {
   budget.Restart();long fake=memory.PointerAt(module+Offsets.FakeDataModel.Pointer),root=memory.PointerAt(fake+Offsets.FakeDataModel.RealDataModel);long engine=memory.PointerAt(module+Offsets.VisualEngine.Pointer);if(root!=dataModel||engine!=visual){dataModel=root;visual=engine;refreshAt=-1;ballNodes.Clear();playerNodes=new long[0];}
   var matrixData=memory.Read(visual+Offsets.VisualEngine.ViewMatrix,64);var matrix=new float[16];double norm=0;for(int i=0;i<16;i++){matrix[i]=BitConverter.ToSingle(matrixData,i*4);if(Single.IsNaN(matrix[i])||Single.IsInfinity(matrix[i])||Math.Abs(matrix[i])>1000000)throw new MemoryFault("Matriz inválida");norm+=Math.Abs(matrix[i]);}if(norm<.01)throw new MemoryFault("Matriz vazia");var dimensions=memory.Read(visual+Offsets.VisualEngine.Dimensions,8);float width=BitConverter.ToSingle(dimensions,0),height=BitConverter.ToSingle(dimensions,4);if(!(width>=100&&width<=16384&&height>=100&&height<=16384))throw new MemoryFault("Dimensões inválidas");
   if(now>=refreshAt)Discover(now);var output=new List<Entity>();int skipped=0;V3? localPosition=null;
   try{long localModel=memory.PointerAt(localPlayer+Offsets.Player.ModelInstance);localPosition=Position(Child(localModel,"HumanoidRootPart"));}catch(MemoryFault){}
   foreach(long player in playerNodes.Take(100)){Budget();if(player==localPlayer)continue;try{long model=memory.PointerAt(player+Offsets.Player.ModelInstance),rootPart=Child(model,"HumanoidRootPart");V3 pos=Position(rootPart),head=new V3(pos.X,pos.Y+3,pos.Z);try{head=Position(Child(model,"Head"));}catch(MemoryFault){}float health=memory.FloatAt(Child(model,"Humanoid")+Offsets.Humanoid.Health);if(health<=0||health>1000000)continue;string name=Name(player);output.Add(new Entity{Kind="player",Name=name,Address=player,Position=pos,Head=head,Health=health,Distance=localPosition.HasValue?V3.Distance(localPosition.Value,pos):Double.NaN});}catch(MemoryFault){skipped++;}}
   foreach(var b in ballNodes.Take(50)){Budget();try{V3 pos=Position(b.Address);output.Add(new Entity{Kind="ball",Name=b.Name,Path=b.Path,Address=b.Address,Position=pos,Head=pos,Distance=localPosition.HasValue?V3.Distance(localPosition.Value,pos):Double.NaN});}catch(MemoryFault){skipped++;}}
   Budget();return new Snapshot{Time=now,Matrix=matrix,Width=width,Height=height,State="READY",Players=output.Count(e=>e.Kind=="player"),Balls=output.Count(e=>e.Kind=="ball"),Entities=output.ToArray(),Skipped=skipped,Visited=visited};
  }
 }
}
