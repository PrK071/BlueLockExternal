// C# | Navigation/NavigationLog.cs | .NET Framework 4.8 x64 | Exportable bounded navigation CSV.
using System;
using System.IO;
using System.Globalization;
using BlueLockV2.Core;

namespace BlueLockV2.Navigation
{
    public sealed class NavigationLog : IDisposable
    {
        readonly StreamWriter writer;
        long rows;
        public readonly string Path;
        public NavigationLog(string folder)
        {
            Directory.CreateDirectory(folder);
            Path = System.IO.Path.Combine(folder, "navigation-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff") + ".csv");
            writer = new StreamWriter(new FileStream(Path, FileMode.CreateNew, FileAccess.Write, FileShare.ReadWrite));
            writer.WriteLine("time,player_x,player_z_or_plane_y,target_x,target_z_or_plane_y,distance,camera_heading_rad,camera_confidence,intended_right,intended_forward,actual_keys,expected_x,expected_y,observed_x,observed_y,direction_error_degrees,motion_error,state,decision_state,reason");
            writer.Flush();
        }
        public void Write(NavigationTrace trace, string actualKeys, string decisionState = "NAVIGATION")
        {
            if (trace == null || rows++ > 100000) return;
            writer.WriteLine(String.Format(CultureInfo.InvariantCulture,
                "{0:R},{1:R},{2:R},{3:R},{4:R},{5:R},{6:R},{7:R},{8:R},{9:R},\"{10}\",{11:R},{12:R},{13:R},{14:R},{15:R},{16:R},{17},{19},\"{18}\"",
                trace.Time, trace.Player.X, trace.Player.Y, trace.Target.X, trace.Target.Y, trace.Distance, trace.CameraHeading, trace.CameraConfidence,
                trace.Intended.X, trace.Intended.Y, actualKeys ?? "", trace.Expected.X, trace.Expected.Y,
                trace.Observed.X, trace.Observed.Y, trace.DirectionError, trace.MotionError, trace.State, (trace.Reason ?? "").Replace("\"", "\"\""), decisionState));
            writer.Flush();
        }
        public void Dispose() { writer.Dispose(); }
    }
}
