// C# | BallControl/Contracts.cs | .NET Framework 4.8 x64 | Explicit controlled-lab reference, commands and cycle states.
using System;
using BlueLockV2.Core;
using BlueLockV2.Navigation;

namespace BlueLockV2.BallControl
{
    public enum PossessionState { BALL_FREE, BALL_APPROACHING, BALL_CONTROLLED, BALL_LOST, BALL_UNKNOWN }
    public enum ShotOutcome { RESULT_UNKNOWN, SHOT_PREPARED, SHOT_EXECUTED, GOAL_CONFIRMED, SHOT_FAILED }
    public enum CycleState { SEARCH_BALL, APPROACH_BALL, VERIFY_POSSESSION, CARRY_BALL, PREPARE_SHOT, SHOOT, VERIFY_GOAL, RECOVERING, RESET_SCENE, STOPPED }
    public sealed class BallFrame
    {
        public NavigationSample Motion;
        public string Session, Result = "NONE", ResultReason = "";
        public int Trial, CaseId;
        public long LocalId, OwnerId;
        public bool OwnerKnown, ConstraintIntact, BallKnown, BallVisible, GoalKnown, Blocked;
        public Vec Ball, BallVelocity, Goal;
        public double BallY, BallVy, Gravity = 196.2, Radius = .8, GoalWidth = 16, GoalHeight = 8, GoalAimY = 3;
        public long Ack, PreparedId, ShotId, ResultShotId, GoalCounter;
        public int ResultTrial;
        public double ShotAge, CrossX, CrossY, CrossZ;
        public double Time { get { return Motion == null ? 0 : Motion.Time; } }
        public bool Fresh(double now) { return Motion != null && now - Time <= .35 && Time <= now + .03; }
    }
    public interface IBallLabSource { BallFrame LatestBall { get; } }
    public sealed class LabCommand
    {
        public long id, plan;
        public string action, session;
        public int trial, scenario;
        public double dx, dz, speed, lift, power;
    }
    public sealed class BallAction
    {
        public readonly ActionIntent Movement;
        public readonly LabCommand Command;
        public BallAction(ActionIntent movement, LabCommand command = null) { Movement = movement; Command = command; }
        public static BallAction Wait(double now, string reason, LabCommand command = null) { return new BallAction(ActionIntent.Wait(now, reason), command); }
    }
    public sealed class CycleReport
    {
        public int Trial, Scenario, Recoveries;
        public long ShotId;
        public double Duration, TrajectoryRmse;
        public int TrajectorySamples;
        public ShotOutcome Outcome;
        public string Reason;
    }
}
