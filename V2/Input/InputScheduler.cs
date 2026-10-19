// C# | Input/InputScheduler.cs | .NET Framework 4.8 x64 | Timed multikey input with cancellation, focus and emergency guards.
using System;
using System.Collections.Generic;
using System.Threading;
using BlueLockV2.Core;
using BlueLockV2.Configuration;

namespace BlueLockV2.Input
{
    public interface IInputTransport
    {
        bool Focused { get; }

        void Send(int key, bool down);
    }

    public sealed class NativeTransport : IInputTransport
    {
        readonly bool studio;
        readonly Func<bool> sourceFocused;
        IntPtr target;
        public NativeTransport(bool studioOnly) : this(studioOnly, null)
        {
        }

        public NativeTransport(bool studioOnly, Func<bool> captureFocus)
        {
            studio = studioOnly;
            sourceFocused = captureFocus;
        }

        public bool Focused
        {
            get
            {
                if (sourceFocused != null && !sourceFocused())
                    return false;
                IntPtr foreground = Platform.GetForegroundWindow();
                if (Platform.IsTarget(foreground, studio))
                {
                    target = foreground;
                    return true;
                }

                return target != IntPtr.Zero && Platform.GameFocused(target) && Platform.IsTarget(target, studio);
            }
        }

        public void Send(int key, bool down)
        {
            if (down && !Focused)
                throw new InvalidOperationException("Foco do destino perdido.");
            Platform.Send(key, down);
        }
    }

    public sealed class MacroStep
    {
        public readonly int[] Keys;
        public readonly int Milliseconds;
        public MacroStep(int[] keys, int milliseconds)
        {
            Keys = keys;
            Milliseconds = milliseconds;
        }

        public static MacroStep[] Parse(string text)
        {
            var result = new List<MacroStep>();
            int total = 0;
            foreach (string item in text.Split(';'))
            {
                if (String.IsNullOrWhiteSpace(item))
                    continue;
                string[] parts = item.Trim().ToUpperInvariant().Split(':');
                int ms;
                if (parts.Length != 2 || !int.TryParse(parts[1], out ms) || ms < 1 || ms > 5000)
                    throw new ArgumentException("Etapa inválida: " + item);
                var keys = new List<int>();
                if (parts[0].Trim() != "WAIT")
                    foreach (string key in parts[0].Split('+'))
                        keys.Add(Platform.Key(key.Trim()));
                total += ms;
                if (total > 15000 || result.Count >= 64)
                    throw new ArgumentException("Macro excede 15 segundos ou 64 etapas.");
                result.Add(new MacroStep(keys.ToArray(), ms));
            }

            if (result.Count == 0)
                throw new ArgumentException("Macro vazia.");
            return result.ToArray();
        }
    }

    public sealed class InputScheduler : IActionExecutor
    {
        readonly object gate = new object ();
        readonly IInputTransport transport;
        readonly IClock clock;
        readonly Settings settings;
        readonly HashSet<int> held = new HashSet<int>();
        readonly AutoResetEvent wake = new AutoResetEvent(false);
        readonly ManualResetEvent finish = new ManualResetEvent(false);
        readonly Thread worker;
        MacroStep[] macro;
        int step;
        double deadline, lease;
        double dribbleUntil, nextDribble;
        int[] movement = new int[0];
        bool stopped, disposed;
        public string LastError { get; private set; }
        public int Events { get; private set; }
        public string HeldKeys
        {
            get
            {
                lock (gate)
                {
                    var names = new List<string>();
                    foreach (int key in held) names.Add(((System.Windows.Forms.Keys)key).ToString());
                    names.Sort();
                    return String.Join("+", names.ToArray());
                }
            }
        }

        public InputScheduler(IInputTransport destination, IClock time, Settings configuration, bool background)
        {
            transport = destination;
            clock = time;
            settings = configuration.Copy();
            if (background)
            {
                worker = new Thread(Loop);
                worker.IsBackground = true;
                worker.Name = "Input lease watchdog";
                worker.Start();
            }
        }

        public int HeldCount
        {
            get
            {
                lock (gate)
                    return held.Count;
            }
        }

        public void StartMacro(MacroStep[] sequence)
        {
            lock (gate)
            {
                if (disposed)
                    throw new ObjectDisposedException("InputScheduler");
                if (sequence == null || sequence.Length == 0)
                    throw new ArgumentException("Macro vazia.");
                EmergencyLocked();
                if (!transport.Focused)
                    throw new InvalidOperationException("Abra o jogo ou Studio em primeiro plano.");
                stopped = false;
                macro = sequence;
                step = 0;
                deadline = clock.Now + macro[0].Milliseconds / 1000.0;
                lease = clock.Now + 16;
                try
                {
                    Apply(macro[0].Keys);
                }
                catch
                {
                    EmergencyLocked();
                    throw;
                }
            }

            Signal();
        }

        public void Execute(ActionIntent intent)
        {
            lock (gate)
            {
                if (disposed)
                    return;
                if (intent.Timestamp > clock.Now + .1 || intent.Expires <= clock.Now)
                {
                    EmergencyLocked();
                    return;
                }

                stopped = false;
                macro = null;
                lease = Math.Min(intent.Expires, clock.Now + .4);
                var keys = new List<int>();
                if (intent.Kind == IntentKind.DRIBBLE && settings.PilotDribble && clock.Now >= nextDribble)
                {
                    dribbleUntil = clock.Now + .06;
                    nextDribble = clock.Now + settings.PilotDribbleInterval;
                }
                else if (intent.Kind != IntentKind.DRIBBLE && intent.Kind != IntentKind.MOVE)
                    dribbleUntil = 0;
                if (intent.Kind == IntentKind.MOVE)
                {
                    if (intent.Direction.X < -.2)
                        keys.Add(settings.UseArrows ? 0x25 : 'A');
                    if (intent.Direction.X > .2)
                        keys.Add(settings.UseArrows ? 0x27 : 'D');
                    if (intent.Direction.Y < -.2)
                        keys.Add(settings.UseArrows ? 0x26 : 'W');
                    if (intent.Direction.Y > .2)
                        keys.Add(settings.UseArrows ? 0x28 : 'S');
                    if (settings.Sprint && keys.Count > 0)
                        keys.Add(0x10);
                }
                else if (intent.Kind == IntentKind.DEFEND)
                    keys.Add(0x11);
                if (clock.Now < dribbleUntil)
                    keys.Add('Q');
                // A shot requires actual aim feedback. A pixel target alone cannot determine camera yaw.
                if (intent.Kind == IntentKind.SHOOT || intent.Kind == IntentKind.PASS)
                {
                    LastError = "Mira de Studio requer calibração; intenção exibida sem chute físico.";
                }

                movement = keys.ToArray();
                try
                {
                    if (!transport.Focused)
                        EmergencyLocked();
                    else
                        Apply(movement);
                }
                catch (Exception e)
                {
                    LastError = e.Message;
                    EmergencyLocked();
                }
            }

            Signal();
        }

        public void Tick()
        {
            lock (gate)
            {
                if (disposed)
                    return;
                if (stopped)
                {
                    if (held.Count > 0)
                        EmergencyLocked();
                    return;
                }

                try
                {
                    double now = clock.Now;
                    if (!transport.Focused || now >= lease)
                    {
                        LastError = !transport.Focused ? "Foco perdido; entradas liberadas." : "Lease expirado; entradas liberadas.";
                        EmergencyLocked();
                        return;
                    }

                    if (macro != null && now >= deadline)
                    {
                        step++;
                        if (step == macro.Length)
                        {
                            macro = null;
                            EmergencyLocked();
                            return;
                        }

                        deadline = now + macro[step].Milliseconds / 1000.0;
                        Apply(macro[step].Keys);
                    }

                    if (macro == null && dribbleUntil > 0 && now >= dribbleUntil)
                    {
                        dribbleUntil = 0;
                        var withoutDribble = new List<int>(movement);
                        withoutDribble.Remove('Q');
                        movement = withoutDribble.ToArray();
                        Apply(movement);
                    }
                }
                catch (Exception e)
                {
                    LastError = e.Message;
                    EmergencyLocked();
                }
            }
        }

        void Apply(IEnumerable<int> keys)
        {
            HashSet<int> desired = new HashSet<int>(keys);
            foreach (int k in new List<int>(held))
                if (!desired.Contains(k))
                {
                    transport.Send(k, false);
                    held.Remove(k);
                }

            foreach (int k in desired)
                if (!held.Contains(k))
                {
                    held.Add(k);
                    transport.Send(k, true);
                    Events++;
                }
        }

        void EmergencyLocked()
        {
            stopped = true;
            macro = null;
            dribbleUntil = 0;
            movement = new int[0];
            foreach (int key in new List<int>(held))
            {
                try
                {
                    transport.Send(key, false);
                    held.Remove(key);
                }
                catch (Exception e)
                {
                    LastError = e.Message;
                }
            }
        }

        public void EmergencyStop()
        {
            lock (gate)
            {
                if (disposed) return;
                EmergencyLocked();
                wake.Set();
            }
        }

        void Signal() { lock (gate) { if (!disposed) wake.Set(); } }

        void Loop()
        {
            WaitHandle[] waits =
            {
                finish,
                wake
            };
            while (!finish.WaitOne(0))
            {
                Tick();
                bool pending;
                lock (gate)
                    pending = held.Count > 0 || macro != null;
                WaitHandle.WaitAny(waits, pending ? 20 : Timeout.Infinite);
            }

            lock (gate)
                EmergencyLocked();
        }

        public void Dispose()
        {
            lock (gate)
            {
                if (disposed)
                    return;
                EmergencyLocked();
                disposed = true;
            }

            finish.Set();
            wake.Set();
            if (worker != null && Thread.CurrentThread != worker)
                worker.Join(1000);
            wake.Dispose();
            finish.Dispose();
        }
    }

    public static class Hotkeys
    {
        public static bool IsPilotKey(int key)
        {
            return (key >= 0x70 && key <= 0x74) || key == 0x79 || key == 0x7A || key == 0x2D || key == 0x24 || key == 0x23 || key == 0x21 || key == 0x22 || key == 0x13 || key == 0x91;
        }

        public static string Map(int key)
        {
            return Map(key, 0x79);
        }

        public static string Map(int key, int pilotKey)
        {
            if (key == pilotKey && IsPilotKey(pilotKey))
                return "PILOT_TOGGLE";
            switch (key)
            {
                case 0x75:
                    return "SHOT_A";
                case 0x76:
                    return "SHOT_B";
                case 0x77:
                    return "COMBO";
                case 0x78:
                    return "TOGGLE";
                case 0x7B:
                    return "STOP";
                default:
                    return "NONE";
            }
        }
    }
}
