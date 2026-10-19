// C# | BallControl/ShotPlanner.cs | .NET Framework 4.8 x64 | Deterministic ballistic plan for this lab's speed/power mechanic.
using System;
using BlueLockV2.Core;
namespace BlueLockV2.BallControl
{
    public sealed class ShotPlan
    {
        public Vec Origin, Direction, Goal;
        public double OriginY, Speed, Lift, Gravity, Flight, Power;
        public Vec Position(double t) { return Origin + Direction * (Speed * t); }
        public double Height(double t) { return OriginY + Lift * t - .5 * Gravity * t * t; }
    }
    public static class ShotPlanner
    {
        public static Vec Waypoint(BallFrame frame)
        {
            Vec away = (frame.Motion.Player - frame.Goal).Normalized;
            if (away.Length < .01) away = new Vec(0, 1);
            return frame.Goal + away * 12;
        }
        public static ShotPlan Prepare(BallFrame frame, PossessionState possession, double power)
        {
            if (frame == null || !frame.GoalKnown || !frame.BallKnown || possession != PossessionState.BALL_CONTROLLED || !frame.Motion.Grounded)
                return null;
            Vec destination = new Vec(frame.Goal.X, frame.Goal.Y - frame.Radius);
            Vec delta = destination - frame.Ball;
            if (delta.Length < 4 || delta.Length > 25 || frame.BallY > frame.GoalHeight - frame.Radius) return null;
            power = Numeric.Clamp(power, .1, 1);
            double speed = 35 + 35 * power, flight = delta.Length / speed;
            double lift = (frame.GoalAimY - frame.BallY + .5 * frame.Gravity * flight * flight) / flight;
            if (!BlueLockV2.Navigation.MotionBasis.Finite(lift) || lift < 0 || lift > 65 || flight > 1.2) return null;
            return new ShotPlan { Origin = frame.Ball, OriginY = frame.BallY, Direction = delta.Normalized, Goal = destination,
                Speed = speed, Lift = lift, Flight = flight, Power = power, Gravity = frame.Gravity };
        }
    }
    public static class GoalDetector
    {
        public static ShotOutcome Observe(BallFrame frame, int trial, long shot, long priorGoalCounter)
        {
            if (frame == null || shot <= 0 || frame.ShotId != shot || frame.ResultTrial != trial || frame.ResultShotId != shot) return ShotOutcome.RESULT_UNKNOWN;
            if (frame.Result == "MISS") return ShotOutcome.SHOT_FAILED;
            bool verified = frame.GoalKnown && frame.Result == "GOAL" && frame.GoalCounter > priorGoalCounter &&
                Math.Abs(frame.CrossX - frame.Goal.X) <= frame.GoalWidth * .5 - frame.Radius &&
                frame.CrossY >= frame.Radius && frame.CrossY <= frame.GoalHeight - frame.Radius &&
                frame.CrossZ <= frame.Goal.Y - frame.Radius + .001;
            return verified ? ShotOutcome.GOAL_CONFIRMED : ShotOutcome.RESULT_UNKNOWN;
        }
    }
}
