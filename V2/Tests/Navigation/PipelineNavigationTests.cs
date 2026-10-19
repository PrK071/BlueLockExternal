// C# | Tests/Navigation/PipelineNavigationTests.cs | .NET Framework 4.8 x64 | Threaded pipeline dispatch without capture and emergency integration.
using System;
using System.Threading;
using OpenCvSharp;
using BlueLockV2.Capture;
using BlueLockV2.Core;
using BlueLockV2.Configuration;
using BlueLockV2.Navigation;

namespace BlueLockV2.NavigationAcceptance
{
    sealed class StudioSourceStub : IFrameSource, INavigationSource
    {
        readonly IClock clock;
        public StudioSourceStub(IClock clock) { this.clock = clock; }
        public NavigationSample Latest { get { double now = clock.Now; return new NavigationSample { Time = now, Sequence = (long)(now * 1000), Player = new Vec(), Target = new Vec(20,0),
            Confidence = 1, CameraConfidence = 1, Basis = MotionBasis.CameraXZ(new Vec(1,0), new Vec(0,-1),16,1), Space = NavigationSpace.WorldXZ }; } }
        public SourceKind Kind { get { return SourceKind.StudioWindow; } }
        public bool Completed { get { return false; } }
        public string Description { get { return "authorized telemetry test fixture"; } }
        public Frame Read(MatPool pool, int width, Rect? roi) { throw new Exception("Vision disabled must suspend capture"); }
        public void Dispose() { }
    }
    sealed class RecordingExecutor : IActionExecutor
    {
        public int Moves, Stops;
        public void Execute(ActionIntent action) { if (action.Kind == IntentKind.MOVE) Interlocked.Increment(ref Moves); }
        public void EmergencyStop() { Interlocked.Increment(ref Stops); }
        public void Dispose() { }
    }
    static class PipelineNavigationTests
    {
        public static void Run(Action<string, Action> test)
        {
            test("threaded Studio pipeline dispatch and F12 latch without capture", delegate
            {
                var clock = new MonotonicClock(); var source = new StudioSourceStub(clock); var executor = new RecordingExecutor();
                using (var pipeline = new Pipeline(new Settings { Mode = RunMode.NAVEGACAO_STUDIO, Vision = false, Sprint = false }, source, executor, clock, source))
                {
                    pipeline.Start();
                    for (int i = 0; i < 100 && Volatile.Read(ref executor.Moves) == 0; i++) Thread.Sleep(10);
                    if (executor.Moves == 0) throw new Exception("no navigation intent dispatched");
                    pipeline.EmergencyStop(); int sent = Volatile.Read(ref executor.Moves);
                    Thread.Sleep(150);
                    if (executor.Moves != sent || executor.Stops == 0 || pipeline.Metrics.Failures != 0) throw new Exception("emergency/capture integration failure");
                }
            });
        }
    }
}
