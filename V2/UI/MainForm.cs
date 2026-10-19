// C# | UI/MainForm.cs | .NET Framework 4.8 x64 | Responsive control panel and optional visual diagnostics.
using System;
using System.Drawing;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using BlueLockV2.Core;
using BlueLockV2.Configuration;
using BlueLockV2.Capture;
using BlueLockV2.Input;
using BlueLockV2.Diagnostics;
using BlueLockV2.Navigation;

namespace BlueLockV2.UI
{
    public sealed class MainForm : Form
    {
        readonly ComboBox mode = new ComboBox(), profile = new ComboBox(), source = new ComboBox(), shotButton = new ComboBox();
        readonly ComboBox pilotKey = new ComboBox();
        readonly CheckBox pilotDribble = new CheckBox();
        readonly CheckBox antenna = new CheckBox();
        readonly System.Windows.Forms.Timer antennaTimer = new System.Windows.Forms.Timer();
        BallOverlayForm ballOverlay;
        StudioTelemetry studioTelemetry;
        StudioNavigationForm studioLab;
        BallLabForm ballLab;
        readonly CheckBox vision = new CheckBox(), prediction = new CheckBox(), overlay = new CheckBox(), camera = new CheckBox(), adaptive = new CheckBox(), arrows = new CheckBox(), sprint = new CheckBox();
        readonly TextBox path = new TextBox(), combo = new TextBox();
        readonly NumericUpDown shotA = new NumericUpDown(), shotB = new NumericUpDown(), confidence = new NumericUpDown(), horizon = new NumericUpDown();
        readonly Button start = new Button(), stop = new Button(), save = new Button(), browse = new Button();
        readonly Button pilot = new Button();
        readonly Label status = new Label(), metrics = new Label(), notice = new Label();
        readonly TextBox objects = new TextBox();
        readonly PictureBox preview = new PictureBox();
        readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
        Settings settings;
        Pipeline pipeline;
        InputScheduler manualPlayer, manualStudio;
        HotkeyService hotkeys;
        bool busy, armed, closing, closed;
        int emergencyGeneration;
        readonly object controlGate = new object ();
        readonly IClock clock = new MonotonicClock();
        public MainForm()
        {
            Text = "BlueLock External V2 — visão e previsão";
            Width = 1080;
            Height = 850;
            MinimumSize = new Size(850, 650);
            BackColor = Color.FromArgb(18, 25, 35);
            ForeColor = Color.White;
            Font = new Font("Segoe UI", 9);
            StartPosition = FormStartPosition.CenterScreen;
            try
            {
                settings = SettingsStore.Load();
            }
            catch (Exception e)
            {
                settings = new Settings();
                notice.Text = "Configuração inválida: " + e.Message;
            }

            Build();
            LoadSettings();
            Shown += OnShown;
            FormClosing += OnClosing;
            FormClosed += OnClosed;
            timer.Interval = UiInterval(settings.Profile);
            timer.Tick += RefreshStatus;
            timer.Start();
            antennaTimer.Interval = 200;
            antennaTimer.Tick += RefreshAntenna;
            antennaTimer.Start();
        }

        static int UiInterval(PerformanceProfile performance)
        {
            return performance == PerformanceProfile.ECONOMICO ? 1000 : performance == PerformanceProfile.EQUILIBRADO ? 500 : 250;
        }

        void Build()
        {
            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 6,
                Padding = new Padding(12)
            };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 86));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 75));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 60));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 150));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 110));
            Controls.Add(root);
            var options = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                WrapContents = true
            };
            AddChoice(options, "Modo", mode, Enum.GetNames(typeof(RunMode)), 175);
            AddChoice(options, "Perfil", profile, Enum.GetNames(typeof(PerformanceProfile)), 130);
            AddChoice(options, "Fonte", source, Enum.GetNames(typeof(SourceKind)), 140);
            foreach (var pair in new[]
            {
                vision,
                prediction,
                overlay,
                antenna,
                camera,
                adaptive,
                arrows,
                sprint
            }

            )
            {
                pair.AutoSize = true;
                options.Controls.Add(pair);
            }

            vision.Text = "Visão";
            prediction.Text = "Previsão";
            overlay.Text = "Sobreposição";
            antenna.Text = "Antena da bola (jogo)";
            antenna.CheckedChanged += delegate
            {
                RefreshAntenna(null, EventArgs.Empty);
            };
            camera.Text = "Compensar câmera";
            adaptive.Text = "Qualidade adaptativa";
            arrows.Text = "Setinhas";
            sprint.Text = "Correr (Shift)";
            root.Controls.Add(options, 0, 0);
            var inputs = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill
            };
            path.Width = 410;
            path.AccessibleName = "Arquivo de vídeo ou pasta de imagens";
            browse.Text = "Abrir fonte";
            browse.AutoSize = true;
            browse.Click += ChooseSource;
            inputs.Controls.Add(path);
            inputs.Controls.Add(browse);
            AddNumber(inputs, "Confiança %", confidence, 50, 99, 83, 0);
            AddNumber(inputs, "Horizonte s", horizon, .05m, 2, .35m, 2);
            root.Controls.Add(inputs, 0, 1);
            var macros = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill
            };
            AddChoice(macros, "Botão", shotButton, new[] { "MOUSELEFT", "MOUSERIGHT" }, 115);
            AddNumber(macros, "F6 ms", shotA, 10, 3000, 180, 0);
            AddNumber(macros, "F7 ms", shotB, 10, 3000, 500, 0);
            macros.Controls.Add(new Label { Text = "F8 combo", AutoSize = true, Padding = new Padding(3, 7, 0, 0) });
            combo.Width = 330;
            combo.AccessibleName = "Macro F8";
            macros.Controls.Add(combo);
            root.Controls.Add(macros, 0, 2);
            var commands = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true
            };
            AddChoice(commands, "Tecla do piloto", pilotKey, new[] { "F1", "F2", "F3", "F4", "F5", "F10", "F11", "Insert", "Home", "End", "PageUp", "PageDown", "Pause", "Scroll" }, 110);
            pilotDribble.Text = "Q por proximidade";
            pilotDribble.AutoSize = true;
            commands.Controls.Add(pilotDribble);
            var feet = new Button
            {
                Text = "Marcar pés (4 s)",
                Width = 145,
                Height = 30,
                ForeColor = Color.Black
            };
            feet.Click += async delegate
            {
                await MarkFeet();
            };
            commands.Controls.Add(feet);
            commands.SetFlowBreak(feet, true);
            pilotKey.SelectedIndexChanged += PilotKeyChanged;
            pilot.Text = "Piloto Roblox / F10";
            pilot.Width = 185;
            pilot.Height = 34;
            pilot.ForeColor = Color.Black;
            pilot.AccessibleName = "Iniciar ou parar piloto Roblox";
            pilot.Click += async delegate
            {
                await TogglePilot();
            };
            commands.Controls.Add(pilot);
            start.Text = "Iniciar / F9";
            stop.Text = "PARAR / F12";
            save.Text = "Salvar XML";
            foreach (Button b in new[]
            {
                start,
                stop,
                save
            }

            )
            {
                b.Width = 140;
                b.Height = 34;
                b.ForeColor = Color.Black;
                commands.Controls.Add(b);
            }

            var calibration = new Button
            {
                Text = "Calibração",
                Width = 140,
                Height = 34,
                ForeColor = Color.Black
            };
            calibration.Click += delegate
            {
                ReadSettings();
                using (var dialog = new CalibrationForm(settings))
                    if (dialog.ShowDialog(this) == DialogResult.OK)
                    {
                        settings = dialog.Result;
                        notice.Text = "Calibração aplicada ao próximo início; Salvar XML para persistir.";
                    }
            };
            commands.Controls.Add(calibration);
            var about = new Button
            {
                Text = "Sobre / licenças",
                Width = 140,
                Height = 34,
                ForeColor = Color.Black
            };
            about.Click += delegate
            {
                string file = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "NOTICE.md");
                MessageBox.Show(this, File.Exists(file) ? File.ReadAllText(file) : "BlueLockExternal V2 — OpenCvSharp/OpenCV Apache-2.0; FFmpeg LGPLv2.1+; bibliotecas Microsoft MIT.", "Componentes e licenças");
            };
            commands.Controls.Add(about);
            var lab = new Button { Text = "Navegação &Studio", Width = 155, Height = 34, ForeColor = Color.Black };
            commands.Controls.Add(lab);
            var ballLabButton = new Button { Text = "&Bola V2.2 Studio", Width = 155, Height = 34, ForeColor = Color.Black };
            commands.Controls.Add(ballLabButton);
            ballLabButton.Click += delegate
            {
                try
                {
                    if (studioTelemetry == null) studioTelemetry = StudioTelemetry.OpenPersistent(clock, Path.Combine(SettingsStore.Folder,"studio-endpoint.txt"));
                    if (ballLab == null || ballLab.IsDisposed)
                        ballLab = new BallLabForm(studioTelemetry, clock, async delegate { Emergency(); await Stop(); }, delegate { lab.PerformClick(); });
                    ballLab.Show(); ballLab.BringToFront();
                }
                catch (Exception error) { notice.Text = error.Message; }
            };
            commands.SetFlowBreak(about, false);
            commands.SetFlowBreak(lab, true);
            lab.Click += delegate
            {
                try
                {
                    if (studioTelemetry == null) studioTelemetry = StudioTelemetry.OpenPersistent(clock, Path.Combine(SettingsStore.Folder,"studio-endpoint.txt"));
                    if (studioLab == null || studioLab.IsDisposed)
                        studioLab = new StudioNavigationForm(studioTelemetry, clock, StartStudioNavigation, Emergency,
                            delegate { return pipeline == null ? null : pipeline.NavigationTrace; },
                            delegate { return pipeline == null ? null : pipeline.NavigationLogPath; });
                    studioLab.Show(); studioLab.BringToFront();
                }
                catch (Exception error) { notice.Text = error.Message; }
            };
            start.Click += async delegate
            {
                await Toggle();
            };
            stop.Click += delegate
            {
                Emergency();
            };
            save.Click += delegate
            {
                try
                {
                    ReadSettings();
                    SettingsStore.Save(settings);
                    notice.Text = "Configuração salva em " + SettingsStore.Folder;
                }
                catch (Exception e)
                {
                    notice.Text = e.Message;
                }
            };
            notice.AutoSize = false;
            notice.Width = 920;
            notice.Height = 38;
            notice.Text = "V2.1: Navegação Studio valida WASD no laboratório. Piloto visual aguarda posição medida e referencial calibrado. F12 interrompe; F6–F9 preservados.";
            commands.SetFlowBreak(lab, true);
            commands.Controls.Add(notice);
            root.Controls.Add(commands, 0, 3);
            var split = new SplitContainer
            {
                Dock = DockStyle.Fill,
                SplitterDistance = 610
            };
            preview.Dock = DockStyle.Fill;
            preview.SizeMode = PictureBoxSizeMode.Zoom;
            preview.BackColor = Color.Black;
            preview.AccessibleName = "Depuração visual";
            split.Panel1.Controls.Add(preview);
            objects.Dock = DockStyle.Fill;
            objects.Multiline = true;
            objects.ReadOnly = true;
            objects.ScrollBars = ScrollBars.Vertical;
            objects.BackColor = Color.FromArgb(25, 35, 48);
            objects.ForeColor = Color.White;
            objects.Font = new Font("Consolas", 9);
            objects.AccessibleName = "Objetos rastreados";
            split.Panel2.Controls.Add(objects);
            root.Controls.Add(split, 0, 4);
            var bottom = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                RowCount = 2,
                ColumnCount = 1
            };
            status.Dock = DockStyle.Fill;
            status.Height = 40;
            status.AccessibleName = "Estado do pipeline";
            metrics.Dock = DockStyle.Fill;
            metrics.AccessibleName = "Diagnósticos CPU RAM latência";
            bottom.Controls.Add(status, 0, 0);
            bottom.Controls.Add(metrics, 0, 1);
            root.Controls.Add(bottom, 0, 5);
        }

        static void AddChoice(FlowLayoutPanel parent, string name, ComboBox box, string[] values, int width)
        {
            parent.Controls.Add(new Label { Text = name, AutoSize = true, Padding = new Padding(0, 6, 0, 0) });
            box.Items.AddRange(values);
            box.DropDownStyle = ComboBoxStyle.DropDownList;
            box.Width = width;
            box.AccessibleName = name;
            parent.Controls.Add(box);
        }

        static void AddNumber(FlowLayoutPanel parent, string name, NumericUpDown control, decimal min, decimal max, decimal value, int decimals)
        {
            parent.Controls.Add(new Label { Text = name, AutoSize = true, Padding = new Padding(3, 6, 0, 0) });
            control.Minimum = min;
            control.Maximum = max;
            control.DecimalPlaces = decimals;
            control.Increment = decimals > 0 ? .05m : 10;
            control.Value = value;
            control.Width = 65;
            control.AccessibleName = name;
            parent.Controls.Add(control);
        }

        void LoadSettings()
        {
            pilotDribble.Checked = settings.PilotDribble;
            pilotKey.SelectedIndex = Array.IndexOf(new[] { 0x70, 0x71, 0x72, 0x73, 0x74, 0x79, 0x7A, 0x2D, 0x24, 0x23, 0x21, 0x22, 0x13, 0x91 }, settings.PilotHotkey);
            UpdatePilotButton();
            mode.SelectedItem = settings.Mode.ToString();
            profile.SelectedItem = settings.Profile.ToString();
            source.SelectedItem = SourceKind.Simulation.ToString();
            vision.Checked = settings.Vision;
            prediction.Checked = settings.Prediction;
            overlay.Checked = settings.Overlay;
            antenna.Checked = settings.BallAntenna;
            camera.Checked = settings.CameraCompensation;
            adaptive.Checked = settings.Adaptive;
            arrows.Checked = settings.UseArrows;
            sprint.Checked = settings.Sprint;
            shotButton.SelectedItem = settings.ShotKey;
            shotA.Value = settings.ShotA;
            shotB.Value = settings.ShotB;
            confidence.Value = (decimal)(settings.Confidence * 100);
            horizon.Value = (decimal)settings.Horizon;
            combo.Text = settings.Combo;
        }

        void ReadSettings()
        {
            settings.PilotDribble = pilotDribble.Checked;
            settings.PilotHotkey = SelectedPilotKey();
            settings.Mode = (RunMode)Enum.Parse(typeof(RunMode), mode.Text);
            settings.Profile = (PerformanceProfile)Enum.Parse(typeof(PerformanceProfile), profile.Text);
            settings.Vision = vision.Checked;
            settings.Prediction = prediction.Checked;
            settings.Overlay = overlay.Checked;
            settings.BallAntenna = antenna.Checked;
            settings.CameraCompensation = camera.Checked;
            settings.Adaptive = adaptive.Checked;
            settings.UseArrows = arrows.Checked;
            settings.Sprint = sprint.Checked;
            settings.ShotKey = shotButton.Text;
            settings.ShotA = (int)shotA.Value;
            settings.ShotB = (int)shotB.Value;
            settings.Confidence = (double)confidence.Value / 100;
            settings.Horizon = (double)horizon.Value;
            settings.Combo = combo.Text;
            MacroStep.Parse(settings.Combo);
            settings.Validate();
        }

        void OnShown(object sender, EventArgs e)
        {
            manualPlayer = new InputScheduler(new NativeTransport(false), clock, settings, true);
            manualStudio = new InputScheduler(new NativeTransport(true), clock, settings, true);
            try
            {
                hotkeys = new HotkeyService(Handle, delegate (int key)
                {
                    if (!closing)
                        BeginInvoke((Action)delegate
                        {
                            HandleHotkey(key);
                        });
                }, Emergency, Emergency);
                hotkeys.PilotKey = settings.PilotHotkey;
            }
            catch (Exception error)
            {
                notice.Text = "Atalhos indisponíveis: " + error.Message;
            }

            status.Text = "Parado. Piloto Roblox / " + pilotKey.Text + " inicia o controle pela bola; F12 interrompe.";
        }

        int SelectedPilotKey()
        {
            int[] keys =
            {
                0x70,
                0x71,
                0x72,
                0x73,
                0x74,
                0x79,
                0x7A,
                0x2D,
                0x24,
                0x23,
                0x21,
                0x22,
                0x13,
                0x91
            };
            return pilotKey.SelectedIndex < 0 ? 0x79 : keys[pilotKey.SelectedIndex];
        }

        void UpdatePilotButton()
        {
            bool running = pipeline != null && armed && settings.Mode == RunMode.PILOTO_ROBLOX;
            pilot.Text = (running ? "Parar piloto / " : "Piloto Roblox / ") + pilotKey.Text;
        }

        void PilotKeyChanged(object sender, EventArgs e)
        {
            int key = SelectedPilotKey();
            UpdatePilotButton();
            if (key == settings.PilotHotkey)
                return;
            settings.PilotHotkey = key;
            if (hotkeys != null)
                hotkeys.PilotKey = key;
            try
            {
                SettingsStore.Save(settings);
                notice.Text = "Tecla do piloto salva: " + pilotKey.Text + ". F12 continua sendo a parada de emergência.";
            }
            catch (Exception error)
            {
                notice.Text = "Tecla aplicada nesta sessão; falha ao salvar: " + error.Message;
            }
        }

        async Task TogglePilot()
        {
            if (busy || closing)
                return;
            int generation = Volatile.Read(ref emergencyGeneration);
            bool wasRunning = pipeline != null && armed && settings.Mode == RunMode.PILOTO_ROBLOX;
            if (pipeline != null || armed)
                await Stop();
            if (wasRunning || closing || generation != Volatile.Read(ref emergencyGeneration))
                return;
            try
            {
                if (!SettingsStore.ImportPilotCalibration(settings))
                {
                    notice.Text = "Antes de iniciar, use Marcar pés (4 s), volte ao jogo e aponte entre os pés. Depois aperte " + pilotKey.Text + ".";
                    return;
                }

                mode.SelectedItem = RunMode.PILOTO_ROBLOX.ToString();
                source.SelectedItem = SourceKind.RobloxWindow.ToString();
                vision.Checked = true;
                // This pilot uses a fixed, calibrated camera-relative player anchor.
                camera.Checked = false;
                await Toggle();
            }
            catch (Exception error)
            {
                notice.Text = "Falha ao preparar piloto: " + error.Message;
            }
        }

        async Task MarkFeet()
        {
            if (busy || closing)
                return;
            await Stop();
            if (closing)
                return;
            busy = true;
            start.Enabled = pilot.Enabled = false;
            int generation = Volatile.Read(ref emergencyGeneration);
            try
            {
                ReadSettings();
                for (int remaining = 4; remaining > 0; remaining--)
                {
                    notice.Text = "Marcar pés em " + remaining + " s: volte ao Roblox e aponte ENTRE OS PÉS, com o ponteiro livre. F12 cancela.";
                    await Task.Delay(1000);
                    if (closing || generation != Volatile.Read(ref emergencyGeneration))
                        return;
                }

                PointF anchor = DesktopEnvironment.ReadRobloxCursorAnchor();
                settings.AnchorX = anchor.X;
                settings.AnchorY = anchor.Y;
                settings.CalibratedLocal = true;
                SettingsStore.Save(settings);
                notice.Text = "Pés salvos: " + anchor.X.ToString("P1") + " / " + anchor.Y.ToString("P1") + ". Mantenha o enquadramento; " + pilotKey.Text + " inicia.";
            }
            catch (Exception error)
            {
                notice.Text = "Calibração não concluída: " + error.Message;
            }
            finally
            {
                busy = false;
                if (!closing)
                    start.Enabled = pilot.Enabled = true;
            }
        }

        async void HandleHotkey(int key)
        {
            switch (Hotkeys.Map(key, settings.PilotHotkey))
            {
                case "PILOT_TOGGLE":
                    await TogglePilot();
                    break;
                case "TOGGLE":
                    await Toggle();
                    break;
                case "SHOT_A":
                    RunMacro(settings.ShotKey + ":" + (int)shotA.Value);
                    break;
                case "SHOT_B":
                    RunMacro(settings.ShotKey + ":" + (int)shotB.Value);
                    break;
                case "COMBO":
                    RunMacro(combo.Text);
                    break;
            }
        }

        protected override bool ProcessCmdKey(ref Message message, Keys key)
        {
            if (key == Keys.F12)
            {
                Emergency();
                return true;
            }

            if (key == Keys.F6 || key == Keys.F7 || key == Keys.F8 || key == Keys.F9 || (int)key == settings.PilotHotkey)
            {
                HandleHotkey((int)key);
                return true;
            }

            return base.ProcessCmdKey(ref message, key);
        }

        void RunMacro(string text)
        {
            lock (controlGate)
            {
                if (!armed)
                {
                    notice.Text = "Ative com F9 antes das macros.";
                    return;
                }

                try
                {
                    MacroStep[] steps = MacroStep.Parse(text);
                    if (pipeline != null)
                        pipeline.EmergencyStop();
                    manualPlayer.EmergencyStop();
                    manualStudio.EmergencyStop();
                    try
                    {
                        manualPlayer.StartMacro(steps);
                    }
                    catch (InvalidOperationException)
                    {
                        manualStudio.StartMacro(steps);
                    }

                    notice.Text = "Macro manual iniciada.";
                }
                catch (Exception e)
                {
                    manualPlayer.EmergencyStop();
                    manualStudio.EmergencyStop();
                    notice.Text = e.Message;
                }
            }
        }

        void ChooseSource(object sender, EventArgs e)
        {
            if (source.Text == SourceKind.Images.ToString())
            {
                using (var dialog = new FolderBrowserDialog())
                    if (dialog.ShowDialog(this) == DialogResult.OK)
                        path.Text = dialog.SelectedPath;
            }
            else
                using (var dialog = new OpenFileDialog
                {
                    Filter = "Vídeo|*.mp4;*.avi;*.mkv;*.mov|Todos|*.*"
                }

                )
                    if (dialog.ShowDialog(this) == DialogResult.OK)
                    {
                        path.Text = dialog.FileName;
                        source.SelectedItem = SourceKind.Video.ToString();
                    }
        }

        async Task Toggle()
        {
            if (busy || closing)
                return;
            if (pipeline != null || armed)
            {
                await Stop();
                return;
            }

            busy = true;
            int generation = Volatile.Read(ref emergencyGeneration);
            start.Enabled = false;
            pilot.Enabled = false;
            try
            {
                ReadSettings();
                SourceKind kind = (SourceKind)Enum.Parse(typeof(SourceKind), source.Text);
                if (settings.Mode == RunMode.NAVEGACAO_STUDIO && (kind != SourceKind.StudioWindow || studioTelemetry == null || !studioTelemetry.ControlValidated))
                    throw new InvalidOperationException("Abra Navegação Studio e valide WASD no cenário próprio antes de navegar.");
                if (settings.Mode == RunMode.AUTONOMO_TESTE && kind == SourceKind.RobloxWindow)
                    throw new InvalidOperationException("Autônomo de teste: selecione Simulation ou StudioWindow. Replay funciona em modo assistido.");
                if (settings.Mode == RunMode.PILOTO_ROBLOX && (kind != SourceKind.RobloxWindow || !settings.CalibratedLocal || !settings.Vision))
                    throw new InvalidOperationException("Piloto Roblox exige fonte RobloxWindow, visão ligada e posição local calibrada. Use o botão Piloto Roblox para preparar.");
                Settings config = settings.Copy();
                string location = path.Text;
                Pipeline created = await Task.Run(delegate
                {
                    IFrameSource input = null;
                    IActionExecutor output = null;
                    Pipeline candidate = null;
                    try
                    {
                        if (kind == SourceKind.Simulation)
                        {
                            var simulation = new Simulation(clock);
                            input = simulation;
                            output = simulation;
                        }
                        else if (kind == SourceKind.Images)
                            input = new ImageSequenceSource(location, clock, false);
                        else if (kind == SourceKind.Video)
                            input = new VideoSource(location, clock);
                        else
                            input = new WindowSource(kind == SourceKind.StudioWindow, clock);
                        if (output == null)
                            output = ((kind == SourceKind.StudioWindow && (config.Mode == RunMode.AUTONOMO_TESTE || config.Mode == RunMode.NAVEGACAO_STUDIO)) || (kind == SourceKind.RobloxWindow && config.Mode == RunMode.PILOTO_ROBLOX)) ? (IActionExecutor)new InputScheduler(new NativeTransport(kind == SourceKind.StudioWindow, delegate
                            {
                                // Telemetry navigation does not need the visual capture loop to bind its HWND.
                                return config.Mode == RunMode.NAVEGACAO_STUDIO ? studioTelemetry != null && DesktopEnvironment.StudioWindowFocused(studioTelemetry.WindowHandle) : ((WindowSource)input).Focused;
                            }), clock, config, true) : new NullExecutor();
                        candidate = new Pipeline(config, input, output, clock, studioTelemetry);
                        if (!String.IsNullOrWhiteSpace(config.TemplatePath))
                            candidate.SetTemplate(config.TemplatePath);
                        return candidate;
                    }
                    catch
                    {
                        if (candidate != null)
                            candidate.Dispose();
                        else
                        {
                            if (input != null)
                                input.Dispose();
                            if (output != null)
                                output.Dispose();
                        }

                        throw;
                    }
                });
                bool accepted;
                lock (controlGate)
                {
                    accepted = generation == Volatile.Read(ref emergencyGeneration) && !closing;
                    if (accepted)
                    {
                        pipeline = created;
                        pipeline.Start();
                        armed = true;
                    }
                }

                if (!accepted)
                {
                    await Task.Run((Action)created.Dispose);
                    return;
                }

                start.Text = "Desativar / F9";
                UpdatePilotButton();
                timer.Interval = UiInterval(config.Profile);
                notice.Text = config.Mode == RunMode.PILOTO_ROBLOX ? "Piloto visual ativado: só movimenta com posição medida, referências de campo e movimento calibrados. Consulte o motivo de espera no estado. " + pilotKey.Text + " liga/desliga; F12 interrompe." : config.Mode + " / " + kind + ". F12 interrompe imediatamente; reinicie com F9 após a parada.";
            }
            catch (Exception e)
            {
                notice.Text = "Falha ao iniciar: " + e.Message;
            }
            finally
            {
                busy = false;
                start.Enabled = true;
                pilot.Enabled = true;
            }
        }

        async Task Stop()
        {
            armed = false;
            if (manualPlayer != null)
                manualPlayer.EmergencyStop();
            if (manualStudio != null)
                manualStudio.EmergencyStop();
            Pipeline old = Interlocked.Exchange(ref pipeline, null);
            if (old != null)
            {
                old.EmergencyStop();
                busy = true;
                start.Enabled = false;
                pilot.Enabled = false;
                try
                {
                    await Task.Run((Action)old.Dispose);
                }
                finally
                {
                    busy = false;
                    start.Enabled = true;
                    pilot.Enabled = true;
                }
            }

            start.Text = "Iniciar / F9";
            UpdatePilotButton();
            status.Text = "Parado; entradas liberadas.";
            status.AccessibleName = status.Text;
        }

        async Task StartStudioNavigation()
        {
            if (busy || closing) return;
            int generation = Volatile.Read(ref emergencyGeneration);
            if (pipeline != null || armed) await Stop();
            if (closing || generation != Volatile.Read(ref emergencyGeneration)) return;
            mode.SelectedItem = RunMode.NAVEGACAO_STUDIO.ToString();
            source.SelectedItem = SourceKind.StudioWindow.ToString();
            vision.Checked = false;
            sprint.Checked = false;
            await Toggle();
        }

        void Emergency()
        {
            if (ballLab != null) ballLab.EmergencySession();
            if (studioLab != null) studioLab.EmergencyDiagnostic();
            Interlocked.Increment(ref emergencyGeneration);
            Volatile.Write(ref armed, false);
            lock (controlGate)
            {
                armed = false;
                Pipeline current = Volatile.Read(ref pipeline);
                if (current != null)
                    try
                    {
                        current.EmergencyStop();
                    }
                    catch (ObjectDisposedException)
                    {
                    }

                if (manualPlayer != null)
                    manualPlayer.EmergencyStop();
                if (manualStudio != null)
                    manualStudio.EmergencyStop();
            }

            if (!closing && IsHandleCreated)
                try
                {
                    BeginInvoke((Action)delegate
                    {
                        UpdatePilotButton();
                        notice.Text = "Interrompido. Entradas liberadas; " + pilotKey.Text + " reinicia o piloto; F9 desativa.";
                        status.Text = "Execução interrompida; percepção permanece disponível para diagnóstico.";
                        status.AccessibleName = status.Text;
                    });
                }
                catch (InvalidOperationException)
                {
                }
        }

        void RefreshAntenna(object sender, EventArgs e)
        {
            Pipeline current = pipeline;
            Observation snapshot = current == null ? null : current.Latest;
            Rectangle bounds;
            try
            {
                if (closing || !antenna.Checked || snapshot == null || clock.Now - snapshot.State.Timestamp > .4 || !current.TryGetLiveBounds(out bounds))
                {
                    if (ballOverlay != null)
                        ballOverlay.Clear();
                    return;
                }

                if (ballOverlay == null)
                    ballOverlay = new BallOverlayForm();
                ballOverlay.Present(snapshot.State, bounds);
            }
            catch (Exception error)
            {
                if (ballOverlay != null)
                    ballOverlay.Clear();
                notice.Text = "Antena indisponível: " + error.Message;
            }
        }

        void RefreshStatus(object sender, EventArgs e)
        {
            Pipeline p = pipeline;
            if (p == null)
                return;
            Observation snapshot = p.Latest;
            ActionIntent action = p.Intent;
            var m = p.Metrics;
            status.Text = "Estado: " + p.State + " | intenção: " + (action == null ? "WAIT" : action.Kind + " — " + action.Reason) + "\r\nFeedback: " + (p.Feedback ?? "aguardando") + (p.Completed ? " | Replay concluído" : "");
            if (p.NavigationTrace != null)
                status.Text += "\r\nJogador " + p.NavigationTrace.Player + " alvo " + p.NavigationTrace.Target + " distância " + p.NavigationTrace.Distance.ToString("0.00") + " | CSV: " + p.NavigationLogPath;
            metrics.Text = "CPU processo: " + m.CpuPercent.ToString("0.0") + "% | RAM privada: " + m.PrivateMb.ToString("0.0") + " MB | working set: " + m.WorkingMb.ToString("0.0") + " MB\r\nCV p95: " + m.Vision.Percentile(.95).ToString("0.00") + " ms | latência p95: " + m.Latency.Percentile(.95).ToString("0.00") + " ms | frames: " + m.Processed + " / descartados: " + p.Dropped + " | qualidade reduzida: " + p.QualityLevel + "\r\nRMSE bola: " + (p.Predictor.BallErrorSamples == 0 ? "n/d" : p.Predictor.BallRmse.ToString("0.00")) + " px / " + p.Predictor.BallErrorSamples + " amostras | erros: " + m.Failures + " " + m.LastError;
            status.AccessibleName = status.Text;
            metrics.AccessibleName = metrics.Text;
            if (snapshot != null)
            {
                StringBuilder text = new StringBuilder();
                foreach (var obj in snapshot.State.Objects)
                {
                    text.AppendLine(obj.Type + " #" + obj.Id + "\r\n  pos " + obj.Position + " vel " + obj.Velocity + "\r\n  confiança " + obj.Confidence.ToString("0.00") + (obj.Lost ? " PERDIDO" : "") + (obj.Measured ? "" : " REFERÊNCIA CALIBRADA"));
                    if (obj.Type == EntityType.BALL && !obj.Lost)
                        text.AppendLine("  " + BallAnnotation.Measurements(snapshot.State, obj));
                }

                objects.Text = text.ToString();
            }

            Bitmap image = p.TakePreview();
            if (image != null)
            {
                Image old = preview.Image;
                preview.Image = image;
                if (old != null)
                    old.Dispose();
            }
        }

        async void OnClosing(object sender, FormClosingEventArgs e)
        {
            if (closed)
                return;
            e.Cancel = true;
            if (closing)
                return;
            closing = true;
            Emergency();
            await Stop();
            closed = true;
            Close();
        }

        void OnClosed(object sender, FormClosedEventArgs e)
        {
            antennaTimer.Stop();
            antennaTimer.Dispose();
            if (ballOverlay != null)
                ballOverlay.Dispose();
            if (studioLab != null && !studioLab.IsDisposed) studioLab.Close();
            if (ballLab != null && !ballLab.IsDisposed) ballLab.Close();
            if (studioTelemetry != null) studioTelemetry.Dispose();
            timer.Stop();
            timer.Dispose();
            if (hotkeys != null)
                hotkeys.Dispose();
            if (manualPlayer != null)
                manualPlayer.Dispose();
            if (manualStudio != null)
                manualStudio.Dispose();
            if (preview.Image != null)
                preview.Image.Dispose();
        }
    }

    internal static class Program
    {
        [STAThread]
        static void Main()
        {
            DesktopEnvironment.EnableDpi();
            bool owns;
            using (var mutex = new Mutex(true, "Local\\BlueLockExternalV2", out owns))
            {
                if (!owns)
                {
                    MessageBox.Show("BlueLock External V2 já está aberto.");
                    return;
                }

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new MainForm());
            }
        }
    }
}
