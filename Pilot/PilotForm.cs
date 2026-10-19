// C# | PilotForm.cs | .NET Framework / Windows x64 | Calibrated visual ball-following pilot.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

internal sealed class PilotForm : Form
{
    private PilotSettings settings;
    private readonly OutputController output=new OutputController();
    private KeyboardHook keyboard;
    private readonly Stopwatch clock=Stopwatch.StartNew();
    private readonly System.Windows.Forms.Timer timer=new System.Windows.Forms.Timer();
    private readonly PictureBox preview=new PictureBox(),sampleView=new PictureBox();
    private readonly Label status=new Label(),tracking=new Label(),anchorLabel=new Label(),diagnostics=new Label();
    private readonly TextBox logs=new TextBox(),shotKey=new TextBox();
    private readonly NumericUpDown shotMs=new NumericUpDown(),diameter=new NumericUpDown(),threshold=new NumericUpDown();
    private readonly CheckBox arrows=new CheckBox(),sprint=new CheckBox(),dribble=new CheckBox(),shoot=new CheckBox(),jump=new CheckBox(),pass=new CheckBox();
    private Bitmap ball;
    private Rectangle lastBox=Rectangle.Empty;
    private readonly HashSet<int> movement=new HashSet<int>();
    private bool running,frameBusy,closing,focusSuspended,templateValid;
    private long probeUntil,lastTelemetry;
    private IntPtr gameWindow;
    private long generation,lastVision,lastBall,lastShot=-5000,lastQ=-5000,lastJump=-5000,lastPass=-10000,nearSince=-1;
    private int consecutive;
    private readonly Color background=Color.FromArgb(18,24,35),surface=Color.FromArgb(29,39,54),ink=Color.FromArgb(229,237,247);

    internal PilotForm()
    {
        Text="Blue Lock External | Piloto visual 1.3";
        AutoScaleMode=AutoScaleMode.None;
        ClientSize=new Size(920,735); FormBorderStyle=FormBorderStyle.FixedSingle; MaximizeBox=false;
        StartPosition=FormStartPosition.CenterScreen; BackColor=background; ForeColor=ink; Font=new Font("Segoe UI",10);
        Label title=LabelAt("BLUE LOCK / PILOTO VISUAL",24,17,875,38); title.Font=new Font("Segoe UI",21,FontStyle.Bold);
        LabelAt("Calibre a bola, alinhe a câmera e use F9 para começar.",26,60,875,25);
        status.SetBounds(26,99,865,31); status.Font=new Font(Font.FontFamily,13,FontStyle.Bold); Controls.Add(status);
        tracking.SetBounds(26,133,865,25); Controls.Add(tracking);
        diagnostics.SetBounds(26,159,865,16);diagnostics.Font=new Font(Font.FontFamily,8);Controls.Add(diagnostics);
        GroupBox options=new GroupBox {Text="Controle",ForeColor=ink,BackColor=background}; options.SetBounds(24,175,447,313); Controls.Add(options);
        SetupCheck(arrows,"Usar setinhas (desmarcado = WASD)",18,30,options);
        SetupCheck(sprint,"Segurar Shift durante o movimento",18,62,options);
        SetupCheck(dribble,"Q perto da bola",18,94,options);
        SetupCheck(shoot,"Chutar após aproximação estável",18,126,options);
        SetupCheck(jump,"Space perto da bola (intervalo de 3 s)",18,158,options);
        SetupCheck(pass,"R ao perder a bola (intervalo de 8 s)",18,190,options);
        LabelAtParent("Chute (ms)",18,235,100,25,options); SetupNumber(shotMs,120,232,95,10,2000,options);
        LabelAtParent("Botão/tecla",230,235,100,25,options);
        shotKey.SetBounds(327,232,100,29); StyleInput(shotKey); options.Controls.Add(shotKey);
        LabelAtParent("Botão: MOUSELEFT / MOUSERIGHT ou letra.",18,274,420,25,options);
        LabelAt("Visão local · quadro mais recente",498,176,396,24);
        preview.SetBounds(498,205,396,222); preview.SizeMode=PictureBoxSizeMode.Zoom; preview.BackColor=Color.Black; Controls.Add(preview);
        sampleView.SetBounds(498,442,50,45); sampleView.SizeMode=PictureBoxSizeMode.Zoom; sampleView.BackColor=surface; Controls.Add(sampleView);
        anchorLabel.SetBounds(560,442,334,45); Controls.Add(anchorLabel);
        LabelAt("Amostra da bola (px)",26,505,172,25); SetupNumber(diameter,199,502,78,12,90,this);
        LabelAt("Pontuação mínima (%)",301,505,195,25); SetupNumber(threshold,497,502,78,75,99,this);
        Button save=ButtonAt("Salvar",594,500,132,35); save.Click+=delegate{Save(true);};
        Button stop=ButtonAt("F12 · Parar tudo",740,500,154,35); stop.Click+=delegate{Stop("Interrompido pelo botão.");};
        LabelAt("F2 teste de caminhada · F3 pés · F4 bola · F5 limpar rastreio · F6/F7 chute",26,548,870,25);
        LabelAt("F8 corrida · F9 piloto · F10 Ctrl · F11 pulo · F12 parar",26,575,870,25);
        logs.SetBounds(26,614,868,88); logs.Multiline=true; logs.ReadOnly=true; logs.ScrollBars=ScrollBars.Vertical; StyleInput(logs); Controls.Add(logs);
        LabelAt("Piloto por aproximação visual; não identifica posse, adversários ou direção do gol.",26,707,870,24).Font=new Font(Font.FontFamily,9);
        try { settings=SettingsStore.Load() ?? new PilotSettings(); } catch(Exception e) { settings=new PilotSettings(); Log("Configuração padrão: "+e.Message); }
        ApplySettings();
        try { if(File.Exists(SettingsStore.TemplatePath)) using(Bitmap file=new Bitmap(SettingsStore.TemplatePath)) ball=(Bitmap)file.Clone(); }
        catch(Exception e) { Log("Amostra não carregada: "+e.Message); }
        templateValid=ball!=null && Vision.SampleProblem(ball)==null;
        if(ball!=null && !templateValid){ShowSample(ball);Log("Amostra anterior rejeitada: "+Vision.SampleProblem(ball)+". Refaça F4.");}
        UpdateSample(); UpdateStatus();
        keyboard=new KeyboardHook(); keyboard.IsPanelFocused=delegate{return ContainsFocus;};
        keyboard.IsGameFocused=delegate{return running ? Platform.GameFocused(gameWindow) : Platform.RobloxFocused();};
        keyboard.Hotkey=delegate(int key){if(!closing) BeginInvoke((Action)delegate{OnHotkey(key);});};
        keyboard.ManualKey=delegate(int key){if(!closing) BeginInvoke((Action)delegate{if(running) Stop("Controle manual: piloto pausado.");});};
        timer.Interval=35; timer.Tick+=Tick; timer.Start();
        FormClosing+=delegate { closing=true; timer.Stop(); if(keyboard!=null) keyboard.Dispose(); Stop(null); Save(false); };
        FormClosed+=delegate { if(!frameBusy && ball!=null) ball.Dispose(); if(preview.Image!=null) preview.Image.Dispose(); if(sampleView.Image!=null) sampleView.Image.Dispose(); };
        Log(!templateValid?"Use F2 para testar caminhada. Depois, aponte o ponteiro na bola e use F4.":"Amostra carregada. Calibre novamente se a bola ou a câmera mudaram.");
        Log("Para F3/F4: Ctrl deve estar destravado para mover o ponteiro. F10 envia Ctrl.");
    }
    private Label LabelAt(string text,int x,int y,int w,int h) { return LabelAtParent(text,x,y,w,h,this); }
    private Label LabelAtParent(string text,int x,int y,int w,int h,Control parent) { Label l=new Label{Text=text}; l.SetBounds(x,y,w,h);parent.Controls.Add(l);return l; }
    private void StyleInput(Control c) { c.BackColor=surface;c.ForeColor=ink; }
    private void SetupNumber(NumericUpDown n,int x,int y,int w,int min,int max,Control parent) { n.SetBounds(x,y,w,29);n.Minimum=min;n.Maximum=max;StyleInput(n);parent.Controls.Add(n); }
    private void SetupCheck(CheckBox c,string text,int x,int y,Control parent) {c.Text=text;c.SetBounds(x,y,420,27);parent.Controls.Add(c);}
    private Button ButtonAt(string text,int x,int y,int w,int h) {Button b=new Button{Text=text,FlatStyle=FlatStyle.Flat,BackColor=surface,ForeColor=ink};b.SetBounds(x,y,w,h);Controls.Add(b);return b;}
    private void Log(string message)
    {
        if(closing||IsDisposed)return;
        string line=DateTime.Now.ToString("HH:mm:ss")+"  "+message+Environment.NewLine;
        if(logs.TextLength>24000)logs.Text=logs.Text.Substring(logs.TextLength-12000);
        logs.AppendText(line);
        try{Directory.CreateDirectory(SettingsStore.Folder);File.AppendAllText(Path.Combine(SettingsStore.Folder,"pilot.log"),line);}catch{}
    }
    private static double Clamp(double x,double min,double max) {return Math.Max(min,Math.Min(max,x));}
    private void ApplySettings()
    {
        settings.AnchorX=Clamp(settings.AnchorX,.05,.95);settings.AnchorY=Clamp(settings.AnchorY,.2,.9);
        settings.CalibratedWindowWidth=Math.Max(100,settings.CalibratedWindowWidth);
        settings.DribbleIntervalMs=Math.Max(700,settings.DribbleIntervalMs);settings.ShotIntervalMs=Math.Max(1200,settings.ShotIntervalMs);
        arrows.Checked=settings.UseArrows;sprint.Checked=settings.Sprint;dribble.Checked=settings.Dribble;shoot.Checked=settings.AutoShoot;jump.Checked=settings.AutoJump;pass.Checked=settings.RequestPass;
        shotMs.Value=(decimal)Clamp(settings.ShotMs,10,2000);diameter.Value=(decimal)Clamp(settings.SampleDiameter,12,90);threshold.Value=(decimal)Clamp(settings.ConfidencePercent,75,99);
        shotKey.Text=settings.ShotKey ?? "MOUSELEFT";
    }
    private void ReadControls()
    {
        string key=shotKey.Text.Trim().ToUpperInvariant();Platform.Key(key);
        if(key=="X") throw new ArgumentException("X foi reservado para não receber comandos.");
        settings.UseArrows=arrows.Checked;settings.Sprint=sprint.Checked;settings.Dribble=dribble.Checked;settings.AutoShoot=shoot.Checked;settings.AutoJump=jump.Checked;settings.RequestPass=pass.Checked;
        settings.ShotMs=(int)shotMs.Value;settings.SampleDiameter=(int)diameter.Value;settings.ConfidencePercent=(int)threshold.Value;settings.ShotKey=key;
    }
    private void Save(bool announce)
    {
        try {ReadControls();SettingsStore.Save(settings);if(announce) Log("Configuração salva.");}
        catch(Exception e){if(announce) Log("Falha ao salvar: "+e.Message);}
    }
    private void UpdateStatus()
    {
        status.Text=!running?"PAUSADO · F9 inicia dentro do Roblox":focusSuspended?"AGUARDANDO ROBLOX · F12 desliga o piloto":"PILOTO ATIVO · F12 interrompe imediatamente";
        status.ForeColor=running?Color.FromArgb(88,222,168):Color.FromArgb(243,192,92);
    }
    private void UpdateSample()
    {
        Image old=sampleView.Image;sampleView.Image=ball==null?null:(Bitmap)ball.Clone();if(old!=null)old.Dispose();
        anchorLabel.Text=String.Format("Bola: {0}\r\nReferência dos pés: {1:0%}, {2:0%}",ball==null?"não calibrada":templateValid?"amostra carregada":"amostra inválida; refaça F4",settings.AnchorX,settings.AnchorY);
    }
    private void ShowSample(Bitmap sample)
    {
        Image old=preview.Image;preview.Image=Vision.Scale(sample,196,196);if(old!=null)old.Dispose();
    }
    private void Stop(string message)
    {
        running=false;focusSuspended=false;probeUntil=0;generation++;movement.Clear();nearSince=-1;consecutive=0;
        try {output.Stop();} catch(Exception e){Log("Falha ao liberar teclas: "+e.Message);}
        UpdateStatus();if(message!=null)Log(message);
    }
    private void SuspendForFocus()
    {
        movement.Clear();nearSince=-1;consecutive=0;
        output.Stop();
        if(!focusSuspended){
            focusSuspended=true;generation++;UpdateStatus();
            tracking.Text="Comandos suspensos; aguardando a janela Roblox original.";
            Log("Foco: "+Platform.FocusReason(gameWindow)+". Aguardando Roblox; sem enviar comandos.");
        }
    }
    private void OnHotkey(int key)
    {
        if(closing) return;
        try {
            if(key==0x7B){Stop("F12: tudo parado.");return;}
            if(!(running?Platform.GameFocused(gameWindow):Platform.RobloxFocused())){Log("Volte ao Roblox para usar os atalhos.");return;}
            long now=clock.ElapsedMilliseconds;
            switch(key) {
                case 0x71:
                    Stop(null);ReadControls();
                    if(keyboard.AnyControlHeld){Log("F2: solte as teclas de movimento para testar.");break;}
                    int forward=Platform.Key(settings.UseArrows?"UP":"W");
                    output.Pulse(forward,450,now);probeUntil=now+450;
                    tracking.Text="TESTE: caminhada de 450 ms, sem usar a visão.";
                    Log("F2: "+(settings.UseArrows?"seta para cima":"W")+" por 450 ms. Verifique o personagem.");break;
                case 0x72: CalibrateAnchor();break;
                case 0x73: CalibrateBall();break;
                case 0x74: generation++;lastBox=Rectangle.Empty;consecutive=0;nearSince=-1;movement.Clear();output.Stop();Log("Rastreio reiniciado.");break;
                case 0x75: ReadControls();output.Pulse(Platform.Key(settings.ShotKey),settings.ShotMs,now);lastShot=now;Log("Chute manual A.");break;
                case 0x76: ReadControls();output.Pulse(Platform.Key(settings.ShotKey),Math.Min(2000,settings.ShotMs*2),now);lastShot=now;Log("Chute manual B.");break;
                case 0x77: sprint.Checked=!sprint.Checked;settings.Sprint=sprint.Checked;Log("Corrida: "+(settings.Sprint?"ligada":"desligada"));break;
                case 0x78:
                    if(running){Stop("Piloto pausado.");break;}
                    ReadControls();
                    Log("F9 recebido.");
                    if(ball==null||!templateValid){Log("Piloto não iniciado: amostra inválida. Aponte na bola e use F4; F2 testa só a caminhada.");break;}
                    if(keyboard.AnyControlHeld){Log("Solte WASD/setas, Shift, Ctrl, Space, Q e R antes de iniciar.");break;}
                    gameWindow=Platform.BindGameWindow();focusSuspended=false;
                    lastBox=Rectangle.Empty;consecutive=0;lastVision=0;lastBall=now;nearSince=-1;running=true;generation++;UpdateStatus();Log("Piloto iniciado. Tecla manual de movimento ou F12 interrompe.");break;
                case 0x79: output.Pulse(Platform.Key("CTRL"),55,now);Log("Ctrl enviado. O programa não identifica o estado do shift lock.");break;
                case 0x7A: output.Pulse(Platform.Key("SPACE"),65,now);lastJump=now;break;
            }
        } catch(Exception e){Stop("Erro: "+e.Message);}
    }
    private void CalibrateAnchor()
    {
        Stop(null);
        Rectangle bounds=Platform.ClientBounds(Platform.GetForegroundWindow());Point p=Platform.CursorPoint();
        if(!bounds.Contains(p))throw new InvalidOperationException("Ponteiro fora da janela Roblox.");
        settings.AnchorX=Clamp((p.X-bounds.X)/(double)bounds.Width,.05,.95);
        settings.AnchorY=Clamp((p.Y-bounds.Y)/(double)bounds.Height,.2,.9);
        UpdateSample();SettingsStore.Save(settings);Log("Referência dos pés salva.");
    }
    private void CalibrateBall()
    {
        Stop(null);ReadControls();
        IntPtr window=Platform.GetForegroundWindow();Rectangle bounds=Platform.ClientBounds(window);Point p=Platform.CursorPoint();int size=settings.SampleDiameter;
        Rectangle crop=new Rectangle(p.X-size/2,p.Y-size/2,size,size);
        if(!bounds.Contains(crop))throw new InvalidOperationException("Centralize o ponteiro na bola, dentro da janela.");
        Bitmap next=Vision.Capture(crop);
        ShowSample(next);
        string problem=Vision.SampleProblem(next);
        if(problem!=null){next.Dispose();throw new InvalidOperationException("Amostra rejeitada: "+problem+". Confira o recorte ampliado no painel.");}
        Bitmap old=ball;ball=next;templateValid=true;if(old!=null)old.Dispose();
        settings.CalibratedWindowWidth=bounds.Width;settings.CalibratedWindowHeight=bounds.Height;
        Directory.CreateDirectory(SettingsStore.Folder);ball.Save(SettingsStore.TemplatePath,ImageFormat.Png);SettingsStore.Save(settings);
        UpdateSample();Log("Bola calibrada. Alinhe a câmera para o ataque e pressione F9.");
    }
    private async void Tick(object sender,EventArgs args)
    {
        if(closing)return;
        long now=clock.ElapsedMilliseconds;
        bool focused=running?Platform.GameFocused(gameWindow):Platform.RobloxFocused();
        diagnostics.Text=String.Format("Entrada enviada ao Windows: {0} eventos · mantidas: {1} · último: {2}",output.SentEvents,output.HeldSummary,output.LastEvent??"nenhum");
        if(!focused) {
            try {
                if(probeUntil!=0){probeUntil=0;tracking.Text="Teste interrompido: Roblox perdeu foco.";Log("F2 interrompido: "+Platform.FocusReason(gameWindow)+".");}
                if(running)SuspendForFocus();
                else {movement.Clear();output.Stop();}
            }catch(Exception e){Stop("Erro ao suspender comandos: "+e.Message);}
            return;
        }
        if(running && focusSuspended){
            focusSuspended=false;generation++;lastBox=Rectangle.Empty;consecutive=0;lastBall=now;nearSince=-1;
            UpdateStatus();Log("Roblox recuperou foco: rastreio retomado.");
        }
        bool ownsVision=false;
        long token=generation;
        try {
            // Independent short tick: key releases and emergency stop never wait for vision.
            if(running && now-lastBall>500)movement.Clear();
            output.Movement(movement,now);
            if(probeUntil!=0 && now>=probeUntil){probeUntil=0;tracking.Text="Teste de caminhada concluído: comandos pressionar/soltar enviados.";Log("F2 concluído. Eventos de entrada enviados: "+output.SentEvents+".");}
            if(!running||frameBusy||now-lastVision<110)return;
            ReadControls();frameBusy=true;ownsVision=true;lastVision=now;token=generation;
            IntPtr window=gameWindow;Rectangle bounds=Platform.ClientBounds(window);
            Rectangle prior=lastBox;
            double minimum=settings.ConfidencePercent/100.0;
            using(Bitmap template=(Bitmap)ball.Clone()) {
                Match match=await Task.Run(delegate{return Vision.Find(window,bounds,template,prior,minimum);});
                if(closing||!running||token!=generation) {match.Preview.Dispose();return;}
                if(!Platform.GameFocused(window)){match.Preview.Dispose();SuspendForFocus();return;}
                if(clock.ElapsedMilliseconds-lastVision>700){match.Preview.Dispose();movement.Clear();output.Stop();tracking.Text="Visão demorou mais de 700 ms; movimento parado.";
                    if(clock.ElapsedMilliseconds-lastTelemetry>=1500){lastTelemetry=clock.ElapsedMilliseconds;Log("Visão acima de 700 ms; nenhum movimento enviado.");}return;}
                now=clock.ElapsedMilliseconds;
                Image old=preview.Image;preview.Image=match.Preview;if(old!=null)old.Dispose();
                using(Graphics g=Graphics.FromImage(match.Preview))using(Pen pen=new Pen(Color.Cyan,1)) {
                    int ax=(int)(settings.AnchorX*match.FrameSize.Width),ay=(int)(settings.AnchorY*match.FrameSize.Height);
                    g.DrawLine(pen,ax-7,ay,ax+7,ay);g.DrawLine(pen,ax,ay-7,ax,ay+7);
                }
                preview.Invalidate();
                lastBox=match.Box;
                if(match.Box.IsEmpty){consecutive=0;nearSince=-1;movement.Clear();tracking.Text=String.Format("Bola não reconhecida · melhor pontuação {0:0%} · {1} candidatas",match.Confidence,match.CandidateCount);
                    if(now-lastTelemetry>=1500){lastTelemetry=now;Log(String.Format("Bola não reconhecida: pontuação {0:0%}, mínimo {1:0%}, candidatas {2}.",match.Confidence,minimum,match.CandidateCount));}
                    if(settings.RequestPass && now-lastBall>2500 && now-lastPass>=8000){output.Pulse(Platform.Key("R"),65,now);lastPass=now;Log("R: passe solicitado.");}
                    output.Movement(movement,now);return;}
                lastBall=now;
                if(!prior.IsEmpty && (Math.Abs(prior.X-match.Box.X)>90 || Math.Abs(prior.Y-match.Box.Y)>70))consecutive=0;
                consecutive++;
                double x=(match.Box.X+match.Box.Width/2.0)/match.FrameSize.Width,y=(match.Box.Y+match.Box.Height/2.0)/match.FrameSize.Height;
                tracking.Text=String.Format("Bola candidata: {0:0%} · posição {1:0%}, {2:0%} · {3}",match.Confidence,x,y,consecutive<2?"confirmando":"seguindo");
                if(consecutive<2){movement.Clear();output.Movement(movement,now);return;}
                Decide(x,y,now);
                output.Movement(movement,now);
                if(now-lastTelemetry>=1500){lastTelemetry=now;Log(String.Format("Rastreio {0:0%}; candidata {1:0%}/{2:0%}; teclas {3}; eventos enviados {4}.",match.Confidence,x,y,output.HeldSummary,output.SentEvents));}
            }
        } catch(OperationCanceledException){
            // A late cancellation from an older capture must not stop a newer run.
            if(!closing && running && token==generation){
                try {SuspendForFocus();}catch(Exception e){Stop("Erro ao suspender comandos: "+e.Message);}
            }
        }
        catch(Exception e){if(!closing && token==generation)Stop("Piloto interrompido: "+e.Message);}
        finally {if(ownsVision)frameBusy=false;}
    }
    private void Decide(double x,double y,long now)
    {
        double dx=x-settings.AnchorX,dy=y-settings.AnchorY;
        bool near=Math.Abs(dx)<.085 && dy>-.085 && dy<.14;
        int forward=Platform.Key(settings.UseArrows?"UP":"W"),back=Platform.Key(settings.UseArrows?"DOWN":"S"),left=Platform.Key(settings.UseArrows?"LEFT":"A"),right=Platform.Key(settings.UseArrows?"RIGHT":"D");
        movement.Clear();
        if(output.Pressed(Platform.Key(settings.ShotKey)) || now-lastShot<500)return;
        if(dx<-.035)movement.Add(left);else if(dx>.035)movement.Add(right);
        if(dy<-.055)movement.Add(forward);else if(dy>.10)movement.Add(back);
        if(near) {
            if(nearSince<0)nearSince=now;
            // Carry in the camera direction before the proximity-based shot.
            movement.Add(forward);
            if(settings.AutoShoot && now-nearSince>=900 && now-lastShot>=settings.ShotIntervalMs && now-lastQ>450) {
                movement.Clear();output.Pulse(Platform.Key(settings.ShotKey),settings.ShotMs,now);lastShot=now;nearSince=-1;Log("Chute por proximidade; posse não confirmada.");
            } else if(settings.Dribble && now-lastQ>=settings.DribbleIntervalMs && now-lastShot>600) {
                output.Pulse(Platform.Key("Q"),65,now);lastQ=now;
            }
            if(settings.AutoJump && now-lastJump>3000 && now-lastShot>600){output.Pulse(Platform.Key("SPACE"),65,now);lastJump=now;}
        } else nearSince=-1;
        if(settings.Sprint && movement.Count>0)movement.Add(Platform.Key("SHIFT"));
    }
    [STAThread] public static void Main()
    {
        Platform.SetProcessDPIAware();Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
        bool created;
        using(Mutex mutex=new Mutex(true,"Local\\BlueLockExternalPilot",out created)) {
            if(!created){MessageBox.Show("O piloto já está aberto.");return;}
            try {Application.Run(new PilotForm());}
            catch(Exception e){MessageBox.Show(e.ToString(),"Blue Lock External");}
        }
    }
}
