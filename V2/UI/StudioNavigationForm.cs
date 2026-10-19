// C# | UI/StudioNavigationForm.cs | .NET Framework 4.8 x64 | Owned-scene telemetry, independent WASD diagnostics, and navigation map.
using System;
using System.IO;
using System.Drawing;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using BlueLockV2.Core;
using BlueLockV2.Input;
using BlueLockV2.Configuration;
using BlueLockV2.Navigation;

namespace BlueLockV2.UI
{
    internal sealed class StudioNavigationForm : Form
    {
        readonly StudioTelemetry source;
        readonly IClock clock;
        readonly Func<Task> startNavigation;
        readonly Action emergency;
        readonly Func<NavigationTrace> trace;
        readonly Func<string> navigationPath;
        readonly TextBox info = new TextBox();
        readonly Panel map = new Panel();
        readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
        readonly string folder;
        volatile bool running;
        bool closed;
        InputScheduler diagnostic;
        readonly object diagnosticGate = new object();
        public StudioNavigationForm(StudioTelemetry telemetry, IClock time, Func<Task> start, Action stop, Func<NavigationTrace> snapshot, Func<string> logPath)
        {
            source = telemetry; clock = time; startNavigation = start; emergency = stop; trace = snapshot;
            navigationPath = logPath;
            Text = "V2.1 — laboratório de navegação Studio"; Width = 900; Height = 760;
            folder = Path.Combine(Directory.GetParent(AppDomain.CurrentDomain.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar)).FullName, "StudioLab", "Generated");
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1 };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 85)); root.RowStyles.Add(new RowStyle(SizeType.Percent, 50)); root.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
            Controls.Add(root);
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill };
            var create = new Button { Text = "&Gerar cenário .rbxlx", Width = 170, Height = 32 };
            var calibrate = new Button { Text = "&Testar WASD × 4 câmeras", Width = 195, Height = 32 };
            var go = new Button { Text = "&Navegar ao alvo", Width = 145, Height = 32 };
            var stopButton = new Button { Text = "PARAR / F12", Width = 130, Height = 32 };
            var export = new Button { Text = "Exportar CSV", Width = 120, Height = 32 };
            buttons.Controls.AddRange(new Control[] { create, calibrate, go, stopButton, export });
            buttons.Controls.Add(new Label { AutoSize = true, Text = "Abra o cenário no Studio e dê Play. Z gira câmera; T troca alvo; Y reinicia posição. Retorne ao Studio durante o diagnóstico." });
            root.Controls.Add(buttons, 0, 0);
            info.Multiline = info.ReadOnly = true; info.ScrollBars = ScrollBars.Vertical; info.Dock = DockStyle.Fill;
            info.Text = "1. Gerar cenário; abrir NavigationLab.rbxlx no Studio e dar Play.\r\n2. Testar WASD com o jogo de teste em foco.\r\n3. Navegar ao alvo. Telemetria só vem desse projeto local de Studio.\r\n";
            root.Controls.Add(info, 0, 1);
            map.Dock = DockStyle.Fill; map.BackColor = Color.FromArgb(20, 40, 30); map.Paint += Draw;
            root.Controls.Add(map, 0, 2);
            create.Click += delegate { try { info.AppendText("\r\nCenário: " + StudioLabProject.Write(folder, source.Endpoint)); } catch (Exception e) { info.AppendText("\r\n" + e.Message); } };
            calibrate.Click += async delegate { await Calibrate(); };
            go.Click += async delegate
            {
                if (!source.ControlValidated)
                {
                    info.AppendText("\r\nNavegação bloqueada: conclua os 16 pulsos WASD. Um novo teste interrompido remove a aprovação anterior.");
                    return;
                }
                info.AppendText("\r\nNavegação: volte ao campo do Studio. Cada pulso é seguido de espera pela resposta estabilizar; acompanhe OBSERVING, FEEDBACK e REACHED no mapa. F12 interrompe.");
                await startNavigation();
            };
            stopButton.Click += delegate { Cancel(); };
            export.Click += delegate
            {
                using (var dialog = new SaveFileDialog { Filter = "CSV|*.csv", FileName = "studio-wasd.csv" })
                    if (dialog.ShowDialog(this) == DialogResult.OK)
                    {
                        string file = navigationPath() ?? Path.Combine(folder, "studio-wasd.csv");
                        if (File.Exists(file)) File.Copy(file, dialog.FileName, true);
                        else info.AppendText("\r\nExecute primeiro o diagnóstico WASD.");
                    }
            };
            timer.Interval = 200;
            timer.Tick += delegate { map.Invalidate(); };
            timer.Start();
            FormClosing += delegate { closed = true; Cancel(); timer.Stop(); timer.Dispose(); };
        }
        void Cancel() { EmergencyDiagnostic(); emergency(); }
        public void EmergencyDiagnostic() { lock (diagnosticGate) { running = false; if (diagnostic != null) diagnostic.EmergencyStop(); } }
        void SendDiagnostic(string sequence)
        {
            lock (diagnosticGate)
            {
                if (!running || diagnostic == null) throw new OperationCanceledException("Diagnóstico cancelado.");
                diagnostic.StartMacro(MacroStep.Parse(sequence));
            }
        }
        void ReleaseDiagnostic()
        {
            lock (diagnosticGate)
            {
                if (!running || diagnostic == null) throw new OperationCanceledException("Diagnóstico cancelado.");
                diagnostic.EmergencyStop();
            }
        }
        async Task<NavigationSample> WaitStationary()
        {
            double began = clock.Now;
            var settled = new MotionSettlement(began);
            while (clock.Now - began < 6)
            {
                if (!running || closed) throw new OperationCanceledException("Diagnóstico cancelado.");
                if (!DesktopEnvironment.StudioWindowFocused(source.WindowHandle))
                    throw new InvalidOperationException("Volte ao viewport do cenário no Roblox Studio.");
                var sample = settled.Observe(source.Latest, clock.Now);
                if (sample != null) return sample;
                await Task.Delay(40);
            }
            throw new InvalidOperationException("Sem posição estável medida por 600 ms. Aguarde o personagem parar e confira a telemetria. " + source.LastError);
        }
        protected override bool ProcessCmdKey(ref Message message, Keys key)
        { if (key == Keys.F12) { Cancel(); return true; } return base.ProcessCmdKey(ref message, key); }
        async Task Calibrate()
        {
            if (running) return;
            emergency();
            source.ControlValidated = false;
            running = true;
            var csv = new StringBuilder("orientation,key,before_x,before_z,after_x,after_z,pulse_seconds,velocity_x,velocity_z,expected_angle_error,before_time,after_time\r\n");
            try
            {
                for (int i = 4; i > 0; i--) { info.AppendText("\r\nVolte ao Studio: início em " + i + " s."); await Task.Delay(1000); if (!running || closed) return; }
                info.AppendText("\r\nAguardando foco do Studio por até 20 s; retorne ao campo.");
                source.WindowHandle = IntPtr.Zero;
                double focusDeadline = clock.Now + 20;
                while (source.WindowHandle == IntPtr.Zero && clock.Now < focusDeadline)
                {
                    if (!running || closed) throw new OperationCanceledException("Diagnóstico cancelado.");
                    try { source.WindowHandle = DesktopEnvironment.BindStudioWindow(); }
                    catch (InvalidOperationException) { }
                    if (source.WindowHandle == IntPtr.Zero) await Task.Delay(100);
                }
                if (source.WindowHandle == IntPtr.Zero) throw new InvalidOperationException("Foco do Studio não confirmado em 20 s.");
                using (diagnostic = new InputScheduler(new NativeTransport(true, delegate { return DesktopEnvironment.StudioWindowFocused(source.WindowHandle); }), clock, new Settings { Sprint = false }, true))
                {
                    for (int camera = 0; camera < 4; camera++)
                    {
                        var calibration = new MovementCalibration();
                        MotionBasis expectedBasis = null;
                        string[] keys = { "W", "A", "S", "D" };
                        for (int key = 0; key < 4; key++)
                        {
                            if (!running || closed) return;
                            ReleaseDiagnostic();
                            info.AppendText("\r\nCâmera " + camera * 90 + "° " + keys[key] + ": aguardando posição estável antes do pulso.");
                            var before = await WaitStationary();
                            expectedBasis = before.Basis;
                            SendDiagnostic(keys[key] + ":250");
                            await Task.Delay(280);
                            if (!running || closed) return;
                            ReleaseDiagnostic();
                            // Include residual motion in this pulse, then establish a fresh stationary baseline for the next key.
                            var after = await WaitStationary();
                            if (MotionBasis.Angle(before.Basis.Forward, after.Basis.Forward) > 3)
                                throw new InvalidOperationException("Observação atrasada ou câmera alterada durante pulso.");
                            Vec velocity = (after.Player - before.Player) / .25;
                            Vec intended = key == 0 ? before.Basis.Forward : key == 1 ? before.Basis.Right * -1 : key == 2 ? before.Basis.Forward * -1 : before.Basis.Right;
                            double angle = MotionBasis.Angle(velocity, intended);
                            calibration.Add(key, before.Player, after.Player, .25, 1);
                            csv.AppendLine(String.Format(System.Globalization.CultureInfo.InvariantCulture,
                                "{0},{1},{2:R},{3:R},{4:R},{5:R},0.25,{6:R},{7:R},{8:R},{9:R},{10:R}", camera * 90, keys[key], before.Player.X, before.Player.Y, after.Player.X, after.Player.Y, velocity.X, velocity.Y, angle, before.Time, after.Time));
                            info.AppendText("\r\nCâmera " + camera * 90 + "° " + keys[key] + ": " + velocity + " / erro " + angle.ToString("0.0") + "°");
                            if (velocity.Length < 2 || angle > 15) throw new InvalidOperationException("Tecla não correspondeu ao referencial da câmera.");
                        }
                        MotionBasis measured = calibration.Build();
                        if (MotionBasis.Angle(measured.Forward, expectedBasis.Forward) > 15) throw new InvalidOperationException("Calibração inconsistente.");
                        if (camera < 3) { SendDiagnostic("Z:60"); await Task.Delay(500); }
                    }
                    source.ControlValidated = true;
                    info.AppendText("\r\n16 pulsos WASD aprovados em quatro câmeras; Navegar ao alvo disponível.");
                }
            }
            catch (Exception error) { info.AppendText("\r\nDiagnóstico interrompido: " + error.Message); }
            finally
            {
                lock (diagnosticGate) { if (diagnostic != null) { diagnostic.Dispose(); diagnostic = null; } }
                running = false;
                Directory.CreateDirectory(folder);
                File.WriteAllText(Path.Combine(folder, "studio-wasd.csv"), csv.ToString());
            }
        }
        void Draw(object sender, PaintEventArgs e)
        {
            var sample = source.Latest;
            var nav = trace();
            string text = sample == null ? "Aguardando telemetria de Studio" : "XZ mundo — jogador " + sample.Player + " alvo " + sample.Target + " distância " + Vec.Distance(sample.Player, sample.Target).ToString("0.00") + "\r\n" +
                (nav == null ? "sem navegação" : nav.State + " / " + nav.Reason) + "\r\n" + (source.ControlValidated ? "WASD validado" : "WASD ainda não validado");
            e.Graphics.DrawString(text, Font, Brushes.White, 8, 8);
            if (sample == null || clock.Now - sample.Time > .4) return;
            float scale = Math.Min(map.Width, map.Height) / 70f;
            PointF p = new PointF(map.Width / 2f + (float)sample.Player.X * scale, map.Height / 2f + (float)sample.Player.Y * scale);
            PointF t = new PointF(map.Width / 2f + (float)sample.Target.X * scale, map.Height / 2f + (float)sample.Target.Y * scale);
            e.Graphics.DrawLine(Pens.Cyan, p, t); e.Graphics.FillEllipse(Brushes.Blue, p.X - 5, p.Y - 5, 10, 10);
            e.Graphics.DrawEllipse(Pens.White, t.X - 5, t.Y - 5, 10, 10);
            if (nav != null) e.Graphics.DrawLine(Pens.Yellow, p.X, p.Y, p.X + (float)nav.Expected.X * scale * 4, p.Y + (float)nav.Expected.Y * scale * 4);
        }
    }
}
