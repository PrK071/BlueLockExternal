// C# | BallControl/BallCycleController.cs | .NET Framework 4.8 x64 | Recover, acquire, carry, prepare, execute, verify, reset with bounded recovery.
using System;
using BlueLockV2.Core;
using BlueLockV2.Navigation;
using BlueLockV2.Tracking;
using BlueLockV2.Prediction;

namespace BlueLockV2.BallControl
{
    public sealed class BallCycleController
    {
        readonly PossessionDetector possession = new PossessionDetector();
        readonly TemporalTracker tracker = new TemporalTracker(4, 12);
        readonly PredictionEngine predictor = new PredictionEngine();
        readonly NavigationController navigation = new NavigationController(true);
        LabCommand pending;
        ShotPlan plan;
        string session;
        int trial = -1, recoveries, completed;
        long nextCommand, sequence = -1, shotId, priorGoals;
        double entered, trialBegan, lastProgress, bestDistance = Double.MaxValue, squaredError;
        int errorSamples;
        Vec carryTarget;
        MotionSettlement shotSettlement;
        volatile bool stopped;
        bool initialResetIssued;
        public CycleState State { get; private set; }
        public PossessionState Possession { get { return possession.State; } }
        public ShotOutcome Outcome { get; private set; }
        public CycleReport LastReport { get; private set; }
        public NavigationTrace Navigation { get { return navigation.Trace; } }
        public string Reason { get; private set; }
        public double Power = .7;
        public double MovementSpeed = 16;
        public bool AutoReset = true, Suite;
        public bool ResetOnStart;
        public int InitialScenario = 1;
        public int MaximumTrials = 10;
        public int Completed { get { return completed; } }
        public long LatestCommandId { get { return nextCommand; } }
        public double PredictionRmse { get { return predictor.Rmse; } }
        public double TrajectoryRmse { get { return errorSamples == 0 ? 0 : Math.Sqrt(squaredError / errorSamples); } }
        public int TrajectorySamples { get { return errorSamples; } }
        public ShotPlan CurrentPlan { get { return plan; } }
        public BallCycleController(long commandSeed = 0) { State = CycleState.SEARCH_BALL; nextCommand = commandSeed; }
        public void EmergencyStop() { stopped = true; State = CycleState.STOPPED; navigation.EmergencyStop(); }
        public void CompensateFocusPause(double seconds, double now)
        {
            if (!MotionBasis.Finite(seconds) || seconds < 0) throw new ArgumentException("Invalid pause duration");
            if (stopped) return;
            entered += seconds; trialBegan += seconds; lastProgress += seconds;
            navigation.Reset();
            if (State == CycleState.PREPARE_SHOT) shotSettlement = new MotionSettlement(now);
        }
        void Enter(CycleState state, double now)
        { State = state; entered = now; pending = null; navigation.Reset(); bestDistance = Double.MaxValue; lastProgress = now; if (state == CycleState.PREPARE_SHOT) shotSettlement = new MotionSettlement(now); }
        LabCommand Command(string action, BallFrame frame)
        { return new LabCommand { id = ++nextCommand, action = action, session = frame.Session, trial = frame.Trial, scenario = frame.CaseId }; }
        BallAction Wait(double now, string reason) { Reason = reason; return BallAction.Wait(now, reason, pending); }
        void Finish(BallFrame frame, double now, ShotOutcome outcome, string reason)
        {
            Outcome = outcome; completed++;
            LastReport = new CycleReport { Trial = trial, Scenario = frame.CaseId, ShotId = shotId, Outcome = outcome,
                Duration = now - trialBegan, Recoveries = recoveries, TrajectoryRmse = TrajectoryRmse, TrajectorySamples = errorSamples, Reason = reason };
            Enter(AutoReset && completed < MaximumTrials ? CycleState.RESET_SCENE : CycleState.STOPPED, now);
        }
        public BallAction Update(BallFrame frame, double now)
        {
            if (stopped) { State = CycleState.STOPPED; pending = null; return Wait(now, "F12: ciclo bloqueado"); }
            if (completed >= MaximumTrials) { State = CycleState.STOPPED; pending = null; return Wait(now, "limite de ensaios concluído"); }
            if (frame == null || !frame.Fresh(now) || !frame.Motion.PlayerObserved || !frame.Motion.Grounded)
                return BallAction.Wait(now, Reason = "aguardando telemetria atual, chão e foco do laboratório");
            if (session != frame.Session || trial != frame.Trial)
            {
                nextCommand = Math.Max(nextCommand, frame.Ack);
                session = frame.Session; trial = frame.Trial; trialBegan = now; recoveries = 0; shotId = 0; priorGoals = frame.GoalCounter;
                squaredError = 0; errorSamples = 0; plan = null; tracker.Reset(); possession.Reset(); sequence = -1;
                Outcome = ShotOutcome.RESULT_UNKNOWN; Enter(CycleState.SEARCH_BALL, now);
            }
            if (State == CycleState.STOPPED) return Wait(now, "ensaio concluído; reinicie explicitamente para continuar");
            if (ResetOnStart && !initialResetIssued)
            {
                initialResetIssued = true; Enter(CycleState.RESET_SCENE, now);
                pending = Command("reset", frame); pending.scenario = InitialScenario; pending.speed = MovementSpeed == 24 ? 24 : 16;
                return Wait(now, "preparar cenário inicial verificável");
            }
            PossessionState ownership = possession.Update(frame, now, State == CycleState.APPROACH_BALL);
            if (frame.Motion.Sequence != sequence)
            {
                sequence = frame.Motion.Sequence;
                var observations = frame.BallKnown ? new[] { new Detection(EntityType.BALL, frame.Ball * 10, 8, 1) } : new Detection[0];
                // The existing tracker thresholds are in pixels: use an explicit 10-units-per-stud analysis plane.
                GameState tracked = tracker.Update(observations, frame.Time, sequence, 2000, 2000, CameraMotion.Stable);
                Forecast[] forecasts = predictor.Predict(tracked, .2); predictor.ObserveAndQueue(tracked, forecasts);
                if ((State == CycleState.SHOOT || State == CycleState.VERIFY_GOAL) && plan != null && frame.ShotId == shotId)
                {
                    double t = frame.ShotAge;
                    double horizontal = Vec.Distance(frame.Ball, plan.Position(t)), vertical = frame.BallY - plan.Height(t);
                    squaredError += horizontal * horizontal + vertical * vertical; errorSamples++;
                }
            }
            if ((State == CycleState.CARRY_BALL || State == CycleState.PREPARE_SHOT) && ownership != PossessionState.BALL_CONTROLLED)
            { Enter(CycleState.RECOVERING, now); recoveries++; return Wait(now, "posse perdida: liberar movimento e recuperar"); }
            if (State != CycleState.RESET_SCENE && now - trialBegan > 150)
            { Finish(frame, now, ShotOutcome.RESULT_UNKNOWN, "timeout global do ensaio"); return Wait(now, "timeout; resultado desconhecido"); }
            switch (State)
            {
                case CycleState.SEARCH_BALL:
                    if (!frame.BallKnown || !frame.OwnerKnown) return Wait(now, "bola ou dono desconhecidos");
                    if (ownership == PossessionState.BALL_CONTROLLED) { carryTarget = ShotPlanner.Waypoint(frame); Enter(CycleState.CARRY_BALL, now); }
                    else Enter(CycleState.APPROACH_BALL, now);
                    return Wait(now, "bola observada; selecionar recuperação");
                case CycleState.APPROACH_BALL:
                    if (!frame.BallKnown) { Enter(CycleState.SEARCH_BALL, now); return Wait(now, "rastreamento perdido"); }
                    if (ownership == PossessionState.BALL_CONTROLLED) { carryTarget = ShotPlanner.Waypoint(frame); Enter(CycleState.CARRY_BALL, now); return Wait(now, "posse confirmada"); }
                    if (Vec.Distance(frame.Motion.Player, frame.Ball) <= 2.0 && frame.BallY < 4)
                    { Enter(CycleState.VERIFY_POSSESSION, now); pending = Command("acquire", frame); return Wait(now, "solicitar aquisição; proximidade não é posse"); }
                    return Navigate(frame, frame.Ball, 1.5, now);
                case CycleState.VERIFY_POSSESSION:
                    if (ownership == PossessionState.BALL_CONTROLLED)
                    { carryTarget = ShotPlanner.Waypoint(frame); Enter(CycleState.CARRY_BALL, now); return Wait(now, "dono e vínculo de posse confirmados em amostras distintas"); }
                    if (now - entered > 3)
                    { Enter(CycleState.APPROACH_BALL, now); return Wait(now, "aquisição não confirmada; reaproximar"); }
                    return Wait(now, "aguardando confirmação física de aquisição");
                case CycleState.CARRY_BALL:
                    if (!frame.GoalKnown) return Wait(now, "gol de referência desconhecido");
                    if (Vec.Distance(frame.Motion.Player, carryTarget) <= 1.2)
                    { Enter(CycleState.PREPARE_SHOT, now); return Wait(now, "posição de finalização alcançada"); }
                    return Navigate(frame, carryTarget, 1.2, now);
                case CycleState.PREPARE_SHOT:
                    if (plan == null)
                    {
                        if (shotSettlement.Observe(frame.Motion, now) == null)
                        { if (now - entered > 6) { recoveries++; Enter(CycleState.RECOVERING, now); } return Wait(now, "estabilizar condução antes de preparar o chute"); }
                        plan = ShotPlanner.Prepare(frame, ownership, Power);
                        if (plan == null) { if (now - entered > 4) { carryTarget = ShotPlanner.Waypoint(frame); Enter(CycleState.CARRY_BALL, now); } return Wait(now, "condições de chute insuficientes"); }
                        pending = Command("prepare", frame); pending.dx = plan.Direction.X; pending.dz = plan.Direction.Y;
                        pending.speed = plan.Speed; pending.lift = plan.Lift; pending.power = plan.Power;
                    }
                    if (pending != null && frame.PreparedId == pending.id)
                    {
                        long prepared = pending.id; Outcome = ShotOutcome.SHOT_PREPARED; Enter(CycleState.SHOOT, now);
                        pending = Command("shoot", frame); pending.plan = prepared; shotId = pending.id;
                    }
                    if (now - entered > 4) { plan = null; Enter(CycleState.RECOVERING, now); recoveries++; }
                    return Wait(now, "preparar direção/potência e aguardar confirmação do laboratório");
                case CycleState.SHOOT:
                    if (frame.ShotId == shotId)
                    { Outcome = ShotOutcome.SHOT_EXECUTED; Enter(CycleState.VERIFY_GOAL, now); return Wait(now, "lançamento físico confirmado; verificar resultado"); }
                    if (now - entered > 4) { Finish(frame, now, ShotOutcome.RESULT_UNKNOWN, "chute não confirmado"); }
                    return Wait(now, "aguardando execução física da finalização");
                case CycleState.VERIFY_GOAL:
                    ShotOutcome result = GoalDetector.Observe(frame, trial, shotId, priorGoals);
                    if (result == ShotOutcome.GOAL_CONFIRMED || result == ShotOutcome.SHOT_FAILED)
                    { Finish(frame, now, result, frame.ResultReason); return Wait(now, "resultado verificado pelo sensor de plano do gol"); }
                    if (now - entered > 8) Finish(frame, now, ShotOutcome.RESULT_UNKNOWN, "timeout sem evidência de resultado");
                    return Wait(now, "aguardando cruzamento verificado ou falha identificada");
                case CycleState.RECOVERING:
                    if (recoveries > 3) { Finish(frame, now, ShotOutcome.RESULT_UNKNOWN, "limite de recuperação"); return Wait(now, "recuperações esgotadas"); }
                    if (pending == null) pending = Command("recover", frame);
                    if (now - entered > .8) { plan = null; Enter(CycleState.SEARCH_BALL, now); }
                    return Wait(now, "recuperar posse/navegação com entradas liberadas");
                case CycleState.RESET_SCENE:
                    if (pending == null) { pending = Command("reset", frame); pending.scenario = Suite ? frame.CaseId % 10 + 1 : frame.CaseId; pending.speed = MovementSpeed == 24 ? 24 : 16; }
                    if (now - entered > 5) { State = CycleState.STOPPED; pending = null; return Wait(now, "reinício do cenário não confirmado"); }
                    return Wait(now, "reiniciar cenário e aguardar novo identificador de ensaio");
            }
            return Wait(now, "aguardar");
        }
        BallAction Navigate(BallFrame frame, Vec target, double radius, double now)
        {
            double distance = Vec.Distance(frame.Motion.Player, target);
            if (distance < bestDistance - .2) { bestDistance = distance; lastProgress = now; }
            Vec velocity = new Vec(); double interceptTime; Vec intercept;
            if (State == CycleState.APPROACH_BALL && PredictionEngine.Intercept(frame.Motion.Player, frame.Ball, frame.BallVelocity, 6, .7, out interceptTime, out intercept))
                target = intercept;
            var sample = new NavigationSample { Time = frame.Time, Sequence = frame.Motion.Sequence, TargetId = frame.Trial * 100 + (int)State,
                Player = frame.Motion.Player, Target = target, TargetVelocity = velocity, Confidence = 1, CameraConfidence = frame.Motion.CameraConfidence,
                Grounded = frame.Motion.Grounded, PlayerObserved = frame.Motion.PlayerObserved, TargetObserved = frame.BallKnown,
                Space = NavigationSpace.WorldXZ, Basis = frame.Motion.Basis, ArrivalRadius = radius };
            ActionIntent action = navigation.Update(sample, now);
            if (navigation.Faulted || now - lastProgress > 12)
            { recoveries++; Enter(CycleState.RECOVERING, now); return Wait(now, "navegação sem progresso ou incompatível; recuperar"); }
            Reason = action.Reason;
            return new BallAction(action);
        }
    }
}
