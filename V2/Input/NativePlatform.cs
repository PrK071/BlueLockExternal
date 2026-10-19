// C# | Input/NativePlatform.cs | .NET Framework / Windows x64 | External game input and user-controlled hotkeys.
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using BlueLockV2.Core;

namespace BlueLockV2.Input
{
    internal sealed class KeyboardHook : IDisposable
    {
        [StructLayout(LayoutKind.Sequential)]
        struct HookData
        {
            public uint vk, scan, flags, time;
            public UIntPtr extra;
        }

        delegate IntPtr HookProc(int code, IntPtr message, IntPtr data);
        [DllImport("user32.dll", SetLastError = true)]
        static extern IntPtr SetWindowsHookEx(int type, HookProc callback, IntPtr module, uint thread);
        [DllImport("user32.dll")]
        static extern bool UnhookWindowsHookEx(IntPtr hook);
        [DllImport("user32.dll")]
        static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);
        [DllImport("kernel32.dll", CharSet = CharSet.Auto)]
        static extern IntPtr GetModuleHandle(string module);
        private readonly HookProc callback;
        private readonly HashSet<int> physical = new HashSet<int>();
        private IntPtr hook;
        internal Action<int> Hotkey;
        internal Action<int> ManualKey;
        internal Func<bool> IsPanelFocused;
        internal Func<bool> IsGameFocused;
        internal Func<int> PilotKey;
        internal bool AnyControlHeld
        {
            get
            {
                foreach (int k in physical)
                    if (IsControl(k))
                        return true;
                return false;
            }
        }

        internal KeyboardHook()
        {
            callback = Dispatch;
            hook = SetWindowsHookEx(13, callback, GetModuleHandle(null), 0);
            if (hook == IntPtr.Zero)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Não foi possível registrar os atalhos.");
        }

        private static bool IsControl(int k)
        {
            return k == 'W' || k == 'A' || k == 'S' || k == 'D' || k == 'Q' || k == 'R' || k == 0x20 || k == 0x11 || k == 0x10 || (k >= 0x25 && k <= 0x28) || (k >= 0xA0 && k <= 0xA3);
        }

        private IntPtr Dispatch(int code, IntPtr message, IntPtr ptr)
        {
            if (code >= 0)
            {
                HookData data = (HookData)Marshal.PtrToStructure(ptr, typeof(HookData));
                // Injected input is never treated as physical input. No keystroke text is stored.
                if ((data.flags & 0x12) == 0)
                {
                    int key = (int)data.vk;
                    int msg = message.ToInt32();
                    bool down = msg == 0x100 || msg == 0x104, up = msg == 0x101 || msg == 0x105;
                    bool first = down && physical.Add(key);
                    if (up)
                        physical.Remove(key);
                    bool gameContext = IsGameFocused != null ? IsGameFocused() : Platform.RobloxFocused();
                    bool context = gameContext || (IsPanelFocused != null && IsPanelFocused());
                    bool reserved = Hotkeys.Map(key, PilotKey == null ? 0x79 : PilotKey()) != "NONE";
                    if ((context && reserved) || key == 0x7B)
                    {
                        if (first && Hotkey != null)
                            Hotkey(key);
                        return new IntPtr(1);
                    }

                    if (first && gameContext && (IsControl(key) || key == 0x0D || key == 0x1B || key == 0xBF) && ManualKey != null)
                        ManualKey(key);
                    // Keep only control and reserved states; no typed text is retained.
                    if (!IsControl(key) && !reserved)
                        physical.Remove(key);
                }
            }

            return CallNextHookEx(hook, code, message, ptr);
        }

        public void Dispose()
        {
            if (hook != IntPtr.Zero)
            {
                UnhookWindowsHookEx(hook);
                hook = IntPtr.Zero;
            }
        }
    }
}
