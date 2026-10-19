// C# | UI/CalibrationForm.cs | .NET Framework 4.8 x64 | Explicit scene calibration and resource controls.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using BlueLockV2.Configuration;

namespace BlueLockV2.UI
{
    public sealed class CalibrationForm : Form
    {
        public Settings Result { get; private set; }

        readonly CheckBox local = new CheckBox(), colors = new CheckBox(), hud = new CheckBox(), roi = new CheckBox();
        readonly Dictionary<string, NumericUpDown> values = new Dictionary<string, NumericUpDown>();
        readonly TextBox template = new TextBox();
        readonly Settings input;
        public CalibrationForm(Settings settings)
        {
            input = settings.Copy();
            Text = "Calibração do cenário e recursos";
            Width = 630;
            Height = 690;
            StartPosition = FormStartPosition.CenterParent;
            var root = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.TopDown,
                AutoScroll = true,
                WrapContents = false,
                Padding = new Padding(12)
            };
            Controls.Add(root);
            local.Text = "Usar referência fixa dos pés (câmera deve permanecer alinhada)";
            local.Checked = input.CalibratedLocal;
            local.Width = 560;
            root.Controls.Add(local);
            Add(root, "Pés X %", input.AnchorX * 100, 5, 95, 1);
            Add(root, "Pés Y %", input.AnchorY * 100, 10, 95, 1);
            colors.Text = "Marcadores de cor calibrados neste cenário";
            colors.Checked = input.ColorsCalibrated;
            colors.Width = 560;
            root.Controls.Add(colors);
            Add(root, "HSV H local", input.LocalHue, 0, 179, 0);
            Add(root, "HSV H aliado", input.AllyHue, 0, 179, 0);
            Add(root, "HSV H adversário", input.OpponentHue, 0, 179, 0);
            Add(root, "HSV H gol", input.GoalHue, 0, 179, 0);
            Add(root, "HSV H goleiro", input.KeeperHue, 0, 179, 0);
            hud.Text = "Excluir regiões do HUD do Blue Lock Rivals";
            hud.Checked = input.GameHudMask;
            hud.Width = 560;
            root.Controls.Add(hud);
            roi.Text = "Capturar região da bola com recuperação periódica do campo";
            roi.Checked = input.UseRoi;
            roi.Width = 560;
            root.Controls.Add(roi);
            Add(root, "Captura FPS (0 = perfil)", input.CaptureFps, 0, 30, 0);
            Add(root, "Decisão Hz (0 = perfil)", input.DecisionFps, 0, 60, 0);
            Add(root, "Largura (0 = perfil)", input.ProcessingWidth, 0, 1280, 0);
            Add(root, "CPU alvo % (redução adaptativa)", input.SoftCpuPercent, 1, 80, 0);
            root.Controls.Add(new Label { Text = "Template opcional de bola (.png/.jpg)", AutoSize = true });
            template.Text = input.TemplatePath;
            template.Width = 550;
            root.Controls.Add(template);
            var buttons = new FlowLayoutPanel
            {
                Width = 550,
                Height = 40
            };
            var open = new Button
            {
                Text = "Escolher imagem",
                Width = 140
            };
            open.Click += delegate
            {
                using (var d = new OpenFileDialog
                {
                    Filter = "Imagem|*.png;*.jpg;*.bmp"
                }

                )
                    if (d.ShowDialog(this) == DialogResult.OK)
                        template.Text = d.FileName;
            };
            var apply = new Button
            {
                Text = "Aplicar",
                Width = 110
            };
            apply.Click += delegate
            {
                Result = input.Copy();
                Result.CalibratedLocal = local.Checked;
                Result.ColorsCalibrated = colors.Checked;
                Result.GameHudMask = hud.Checked;
                Result.UseRoi = roi.Checked;
                Result.AnchorX = Value("Pés X %") / 100;
                Result.AnchorY = Value("Pés Y %") / 100;
                Result.LocalHue = Value("HSV H local");
                Result.AllyHue = Value("HSV H aliado");
                Result.OpponentHue = Value("HSV H adversário");
                Result.GoalHue = Value("HSV H gol");
                Result.KeeperHue = Value("HSV H goleiro");
                Result.CaptureFps = (int)Value("Captura FPS (0 = perfil)");
                Result.DecisionFps = (int)Value("Decisão Hz (0 = perfil)");
                Result.ProcessingWidth = (int)Value("Largura (0 = perfil)");
                Result.SoftCpuPercent = Value("CPU alvo % (redução adaptativa)");
                Result.TemplatePath = template.Text;
                Result.Validate();
                DialogResult = DialogResult.OK;
                Close();
            };
            buttons.Controls.Add(open);
            buttons.Controls.Add(apply);
            root.Controls.Add(buttons);
            root.Controls.Add(new Label { Text = "Cores não identificam equipes em qualquer partida. Use marcadores distintos no cenário de Studio. Referência fixa não é detecção visual do personagem.", Width = 550, Height = 50 });
        }

        void Add(FlowLayoutPanel parent, string label, double value, decimal min, decimal max, int decimals)
        {
            var row = new FlowLayoutPanel
            {
                Width = 560,
                Height = 31
            };
            row.Controls.Add(new Label { Text = label, Width = 330, Padding = new Padding(0, 4, 0, 0) });
            var number = new NumericUpDown
            {
                Minimum = min,
                Maximum = max,
                DecimalPlaces = decimals,
                Value = Math.Max(min, Math.Min(max, (decimal)value)),
                Width = 100
            };
            values[label] = number;
            row.Controls.Add(number);
            parent.Controls.Add(row);
        }

        double Value(string key)
        {
            return (double)values[key].Value;
        }
    }
}
