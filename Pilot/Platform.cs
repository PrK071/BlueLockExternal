// C# | Platform.cs | .NET Framework / Windows x64 | External game input and user-controlled hotkeys.
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

internal static class Platform
{
    [StructLayout(LayoutKind.Sequential)] internal struct PointNative { public int X,Y; }
    [StructLayout(LayoutKind.Sequential)] internal struct RectNative { public int L,T,R,B; }
    [StructLayout(LayoutKind.Sequential)] struct Mouse { public int x,y; public uint data,flags,time; public UIntPtr extra; }
    [StructLayout(LayoutKind.Sequential)] struct Keyboard { public ushort vk,scan; public uint flags,time; public UIntPtr extra; }
    [StructLayout(LayoutKind.Explicit)] struct Payload { [FieldOffset(0)] public Mouse mouse; [FieldOffset(0)] public Keyboard keyboard; }
    [StructLayout(LayoutKind.Sequential)] struct Input { public uint type; public Payload payload; }
    [DllImport("user32.dll")] internal static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr window, out uint pid);
    [DllImport("user32.dll")] static extern IntPtr GetAncestor(IntPtr window,uint flags);
    [DllImport("user32.dll")] static extern bool GetClientRect(IntPtr window, out RectNative rect);
    [DllImport("user32.dll")] static extern bool ClientToScreen(IntPtr window, ref PointNative point);
    [DllImport("user32.dll")] static extern bool GetCursorPos(out PointNative point);
    [DllImport("user32.dll")] internal static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")] static extern uint MapVirtualKey(uint key,uint type);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern int GetClassName(IntPtr window,System.Text.StringBuilder buffer,int count);
    [DllImport("user32.dll", SetLastError=true)] static extern uint SendInput(uint n, Input[] values, int size);
    internal static bool RobloxWindow(IntPtr window)
    {
        uint pid; GetWindowThreadProcessId(window, out pid);
        if (pid == 0) return false;
        try { using (Process p = Process.GetProcessById((int)pid)) return p.ProcessName.Equals("RobloxPlayerBeta", StringComparison.OrdinalIgnoreCase); }
        catch { return false; }
    }
    internal static bool RobloxFocused() { return RobloxWindow(GetForegroundWindow()); }
    internal static IntPtr BindGameWindow()
    {
        IntPtr foreground=GetForegroundWindow();
        if(!RobloxWindow(foreground))throw new InvalidOperationException("Roblox não está em primeiro plano.");
        IntPtr root=GetAncestor(foreground,2);
        return root!=IntPtr.Zero && SameProcess(root,foreground) ? root : foreground;
    }
    private static uint WindowPid(IntPtr window)
    {
        if(window==IntPtr.Zero)return 0;
        uint pid;return GetWindowThreadProcessId(window,out pid)==0?0:pid;
    }
    private static bool SameProcess(IntPtr a,IntPtr b)
    {
        uint first=WindowPid(a),second=WindowPid(b);
        return first!=0 && first==second;
    }
    internal static bool GameFocused(IntPtr gameWindow)
    {
        // The target was verified once on F9. Child/owned windows of that same
        // process do not count as a new application, and no process-name query
        // is needed on every 35 ms control tick.
        return gameWindow!=IntPtr.Zero && SameProcess(gameWindow,GetForegroundWindow());
    }
    internal static string FocusReason(IntPtr gameWindow)
    {
        IntPtr current=GetForegroundWindow();uint targetPid=WindowPid(gameWindow),foregroundPid=WindowPid(current);
        if(current==IntPtr.Zero || foregroundPid==0)return "janela em primeiro plano temporariamente indisponível";
        if(targetPid==0)return "a janela Roblox original foi encerrada ou recriada";
        if(targetPid==foregroundPid)return "mesmo processo Roblox, janela interna alterada";
        string name;
        try {using(Process p=Process.GetProcessById((int)foregroundPid))name=p.ProcessName;}
        catch {name="processo não identificado";}
        var className=new System.Text.StringBuilder(128);GetClassName(current,className,className.Capacity);
        return "primeiro plano em "+name+"/"+className+" (PID "+foregroundPid+"); Roblox PID "+targetPid;
    }
    internal static Rectangle ClientBounds(IntPtr window)
    {
        RectNative r; PointNative p = new PointNative();
        if (!GetClientRect(window,out r) || !ClientToScreen(window,ref p) || r.R-r.L<100 || r.B-r.T<100)
            throw new InvalidOperationException("Janela Roblox minimizada ou sem área de captura.");
        return new Rectangle(p.X,p.Y,r.R-r.L,r.B-r.T);
    }
    internal static Point CursorPoint() { PointNative p; if(!GetCursorPos(out p)) throw new Win32Exception(); return new Point(p.X,p.Y); }
    internal static int Key(string name)
    {
        switch(name) {
            case "MOUSELEFT": return 1; case "MOUSERIGHT": return 2;
            case "SPACE": return 0x20; case "SHIFT": return 0x10; case "CTRL": return 0x11;
            case "UP": return 0x26; case "DOWN": return 0x28; case "LEFT": return 0x25; case "RIGHT": return 0x27;
        }
        if(name.Length==1 && ((name[0]>='A' && name[0]<='Z') || (name[0]>='0' && name[0]<='9'))) return name[0];
        throw new ArgumentException("Tecla inválida: " + name);
    }
    internal static void Send(int key,bool down)
    {
        Input value=new Input();
        if(key==1 || key==2) { value.type=0; value.payload.mouse.flags=key==1 ? (down?2u:4u) : (down?8u:16u); }
        else {
            value.type=1;
            uint scan=MapVirtualKey((uint)key,0);
            if(scan==0)throw new InvalidOperationException("Tecla sem scan code: "+key);
            value.payload.keyboard.scan=(ushort)scan;
            value.payload.keyboard.flags=8u | (down?0u:2u) | ((key>=0x25 && key<=0x28)?1u:0u);
        }
        if(SendInput(1,new Input[]{value},Marshal.SizeOf(typeof(Input)))!=1)
            throw new InvalidOperationException("Windows bloqueou SendInput. Código " + Marshal.GetLastWin32Error());
    }
}

internal sealed class OutputController
{
    private readonly HashSet<int> held = new HashSet<int>();
    private readonly Dictionary<int,long> pulses = new Dictionary<int,long>();
    internal int SentEvents {get;private set;}
    internal string LastEvent {get;private set;}
    internal string HeldSummary {get{return held.Count==0?"nenhuma":String.Join("+",new List<int>(held).ConvertAll(KeyName).ToArray());}}
    private static string KeyName(int key)
    {
        switch(key){case 1:return "MOUSELEFT";case 2:return "MOUSERIGHT";case 0x10:return "SHIFT";case 0x11:return "CTRL";case 0x20:return "SPACE";
            case 0x25:return "LEFT";case 0x26:return "UP";case 0x27:return "RIGHT";case 0x28:return "DOWN";default:return ((char)key).ToString();}
    }
    private void Send(int key,bool down)
    {
        Platform.Send(key,down);SentEvents++;LastEvent=KeyName(key)+(down?" pressionada":" solta");
    }
    internal void Movement(IEnumerable<int> keys, long now)
    {
        HashSet<int> desired=new HashSet<int>(keys);
        foreach(var pair in new List<KeyValuePair<int,long>>(pulses)) {
            if(pair.Value<=now) pulses.Remove(pair.Key); else desired.Add(pair.Key);
        }
        foreach(int key in new List<int>(held)) if(!desired.Contains(key)) { Send(key,false); held.Remove(key); }
        foreach(int key in desired) if(!held.Contains(key)) { Send(key,true); held.Add(key); }
    }
    internal void Pulse(int key,int milliseconds,long now)
    {
        if(!held.Contains(key)) { Send(key,true); held.Add(key); }
        pulses[key]=now+milliseconds;
    }
    internal bool Pressed(int key) { return held.Contains(key); }
    internal void Stop()
    {
        Exception failure=null;
        foreach(int key in new List<int>(held)) {
            try { Send(key,false); held.Remove(key); }
            catch(Exception e) { failure=e; }
        }
        pulses.Clear();
        if(failure!=null) throw failure;
    }
}

internal sealed class KeyboardHook : IDisposable
{
    [StructLayout(LayoutKind.Sequential)] struct HookData { public uint vk,scan,flags,time; public UIntPtr extra; }
    delegate IntPtr HookProc(int code,IntPtr message,IntPtr data);
    [DllImport("user32.dll",SetLastError=true)] static extern IntPtr SetWindowsHookEx(int type,HookProc callback,IntPtr module,uint thread);
    [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr hook,int code,IntPtr message,IntPtr data);
    [DllImport("kernel32.dll",CharSet=CharSet.Auto)] static extern IntPtr GetModuleHandle(string module);
    private readonly HookProc callback;
    private readonly HashSet<int> physical = new HashSet<int>();
    private IntPtr hook;
    internal Action<int> Hotkey;
    internal Action<int> ManualKey;
    internal Func<bool> IsPanelFocused;
    internal Func<bool> IsGameFocused;
    internal bool AnyControlHeld {
        get { foreach(int k in physical) if(IsControl(k)) return true; return false; }
    }
    internal KeyboardHook()
    {
        callback=Dispatch;
        hook=SetWindowsHookEx(13,callback,GetModuleHandle(null),0);
        if(hook==IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error(),"Não foi possível registrar os atalhos.");
    }
    private static bool IsControl(int k)
    {
        return k=='W'||k=='A'||k=='S'||k=='D'||k=='Q'||k=='R'||k==0x20||k==0x11||k==0x10||
            (k>=0x25&&k<=0x28)||(k>=0xA0&&k<=0xA3);
    }
    private IntPtr Dispatch(int code,IntPtr message,IntPtr ptr)
    {
        if(code>=0) {
            HookData data=(HookData)Marshal.PtrToStructure(ptr,typeof(HookData));
            // Injected input is never treated as physical input. No keystroke text is stored.
            if((data.flags & 0x12)==0) {
                int key=(int)data.vk; int msg=message.ToInt32();
                bool down=msg==0x100||msg==0x104, up=msg==0x101||msg==0x105;
                bool first=down && physical.Add(key);
                if(up) physical.Remove(key);
                bool gameContext=IsGameFocused!=null ? IsGameFocused() : Platform.RobloxFocused();
                bool context=gameContext || (IsPanelFocused!=null && IsPanelFocused());
                bool reserved=(key>=0x71 && key<=0x7B); // F2-F12.
                if((context && reserved) || key==0x7B) {
                    if(first && Hotkey!=null) Hotkey(key);
                    return new IntPtr(1);
                }
                if(first && gameContext && (IsControl(key)||key==0x0D||key==0x1B||key==0xBF) && ManualKey!=null) ManualKey(key);
                // Keep only control and reserved states; no typed text is retained.
                if(!IsControl(key) && !reserved) physical.Remove(key);
            }
        }
        return CallNextHookEx(hook,code,message,ptr);
    }
    public void Dispose() { if(hook!=IntPtr.Zero) { UnhookWindowsHookEx(hook); hook=IntPtr.Zero; } }
}
