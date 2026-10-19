// C# | Tests/Navigation/Acceptance.cs | .NET Framework 4.8 x64 | Independent camera/world/keyboard plant and deterministic navigation acceptance.
using System;
using System.IO;
using System.Text;
using System.Diagnostics;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Threading;
using BlueLockV2.Capture;
using OpenCvSharp;
using BlueLockV2.Core;
using BlueLockV2.Configuration;
using BlueLockV2.Input;
using BlueLockV2.Navigation;

namespace BlueLockV2.NavigationAcceptance
{
    sealed class Clock : IClock { public double Time; public double Now { get { return Time; } } }
    sealed class Transport : IInputTransport
    {
        public readonly HashSet<int> Keys = new HashSet<int>();
        public bool Focus = true;
        public bool Focused { get { return Focus; } }
        public void Send(int key, bool down) { if (down) Keys.Add(key); else Keys.Remove(key); }
        public Vec Control { get { return new Vec((Keys.Contains('D') ? 1 : 0) - (Keys.Contains('A') ? 1 : 0), (Keys.Contains('W') ? 1 : 0) - (Keys.Contains('S') ? 1 : 0)); } }
    }
    static class Acceptance
    {
        static int passed, failed;
        static readonly string Folder = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "reports", "navigation-v21"));
        static readonly StringBuilder results = new StringBuilder("controller,case,camera_deg,initial_distance,final_distance,mean_direction_error_deg,max_inverted_seconds,success,elapsed_sim_s\r\n");
        static void Check(bool value, string message) { if (!value) throw new Exception(message); }
        static void Test(string name, Action action) { try { action(); passed++; Console.WriteLine("PASS " + name); } catch (Exception e) { failed++; Console.WriteLine("FAIL " + name + " | " + e.Message); } }
        static MotionBasis Basis(double degrees)
        {
            double a = degrees * Math.PI / 180;
            return MotionBasis.CameraXZ(new Vec(Math.Cos(a), Math.Sin(a)), new Vec(Math.Sin(a), -Math.Cos(a)), 16, 1);
        }
        static Vec Target(int s) { return s == 0 ? new Vec(0, -20) : s == 1 ? new Vec(0, 20) : s == 2 ? new Vec(-20, 0) : s == 3 ? new Vec(20, 0) : s == 4 ? new Vec(16, -16) : new Vec(15, -16); }
        static string Name(int s) { return new[] { "front", "behind", "left", "right", "diagonal", "moving", "camera_rotation", "temporary_loss" }[s]; }
        static NavigationSample Sample(double time, long sequence, Vec position, Vec target)
        { return new NavigationSample { Time = time, Sequence = sequence, Player = position, Target = target, Basis = Basis(0), Confidence = 1, CameraConfidence = 1, Space = NavigationSpace.WorldXZ }; }
        static bool Scenario(int scenario, double initialHeading, bool legacy, bool settledWorld = false)
        {
            var clock = new Clock(); var transport = new Transport(); var nav = new NavigationController(settledWorld);
            Vec player = new Vec(), target = Target(scenario);
            double initial = Vec.Distance(player, target), errorSum = 0, reverse = 0, maxReverse = 0;
            int commands = 0; bool success = false;
            NavigationSample sample = null;
            using (var input = new InputScheduler(transport, clock, new Settings { Sprint = false }, false))
            {
                for (int tick = 0; tick < 1800; tick++)
                {
                    clock.Time = tick * .02;
                    MotionBasis actual = Basis(initialHeading + (scenario == 6 && clock.Time >= .8 ? 90 : 0));
                    target = Target(scenario) + (scenario == 5 ? new Vec(5 * Math.Sin(clock.Time * .35), 0) : new Vec());
                    bool visible = !(scenario == 7 && clock.Time >= 1 && clock.Time < 2);
                    ActionIntent current;
                    if (legacy)
                    {
                        // Actual audited screen rule with a stale 50% anchor; the true feet are at y=500.
                        Vec delta = target - player;
                        double right = (delta.X * actual.Right.X + delta.Y * actual.Right.Y) / 16;
                        double forward = (delta.X * actual.Forward.X + delta.Y * actual.Forward.Y) / 16;
                        double dx = right * 8 / 1280, dy = (500 - forward * 4 - 339) / 678;
                        bool near = Math.Abs(dx) < .06 && dy > -.075 && dy < .10;
                        Vec command = !visible || near ? new Vec() : new Vec(dx < -.035 ? -1 : dx > .035 ? 1 : 0, dy < -.055 ? -1 : dy > .10 ? 1 : 0);
                        current = new ActionIntent(IntentKind.MOVE, target, command, clock.Time, .12, 1, 1, "legacy stale-anchor regression");
                    }
                    else
                    {
                        if (tick % 10 == 0)
                        {
                            sample = Sample(clock.Time, tick / 10 + 1, player, target); sample.Basis = actual;
                            sample.TargetVelocity = scenario == 5 ? new Vec(1.75 * Math.Cos(clock.Time * .35), 0) : new Vec();
                            sample.TargetObserved = visible;
                        }
                        current = nav.Update(sample, clock.Time);
                    }
                    input.Execute(current); input.Tick();
                    if (!visible) Check(transport.Keys.Count == 0, "input while target unavailable");
                    Vec velocity = actual.Displacement(transport.Control);
                    if (velocity.Length > 16) velocity = velocity.Normalized * 16;
                    if (velocity.Length > 0)
                    {
                        double angle = MotionBasis.Angle(target - player, velocity);
                        errorSum += angle; commands++; reverse = angle > 90 ? reverse + .02 : 0;
                        maxReverse = Math.Max(maxReverse, reverse);
                    }
                    else reverse = 0;
                    player += velocity * .02;
                    if (Vec.Distance(player, target) <= 1.25) { success = true; break; }
                }
                input.EmergencyStop(); Check(transport.Keys.Count == 0, "final keys held");
            }
            double final = Vec.Distance(player, target);
            results.AppendLine(String.Format(CultureInfo.InvariantCulture, "{0},{1},{2},{3:F3},{4:F3},{5:F3},{6:F3},{7},{8:F3}",
                legacy ? "legacy_stale_anchor" : settledWorld ? "v21_settled_feedback" : "v21", Name(scenario), initialHeading, initial, final, commands == 0 ? 0 : errorSum / commands, maxReverse, success, clock.Time));
            if (!legacy) { Check(success && final < initial, "did not reach: " + final); Check(maxReverse <= .16, "persistent inversion " + maxReverse); Check(!nav.Faulted, "unexpected recalibration"); }
            return success;
        }
        public static int Main(string[] args)
        {
            if (args.Length == 3 && args[0] == "--generate") { Console.WriteLine(StudioLabProject.Write(args[1], args[2])); return 0; }
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture; Directory.CreateDirectory(Folder); Stopwatch total = Stopwatch.StartNew();
            PlaceFormatTests.Run(Test, Folder);
            PipelineNavigationTests.Run(Test);
            SettlementTests.Run(Test);
            FeedbackTests.Run(Test, Folder);
            for (int s = 0; s < 8; s++) for (int h = 0; h < 360; h += 90)
            {
                int scenario = s, heading = h;
                Test(Name(s) + " camera " + h, delegate { Scenario(scenario, heading, false); });
                Test("settled feedback " + Name(s) + " camera " + h, delegate { Scenario(scenario, heading, false, true); });
                Scenario(s, h, true);
            }
            Test("independent WASD calibration in four cameras", delegate
            {
                for (int h = 0; h < 360; h += 90)
                {
                    MotionBasis actual = Basis(h); var calibration = new MovementCalibration();
                    Vec[] controls = { new Vec(0, 1), new Vec(-1, 0), new Vec(0, -1), new Vec(1, 0) };
                    for (int k = 0; k < 4; k++) calibration.Add(k, new Vec(), actual.Displacement(controls[k]) * .25, .25, 1);
                    MotionBasis measured = calibration.Build();
                    Check(MotionBasis.Angle(measured.Forward, actual.Forward) < .001 && MotionBasis.Angle(measured.Right, actual.Right) < .001, "basis mismatch");
                }
            });
            Test("reject inconsistent calibration", delegate
            {
                var calibration = new MovementCalibration(); bool rejected = false;
                for (int i = 0; i < 4; i++) calibration.Add(i, new Vec(), new Vec(1, 0), .1, 1);
                try { calibration.Build(); } catch (InvalidOperationException) { rejected = true; }
                Check(rejected, "invalid calibration accepted");
            });
            Test("fixed pixel feet cannot authorize navigation", delegate
            {
                var engine = new BlueLockV2.Decision.VisualPilotDecision(new Settings { CalibratedLocal = true });
                var local = new TrackedObject(1, EntityType.LOCAL_PLAYER, new Vec(640, 339), new Vec(), 0, 0, .7, 16, false, false, false, 4, false);
                var ball = new TrackedObject(2, EntityType.BALL, new Vec(640, 430), new Vec(), 0, 0, .95, 10, false, false, false, 4, true);
                Check(engine.Decide(new GameState(0, 1, 1280, 678, new[] { local, ball }, CameraMotion.Stable, 0), new Forecast[0], 0).Kind == IntentKind.WAIT, "unsafe pixel steering");
            });
            Test("inverted response faults after one pulse", delegate
            {
                var nav = new NavigationController(); var sample = Sample(0, 1, new Vec(), new Vec(20, 0));
                nav.Update(sample, 0); sample = Sample(.1, 2, new Vec(), new Vec(20, 0)); Check(nav.Update(sample, .1).Kind == IntentKind.MOVE, "no pulse");
                sample = Sample(.3, 3, new Vec(-1.9, 0), new Vec(20, 0));
                Check(nav.Update(sample, .3).Kind == IntentKind.WAIT && nav.Faulted, "inversion not blocked");
            });
            Test("F12 latch", delegate { var nav = new NavigationController(); nav.EmergencyStop(); Check(nav.Update(Sample(0, 1, new Vec(), new Vec(20, 0)), 0).Kind == IntentKind.WAIT, "rearmed"); });
            Test("stale sample cannot renew pulse", delegate
            {
                var nav = new NavigationController(); var clock = new Clock(); var transport = new Transport();
                using (var input = new InputScheduler(transport, clock, new Settings { Sprint = false }, false))
                {
                    nav.Update(Sample(0, 1, new Vec(), new Vec(20, 0)), 0); clock.Time = .1;
                    var sample = Sample(.1, 2, new Vec(), new Vec(20, 0)); input.Execute(nav.Update(sample, .1));
                    Check(transport.Keys.Count > 0, "no key"); clock.Time = .5; input.Execute(nav.Update(sample, .5)); input.Tick();
                    Check(transport.Keys.Count == 0, "stale keys held");
                }
            });
            Test("focus loss releases keys", delegate
            {
                var clock = new Clock(); var transport = new Transport();
                using (var input = new InputScheduler(transport, clock, new Settings { Sprint = false }, false))
                { input.Execute(new ActionIntent(IntentKind.MOVE, new Vec(), new Vec(0, -1), 0, .12, 1, 1, "test")); transport.Focus = false; input.Tick(); Check(transport.Keys.Count == 0, "focus release"); }
            });
            Test("non-Studio telemetry rejected", delegate
            {
                bool rejected = false; try { StudioTelemetry.Parse("{\"studio\":false}", 0); } catch (ArgumentException) { rejected = true; } Check(rejected, "accepted");
            });
            Test("actual loopback telemetry transport", delegate
            {
                var clock = new Clock();
                using (var bridge = new StudioTelemetry(clock)) using (var web = new WebClient())
                {
                    web.Proxy = null; web.Headers[HttpRequestHeader.ContentType] = "application/json";
                    web.UploadString(bridge.Endpoint, "{\"studio\":true,\"seq\":1,\"px\":0,\"pz\":0,\"tx\":10,\"tz\":-10,\"vx\":0,\"vz\":0,\"rx\":1,\"rz\":0,\"fx\":0,\"fz\":-1,\"speed\":16,\"grounded\":true}");
                    Check(bridge.Accepted == 1 && bridge.Latest.Basis.Valid, "not received");
                }
            });
            Test("vertical camera rejected", delegate { Check(!MotionBasis.CameraXZ(new Vec(1, 0), new Vec(), 16, 1).Valid, "vertical accepted"); });
            Test("WASD signs and opposite keys release", delegate
            {
                var clock = new Clock(); var transport = new Transport();
                using (var input = new InputScheduler(transport, clock, new Settings { Sprint = false }, false))
                {
                    Vec[] vectors = { new Vec(0,-1), new Vec(0,1), new Vec(-1,0), new Vec(1,0) };
                    int[] keys = { 'W', 'S', 'A', 'D' };
                    for (int i = 0; i < 4; i++)
                    {
                        input.Execute(new ActionIntent(IntentKind.MOVE, new Vec(), vectors[i], 0, .12, 1, 1, "map"));
                        Check(transport.Keys.Count == 1 && transport.Keys.Contains(keys[i]), "opposite key leaked");
                    }
                }
            });
            Test("unknown orientation confidence and airborne stop", delegate
            {
                var nav = new NavigationController(); var sample = Sample(0, 1, new Vec(), new Vec(20, 0));
                sample.CameraConfidence = .3; Check(nav.Update(sample, 0).Kind == IntentKind.WAIT, "camera uncertainty");
                sample.CameraConfidence = 1; sample.Grounded = false; Check(nav.Update(sample, 0).Kind == IntentKind.WAIT, "airborne movement");
            });
            Test("coordinate space change invalidates active pulse", delegate
            {
                var nav = new NavigationController(); nav.Update(Sample(0, 1, new Vec(), new Vec(20, 0)), 0);
                Check(nav.Update(Sample(.1, 2, new Vec(), new Vec(20, 0)), .1).Kind == IntentKind.MOVE, "no pulse");
                var sample = Sample(.14, 3, new Vec(), new Vec(20,0)); sample.Space = NavigationSpace.CalibratedPlane;
                Check(nav.Update(sample, .14).Kind == IntentKind.WAIT && nav.Faulted, "mixed spaces");
            });
            Test("neutral interval before opposite direction", delegate
            {
                var nav = new NavigationController(); nav.Update(Sample(0, 1, new Vec(), new Vec(20,0)), 0);
                nav.Update(Sample(.1,2,new Vec(),new Vec(20,0)),.1);
                nav.Update(Sample(.3,3,new Vec(1.92,0),new Vec(-20,0)),.3);
                var action = nav.Update(Sample(.36,4,new Vec(1.92,0),new Vec(-20,0)),.36);
                Check(action.Kind == IntentKind.WAIT && nav.Trace.State == "HYSTERESIS", "rapid reversal");
            });
            Test("CSV contains actual keys and observed feedback", delegate
            {
                string path;
                using (var log = new NavigationLog(Path.Combine(Folder, "export")))
                {
                    path = log.Path;
                    log.Write(new NavigationTrace { Time = .2, Player = new Vec(1,2), Target = new Vec(5,6),
                        Expected = new Vec(1,0), Observed = new Vec(.9,0), State = "FEEDBACK", Reason = "observed, verified" }, "D");
                }
                string csv = File.ReadAllText(path);
                Check(csv.Contains("actual_keys") && csv.Contains("\"D\"") && csv.Contains("0.9"), "missing evidence fields");
            });
            Test("telemetry numeric type validation", delegate
            {
                bool rejected = false;
                try { StudioTelemetry.Parse("{\"studio\":true,\"seq\":true}", 0); } catch (ArgumentException) { rejected = true; }
                Check(rejected, "boolean sequence accepted");
            });
            File.WriteAllText(Path.Combine(Folder, "acceptance.csv"), results.ToString());
            var timer = Stopwatch.StartNew(); var bench = new NavigationController();
            for (int i = 0; i < 100000; i++) bench.Update(Sample(i * .02, i, new Vec(i * .16, 0), new Vec(i * .16 + 10, 0)), i * .02);
            timer.Stop(); var process = Process.GetCurrentProcess();
            File.WriteAllText(Path.Combine(Folder, "performance.txt"), "100000_updates_ms=" + timer.Elapsed.TotalMilliseconds.ToString("F3") +
                "\r\nmean_update_us=" + (timer.Elapsed.TotalMilliseconds / 100).ToString("F3") +
                "\r\nprivate_MiB=" + (process.PrivateMemorySize64 / 1048576d).ToString("F3") + "\r\nworking_MiB=" + (process.WorkingSet64 / 1048576d).ToString("F3") + "\r\ntest_wall_s=" + total.Elapsed.TotalSeconds.ToString("F3"));
            Console.WriteLine("TOTAL " + passed + " passed / " + failed + " failed"); return failed == 0 ? 0 : 1;
        }
    }
}
