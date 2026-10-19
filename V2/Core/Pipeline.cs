// C# | Core/Pipeline.cs | .NET Framework 4.8 x64 | Independent capture, perception and decision workers.
using System;
using System.Diagnostics;
using System.Drawing;
using System.Threading;
using OpenCvSharp;
using BlueLockV2.Capture;
using BlueLockV2.Configuration;
using BlueLockV2.Vision;
using BlueLockV2.Tracking;
using BlueLockV2.Prediction;
using BlueLockV2.Decision;
using BlueLockV2.Diagnostics;
using BlueLockV2.Navigation;

namespace BlueLockV2.Core
{
    public sealed class Observation
    {
        public readonly GameState State;
        public readonly Forecast[] Forecasts;
        public Observation(GameState state, Forecast[] forecasts)
        {
            State = state;
            Forecasts = forecasts;
        }
    }

    public sealed class Pipeline : IDisposable
    {
        readonly Settings settings;
        readonly IClock clock;
        readonly IFrameSource source;
        readonly IActionExecutor executor;
        readonly LatestSlot<Frame> frames = new LatestSlot<Frame>();
        readonly MatPool pool = new MatPool(3);
        readonly ManualResetEvent stop = new ManualResetEvent(false);
        readonly VisionEngine vision = new VisionEngine();
        readonly CameraEstimator camera = new CameraEstimator();
        readonly TemporalTracker tracker;
        readonly PredictionEngine predictor = new PredictionEngine();
        readonly DecisionEngine decision = new DecisionEngine();
        readonly VisualPilotDecision visualPilot;
        readonly NavigationController navigation;
        readonly INavigationSource navigationSource;
        readonly NavigationLog navigationLog;
        public NavigationTrace NavigationTrace { get { return navigation == null ? null : navigation.Trace; } }
        public string NavigationLogPath { get { return navigationLog == null ? null : navigationLog.Path; } }
        readonly Thread captureThread, visionThread, decisionThread;
        readonly object previewGate = new object ();
        readonly object executionGate = new object ();
        Bitmap preview;
        Observation latest;
        ActionIntent intent;
        int started, closed, emergency, quality;
        long lastLog = -1;
        double lastPreview;
        readonly double createdAt;
        public readonly Telemetry Metrics = new Telemetry();
        readonly StructuredLog log;
        public Pipeline(Settings configuration, IFrameSource frameSource, IActionExecutor actionExecutor, IClock time, INavigationSource studioNavigation = null)
        {
            createdAt = time.Now;
            settings = configuration.Copy();
            settings.Validate();
            if (settings.Mode == RunMode.NAVEGACAO_STUDIO || (settings.Mode == RunMode.AUTONOMO_TESTE && frameSource.Kind == SourceKind.StudioWindow))
            {
                navigation = new NavigationController(true);
                navigationSource = studioNavigation;
                navigationLog = new NavigationLog(System.IO.Path.Combine(SettingsStore.Folder, "navigation"));
            }
            if (settings.Mode == RunMode.PILOTO_ROBLOX)
            {
                visualPilot = new VisualPilotDecision(settings);
                // Keep constant image scale: a cropped ball ROI previously changed detection scale every few frames.
                settings.UseRoi = false;
            }

            source = frameSource;
            executor = actionExecutor;
            clock = time;
            tracker = new TemporalTracker(settings.MaxTracks, settings.HistoryLength);
            Cv2.SetNumThreads(settings.OpenCvThreads);
            captureThread = Worker(CaptureLoop, "Capture");
            visionThread = Worker(VisionLoop, "Perception");
            decisionThread = Worker(DecisionLoop, "Decision");
            log = new StructuredLog(System.IO.Path.Combine(SettingsStore.Folder, "pipeline.jsonl"));
        }

        static Thread Worker(ThreadStart work, string name)
        {
            Thread thread = new Thread(work);
            thread.Name = "BLV2 " + name;
            thread.IsBackground = true;
            thread.Priority = ThreadPriority.BelowNormal;
            return thread;
        }

        public Observation Latest
        {
            get
            {
                return Volatile.Read(ref latest);
            }
        }

        public ActionIntent Intent
        {
            get
            {
                return Volatile.Read(ref intent);
            }
        }

        public PlayState State
        {
            get
            {
                if (navigation != null)
                    return navigation.Trace != null && navigation.Trace.State == "PULSE" ? PlayState.MOVING_TO_TARGET : PlayState.UNCERTAIN;
                return visualPilot == null ? decision.State : visualPilot.State;
            }
        }

        public long Dropped
        {
            get
            {
                return frames.Dropped;
            }
        }

        public int PeakFrames
        {
            get
            {
                return pool.Peak;
            }
        }

        public int QualityLevel
        {
            get
            {
                return quality;
            }
        }

        public bool Completed
        {
            get
            {
                return source.Completed;
            }
        }

        public double ProcessingFps
        {
            get
            {
                return Metrics.Processed / Math.Max(.001, clock.Now - createdAt);
            }
        }

        public PredictionEngine Predictor
        {
            get
            {
                return predictor;
            }
        }

        public string Feedback
        {
            get
            {
                return navigation != null && navigation.Trace != null ? navigation.Trace.State + ": " + navigation.Trace.Reason : visualPilot == null ? decision.Feedback : visualPilot.Feedback;
            }
        }

        public void Start()
        {
            if (Interlocked.Exchange(ref started, 1) != 0)
                return;
            log.Write("pipeline", "start", settings.Mode + " / " + source.Kind);
            captureThread.Start();
            visionThread.Start();
            decisionThread.Start();
        }

        public void EmergencyStop()
        {
            Interlocked.Exchange(ref emergency, 1);
            lock (executionGate)
            {
                if (navigation != null) navigation.EmergencyStop();
                executor.EmergencyStop();
                Volatile.Write(ref intent, ActionIntent.Wait(clock.Now, "Interrupção F12"));
            }

            log.Write("input", "emergency", "Entradas liberadas; execução bloqueada até reiniciar.");
        }

        public bool EmergencyLatched
        {
            get
            {
                return Volatile.Read(ref emergency) != 0;
            }
        }

        public Bitmap TakePreview()
        {
            lock (previewGate)
            {
                Bitmap result = preview;
                preview = null;
                return result;
            }
        }

        public bool TryGetLiveBounds(out Rectangle bounds)
        {
            bounds = Rectangle.Empty;
            var window = source as WindowSource;
            return window != null && window.TryGetFocusedBounds(out bounds);
        }

        public void SetTemplate(string path)
        {
            if (started != 0)
                throw new InvalidOperationException("Configure o template antes de iniciar.");
            vision.SetTemplate(path);
        }

        Rect? Roi(double now)
        {
            if (!settings.UseRoi || source.Kind == SourceKind.Simulation || source.Kind == SourceKind.Images || source.Kind == SourceKind.Video || ((int)(now * 5) % 10) == 0)
                return null;
            Observation snapshot = Latest;
            if (snapshot == null)
                return null;
            TrackedObject ball = snapshot.State.Best(EntityType.BALL);
            if (ball == null || ball.Confidence < .7 || now - ball.LastSeen > .4)
                return null;
            int size = Math.Max(240, (int)(ball.Radius * 12));
            int x = (int)ball.Position.X - size / 2, y = (int)ball.Position.Y - size / 2;
            return new Rect(Math.Max(0, x), Math.Max(0, y), Math.Min(size, snapshot.State.Width - Math.Max(0, x)), Math.Min(size, snapshot.State.Height - Math.Max(0, y)));
        }

        void CaptureLoop()
        {
            Profile profile = Profile.For(settings);
            double interval = 1.0 / profile.PerceptionFps;
            while (!stop.WaitOne(0))
            {
                double began = clock.Now;
                try
                {
                    Metrics.Sample();
                    if (settings.Adaptive)
                    {
                        bool overload = Metrics.CpuPercent > settings.SoftCpuPercent || Metrics.Latency.Percentile(.95) > interval * 850;
                        if (overload)
                            quality = Math.Min(2, quality + 1);
                        else if (Metrics.CpuPercent < settings.SoftCpuPercent * .5 && Metrics.Latency.Percentile(.95) < interval * 400)
                            quality = Math.Max(0, quality - 1);
                    }

                    if (settings.Vision && settings.Mode != RunMode.MANUAL)
                    {
                        // Tiny distant balls lose their black/white pattern below this resolution in the live pilot.
                        int minimumWidth = visualPilot == null ? 320 : 640;
                        Frame frame = source.Read(pool, Math.Max(minimumWidth, profile.Width / (quality + 1)), Roi(began));
                        if (frame != null)
                        {
                            Interlocked.Increment(ref Metrics.Captured);
                            frames.Put(frame);
                        }
                    }

                    Metrics.Capture.Add((clock.Now - began) * 1000);
                }
                catch (Exception e)
                {
                    Failure("capture", e);
                }

                int wait = Math.Max(1, (int)((interval * (1 + quality * .5) - (clock.Now - began)) * 1000));
                if (stop.WaitOne(wait))
                    break;
            }
        }

        void VisionLoop()
        {
            WaitHandle[] waits =
            {
                stop,
                frames.Ready
            };
            while (WaitHandle.WaitAny(waits) != 0)
            {
                Frame frame = frames.Take();
                if (frame == null)
                    continue;
                using (frame)
                {
                    try
                    {
                        double began = clock.Now;
                        if (began - frame.Timestamp > settings.MaxFrameAge)
                        {
                            Interlocked.Increment(ref Metrics.Stale);
                            continue;
                        }

                        CameraMotion motion = camera.Estimate(frame, settings.CameraCompensation && source.Kind != SourceKind.Simulation);
                        Detection[] detections = vision.Detect(frame, settings, source.Kind == SourceKind.Simulation);
                        Metrics.Vision.Add((clock.Now - began) * 1000);
                        double stage = clock.Now;
                        GameState state = tracker.Update(detections, frame.Timestamp, frame.Id, frame.Width, frame.Height, motion);
                        Metrics.Tracking.Add((clock.Now - stage) * 1000);
                        stage = clock.Now;
                        Forecast[] forecasts = settings.Prediction ? predictor.Predict(state, settings.Horizon) : new Forecast[0];
                        predictor.ObserveAndQueue(state, forecasts);
                        Metrics.Prediction.Add((clock.Now - stage) * 1000);
                        Volatile.Write(ref latest, new Observation(state, forecasts));
                        Interlocked.Increment(ref Metrics.Processed);
                        Metrics.Latency.Add((clock.Now - frame.Timestamp) * 1000);
                        double previewInterval = settings.Profile == PerformanceProfile.ECONOMICO ? 1.0 : settings.Profile == PerformanceProfile.EQUILIBRADO ? .5 : .25;
                        if (settings.Overlay && clock.Now - lastPreview > previewInterval)
                        {
                            lastPreview = clock.Now;
                            Bitmap image = Render(frame, state, forecasts);
                            lock (previewGate)
                            {
                                if (preview != null)
                                    preview.Dispose();
                                preview = image;
                            }
                        }
                    }
                    catch (Exception e)
                    {
                        Failure("vision", e);
                    }
                }
            }
        }

        Bitmap Render(Frame frame, GameState state, Forecast[] forecasts)
        {
            Bitmap image = Pixels.Bitmap(frame.Image);
            using (Graphics g = Graphics.FromImage(image))
            using (Font font = new Font("Consolas", 8))
            {
                foreach (TrackedObject obj in state.Objects)
                {
                    if (obj.Lost)
                        continue;
                    float x = (float)((obj.Position.X - frame.Region.X) / frame.Scale), y = (float)((obj.Position.Y - frame.Region.Y) / frame.Scale), r = (float)Math.Max(4, obj.Radius / frame.Scale);
                    g.DrawRectangle(Pens.Yellow, x - r, y - r, r * 2, r * 2);
                    g.DrawLine(Pens.Red, x - 3, y, x + 3, y);
                    g.DrawLine(Pens.Red, x, y - 3, x, y + 3);
                    g.DrawString(obj.Type + " #" + obj.Id + " " + obj.Confidence.ToString("0.00"), font, Brushes.White, x - r, y - r - 12);
                }

                foreach (Forecast f in forecasts)
                    if (f.Valid)
                    {
                        float x = (float)((f.Position.X - frame.Region.X) / frame.Scale), y = (float)((f.Position.Y - frame.Region.Y) / frame.Scale);
                        g.DrawEllipse(Pens.Cyan, x - 4, y - 4, 8, 8);
                    }

                if (settings.BallAntenna)
                    BallAnnotation.Draw(g, state, image.Size, new Vec(frame.Region.X, frame.Region.Y), (double)frame.Image.Width / frame.Region.Width, (double)frame.Image.Height / frame.Region.Height);
                g.DrawString(ProcessingFps.ToString("0.0") + " FPS | CV " + Metrics.Vision.Percentile(.95).ToString("0.0") + " ms | fila " + Dropped, font, Brushes.White, 4, 4);
            }

            return image;
        }

        void DecisionLoop()
        {
            int wait = 1000 / Profile.For(settings).DecisionFps;
            while (!stop.WaitOne(0))
            {
                try
                {
                    double began = clock.Now;
                    Observation snapshot = Latest;
                    IDecisionEngine engine = visualPilot == null ? (IDecisionEngine)decision : visualPilot;
                    ActionIntent action = navigation == null ? engine.Decide(snapshot == null ? null : snapshot.State, snapshot == null ? new Forecast[0] : snapshot.Forecasts, began) :
                        navigation.Update(navigationSource == null ? null : navigationSource.Latest, began);
                    lock (executionGate)
                    {
                        if (Volatile.Read(ref emergency) != 0)
                            action = ActionIntent.Wait(began, "Execução interrompida; reinicie para rearmar.");
                        Volatile.Write(ref intent, action);
                        Interlocked.Increment(ref Metrics.Decisions);
                        if ((settings.Mode == RunMode.AUTONOMO_TESTE || settings.Mode == RunMode.PILOTO_ROBLOX || settings.Mode == RunMode.NAVEGACAO_STUDIO) && Volatile.Read(ref emergency) == 0)
                        {
                            if ((settings.Mode == RunMode.AUTONOMO_TESTE && (source.Kind == SourceKind.Simulation || source.Kind == SourceKind.StudioWindow)) || (settings.Mode == RunMode.PILOTO_ROBLOX && source.Kind == SourceKind.RobloxWindow) ||
                                (settings.Mode == RunMode.NAVEGACAO_STUDIO && source.Kind == SourceKind.StudioWindow))
                                executor.Execute(action);
                            else
                                executor.EmergencyStop();
                        }
                        if (navigationLog != null)
                        {
                            var input = executor as BlueLockV2.Input.InputScheduler;
                            navigationLog.Write(navigation.Trace, input == null ? "simulation" : input.HeldKeys, State.ToString());
                        }
                    }

                    if (snapshot != null && snapshot.State.FrameId != lastLog)
                    {
                        lastLog = snapshot.State.FrameId;
                        log.Write("decision", action.Kind.ToString(), State + " | " + action.Reason);
                    }

                    Metrics.Decision.Add((clock.Now - began) * 1000);
                }
                catch (Exception e)
                {
                    Failure("decision", e);
                }

                if (stop.WaitOne(wait))
                    break;
            }
        }

        void Failure(string stage, Exception e)
        {
            Interlocked.Increment(ref Metrics.Failures);
            Metrics.LastError = stage + ": " + e.GetType().Name + " " + e.Message;
            Interlocked.Exchange(ref emergency, 1);
            try
            {
                lock (executionGate)
                    executor.EmergencyStop();
            }
            catch
            {
            }

            log.Write(stage, "error", Metrics.LastError);
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref closed, 1) != 0)
                return;
            EmergencyStop();
            stop.Set();
            frames.Dispose();
            if (started != 0)
            {
                captureThread.Join();
                visionThread.Join();
                decisionThread.Join();
            }

            source.Dispose();
            executor.Dispose();
            vision.Dispose();
            camera.Dispose();
            pool.Dispose();
            frames.Ready.Dispose();
            stop.Dispose();
            Metrics.Dispose();
            lock (previewGate)
            {
                if (preview != null)
                    preview.Dispose();
                preview = null;
            }

            log.Dispose();
            if (navigationLog != null) navigationLog.Dispose();
        }
    }

    public sealed class NullExecutor : IActionExecutor
    {
        public void Execute(ActionIntent intent)
        {
        }

        public void EmergencyStop()
        {
        }

        public void Dispose()
        {
        }
    }
}
