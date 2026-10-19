// C# | Tests/TestMain.cs | .NET Framework 4.8 x64 | Deterministic tests, real fixtures, closed-loop demo and benchmarks.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading;
using OpenCvSharp;
using BlueLockV2.Core;
using BlueLockV2.Configuration;
using BlueLockV2.Capture;
using BlueLockV2.Vision;
using BlueLockV2.Tracking;
using BlueLockV2.Prediction;
using BlueLockV2.Decision;
using BlueLockV2.Input;

namespace BlueLockV2.Tests
{
    public sealed class FakeClock : IClock
    {
        public double Time;
        public double Now
        {
            get
            {
                return Time;
            }
        }
    }

    sealed class FakeTransport : IInputTransport
    {
        public bool Focus = true, FailDown;
        public readonly HashSet<int> Keys = new HashSet<int>();
        public bool Focused
        {
            get
            {
                return Focus;
            }
        }

        public void Send(int key, bool down)
        {
            if (down)
            {
                Keys.Add(key);
                if (FailDown)
                    throw new InvalidOperationException("injected test failure");
            }
            else
                Keys.Remove(key);
        }
    }

    sealed class DisposableItem : IDisposable
    {
        public bool Closed;
        public void Dispose()
        {
            Closed = true;
        }
    }

    sealed class CachedImageSource : IFrameSource
    {
        readonly Mat image;
        readonly IClock clock;
        long id;
        public CachedImageSource(string path, IClock time)
        {
            image = Cv2.ImRead(path);
            clock = time;
            if (image.Empty())
                throw new ArgumentException("Fixture inválida");
        }

        public SourceKind Kind
        {
            get
            {
                return SourceKind.Images;
            }
        }

        public bool Completed
        {
            get
            {
                return false;
            }
        }

        public string Description
        {
            get
            {
                return "Imagem real estática em memória";
            }
        }

        public Frame Read(MatPool pool, int width, Rect? roi)
        {
            return Pixels.Resize(image, pool, width, roi, clock.Now, ++id);
        }

        public void Dispose()
        {
            image.Dispose();
        }
    }

    sealed class BlockingExecutor : IActionExecutor
    {
        public readonly ManualResetEvent Entered = new ManualResetEvent(false), Continue = new ManualResetEvent(false);
        public volatile bool Active;
        int blocked;
        public void Execute(ActionIntent intent)
        {
            if (intent.Kind != IntentKind.MOVE)
            {
                Active = false;
                return;
            }

            if (Interlocked.CompareExchange(ref blocked, 1, 0) == 0)
            {
                Entered.Set();
                Continue.WaitOne(2000);
            }

            Active = true;
        }

        public void EmergencyStop()
        {
            Active = false;
        }

        public void Dispose()
        {
            EmergencyStop();
            Continue.Set();
            Entered.Dispose();
            Continue.Dispose();
        }
    }

    public static class TestMain
    {
        static int passed, failed;
        static string root = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ".."));
        static readonly List<string> output = new List<string>();
        static void Line(string s)
        {
            Console.WriteLine(s);
            output.Add(s);
        }

        static void Check(bool condition, string message)
        {
            if (!condition)
                throw new Exception(message);
        }

        static void Near(double a, double b, double tolerance)
        {
            Check(Math.Abs(a - b) <= tolerance, "expected " + b + ", got " + a);
        }

        static void Test(string name, Action action)
        {
            try
            {
                action();
                passed++;
                Line("PASS " + name);
            }
            catch (Exception e)
            {
                failed++;
                Line("FAIL " + name + " | " + e);
            }
        }

        static Detection Ball(double x, double y)
        {
            return new Detection(EntityType.BALL, new Vec(x, y), 8, .95);
        }

        static TrackedObject Obj(int id, EntityType type, Vec pos, Vec velocity, double time, double confidence, bool changed = false)
        {
            return new TrackedObject(id, type, pos, velocity, time, time, confidence, 8, false, false, changed, 5, true);
        }

        static GameState State(double time, double possession, params TrackedObject[] objects)
        {
            return new GameState(time, 1, 640, 360, objects, CameraMotion.Stable, possession);
        }

        static GameState Pair(double time, double confidence)
        {
            return State(time, 0, Obj(1, EntityType.BALL, new Vec(200, 100), new Vec(), time, confidence), Obj(2, EntityType.LOCAL_PLAYER, new Vec(100, 100), new Vec(), time, .95));
        }

        public static int Main(string[] args)
        {
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            Directory.CreateDirectory(Path.Combine(root, "reports"));
            try
            {
                Cv2.SetNumThreads(1);
                if (args.Length > 0 && args[0] == "--benchmark-image")
                {
                    BenchmarkImage(args.Length > 1 ? int.Parse(args[1]) : 15);
                    return 0;
                }

                if (args.Length > 0 && args[0] == "--benchmark")
                {
                    Benchmark(args.Length > 1 ? int.Parse(args[1]) : 15);
                    return 0;
                }

                if (args.Length > 0 && args[0] == "--video")
                {
                    Video(args[1]);
                    return 0;
                }

                Run();
                Line("TOTAL " + passed + " passed / " + failed + " failed");
                File.WriteAllLines(Path.Combine(root, "reports", "tests.txt"), output);
                return failed == 0 ? 0 : 1;
            }
            catch (Exception e)
            {
                Line(e.ToString());
                return 2;
            }
        }

        static void Run()
        {
            Test("OpenCV native load", delegate
            {
                using (Mat m = new Mat(10, 10, MatType.CV_8UC3, Scalar.All(12)))
                    Near(Cv2.Mean(m).Val0, 12, .001);
                Line("OpenCV " + Cv2.GetVersionString());
            });
            Test("Latest frame replacement disposes old", delegate
            {
                using (var slot = new LatestSlot<DisposableItem>())
                {
                    var a = new DisposableItem();
                    var b = new DisposableItem();
                    slot.Put(a);
                    slot.Put(b);
                    Check(a.Closed && !b.Closed && slot.Dropped == 1, "drop ownership");
                    Check(slot.Take() == b, "latest");
                    b.Dispose();
                    slot.Dispose();
                    var c = new DisposableItem();
                    slot.Put(c);
                    Check(c.Closed, "closed slot");
                    slot.Ready.Dispose();
                }
            });
            Test("Frame pool reuse and bounded queue", delegate
            {
                using (var pool = new MatPool(3))
                using (var slot = new LatestSlot<Frame>())
                {
                    for (int i = 0; i < 100; i++)
                    {
                        Mat m = pool.Rent();
                        m.Create(30, 40, MatType.CV_8UC3);
                        slot.Put(new Frame(m, pool, i, i, 40, 30, new Rect(0, 0, 40, 30)));
                    }

                    slot.Dispose();
                    Check(pool.Outstanding == 0 && pool.Peak <= 2, "bounded ownership");
                    Check(slot.Dropped == 99, "drop count");
                    slot.Ready.Dispose();
                }
            });
            Test("Simulation pixel segmentation entities", delegate
            {
                var clock = new FakeClock();
                using (var sim = new Simulation(clock))
                using (var pool = new MatPool(3))
                using (var vision = new VisionEngine())
                using (Frame frame = sim.Read(pool, 480, null))
                {
                    Cv2.ImWrite(Path.Combine(root, "reports", "simulation.png"), frame.Image);
                    var entities = vision.Detect(frame, new Settings(), true);
                    foreach (var d in entities)
                        Line("synthetic " + d.Type + " " + d.Position + " " + d.Confidence);
                    foreach (EntityType type in new[]
                    {
                        EntityType.BALL,
                        EntityType.LOCAL_PLAYER,
                        EntityType.ALLY,
                        EntityType.OPPONENT,
                        EntityType.GOAL,
                        EntityType.GOALKEEPER
                    }

                    )
                    {
                        bool found = false;
                        foreach (var d in entities)
                            if (d.Type == type)
                                found = true;
                        Check(found, type.ToString() + " candidates=" + vision.Candidates);
                    }
                }
            });
            Test("Real screenshot ball detection", delegate
            {
                string file = Path.Combine(root, "Tests", "Fixtures", "screenshot.png");
                RealDetection(file, new Vec(1097, 525), 30);
            });
            Test("Recorded frame 06s ball detection", delegate
            {
                RealDetection(Path.Combine(root, "Tests", "Fixtures", "06s.png"), new Vec(622, 262), 25);
            });
            Test("Recorded frame 24s ball detection", delegate
            {
                RealDetection(Path.Combine(root, "Tests", "Fixtures", "24s.png"), new Vec(625, 308), 25);
            });
            Test("Economic 480px screenshot detection", delegate
            {
                RealDetection(Path.Combine(root, "Tests", "Fixtures", "screenshot.png"), new Vec(1097, 525), 30, 480);
            });
            Test("Economic 480px recorded frame 06s", delegate
            {
                RealDetection(Path.Combine(root, "Tests", "Fixtures", "06s.png"), new Vec(622, 262), 25, 480);
            });
            Test("Economic 480px recorded frame 24s", delegate
            {
                RealDetection(Path.Combine(root, "Tests", "Fixtures", "24s.png"), new Vec(625, 308), 25, 480);
            });
            Test("Grass negative rejects ball", delegate
            {
                using (var image = new Mat(360, 640, MatType.CV_8UC3, new Scalar(20, 150, 30)))
                using (var vision = new VisionEngine())
                using (var f = new Frame(image.Clone(), null, 0, 1, 640, 360, new Rect(0, 0, 640, 360)))
                {
                    Check(vision.Detect(f, new Settings(), false).Length == 0, "false positive");
                }
            });
            Test("White field line rejected", delegate
            {
                using (var image = new Mat(360, 640, MatType.CV_8UC3, new Scalar(20, 150, 30)))
                using (var vision = new VisionEngine())
                {
                    Cv2.Line(image, new Point(20, 180), new Point(620, 180), Scalar.All(250), 5);
                    using (var f = new Frame(image.Clone(), null, 0, 1, 640, 360, new Rect(0, 0, 640, 360)))
                        Check(vision.Detect(f, new Settings(), false).Length == 0, "line false positive");
                }
            });
            Test("Ball intersecting field line", delegate
            {
                using (var image = new Mat(360, 640, MatType.CV_8UC3, new Scalar(20, 150, 30)))
                using (var v = new VisionEngine())
                {
                    Cv2.Line(image, new Point(320, 15), new Point(320, 345), Scalar.All(230), 1);
                    Cv2.Circle(image, new Point(324, 160), 8, Scalar.All(245), -1);
                    Cv2.Circle(image, new Point(324, 160), 3, Scalar.All(30), -1);
                    using (var pool = new MatPool(3))
                    using (Frame f = Pixels.Resize(image, pool, 480, null, 0, 1))
                    {
                        bool found = false;
                        foreach (var d in v.Detect(f, new Settings(), true))
                            if (d.Type == EntityType.BALL && Vec.Distance(d.Position, new Vec(324, 160)) < 10)
                                found = true;
                        Check(found, "line-merged ball");
                    }
                }
            });
            Test("ROI coordinate conversion", delegate
            {
                using (var f = new Frame(new Mat(100, 100, MatType.CV_8UC3), null, 0, 1, 1280, 720, new Rect(200, 300, 400, 400)))
                {
                    Vec p = f.ToScreen(25, 50);
                    Near(p.X, 300, .001);
                    Near(p.Y, 500, .001);
                }
            });
            Test("Velocity uses irregular timestamps", delegate
            {
                var t = new TemporalTracker();
                t.Update(new[] { Ball(100, 100) }, 0, 1, 640, 360, CameraMotion.Stable);
                t.Update(new[] { Ball(110, 100) }, .1, 2, 640, 360, CameraMotion.Stable);
                var s = t.Update(new[] { Ball(145, 100) }, .45, 3, 640, 360, CameraMotion.Stable);
                Near(s.Best(EntityType.BALL).Velocity.X, 100, 1);
            });
            Test("Occlusion retains ID and expires", delegate
            {
                var t = new TemporalTracker();
                int id = t.Update(new[] { Ball(100, 100) }, 0, 1, 640, 360, CameraMotion.Stable).Objects[0].Id;
                t.Update(new Detection[0], .2, 2, 640, 360, CameraMotion.Stable);
                GameState s = t.Update(new[] { Ball(102, 100) }, .4, 3, 640, 360, CameraMotion.Stable);
                Check(s.Best(EntityType.BALL).Id == id, "reacquisition");
                s = t.Update(new Detection[0], 2, 4, 640, 360, CameraMotion.Stable);
                Check(s.Objects.Length == 0, "expiry");
            });
            Test("Camera translation removed from velocity", delegate
            {
                var t = new TemporalTracker();
                t.Update(new[] { Ball(100, 100) }, 0, 1, 640, 360, CameraMotion.Stable);
                var s = t.Update(new[] { Ball(120, 110) }, .2, 2, 640, 360, new CameraMotion(new Vec(20, 10), .95, false));
                Near(s.Objects[0].Velocity.Length, 0, .001);
            });
            Test("Camera jump invalidates forecast", delegate
            {
                var t = new TemporalTracker();
                t.Update(new[] { Ball(100, 100) }, 0, 1, 640, 360, CameraMotion.Stable);
                var s = t.Update(new[] { Ball(120, 110) }, .2, 2, 640, 360, new CameraMotion(new Vec(20, 10), .1, true));
                Check(!new PredictionEngine().Predict(s, .3)[0].Valid, "jump");
            });
            Test("Bounded history and track capacity", delegate
            {
                var t = new TemporalTracker(3, 4);
                for (int i = 0; i < 100; i++)
                    t.Update(new[] { Ball(100, 100), Ball(200, 100), Ball(300, 100), Ball(400, 100) }, i * .1, i, 640, 360, CameraMotion.Stable);
                Check(t.Count <= 3 && t.HistoryCount <= 12, "limits");
            });
            Test("Ambiguous association lowers confidence", delegate
            {
                var t = new TemporalTracker();
                t.Update(new[] { Ball(100, 100) }, 0, 1, 640, 360, CameraMotion.Stable);
                var s = t.Update(new[] { Ball(95, 100), Ball(105, 100) }, .1, 2, 640, 360, CameraMotion.Stable);
                Check(s.Objects[0].Ambiguous && s.Objects[0].Confidence < .65, "ambiguity");
            });
            Test("Constant velocity prediction", delegate
            {
                var s = State(1, 0, Obj(1, EntityType.BALL, new Vec(10, 20), new Vec(30, -10), 1, .95));
                Forecast f = new PredictionEngine().Predict(s, .5)[0];
                Near(f.Position.X, 25, .001);
                Near(f.Position.Y, 15, .001);
                Check(f.Valid, "valid forecast");
            });
            Test("Observed forecast error", delegate
            {
                var p = new PredictionEngine();
                for (int i = 0; i < 12; i++)
                {
                    double t = i * .1;
                    var s = State(t, 0, Obj(1, EntityType.BALL, new Vec(100 + 20 * t, 100), new Vec(20, 0), t, .95));
                    p.ObserveAndQueue(s, p.Predict(s, .25));
                }

                Check(p.ErrorSamples >= 5, "samples");
                Near(p.Rmse, 0, .001);
            });
            Test("Abrupt direction lowers confidence", delegate
            {
                var p = new PredictionEngine();
                double a = p.Predict(State(0, 0, Obj(1, EntityType.BALL, new Vec(), new Vec(100, 0), 0, .95)), .3)[0].Confidence;
                double b = p.Predict(State(0, 0, Obj(1, EntityType.BALL, new Vec(), new Vec(-100, 0), 0, .95, true)), .3)[0].Confidence;
                Check(b < a * .5, "direction uncertainty");
            });
            Test("Intercept stationary and unreachable", delegate
            {
                double time;
                Vec point;
                Check(PredictionEngine.Intercept(new Vec(), new Vec(100, 0), new Vec(), 50, 3, out time, out point), "stationary");
                Near(time, 2, .001);
                Check(!PredictionEngine.Intercept(new Vec(), new Vec(100, 0), new Vec(100, 0), 50, 3, out time, out point), "unreachable");
            });
            Test("FSM missing low confidence and stale", delegate
            {
                var d = new DecisionEngine();
                Check(d.Decide(null, new Forecast[0], 0).Kind == IntentKind.WAIT, "missing");
                Check(d.Decide(Pair(0, .4), new Forecast[0], 0).Kind == IntentKind.WAIT, "confidence");
                Check(d.Decide(Pair(0, .95), new Forecast[0], 1).Kind == IntentKind.WAIT, "stale");
            });
            Test("FSM move shoot pass defend", delegate
            {
                var d = new DecisionEngine();
                Check(d.Decide(Pair(0, .95), new Forecast[0], 0).Kind == IntentKind.MOVE, "move");
                var ball = Obj(1, EntityType.BALL, new Vec(200, 100), new Vec(), 1, .95);
                var local = Obj(2, EntityType.LOCAL_PLAYER, new Vec(180, 100), new Vec(), 1, .95);
                var goal = Obj(3, EntityType.GOAL, new Vec(600, 100), new Vec(), 1, .95);
                Check(d.Decide(State(1, .9, ball, local, goal), new Forecast[0], 1).Kind == IntentKind.SHOOT, "shot");
                var ally = Obj(4, EntityType.ALLY, new Vec(350, 150), new Vec(), 2, .95);
                var opponent = Obj(5, EntityType.OPPONENT, new Vec(185, 110), new Vec(), 2, .95);
                Check(d.Decide(State(2, .9, ball, local, goal, ally, opponent), new Forecast[0], 2).Kind == IntentKind.PASS, "pass");
                ball = Obj(1, EntityType.BALL, new Vec(100, 100), new Vec(), 3, .95);
                Check(d.Decide(State(3, 0, ball, local, opponent), new Forecast[0], 3).Kind == IntentKind.DEFEND, "defend");
            });
            Test("Multikey macro cancellation", delegate
            {
                var clock = new FakeClock();
                var transport = new FakeTransport();
                using (var input = new InputScheduler(transport, clock, new Settings(), false))
                {
                    input.StartMacro(MacroStep.Parse("W+SHIFT:100; WAIT:50; MOUSELEFT:80"));
                    Check(transport.Keys.Count == 2, "simultaneous");
                    clock.Time = .11;
                    input.Tick();
                    Check(transport.Keys.Count == 0, "wait release");
                    clock.Time = .17;
                    input.Tick();
                    Check(transport.Keys.Contains(1), "mouse");
                    input.EmergencyStop();
                    Check(transport.Keys.Count == 0, "cancel");
                }
            });
            Test("Focus loss releases keys", delegate
            {
                var transport = new FakeTransport();
                using (var input = new InputScheduler(transport, new FakeClock(), new Settings(), false))
                {
                    input.StartMacro(MacroStep.Parse("W+SHIFT:500"));
                    transport.Focus = false;
                    input.Tick();
                    Check(transport.Keys.Count == 0 && input.HeldCount == 0, "release");
                }
            });
            Test("Expired action watchdog", delegate
            {
                var clock = new FakeClock();
                var transport = new FakeTransport();
                using (var input = new InputScheduler(transport, clock, new Settings(), false))
                {
                    input.Execute(new ActionIntent(IntentKind.MOVE, new Vec(), new Vec(1, 0), 0, .2, .9, 40, "test"));
                    Check(transport.Keys.Count > 0, "press");
                    clock.Time = .3;
                    input.Tick();
                    Check(transport.Keys.Count == 0, "lease timeout");
                }
            });
            Test("Transport error releases partially sent keys", delegate
            {
                var transport = new FakeTransport
                {
                    FailDown = true
                };
                using (var input = new InputScheduler(transport, new FakeClock(), new Settings(), false))
                {
                    try
                    {
                        input.StartMacro(MacroStep.Parse("W+SHIFT:100"));
                    }
                    catch (InvalidOperationException)
                    {
                    }

                    input.EmergencyStop();
                    Check(transport.Keys.Count == 0, "partial failure");
                }
            });
            Test("Legacy shortcuts preserved", delegate
            {
                Check(Hotkeys.Map(0x75) == "SHOT_A" && Hotkeys.Map(0x76) == "SHOT_B" && Hotkeys.Map(0x77) == "COMBO" && Hotkeys.Map(0x78) == "TOGGLE" && Hotkeys.Map(0x7B) == "STOP", "F6-F12");
                Check(MacroStep.Parse("Q:45; WAIT:120; MOUSELEFT:300").Length == 3, "legacy combo");
            });
            Test("Macro rejects excessive durations", delegate
            {
                bool rejected = false;
                try
                {
                    MacroStep.Parse("W:99999");
                }
                catch (ArgumentException)
                {
                    rejected = true;
                }

                Check(rejected, "duration");
            });
            Test("Settings snapshot independence", delegate
            {
                var s = new Settings
                {
                    ImageCorners = new[]
                    {
                        1d,
                        2d,
                        3d,
                        4d,
                        5d,
                        6d,
                        7d,
                        8d
                    }
                };
                var copy = s.Copy();
                copy.ImageCorners[0] = 9;
                Near(s.ImageCorners[0], 1, 0);
                s.Confidence = 8;
                s.Validate();
                Check(s.Confidence < 1, "validation");
            });
            Test("Homography calibrated rectangle", delegate
            {
                using (var p = new FieldProjector(new[] { 0d, 0d, 640d, 0d, 640d, 360d, 0d, 360d }, 100, 60))
                {
                    Vec v = p.Project(new Vec(320, 180));
                    Near(v.X, 50, .01);
                    Near(v.Y, 30, .01);
                }
            });
            Test("Camera optical flow translation from pixels", delegate
            {
                using (var estimator = new CameraEstimator())
                using (Mat a = new Mat(240, 320, MatType.CV_8UC3, new Scalar(20, 130, 30)))
                using (Mat b = new Mat())
                {
                    for (int y = 20; y < 220; y += 25)
                        for (int x = 20; x < 300; x += 27)
                            Cv2.Rectangle(a, new Rect(x, y, 7, 7), Scalar.All((x + y) % 100 + 140), -1);
                    using (Mat transform = Mat.FromPixelData(2, 3, MatType.CV_64FC1, new double[] { 1, 0, 6, 0, 1, 4 }))
                        Cv2.WarpAffine(a, b, transform, a.Size());
                    using (var f1 = new Frame(a.Clone(), null, 0, 1, 320, 240, new Rect(0, 0, 320, 240)))
                    using (var f2 = new Frame(b.Clone(), null, .2, 2, 320, 240, new Rect(0, 0, 320, 240)))
                    {
                        estimator.Estimate(f1, true);
                        CameraMotion motion = estimator.Estimate(f2, true);
                        Near(motion.Translation.X, 6, 1);
                        Near(motion.Translation.Y, 4, 1);
                        Check(!motion.Discontinuity, "flow confidence");
                    }
                }
            });
            Test("Out-of-order timestamp does not change velocity", delegate
            {
                var tracker = new TemporalTracker();
                tracker.Update(new[] { Ball(100, 100) }, 1, 1, 640, 360, CameraMotion.Stable);
                var current = tracker.Update(new[] { Ball(110, 100) }, 1.1, 2, 640, 360, CameraMotion.Stable);
                var stale = tracker.Update(new[] { Ball(600, 100) }, 1.05, 3, 640, 360, CameraMotion.Stable);
                Near(current.Objects[0].Velocity.X, stale.Objects[0].Velocity.X, .001);
            });
            Test("Configurable independent rates", delegate
            {
                var s = new Settings
                {
                    CaptureFps = 3,
                    DecisionFps = 22,
                    ProcessingWidth = 560
                };
                var profile = Profile.For(s);
                Check(profile.PerceptionFps == 3 && profile.DecisionFps == 22 && profile.Width == 560, "overrides");
            });
            Test("Image replay respects recorded timeline", delegate
            {
                string folder = Path.Combine(root, "reports", "timeline-test");
                Directory.CreateDirectory(folder);
                File.Copy(Path.Combine(root, "Tests", "Fixtures", "06s.png"), Path.Combine(folder, "a.png"), true);
                File.Copy(Path.Combine(root, "Tests", "Fixtures", "24s.png"), Path.Combine(folder, "b.png"), true);
                File.WriteAllText(Path.Combine(folder, "timeline.csv"), "file,seconds\na.png,6\nb.png,24\n");
                var clock = new FakeClock
                {
                    Time = 100
                };
                using (var source = new ImageSequenceSource(folder, clock, false))
                using (var pool = new MatPool(3))
                {
                    using (Frame first = source.Read(pool, 480, null))
                        Near(first.Timestamp, 100, .001);
                    Check(source.Read(pool, 480, null) == null, "must wait for media time");
                    clock.Time = 118;
                    using (Frame second = source.Read(pool, 480, null))
                        Near(second.Timestamp, 118, .001);
                    Check(source.Completed, "sequence complete");
                }
            });
            Test("XML serialization preserves macro and vision settings", delegate
            {
                var serializer = new System.Xml.Serialization.XmlSerializer(typeof(Settings));
                var original = new Settings
                {
                    ShotA = 225,
                    ShotB = 650,
                    Combo = "W+SHIFT:200; WAIT:50; MOUSELEFT:90",
                    CaptureFps = 7,
                    Horizon = .6,
                    CalibratedLocal = true
                };
                using (var stream = new MemoryStream())
                {
                    serializer.Serialize(stream, original);
                    stream.Position = 0;
                    var loaded = (Settings)serializer.Deserialize(stream);
                    Check(loaded.Combo == original.Combo && loaded.ShotA == 225 && loaded.CaptureFps == 7 && loaded.CalibratedLocal, "round trip");
                    Near(loaded.Horizon, .6, .001);
                }
            });
            Test("No player detection without calibration", delegate
            {
                using (var image = new Mat(240, 320, MatType.CV_8UC3, new Scalar(20, 150, 30)))
                using (var f = new Frame(image.Clone(), null, 0, 1, 320, 240, new Rect(0, 0, 320, 240)))
                using (var v = new VisionEngine())
                {
                    Check(v.Detect(f, new Settings(), false).Length == 0, "no invented identity");
                    var config = new Settings
                    {
                        CalibratedLocal = true
                    };
                    var d = v.Detect(f, config, false);
                    Check(d.Length == 1 && !d[0].Measured, "reference flagged");
                }
            });
            Test("Template path participates in detection", delegate
            {
                using (Mat raw = Cv2.ImRead(Path.Combine(root, "Tests", "Fixtures", "24s.png")))
                using (var pool = new MatPool(3))
                using (Frame frame = Pixels.Resize(raw, pool, 640, null, 0, 1))
                using (var v = new VisionEngine())
                {
                    v.SetTemplate(Path.Combine(root, "Tests", "Fixtures", "ball-template.png"));
                    var detections = v.Detect(frame, new Settings(), false);
                    bool found = false;
                    foreach (var d in detections)
                        if (Vec.Distance(d.Position, new Vec(625, 308)) < 20)
                            found = true;
                    Check(found, "template supported ball");
                }
            });
            Test("Negative real fixture with ball removed", delegate
            {
                using (Mat raw = Cv2.ImRead(Path.Combine(root, "Tests", "Fixtures", "screenshot.png")))
                using (var pool = new MatPool(3))
                using (var v = new VisionEngine())
                {
                    using (Mat grass = new Mat(raw, new Rect(1130, 500, 45, 45)))
                    using (Mat target = new Mat(raw, new Rect(1075, 502, 45, 45)))
                        grass.CopyTo(target);
                    using (Frame frame = Pixels.Resize(raw, pool, 640, null, 0, 1))
                    {
                        var detections = v.Detect(frame, new Settings(), false);
                        foreach (var d in detections)
                            Check(Vec.Distance(d.Position, new Vec(1097, 525)) > 40, "ball falsely retained");
                    }
                }
            });
            Test("Vision disabled suspends capture work", delegate
            {
                var clock = new MonotonicClock();
                var sim = new Simulation(clock);
                using (var p = new Pipeline(new Settings { Vision = false, Overlay = false }, sim, sim, clock))
                {
                    p.Start();
                    Thread.Sleep(350);
                    Check(p.Metrics.Captured == 0 && p.Metrics.Processed == 0, "module disabled");
                }
            });
            Test("Assisted mode never executes movement", delegate
            {
                var clock = new MonotonicClock();
                var sim = new Simulation(clock);
                using (var p = new Pipeline(new Settings { Mode = RunMode.ASSISTIDO, Overlay = false }, sim, sim, clock))
                {
                    Vec original = sim.Player;
                    p.Start();
                    Thread.Sleep(850);
                    Near(Vec.Distance(original, sim.Player), 0, .01);
                    Check(sim.Moves == 0, "recommendations only");
                }
            });
            Test("Closed loop pixels movement shot feedback", ClosedLoop);
            Test("Emergency serializes with an in-flight action", delegate
            {
                var clock = new MonotonicClock();
                var sim = new Simulation(clock);
                var blocked = new BlockingExecutor();
                using (var p = new Pipeline(new Settings { Mode = RunMode.AUTONOMO_TESTE, Overlay = false }, sim, blocked, clock))
                {
                    p.Start();
                    Check(blocked.Entered.WaitOne(4000), "action must enter dispatch");
                    var emergency = new Thread(p.EmergencyStop);
                    emergency.Start();
                    Stopwatch limit = Stopwatch.StartNew();
                    while (!p.EmergencyLatched && limit.Elapsed.TotalSeconds < 1)
                        Thread.Sleep(1);
                    Check(p.EmergencyLatched, "cancellation requested");
                    blocked.Continue.Set();
                    Check(emergency.Join(1500), "emergency must complete after in-flight call");
                    Check(!blocked.Active, "in-flight action must be released after dispatch finishes");
                    Thread.Sleep(150);
                    Check(!blocked.Active && p.Intent.Kind == IntentKind.WAIT, "no rearm after emergency");
                }
            });
            Test("Emergency before start remains latched", delegate
            {
                var clock = new MonotonicClock();
                var sim = new Simulation(clock);
                using (var p = new Pipeline(new Settings { Mode = RunMode.AUTONOMO_TESTE, Overlay = false }, sim, sim, clock))
                {
                    p.EmergencyStop();
                    p.Start();
                    Thread.Sleep(650);
                    Check(p.EmergencyLatched && sim.Moves == 0, "startup must not reset cancellation");
                }
            });
            Test("Threaded pipeline emergency and bounded resources", delegate
            {
                var clock = new MonotonicClock();
                var sim = new Simulation(clock);
                var settings = new Settings
                {
                    Mode = RunMode.AUTONOMO_TESTE,
                    Overlay = false
                };
                using (var p = new Pipeline(settings, sim, sim, clock))
                {
                    p.Start();
                    Stopwatch deadline = Stopwatch.StartNew();
                    while (p.Metrics.Processed < 4 && deadline.Elapsed.TotalSeconds < 4)
                        Thread.Sleep(20);
                    Check(p.Metrics.Processed >= 4, "processing");
                    Check(p.Metrics.Decisions > p.Metrics.Processed, "independent decisions");
                    p.EmergencyStop();
                    Vec old = sim.Player;
                    Thread.Sleep(450);
                    Check(Vec.Distance(old, sim.Player) < 1, "emergency latch");
                    Check(p.PeakFrames <= 3 && p.Metrics.Failures == 0, "bounded and no errors");
                }
            });
        }

        static void RealDetection(string path, Vec? expected, double tolerance, int width = 640)
        {
            Check(File.Exists(path), "Fixture absent: " + path);
            using (Mat raw = Cv2.ImRead(path))
            using (var pool = new MatPool(3))
            using (Frame frame = Pixels.Resize(raw, pool, width, null, 0, 1))
            using (var vision = new VisionEngine())
            {
                Detection[] result = vision.Detect(frame, new Settings(), false);
                Detection ball = null;
                foreach (var d in result)
                    if (d.Type == EntityType.BALL && (ball == null || d.Confidence > ball.Confidence))
                        ball = d;
                Check(ball != null, "ball not detected; candidates " + vision.Candidates);
                if (expected.HasValue)
                    Check(Vec.Distance(ball.Position, expected.Value) < tolerance, "wrong location " + ball.Position);
                Line("fixture " + Path.GetFileName(path) + " -> " + ball.Position + " confidence " + ball.Confidence.ToString("0.000"));
                using (Mat annotated = raw.Clone())
                {
                    int radius = (int)Math.Ceiling(ball.Radius);
                    Cv2.Rectangle(annotated, new Rect((int)ball.Position.X - radius, (int)ball.Position.Y - radius, radius * 2, radius * 2), new Scalar(0, 255, 255), 2);
                    Cv2.PutText(annotated, "BALL score " + ball.Confidence.ToString("0.00"), new Point(ball.Position.X - radius, ball.Position.Y - radius - 8), HersheyFonts.HersheySimplex, .55, new Scalar(0, 255, 255), 1);
                    Cv2.ImWrite(Path.Combine(root, "reports", "detected-" + width + "-" + Path.GetFileName(path)), annotated);
                }
            }
        }

        static void ClosedLoop()
        {
            Loop(.1);
            Loop(.2);
        }

        static void Loop(double interval)
        {
            var clock = new FakeClock();
            using (var sim = new Simulation(clock))
            using (var pool = new MatPool(3))
            using (var vision = new VisionEngine())
            {
                var tracker = new TemporalTracker();
                var predictor = new PredictionEngine();
                var decision = new DecisionEngine();
                bool feedback = false;
                for (int i = 0; i < 150; i++)
                {
                    clock.Time = i * interval;
                    using (Frame f = sim.Read(pool, 480, null))
                    {
                        var state = tracker.Update(vision.Detect(f, new Settings(), true), clock.Now, i, 640, 360, CameraMotion.Stable);
                        var forecasts = predictor.Predict(state, .35);
                        predictor.ObserveAndQueue(state, forecasts);
                        sim.Execute(decision.Decide(state, forecasts, clock.Now));
                        if ((decision.Feedback ?? "").Contains("após chute"))
                            feedback = true;
                    }
                }

                Line("closed loop dt=" + interval + ": travelled=" + sim.Travelled.ToString("0.0") + " px, shots=" + sim.Shots + ", feedback=" + feedback + ", prediction RMSE=" + predictor.Rmse.ToString("0.00") + " px / " + predictor.ErrorSamples + " samples");
                Check(sim.Travelled > 100 && sim.Shots > 0 && feedback, "complete loop");
            }
        }

        static void Benchmark(int seconds)
        {
            Line("profile,seconds,captured,processed,decisions,dropped,private_mb,private_delta_mb,working_mb,cpu_mean_pct,cpu_peak_pct,vision_p95_ms,latency_p95_ms,prediction_rmse_px,error_samples,ball_rmse_px,ball_error_samples,peak_private_mb,quality_level,failures");
            foreach (PerformanceProfile profile in Enum.GetValues(typeof(PerformanceProfile)))
            {
                var clock = new MonotonicClock();
                var sim = new Simulation(clock);
                var settings = new Settings
                {
                    Mode = RunMode.AUTONOMO_TESTE,
                    Profile = profile,
                    Overlay = false,
                    Adaptive = true
                };
                using (var pipeline = new Pipeline(settings, sim, sim, clock))
                {
                    pipeline.Start();
                    double sum = 0, peak = 0;
                    int samples = 0;
                    for (int i = 0; i < seconds * 2; i++)
                    {
                        Thread.Sleep(500);
                        pipeline.Metrics.Sample();
                        sum += pipeline.Metrics.CpuPercent;
                        peak = Math.Max(peak, pipeline.Metrics.CpuPercent);
                        samples++;
                    }

                    var m = pipeline.Metrics;
                    Line(String.Join(",", new[] { profile.ToString(), clock.Now.ToString("0.00"), m.Captured.ToString(), m.Processed.ToString(), m.Decisions.ToString(), pipeline.Dropped.ToString(), m.PrivateMb.ToString("0.00"), (m.PrivateMb - m.BaselinePrivate / 1048576.0).ToString("0.00"), m.WorkingMb.ToString("0.00"), (sum / samples).ToString("0.00"), peak.ToString("0.00"), m.Vision.Percentile(.95).ToString("0.000"), m.Latency.Percentile(.95).ToString("0.000"), pipeline.Predictor.Rmse.ToString("0.00"), pipeline.Predictor.ErrorSamples.ToString(), pipeline.Predictor.BallRmse.ToString("0.00"), pipeline.Predictor.BallErrorSamples.ToString(), m.PeakPrivateMb.ToString("0.00"), pipeline.QualityLevel.ToString(), m.Failures.ToString() }));
                }
            }

            File.WriteAllLines(Path.Combine(root, "reports", "benchmark.csv"), output);
            File.WriteAllText(Path.Combine(root, "reports", "opencv-build.txt"), Cv2.GetBuildInformation());
        }

        static void BenchmarkImage(int seconds)
        {
            var clock = new MonotonicClock();
            var image = new CachedImageSource(Path.Combine(root, "Tests", "Fixtures", "screenshot.png"), clock);
            using (var pipeline = new Pipeline(new Settings { Mode = RunMode.ASSISTIDO, Overlay = false, Profile = PerformanceProfile.ECONOMICO }, image, new NullExecutor(), clock))
            {
                pipeline.Start();
                double cpu = 0;
                int samples = 0;
                for (int i = 0; i < seconds * 2; i++)
                {
                    Thread.Sleep(500);
                    pipeline.Metrics.Sample();
                    cpu += pipeline.Metrics.CpuPercent;
                    samples++;
                }

                var m = pipeline.Metrics;
                Line("source,seconds,processed,fps,private_mb,peak_private_mb,working_mb,cpu_mean_pct,vision_p95_ms,latency_p95_ms,failures");
                Line(String.Join(",", new[] { "recorded_screenshot_1916x1007", clock.Now.ToString("0.00"), m.Processed.ToString(), pipeline.ProcessingFps.ToString("0.00"), m.PrivateMb.ToString("0.00"), m.PeakPrivateMb.ToString("0.00"), m.WorkingMb.ToString("0.00"), (cpu / samples).ToString("0.00"), m.Vision.Percentile(.95).ToString("0.000"), m.Latency.Percentile(.95).ToString("0.000"), m.Failures.ToString() }));
                File.WriteAllLines(Path.Combine(root, "reports", "benchmark-image.csv"), output);
            }
        }

        static void Video(string path)
        {
            var clock = new FakeClock();
            using (var video = new VideoSource(path, clock))
            using (var pool = new MatPool(3))
            using (var vision = new VisionEngine())
            using (var camera = new CameraEstimator())
            {
                var tracker = new TemporalTracker();
                var predictor = new PredictionEngine();
                int frames = 0, balls = 0;
                Stopwatch watch = Stopwatch.StartNew();
                while (!video.Completed && clock.Time < 40)
                {
                    using (Frame f = video.Read(pool, 640, null))
                    {
                        if (f != null)
                        {
                            var det = vision.Detect(f, new Settings(), false);
                            foreach (var d in det)
                                if (d.Type == EntityType.BALL)
                                {
                                    balls++;
                                    break;
                                }

                            var s = tracker.Update(det, f.Timestamp, f.Id, f.Width, f.Height, camera.Estimate(f, true));
                            predictor.ObserveAndQueue(s, predictor.Predict(s, .35));
                            frames++;
                        }
                    }

                    clock.Time += .2;
                }

                Line("Replay: frames=" + frames + ", frames com candidatos BALL=" + balls + ", RMSE=" + (predictor.ErrorSamples == 0 ? "n/d" : predictor.Rmse.ToString("0.00")) + " px, amostras=" + predictor.ErrorSamples + ", processamento=" + watch.Elapsed.TotalSeconds.ToString("0.00") + "s. Sem ground truth manual: não é precisão de detecção.");
                File.WriteAllLines(Path.Combine(root, "reports", "video.txt"), output);
            }
        }
    }
}
