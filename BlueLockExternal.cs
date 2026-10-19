// C# | BlueLockExternal.cs | .NET Framework 4.x / Windows x64 | Roblox Player external input macros.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Xml.Serialization;

public class MacroSettings
{
    public int ShotA = 180;
    public int ShotB = 500;
    public string ShotButton = "MOUSELEFT";
    public string Combo = "Q:45; WAIT:120; MOUSELEFT:300";
}

internal static class Native
{
    [StructLayout(LayoutKind.Sequential)] internal struct MouseInput
    { public int dx, dy; public uint mouseData, flags, time; public UIntPtr extraInfo; }
    [StructLayout(LayoutKind.Sequential)] internal struct KeyboardInput
    { public ushort vk, scan; public uint flags, time; public UIntPtr extraInfo; }
    [StructLayout(LayoutKind.Explicit)] internal struct InputUnion
    {
        [FieldOffset(0)] public MouseInput mouse;
        [FieldOffset(0)] public KeyboardInput keyboard;
    }
    [StructLayout(LayoutKind.Sequential)] internal struct Input
    { public uint type; public InputUnion data; }
    [DllImport("user32.dll")] internal static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")] internal static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("user32.dll", SetLastError=true)] private static extern uint SendInput(uint n, Input[] inputs, int size);
    internal static bool IsDown(int key) { return (GetAsyncKeyState(key) & 0x8000) != 0; }
    internal static bool RobloxFocused()
    {
        uint pid;
        GetWindowThreadProcessId(GetForegroundWindow(), out pid);
        if (pid == 0) return false;
        try { using (Process p = Process.GetProcessById((int)pid))
            return String.Equals(p.ProcessName, "RobloxPlayerBeta", StringComparison.OrdinalIgnoreCase); }
        catch { return false; }
    }
    internal static int KeyCode(string name)
    {
        name = name.Trim().ToUpperInvariant();
        if (name.Length == 1 && ((name[0] >= 'A' && name[0] <= 'Z') || (name[0] >= '0' && name[0] <= '9'))) return name[0];
        switch (name) {
            case "SPACE": return 0x20;
            case "SHIFT": return 0x10;
            case "CTRL": return 0x11;
            case "ALT": return 0x12;
            case "TAB": return 0x09;
            case "UP": return 0x26;
            case "DOWN": return 0x28;
            case "LEFT": return 0x25;
            case "RIGHT": return 0x27;
            default: throw new FormatException("Tecla desconhecida: " + name);
        }
    }
    internal static void ValidateKey(string name)
    {
        if (name != "MOUSELEFT" && name != "MOUSERIGHT") KeyCode(name);
    }
    internal static void Send(string key, bool down)
    {
        Input input = new Input();
        if (key == "MOUSELEFT" || key == "MOUSERIGHT") {
            input.type = 0;
            input.data.mouse.flags = key == "MOUSELEFT" ? (down ? 0x0002u : 0x0004u) : (down ? 0x0008u : 0x0010u);
        } else {
            input.type = 1;
            int vk = KeyCode(key);
            uint extended = (vk >= 0x25 && vk <= 0x28) ? 1u : 0u;
            input.data.keyboard = new KeyboardInput { vk = (ushort)vk, flags = extended | (down ? 0u : 2u) };
        }
        if (SendInput(1, new Input[] { input }, Marshal.SizeOf(typeof(Input))) != 1)
            throw new InvalidOperationException("O Windows bloqueou o comando. Win32=" + Marshal.GetLastWin32Error());
    }
}

internal sealed class Step
{
    public string Key;
    public int Milliseconds;
}

internal sealed class MainForm : Form
{
    private readonly NumericUpDown shotA = new NumericUpDown(), shotB = new NumericUpDown();
    private readonly ComboBox shotButton = new ComboBox();
    private readonly TextBox combo = new TextBox(), log = new TextBox();
    private readonly Label state = new Label(), focusState = new Label();
    private readonly Button arm = new Button();
    private readonly System.Windows.Forms.Timer poll = new System.Windows.Forms.Timer();
    private readonly Dictionary<int, bool> previous = new Dictionary<int, bool>();
    private readonly string settingsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BlueLockExternal", "settings.xml");
    private readonly object heldLock = new object();
    private string held;
    private CancellationTokenSource cancellation;
    private bool armed, busy;
    private bool? lastFocus;
    private readonly Color bg = Color.FromArgb(17, 23, 34), panel = Color.FromArgb(29, 38, 53);

    public MainForm()
    {
        Text = "Blue Lock External | Macros";
        ClientSize = new Size(700, 580);
        MinimumSize = MaximumSize = new Size(716, 619);
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = bg;
        ForeColor = Color.FromArgb(226, 233, 245);
        Font = new Font("Segoe UI", 10);
        Label title = LabelAt("BLUE LOCK / EXTERNAL", 24, 20, 650, 35);
        title.Font = new Font("Segoe UI", 20, FontStyle.Bold);
        LabelAt("Macros locais de teclado e mouse", 26, 60, 640, 26);
        state.SetBounds(26, 101, 430, 30); state.Font = new Font(Font, FontStyle.Bold); Controls.Add(state);
        focusState.SetBounds(26, 132, 640, 25); Controls.Add(focusState);
        LabelAt("F6  Chute A (ms)", 26, 180, 175, 25);
        LabelAt("F7  Chute B (ms)", 245, 180, 175, 25);
        LabelAt("Botão do chute", 465, 180, 205, 25);
        SetupNumber(shotA, 26, 210); SetupNumber(shotB, 245, 210);
        shotButton.SetBounds(465, 210, 205, 32);
        shotButton.DropDownStyle = ComboBoxStyle.DropDownList;
        shotButton.Items.AddRange(new object[] { "MOUSELEFT", "MOUSERIGHT" }); Controls.Add(shotButton);
        LabelAt("F8  Sequência configurável", 26, 260, 640, 26);
        combo.SetBounds(26, 291, 644, 32); combo.MaxLength = 2000; StyleInput(combo); Controls.Add(combo);
        LabelAt("Exemplo: Q:45; WAIT:120; MOUSELEFT:300  ·  tempos em ms", 26, 327, 650, 25);
        LabelAt("Valores iniciais são exemplos; ajuste aos controles e ao timing do jogo.", 26, 353, 650, 25);
        arm.SetBounds(26, 393, 260, 43); arm.Click += delegate { ToggleArm(); }; StyleButton(arm); Controls.Add(arm);
        Button save = new Button { Text = "Salvar configuração" }; save.SetBounds(300, 393, 185, 43);
        save.Click += delegate { SaveSettings(true); }; StyleButton(save); Controls.Add(save);
        Button stop = new Button { Text = "F12  Parar" }; stop.SetBounds(499, 393, 171, 43);
        stop.Click += delegate { StopEverything("Interrompido pelo botão."); }; StyleButton(stop); Controls.Add(stop);
        log.SetBounds(26, 453, 644, 78); log.Multiline = true; log.ReadOnly = true; log.ScrollBars = ScrollBars.Vertical; StyleInput(log); Controls.Add(log);
        LabelAt("F9 ativa/pausa · F12 interrompe · pause antes de abrir o chat", 26, 541, 650, 25);
        LoadSettings(); UpdateState();
        foreach (int key in new int[] { 0x75, 0x76, 0x77, 0x78, 0x7B }) previous[key] = Native.IsDown(key);
        poll.Interval = 20; poll.Tick += Poll; poll.Start();
        FormClosing += delegate { poll.Stop(); StopEverything(null); SaveSettings(false); };
        AddLog("Pronto. Volte ao Roblox e pressione F9 para ativar.");
    }
    private Label LabelAt(string text, int x, int y, int w, int h)
    { Label l = new Label { Text = text }; l.SetBounds(x,y,w,h); Controls.Add(l); return l; }
    private void StyleInput(Control c) { c.BackColor = panel; c.ForeColor = ForeColor; }
    private void StyleButton(Button b) { b.FlatStyle = FlatStyle.Flat; b.BackColor = panel; b.ForeColor = ForeColor; }
    private void SetupNumber(NumericUpDown n, int x, int y)
    { n.Minimum = 10; n.Maximum = 3000; n.Increment = 10; n.SetBounds(x,y,195,32); StyleInput(n); Controls.Add(n); }
    private void AddLog(string message)
    { if (!IsDisposed) log.AppendText(DateTime.Now.ToString("HH:mm:ss") + "  " + message + Environment.NewLine); }
    private void UpdateState()
    {
        state.Text = armed ? (busy ? "ATIVO · executando" : "ATIVO · aguardando atalho") : "PAUSADO";
        state.ForeColor = armed ? Color.FromArgb(76, 222, 168) : Color.FromArgb(244, 189, 89);
        arm.Text = armed ? "F9  Pausar macros" : "F9  Ativar macros";
    }
    private List<Step> ParseCombo()
    {
        List<Step> result = new List<Step>(); int total = 0;
        foreach (string raw in combo.Text.Split(';')) {
            if (String.IsNullOrWhiteSpace(raw)) continue;
            string[] parts = raw.Trim().Split(':'); int ms;
            if (parts.Length != 2 || !Int32.TryParse(parts[1].Trim(), out ms) || ms < 10 || ms > 3000)
                throw new FormatException("Use TECLA:tempo entre 10 e 3000 ms; separe com ponto e vírgula.");
            string key = parts[0].Trim().ToUpperInvariant();
            if (key != "WAIT") Native.ValidateKey(key);
            total += ms; result.Add(new Step { Key = key, Milliseconds = ms });
        }
        if (result.Count == 0 || result.Count > 30 || total > 15000)
            throw new FormatException("Sequência: 1 a 30 passos, até 15 segundos no total.");
        return result;
    }
    private void ToggleArm()
    {
        if (armed) { StopEverything("Macros pausadas."); return; }
        try { ParseCombo(); armed = true; UpdateState(); AddLog("Ativo: F6 chute A, F7 chute B, F8 sequência."); }
        catch (Exception e) { AddLog(e.Message); }
    }
    private void ReleaseHeld()
    {
        lock (heldLock) {
            if (held == null) return;
            string key = held;
            Native.Send(key, false);
            held = null;
        }
    }
    private void StopEverything(string message)
    {
        armed = false;
        if (cancellation != null) cancellation.Cancel();
        try { ReleaseHeld(); } catch (Exception e) { AddLog("Falha ao soltar comando: " + e.Message); }
        UpdateState(); if (message != null) AddLog(message);
    }
    private void Poll(object sender, EventArgs args)
    {
        bool focused = Native.RobloxFocused();
        if (lastFocus != focused) {
            focusState.Text = focused ? "Roblox em primeiro plano · atalhos disponíveis quando ativo" : "Roblox fora de foco · comandos bloqueados";
            lastFocus = focused;
        }
        if (busy && !focused && cancellation != null) { cancellation.Cancel(); try { ReleaseHeld(); } catch (Exception e) { StopEverything(e.Message); } }
        List<int> rising = new List<int>();
        foreach (int key in new int[] { 0x75, 0x76, 0x77, 0x78, 0x7B }) {
            bool down = Native.IsDown(key); if (down && !previous[key]) rising.Add(key); previous[key] = down;
        }
        if (rising.Contains(0x7B)) { StopEverything("F12: interrompido."); return; }
        if (rising.Contains(0x78) && focused) { ToggleArm(); return; }
        if (!focused || !armed || busy) return;
        try {
            if (rising.Contains(0x75)) RunSequence(new List<Step> { new Step { Key = (string)shotButton.SelectedItem, Milliseconds = (int)shotA.Value } }, "Chute A");
            else if (rising.Contains(0x76)) RunSequence(new List<Step> { new Step { Key = (string)shotButton.SelectedItem, Milliseconds = (int)shotB.Value } }, "Chute B");
            else if (rising.Contains(0x77)) RunSequence(ParseCombo(), "Sequência");
        } catch (Exception e) { StopEverything(e.Message); }
    }
    private async void RunSequence(List<Step> steps, string name)
    {
        busy = true; cancellation = new CancellationTokenSource();
        CancellationTokenSource source = cancellation; UpdateState();
        try {
            foreach (Step step in steps) {
                source.Token.ThrowIfCancellationRequested();
                if (!Native.RobloxFocused()) throw new OperationCanceledException();
                if (step.Key != "WAIT") {
                    lock (heldLock) {
                        if (Native.IsDown(step.Key == "MOUSELEFT" ? 1 : step.Key == "MOUSERIGHT" ? 2 : Native.KeyCode(step.Key)))
                            throw new InvalidOperationException("Solte " + step.Key + " antes de usar o atalho.");
                        held = step.Key;
                        Native.Send(step.Key, true);
                    }
                }
                try { await Task.Delay(step.Milliseconds, source.Token); }
                finally { ReleaseHeld(); }
            }
            AddLog(name + " enviada. O timing deve ser calibrado no jogo.");
        } catch (OperationCanceledException) { AddLog(name + " cancelada."); }
        catch (Exception e) { armed = false; AddLog("Erro: " + e.Message); }
        finally {
            try { ReleaseHeld(); } catch (Exception e) { armed = false; AddLog("Falha ao soltar comando: " + e.Message); }
            cancellation = null; source.Dispose(); busy = false; UpdateState();
        }
    }
    private void LoadSettings()
    {
        MacroSettings cfg = new MacroSettings();
        try { if (File.Exists(settingsPath)) using (FileStream stream = File.OpenRead(settingsPath)) cfg = (MacroSettings)new XmlSerializer(typeof(MacroSettings)).Deserialize(stream); }
        catch (Exception e) { AddLog("Configuração padrão: " + e.Message); }
        shotA.Value = Math.Max(10, Math.Min(3000,cfg.ShotA)); shotB.Value = Math.Max(10, Math.Min(3000,cfg.ShotB));
        shotButton.SelectedIndex = cfg.ShotButton == "MOUSERIGHT" ? 1 : 0;
        combo.Text = cfg.Combo ?? "Q:45; WAIT:120; MOUSELEFT:300";
    }
    private void SaveSettings(bool show)
    {
        try {
            ParseCombo();
            Directory.CreateDirectory(Path.GetDirectoryName(settingsPath));
            MacroSettings cfg = new MacroSettings { ShotA = (int)shotA.Value, ShotB = (int)shotB.Value, ShotButton = (string)shotButton.SelectedItem, Combo = combo.Text };
            using (FileStream stream = File.Create(settingsPath)) new XmlSerializer(typeof(MacroSettings)).Serialize(stream,cfg);
            if (show) AddLog("Configuração salva.");
        } catch (Exception e) { if (show) AddLog("Não foi possível salvar: " + e.Message); }
    }
    [STAThread] public static void Main()
    {
        Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
        using (Mutex instance = new Mutex(true, "Local\\BlueLockExternalMacros", out instanceCreated)) {
            if (!instanceCreated) { MessageBox.Show("O programa já está aberto.", "Blue Lock External"); return; }
            Application.Run(new MainForm());
        }
    }
    private static bool instanceCreated;
}
