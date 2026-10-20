// C# | ExternalEsp/Overlay.cs | .NET Framework 4.8 x64 / Windows | External click-through ESP, foreground and freshness checks, F9 pause and F12 exit.
using System;using System.Drawing;using System.Drawing.Drawing2D;using System.Diagnostics;using System.Runtime.InteropServices;using System.Threading;using System.Windows.Forms;
namespace BlueLockEsp
{
 static class WindowApi
 {
  [StructLayout(LayoutKind.Sequential)]public struct Rect {public int Left,Top,Right,Bottom;}
  [StructLayout(LayoutKind.Sequential)]public struct Point {public int X,Y;}
  [DllImport("user32.dll")]public static extern bool GetClientRect(IntPtr h,out Rect r);
  [DllImport("user32.dll")]public static extern bool ClientToScreen(IntPtr h,ref Point p);
  [DllImport("user32.dll")]public static extern IntPtr GetForegroundWindow();
  [DllImport("user32.dll")]public static extern bool IsIconic(IntPtr h);
  [DllImport("user32.dll",SetLastError=true)]public static extern bool RegisterHotKey(IntPtr h,int id,uint modifiers,uint key);
  [DllImport("user32.dll")]public static extern bool UnregisterHotKey(IntPtr h,int id);
  [DllImport("user32.dll")]public static extern bool SetProcessDPIAware();
  public static bool Bounds(IntPtr window,out Rectangle r){r=Rectangle.Empty;Rect client;var p=new Point();if(window==IntPtr.Zero||IsIconic(window)||!GetClientRect(window,out client)||!ClientToScreen(window,ref p)||client.Right<100||client.Bottom<100)return false;r=new Rectangle(p.X,p.Y,client.Right-client.Left,client.Bottom-client.Top);return true;}
 }
 public static class EspDrawing
 {
  public static int Draw(Graphics g,Snapshot snapshot,int width,int height,bool players,bool balls,double maxDistance)
  {
   int count=0;if(snapshot==null)return count;g.SmoothingMode=SmoothingMode.AntiAlias;using(var font=new Font("Segoe UI",10,FontStyle.Bold))using(var shadow=new SolidBrush(Color.Black))using(var playerBrush=new SolidBrush(Color.OrangeRed))using(var ballBrush=new SolidBrush(Color.Yellow))using(var playerPen=new Pen(Color.OrangeRed,2))using(var ballPen=new Pen(Color.Yellow,2))
   foreach(var e in snapshot.Entities){if(e.Kind=="player"&&!players||e.Kind=="ball"&&!balls||!Double.IsNaN(e.Distance)&&e.Distance>maxDistance)continue;PointF center;if(!Projection.Screen(e.Position,snapshot.Matrix,width,height,out center))continue;string distance=Double.IsNaN(e.Distance)?"distância indisponível":e.Distance.ToString("F1")+" studs";
    if(e.Kind=="ball"){g.DrawEllipse(ballPen,center.X-8,center.Y-8,16,16);string text=e.Name+" · "+distance;g.DrawString(text,font,shadow,center.X+11,center.Y+1);g.DrawString(text,font,ballBrush,center.X+10,center.Y);count++;}
    else{PointF top,bottom;var head=new V3(e.Head.X,e.Head.Y+.7f,e.Head.Z);var feet=new V3(e.Position.X,e.Position.Y-2.8f,e.Position.Z);if(!Projection.Screen(head,snapshot.Matrix,width,height,out top)||!Projection.Screen(feet,snapshot.Matrix,width,height,out bottom))continue;float h=Math.Abs(bottom.Y-top.Y);if(h<5||h>height*1.5)continue;float w=h*.45f;g.DrawRectangle(playerPen,top.X-w/2,Math.Min(top.Y,bottom.Y),w,h);string text=e.Name+" · "+distance+" · HP "+e.Health.ToString("F0");g.DrawString(text,font,shadow,top.X-w/2+1,Math.Min(top.Y,bottom.Y)-22);g.DrawString(text,font,playerBrush,top.X-w/2,Math.Min(top.Y,bottom.Y)-23);count++;}
   }return count;
  }
 }
 sealed class EspOverlay:Form
 {
  public Snapshot Snapshot;public bool Players=true,Balls=true;public double MaxDistance=2000;
  public EspOverlay(){Text="BlueLock ESP overlay";FormBorderStyle=FormBorderStyle.None;ShowInTaskbar=false;TopMost=true;BackColor=Color.Magenta;TransparencyKey=Color.Magenta;DoubleBuffered=true;}
  protected override bool ShowWithoutActivation {get{return true;}}
  protected override CreateParams CreateParams {get{var p=base.CreateParams;p.ExStyle|=0x00000020|0x00080000|0x08000000|0x00000080;return p;}}
  protected override void OnPaint(PaintEventArgs e){base.OnPaint(e);EspDrawing.Draw(e.Graphics,Snapshot,ClientSize.Width,ClientSize.Height,Players,Balls,MaxDistance);}
 }
 sealed class EspPanel:Form
 {
  readonly Stopwatch time=Stopwatch.StartNew();readonly object sync=new object();readonly int pid;readonly string[] names;readonly Journal journal;readonly Label status=new Label();readonly CheckBox showPlayers=new CheckBox(),showBalls=new CheckBox();readonly NumericUpDown range=new NumericUpDown();readonly Button pause=new Button();readonly System.Windows.Forms.Timer timer=new System.Windows.Forms.Timer();readonly EspOverlay overlay=new EspOverlay();Thread reader;volatile bool stopped,paused;Snapshot snapshot;string message="Procurando Roblox...";IntPtr targetWindow;bool f9,f12;
  public EspPanel(int targetPid,string[] ballNames,Journal output)
  {
   pid=targetPid;names=ballNames;journal=output;Text="BlueLock External | ESP";ClientSize=new Size(620,255);BackColor=Color.FromArgb(18,23,34);ForeColor=Color.White;Font=new Font("Segoe UI",10);FormBorderStyle=FormBorderStyle.FixedDialog;MaximizeBox=false;
   var title=new Label{Text="BLUE LOCK EXTERNAL · ESP",AutoSize=false,Font=new Font("Segoe UI",17,FontStyle.Bold)};title.SetBounds(20,15,580,40);Controls.Add(title);status.SetBounds(20,60,580,76);Controls.Add(status);
   showPlayers.Text="Jogadores";showPlayers.Checked=true;showPlayers.SetBounds(20,140,130,25);Controls.Add(showPlayers);showBalls.Text="Bola";showBalls.Checked=true;showBalls.SetBounds(165,140,100,25);Controls.Add(showBalls);range.Minimum=10;range.Maximum=20000;range.Value=2000;range.Increment=100;range.SetBounds(380,140,110,25);Controls.Add(range);var unit=new Label{Text="studs",AutoSize=true};unit.SetBounds(500,142,80,25);Controls.Add(unit);
   pause.Text="F9 · Pausar";pause.SetBounds(20,193,190,35);pause.Click+=delegate{paused=!paused;};Controls.Add(pause);var exit=new Button{Text="F12 · Encerrar"};exit.SetBounds(225,193,190,35);exit.Click+=delegate{Close();};Controls.Add(exit);
   Shown+=delegate{f12=WindowApi.RegisterHotKey(Handle,12,0x4000,0x7B);f9=WindowApi.RegisterHotKey(Handle,9,0x4000,0x78);if(!f12){MessageBox.Show("F12 indisponível. Feche outro programa que reservou essa tecla.",Text);Close();return;}reader=new Thread(ReadLoop){IsBackground=true,Name="BlueLock ESP reader"};reader.Start();timer.Interval=80;timer.Tick+=Tick;timer.Start();};
   FormClosing+=delegate{stopped=true;timer.Stop();overlay.Hide();if(f12)WindowApi.UnregisterHotKey(Handle,12);if(f9)WindowApi.UnregisterHotKey(Handle,9);if(reader!=null)reader.Join(1000);overlay.Dispose();journal.Write("closed",new{reason="panel close or F12",noInputsHeld=true});};
  }
  protected override void WndProc(ref Message m){if(m.Msg==0x0312){if(m.WParam.ToInt32()==12){stopped=true;overlay.Hide();Close();return;}if(m.WParam.ToInt32()==9)paused=!paused;}base.WndProc(ref m);}
  void Set(Snapshot value,string text,IntPtr window){lock(sync){snapshot=value;message=text;targetWindow=window;}}
  void ReadLoop()
  {
   Target target=null;SceneReader scene=null;string last="";double logAt=0;
   try{while(!stopped){try{if(target==null){target=Target.Find(pid);scene=new SceneReader(target.Memory,target.Base,names);journal.Write("attached",new{pid=target.Process.Id,version=target.Version,process=target.Path});}if(target.Process.HasExited)throw new InvalidOperationException("Cliente encerrou");target.Process.Refresh();IntPtr window=target.Process.MainWindowHandle;
      if(paused){Set(null,"PAUSADO · F9 para retomar",window);Thread.Sleep(100);continue;}
      Snapshot next=scene.Read(time.Elapsed.TotalSeconds);Set(next,"Leitura válida · jogadores: "+next.Players+" · candidatos a bola: "+next.Balls+"\nBola por nome: "+String.Join(", ",names),window);if(time.Elapsed.TotalSeconds>=logAt){logAt=time.Elapsed.TotalSeconds+1;journal.Write("frame",new{next.Time,next.Players,next.Balls,next.Skipped,next.Visited,readCalls=target.Memory.ReadCalls,bytes=target.Memory.BytesRead});}last="";Thread.Sleep(50);
     }catch(Exception e){Set(null,e.Message,IntPtr.Zero);if(last!=e.Message){journal.Write("unavailable",new{error=e.Message});last=e.Message;}if(target!=null&&(!(e is MemoryFault)||target.Process.HasExited)){target.Dispose();target=null;scene=null;}Thread.Sleep(500);}}
   }finally{if(target!=null)target.Dispose();}
  }
  void Tick(object sender,EventArgs e)
  {
   Snapshot s;string text;IntPtr w;lock(sync){s=snapshot;text=message;w=targetWindow;}status.Text=text;pause.Text=paused?"F9 · Retomar":"F9 · Pausar";Rectangle bounds;bool fresh=s!=null&&time.Elapsed.TotalSeconds-s.Time<=.25;
   if(stopped||paused||!fresh||WindowApi.GetForegroundWindow()!=w||!WindowApi.Bounds(w,out bounds)){overlay.Hide();return;}overlay.Snapshot=s;overlay.Players=showPlayers.Checked;overlay.Balls=showBalls.Checked;overlay.MaxDistance=(double)range.Value;overlay.Bounds=bounds;if(!overlay.Visible)overlay.Show();overlay.Invalidate();
  }
 }
}
