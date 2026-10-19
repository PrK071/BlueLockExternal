// C# | Diagnostics/VisualE2E/NativePulse.cs | .NET Framework 4.8 x64 | Bounded real InputScheduler pulse, foreground guard and guaranteed release.
using System;
using System.Threading;
using BlueLockV2.Core;
using BlueLockV2.Input;
using BlueLockV2.Configuration;
class NativePulse
{
 static int Main(){var t=new NativeTransport(true);if(!t.Focused){Console.WriteLine("BLOCKED_FOCUS");return 2;}using(var s=new InputScheduler(t,new MonotonicClock(),new Settings{Sprint=false},true)){s.StartMacro(MacroStep.Parse("W:250"));Thread.Sleep(400);s.EmergencyStop();Console.WriteLine("native_events="+s.Events+" held="+s.HeldCount+" error="+s.LastError);}return 0;}
}
