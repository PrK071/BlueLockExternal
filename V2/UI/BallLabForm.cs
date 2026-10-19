// C# | UI/BallLabForm.cs | .NET Framework 4.8 x64 | Physical ball-cycle controls and reference diagnostics, independent from visual estimates.
using System;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using BlueLockV2.BallControl;
using BlueLockV2.Core;
using BlueLockV2.Navigation;
using BlueLockV2.Input;
using BlueLockV2.Capture;
using BlueLockV2.Configuration;

namespace BlueLockV2.UI
{
    internal sealed class BallLabForm : Form
    {
        readonly StudioTelemetry source;
        readonly IClock clock;
        readonly Func<Task> stopOther;
        readonly Action showCalibration;
        readonly TextBox status = new TextBox();
        readonly NumericUpDown power = new NumericUpDown { Minimum=10, Maximum=100, Value=70, Width=55 };
        readonly NumericUpDown count = new NumericUpDown { Minimum=1, Maximum=50, Value=10, Width=45 };
        readonly ComboBox scenario = new ComboBox { DropDownStyle=ComboBoxStyle.DropDownList, Width=240 };
        readonly CheckBox suite = new CheckBox { Text="Bateria dos 10 cenários", Checked=true, AutoSize=true };
        readonly CheckBox visual = new CheckBox { Text="OpenCV assistido (opcional)", AutoSize=true };
        readonly CheckBox running = new CheckBox { Text="&Corrida de teste (24 studs/s)", AutoSize=true };
        readonly Panel map = new Panel();
        readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
        readonly string folder;
        BallLabSession session;
        Pipeline vision;
        bool starting;
        string message = "Gere BallControlLab.rbxlx, abra no Studio e dê Play. Valide WASD; depois Iniciar ciclo.";
        public BallLabForm(StudioTelemetry bridge, IClock time, Func<Task> stopPrevious, Action calibration)
        {
            source=bridge; clock=time; stopOther=stopPrevious; showCalibration=calibration;
            Text="BlueLock V2.2 — posse, condução e gol (Studio)"; Width=1040; Height=760;
            folder=Path.Combine(Directory.GetParent(AppDomain.CurrentDomain.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar)).FullName,"StudioLab","Generated");
            var root=new TableLayoutPanel { Dock=DockStyle.Fill, RowCount=3, ColumnCount=1 };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute,110)); root.RowStyles.Add(new RowStyle(SizeType.Percent,48)); root.RowStyles.Add(new RowStyle(SizeType.Percent,52)); Controls.Add(root);
            var controls=new FlowLayoutPanel { Dock=DockStyle.Fill };
            var generate=new Button { Text="&Gerar BallControlLab", Width=170, Height=32 };
            var calibrate=new Button { Text="&Validar WASD", Width=125, Height=32 };
            var start=new Button { Text="&Iniciar ciclo", Width=120, Height=32 };
            var stop=new Button { Text="PARAR / F12", Width=120, Height=32 };
            var export=new Button { Text="Exportar evidências", Width=150, Height=32 };
            controls.Controls.AddRange(new Control[]{generate,calibrate,start,stop,export}); controls.SetFlowBreak(export,true);
            scenario.Items.AddRange(new object[]{"1 Bola próxima","2 Bola distante","3 Bola em movimento","4 Costas para o gol","5 Câmera rotacionada","6 Perda de posse","7 Chute com gol","8 Chute com desvio/erro","9 Oclusão visual","10 Falha de navegação"}); scenario.SelectedIndex=0;
            controls.Controls.AddRange(new Control[]{scenario,new Label{Text="Potência %",AutoSize=true},power,new Label{Text="Ensaios",AutoSize=true},count,suite,visual,running});
            root.Controls.Add(controls,0,0); status.Multiline=status.ReadOnly=true; status.ScrollBars=ScrollBars.Vertical; status.Dock=DockStyle.Fill; root.Controls.Add(status,0,1);
            map.Dock=DockStyle.Fill; map.BackColor=Color.FromArgb(20,65,30); map.Paint+=Draw; root.Controls.Add(map,0,2);
            generate.Click+=delegate { try { message="Gerado: "+BallLabProject.Write(folder,source.Endpoint)+". Abra esse arquivo no Studio e dê Play."; } catch(Exception e){message=e.ToString();} };
            calibrate.Click+=delegate { showCalibration(); };
            start.Click+=async delegate { await StartCycle(); };
            stop.Click+=delegate { EmergencySession(); message="F12: entradas liberadas e ciclo interrompido."; };
            export.Click+=delegate
            {
                if(session==null){message="Inicie um ensaio para gerar evidências.";return;}
                using(var dialog=new FolderBrowserDialog()) if(dialog.ShowDialog(this)==DialogResult.OK)
                {
                    File.Copy(session.LogPath,Path.Combine(dialog.SelectedPath,Path.GetFileName(session.LogPath)),true);
                    File.Copy(session.AttemptsPath,Path.Combine(dialog.SelectedPath,Path.GetFileName(session.AttemptsPath)),true);
                    message="Evidências copiadas para "+dialog.SelectedPath;
                }
            };
            timer.Interval=250; timer.Tick+=delegate { RefreshStatus(); map.Invalidate(); }; timer.Start();
            FormClosing+=delegate { EmergencySession(); if(session!=null){session.Dispose();session=null;} if(vision!=null){vision.Dispose();vision=null;} timer.Stop();timer.Dispose(); };
        }
        public void EmergencySession() { if(session!=null) session.EmergencyStop(); }
        protected override bool ProcessCmdKey(ref Message message,Keys key)
        { if(key==Keys.F12){EmergencySession();return true;}return base.ProcessCmdKey(ref message,key); }
        async Task StartCycle()
        {
            if(starting)return; starting=true;
            try
            {
                if(source.LatestBall==null)throw new InvalidOperationException("Telemetria V2.2 ausente. Abra BallControlLab.rbxlx e dê Play.");
                if(!source.ControlValidated)throw new InvalidOperationException("Valide os 16 pulsos WASD nesse laboratório antes de iniciar.");
                if(session!=null){session.Dispose();session=null;} if(vision!=null){vision.Dispose();vision=null;}
                await stopOther();
                if(visual.Checked)
                {
                    vision=new Pipeline(new Settings{Mode=RunMode.ASSISTIDO,Vision=true,Sprint=false,Overlay=false,UseRoi=false},new WindowSource(true,clock),new NullExecutor(),clock); vision.Start();
                }
                var transport=new NativeTransport(true,delegate {return DesktopEnvironment.StudioWindowFocused(source.WindowHandle);});
                session=new BallLabSession(source,clock,transport,(double)power.Value/100,suite.Checked,(int)count.Value,scenario.SelectedIndex+1,running.Checked);
                message="Ciclo iniciado. Volte ao campo do Studio. F12 interrompe. A posse depende de dono+vínculo; gol depende do cruzamento físico do plano.";
            }
            catch(Exception error){message=error.ToString();}
            finally{starting=false;}
        }
        void RefreshStatus()
        {
            BallFrame frame=source.LatestBall;
            string text=message+"\r\n";
            text+=frame==null?"Aguardando telemetria V2.2. "+source.LastError:
                "REFERÊNCIA STUDIO: cenário "+frame.CaseId+", ensaio "+frame.Trial+", idade "+(clock.Now-frame.Time).ToString("0.00")+" s\r\n"+
                "Dono: "+frame.OwnerId+" / local "+frame.LocalId+" | vínculo físico: "+frame.ConstraintIntact+" | bola visível: "+frame.BallVisible+" | gol registrado: "+frame.GoalCounter+"\r\n"+
                "Bola "+frame.Ball+" Y="+frame.BallY.ToString("0.00")+" | gol "+frame.Goal+" | velocidade "+frame.Motion.Basis.Forward.Length.ToString("0.0")+" studs/s | WASD "+(source.ControlValidated?"VALIDADO":"PENDENTE")+"\r\n";
            if(session!=null)
            {
                var c=session.Controller;
                text+="CICLO: "+c.State+" | posse "+c.Possession+" | resultado "+c.Outcome+" | concluídos "+c.Completed+"\r\n"+c.Reason+"\r\n"+
                    "Foco suspenso: "+session.Paused+" | CPU "+session.Metrics.CpuPercent.ToString("0.0")+"% | RAM privada "+session.Metrics.PrivateMb.ToString("0.0")+" MiB\r\n"+
                    "RMSE trajetória "+(c.TrajectorySamples==0?"sem amostras":c.TrajectoryRmse.ToString("0.000")+" / "+c.TrajectorySamples+" amostras")+" | decisão p95 "+session.Metrics.Decision.Percentile(.95).ToString("0.00")+" ms\r\n"+
                    "CSV/JSONL: "+session.LogPath+"\r\n"+session.LastError;
            }
            var image=vision==null || vision.Latest==null?null:vision.Latest.State;
            var estimate=PossessionDetector.EstimateVision(image);
            text+="\r\nESTIMATIVA OPENCV (separada): "+estimate.State+" / confiança "+estimate.Confidence.ToString("0.00")+" — "+estimate.Reason;
            if(status.Text!=text)status.Text=text;
        }
        void Draw(object sender,PaintEventArgs e)
        {
            BallFrame frame=source.LatestBall;if(frame==null)return;
            Func<Vec,PointF> point=delegate(Vec v){return new PointF(map.Width/2f+(float)v.X*4,map.Height-30+(float)v.Y*4);};
            PointF p=point(frame.Motion.Player),b=point(frame.Ball),g=point(frame.Goal);
            e.Graphics.DrawLine(Pens.White,g.X-32,g.Y,g.X+32,g.Y);e.Graphics.DrawString("GOL",Font,Brushes.White,g.X-12,g.Y-18);
            e.Graphics.FillEllipse(Brushes.Blue,p.X-5,p.Y-5,10,10);e.Graphics.FillEllipse(Brushes.White,b.X-4,b.Y-4,8,8);
            e.Graphics.DrawLine(Pens.Cyan,p,b);
            if(session!=null && session.Controller.Navigation!=null)
            { PointF t=point(session.Controller.Navigation.Target);e.Graphics.DrawLine(Pens.Yellow,p,t); }
        }
    }
}
