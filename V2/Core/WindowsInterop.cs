// C# | Core/WindowsInterop.cs | .NET Framework / Windows x64 | External game input and user-controlled hotkeys.
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace BlueLockV2.Core
{
    internal static class Platform
    {
        [StructLayout(LayoutKind.Sequential)]
        internal struct PointNative
        {
            public int X, Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct RectNative
        {
            public int L, T, R, B;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct Mouse
        {
            public int x, y;
            public uint data, flags, time;
            public UIntPtr extra;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct Keyboard
        {
            public ushort vk, scan;
            public uint flags, time;
            public UIntPtr extra;
        }

        [StructLayout(LayoutKind.Explicit)]
        struct Payload
        {
            [FieldOffset(0)]
            public Mouse mouse;
            [FieldOffset(0)]
            public Keyboard keyboard;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct Input
        {
            public uint type;
            public Payload payload;
        }

        [DllImport("user32.dll")]
        internal static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")]
        static extern uint GetWindowThreadProcessId(IntPtr window, out uint pid);
        [DllImport("user32.dll")]
        static extern IntPtr GetAncestor(IntPtr window, uint flags);
        [DllImport("user32.dll")]
        static extern bool GetClientRect(IntPtr window, out RectNative rect);
        [DllImport("user32.dll")]
        static extern bool ClientToScreen(IntPtr window, ref PointNative point);
        [DllImport("user32.dll")]
        static extern bool GetCursorPos(out PointNative point);
        [DllImport("user32.dll")]
        internal static extern bool SetProcessDPIAware();
        [DllImport("user32.dll")]
        static extern uint MapVirtualKey(uint key, uint type);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern int GetClassName(IntPtr window, System.Text.StringBuilder buffer, int count);
        [DllImport("user32.dll", SetLastError = true)]
        static extern uint SendInput(uint n, Input[] values, int size);
        internal static bool RobloxWindow(IntPtr window)
        {
            uint pid;
            GetWindowThreadProcessId(window, out pid);
            if (pid == 0)
                return false;
            try
            {
                using (Process p = Process.GetProcessById((int)pid))
                    return p.ProcessName.Equals("RobloxPlayerBeta", StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        internal static bool RobloxFocused()
        {
            return RobloxWindow(GetForegroundWindow());
        }

        internal static bool IsTarget(IntPtr window, bool studio)
        {
            uint pid = WindowPid(window);
            if (pid == 0)
                return false;
            try
            {
                using (Process p = Process.GetProcessById((int)pid))
                    return p.ProcessName.Equals(studio ? "RobloxStudioBeta" : "RobloxPlayerBeta", StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        internal static IntPtr FindTarget(bool studio)
        {
            Process[] processes = Process.GetProcessesByName(studio ? "RobloxStudioBeta" : "RobloxPlayerBeta");
            IntPtr found = IntPtr.Zero;
            foreach (Process p in processes)
            {
                using (p)
                {
                    if (found == IntPtr.Zero && p.MainWindowHandle != IntPtr.Zero)
                        found = p.MainWindowHandle;
                }
            }

            return found;
        }

        internal static IntPtr BindGameWindow()
        {
            IntPtr foreground = GetForegroundWindow();
            if (!RobloxWindow(foreground))
                throw new InvalidOperationException("Roblox não está em primeiro plano.");
            IntPtr root = GetAncestor(foreground, 2);
            return root != IntPtr.Zero && SameProcess(root, foreground) ? root : foreground;
        }

        private static uint WindowPid(IntPtr window)
        {
            if (window == IntPtr.Zero)
                return 0;
            uint pid;
            return GetWindowThreadProcessId(window, out pid) == 0 ? 0 : pid;
        }

        private static bool SameProcess(IntPtr a, IntPtr b)
        {
            uint first = WindowPid(a), second = WindowPid(b);
            return first != 0 && first == second;
        }

        internal static bool GameFocused(IntPtr gameWindow)
        {
            // The target was verified once on F9. Child/owned windows of that same
            // process do not count as a new application, and no process-name query
            // is needed on every 35 ms control tick.
            return gameWindow != IntPtr.Zero && SameProcess(gameWindow, GetForegroundWindow());
        }

        internal static string FocusReason(IntPtr gameWindow)
        {
            IntPtr current = GetForegroundWindow();
            uint targetPid = WindowPid(gameWindow), foregroundPid = WindowPid(current);
            if (current == IntPtr.Zero || foregroundPid == 0)
                return "janela em primeiro plano temporariamente indisponível";
            if (targetPid == 0)
                return "a janela Roblox original foi encerrada ou recriada";
            if (targetPid == foregroundPid)
                return "mesmo processo Roblox, janela interna alterada";
            string name;
            try
            {
                using (Process p = Process.GetProcessById((int)foregroundPid))
                    name = p.ProcessName;
            }
            catch
            {
                name = "processo não identificado";
            }

            var className = new System.Text.StringBuilder(128);
            GetClassName(current, className, className.Capacity);
            return "primeiro plano em " + name + "/" + className + " (PID " + foregroundPid + "); Roblox PID " + targetPid;
        }

        internal static Rectangle ClientBounds(IntPtr window)
        {
            RectNative r;
            PointNative p = new PointNative();
            if (!GetClientRect(window, out r) || !ClientToScreen(window, ref p) || r.R - r.L < 100 || r.B - r.T < 100)
                throw new InvalidOperationException("Janela Roblox minimizada ou sem área de captura.");
            return new Rectangle(p.X, p.Y, r.R - r.L, r.B - r.T);
        }

        internal static Point CursorPoint()
        {
            PointNative p;
            if (!GetCursorPos(out p))
                throw new Win32Exception();
            return new Point(p.X, p.Y);
        }

        internal static int Key(string name)
        {
            switch (name)
            {
                case "MOUSELEFT":
                    return 1;
                case "MOUSERIGHT":
                    return 2;
                case "SPACE":
                    return 0x20;
                case "SHIFT":
                    return 0x10;
                case "CTRL":
                    return 0x11;
                case "ALT":
                    return 0x12;
                case "TAB":
                    return 0x09;
                case "UP":
                    return 0x26;
                case "DOWN":
                    return 0x28;
                case "LEFT":
                    return 0x25;
                case "RIGHT":
                    return 0x27;
            }

            if (name.Length == 1 && ((name[0] >= 'A' && name[0] <= 'Z') || (name[0] >= '0' && name[0] <= '9')))
                return name[0];
            throw new ArgumentException("Tecla inválida: " + name);
        }

        internal static void Send(int key, bool down)
        {
            Input value = new Input();
            if (key == 1 || key == 2)
            {
                value.type = 0;
                value.payload.mouse.flags = key == 1 ? (down ? 2u : 4u) : (down ? 8u : 16u);
            }
            else
            {
                value.type = 1;
                uint scan = MapVirtualKey((uint)key, 0);
                if (scan == 0)
                    throw new InvalidOperationException("Tecla sem scan code: " + key);
                value.payload.keyboard.scan = (ushort)scan;
                value.payload.keyboard.flags = 8u | (down ? 0u : 2u) | ((key >= 0x25 && key <= 0x28) ? 1u : 0u);
            }

            if (SendInput(1, new Input[] { value }, Marshal.SizeOf(typeof(Input))) != 1)
                throw new InvalidOperationException("Windows bloqueou SendInput. Código " + Marshal.GetLastWin32Error());
        }
    }
}
