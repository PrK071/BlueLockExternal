// C# | Input/HotkeyService.cs | .NET Framework 4.8 x64 | Independent hotkey message pump and immediate F12 callback.
using System;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;
using BlueLockV2.Core;

namespace BlueLockV2.Input
{
    public sealed class HotkeyService : IDisposable
    {
        readonly Thread thread;
        readonly ManualResetEvent ready = new ManualResetEvent(false);
        ApplicationContext context;
        Control dispatch;
        Exception error;
        bool closed;
        int pilotKey = 0x79;
        public int PilotKey
        {
            get
            {
                return Volatile.Read(ref pilotKey);
            }

            set
            {
                if (!Hotkeys.IsPilotKey(value))
                    throw new ArgumentException("Tecla reservada ou indisponível para o piloto.");
                Volatile.Write(ref pilotKey, value);
            }
        }

        public HotkeyService(IntPtr panel, Action<int> hotkey, Action emergency, Action manual)
        {
            thread = new Thread(delegate ()
            {
                try
                {
                    using (var hook = new KeyboardHook())
                    using (var control = new Control())
                    {
                        dispatch = control;
                        IntPtr handle = control.Handle;
                        context = new ApplicationContext();
                        hook.IsPanelFocused = delegate
                        {
                            return Platform.GetForegroundWindow() == panel;
                        };
                        hook.PilotKey = delegate
                        {
                            return PilotKey;
                        };
                        hook.IsGameFocused = delegate
                        {
                            IntPtr w = Platform.GetForegroundWindow();
                            return Platform.IsTarget(w, false) || Platform.IsTarget(w, true);
                        };
                        hook.Hotkey = delegate (int key)
                        {
                            if (key == 0x7B)
                                emergency();
                            else
                                hotkey(key);
                        };
                        hook.ManualKey = delegate (int key)
                        {
                            manual();
                        };
                        ready.Set();
                        Application.Run(context);
                    }
                }
                catch (Exception e)
                {
                    error = e;
                    ready.Set();
                }
            });
            thread.Name = "BLV2 Hotkeys and emergency";
            thread.IsBackground = true;
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            if (!ready.WaitOne(3000))
                throw new TimeoutException("Inicialização dos atalhos excedeu 3 segundos.");
            if (error != null)
                throw new InvalidOperationException("Falha nos atalhos", error);
        }

        public void Dispose()
        {
            if (closed)
                return;
            closed = true;
            if (dispatch != null && !dispatch.IsDisposed)
                try
                {
                    dispatch.BeginInvoke((Action)delegate
                    {
                        context.ExitThread();
                    });
                }
                catch (InvalidOperationException)
                {
                }

            thread.Join(1500);
            ready.Dispose();
        }
    }

    public static class DesktopEnvironment
    {
        public static IntPtr BindStudioWindow()
        {
            IntPtr window = Platform.GetForegroundWindow();
            if (!Platform.IsTarget(window, true)) throw new InvalidOperationException("Volte ao viewport do cenário no Roblox Studio.");
            return window;
        }
        public static bool StudioWindowFocused(IntPtr window)
        { return window != IntPtr.Zero && Platform.IsTarget(window, true) && Platform.GameFocused(window); }
        public static PointF ReadRobloxCursorAnchor()
        {
            IntPtr window = Platform.GetForegroundWindow();
            if (!Platform.IsTarget(window, false))
                throw new InvalidOperationException("Volte à janela Roblox antes de marcar os pés.");
            Rectangle bounds = Platform.ClientBounds(window);
            Point cursor = Platform.CursorPoint();
            if (!bounds.Contains(cursor) || bounds.Width < 100 || bounds.Height < 100)
                throw new InvalidOperationException("O ponteiro precisa estar entre os pés, dentro da janela do jogo.");
            float x = (float)(cursor.X - bounds.X) / bounds.Width, y = (float)(cursor.Y - bounds.Y) / bounds.Height;
            if (x < .05 || x > .95 || y < .1 || y > .95)
                throw new InvalidOperationException("Referência fora da região jogável.");
            return new PointF(x, y);
        }

        public static void EnableDpi()
        {
            Platform.SetProcessDPIAware();
        }
    }
}
