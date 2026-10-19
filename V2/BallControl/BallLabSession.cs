// C# | BallControl/BallLabSession.cs | .NET Framework 4.8 x64 | Background closed loop, actual input leases, resource sampling and bounded evidence.
using System;
using System.IO;
using System.Threading;
using System.Web.Script.Serialization;
using BlueLockV2.Core;
using BlueLockV2.Navigation;
using BlueLockV2.Input;
using BlueLockV2.Configuration;
using BlueLockV2.Diagnostics;

namespace BlueLockV2.BallControl
{
    public sealed class BallLabSession : IDisposable
    {
        readonly StudioTelemetry source;
        readonly IClock clock;
        readonly IInputTransport transport;
        readonly InputScheduler input;
        readonly object gate = new object();
        readonly ManualResetEvent stop = new ManualResetEvent(false);
        readonly Thread thread;
        readonly StreamWriter log, attempts;
        readonly JavaScriptSerializer serializer = new JavaScriptSerializer();
        CycleReport written;
        long lastSequence = -1, rows;
        CycleState previousState;
        double pauseBegan = -1;
        bool disposed, paused, emergency;
        LabCommand stopCommand;
        public readonly BallCycleController Controller;
        public readonly Telemetry Metrics = new Telemetry();
        public readonly string LogPath, AttemptsPath;
        public string LastError { get; private set; }
        public bool Paused { get { return paused; } }
        public BallLabSession(StudioTelemetry bridge, IClock time, IInputTransport destination, double power, bool suite, int count, int scenario = 1, bool running = false)
        {
            source = bridge; clock = time; transport = destination;
            Controller = new BallCycleController((long)(time.Now * 1000)) { Power = power, MovementSpeed = running ? 24 : 16, Suite = suite, MaximumTrials = count, ResetOnStart = true, InitialScenario = scenario };
            input = new InputScheduler(destination, time, new Settings { Sprint = false }, true);
            string folder = Path.Combine(SettingsStore.Folder, "ball-lab"); Directory.CreateDirectory(folder);
            string stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff");
            LogPath = Path.Combine(folder, "cycle-" + stamp + ".jsonl"); AttemptsPath = Path.Combine(folder, "attempts-" + stamp + ".jsonl");
            log = new StreamWriter(new FileStream(LogPath, FileMode.CreateNew, FileAccess.Write, FileShare.ReadWrite));
            attempts = new StreamWriter(new FileStream(AttemptsPath, FileMode.CreateNew, FileAccess.Write, FileShare.ReadWrite));
            thread = new Thread(Loop) { IsBackground = true, Name = "Ball lab closed loop", Priority = ThreadPriority.BelowNormal }; thread.Start();
        }
        public void EmergencyStop()
        {
            lock (gate)
            {
                if (disposed) return;
                emergency = true; Controller.EmergencyStop(); input.EmergencyStop();
                var frame = source.LatestBall;
                if (frame != null) stopCommand = new LabCommand { id = Math.Max((long)(clock.Now * 1000) + 100, Math.Max(frame.Ack,Controller.LatestCommandId)+1), action = "stop", session = frame.Session, trial = frame.Trial };
                source.SetBallCommand(stopCommand);
            }
        }
        void Loop()
        {
            while (!stop.WaitOne(0))
            {
                try
                {
                    double began = clock.Now;
                    lock (gate)
                    {
                        BallFrame frame = source.LatestBall;
                        paused = !transport.Focused;
                        if (paused && pauseBegan < 0) pauseBegan = began;
                        if (!paused && pauseBegan >= 0) { Controller.CompensateFocusPause(began-pauseBegan,began); pauseBegan = -1; }
                        BallAction action = paused ? BallAction.Wait(began, "laboratório sem foco") : Controller.Update(frame, began);
                        input.Execute(action.Movement);
                        source.SetBallCommand(emergency ? stopCommand : paused ? null : action.Command);
                        Metrics.Decisions++; Metrics.Sample();
                        if (frame != null && rows < 100000 && (frame.Motion.Sequence != lastSequence || Controller.State != previousState))
                        {
                            rows++; lastSequence = frame.Motion.Sequence; previousState = Controller.State;
                            ShotPlan shot = Controller.CurrentPlan;
                            log.WriteLine(serializer.Serialize(new { time = began, schema = "ball-lab-v22", trial = frame.Trial, scenario = frame.CaseId,
                                stage = Controller.State.ToString(), possession = Controller.Possession.ToString(), outcome = Controller.Outcome.ToString(),
                                player_x = frame.Motion.Player.X, player_z = frame.Motion.Player.Y, movement_speed = frame.Motion.Basis.Forward.Length, requested_speed = Controller.MovementSpeed, ball_x = frame.Ball.X, ball_z = frame.Ball.Y, ball_y = frame.BallY,
                                owner_id = frame.OwnerId, constraint = frame.ConstraintIntact, visible = frame.BallVisible, goal_x = frame.Goal.X, goal_z = frame.Goal.Y,
                                actual_keys = input.HeldKeys, command = action.Command == null ? "" : action.Command.action, command_id = action.Command == null ? 0 : action.Command.id,
                                shot_id = frame.ShotId, result = frame.Result, goal_counter = frame.GoalCounter, cross_x = frame.CrossX, cross_y = frame.CrossY, cross_z = frame.CrossZ,
                                power = shot == null ? 0 : shot.Power, aim_dx = shot == null ? 0 : shot.Direction.X, aim_dz = shot == null ? 0 : shot.Direction.Y,
                                launch_speed = shot == null ? 0 : shot.Speed, launch_lift = shot == null ? 0 : shot.Lift, planned_flight = shot == null ? 0 : shot.Flight,
                                trajectory_rmse = Controller.TrajectoryRmse, trajectory_samples = Controller.TrajectorySamples, prediction_rmse_studs = Controller.PredictionRmse / 10,
                                cpu_percent = Metrics.CpuPercent, private_mb = Metrics.PrivateMb, working_mb = Metrics.WorkingMb, paused = paused, reason = action.Movement.Reason }));
                            log.Flush();
                        }
                        if (Controller.LastReport != null && Controller.LastReport != written)
                        {
                            written = Controller.LastReport;
                            attempts.WriteLine(serializer.Serialize(new { trial=written.Trial, scenario=written.Scenario, shot_id=written.ShotId,
                                outcome=written.Outcome.ToString(), duration=written.Duration, recoveries=written.Recoveries,
                                trajectory_rmse=written.TrajectoryRmse, trajectory_samples=written.TrajectorySamples, reason=written.Reason })); attempts.Flush();
                        }
                    }
                    Metrics.Decision.Add((clock.Now - began) * 1000);
                }
                catch (Exception error) { LastError = error.ToString(); EmergencyStop(); }
                if (stop.WaitOne(67)) break;
            }
        }
        public void Dispose()
        {
            lock (gate) { if (disposed) return; Controller.EmergencyStop(); input.EmergencyStop(); source.SetBallCommand(null); disposed = true; }
            stop.Set(); if (Thread.CurrentThread != thread) thread.Join();
            input.Dispose(); log.Dispose(); attempts.Dispose(); Metrics.Dispose(); stop.Dispose();
        }
    }
}
