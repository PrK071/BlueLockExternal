// C# | BallControl/PossessionDetector.cs | .NET Framework 4.8 x64 | Authoritative constraint/owner evidence separated from visual proximity.
using BlueLockV2.Core;
namespace BlueLockV2.BallControl
{
    public sealed class VisualBallEvidence
    {
        public PossessionState State = PossessionState.BALL_UNKNOWN;
        public bool BallDetected, LocalDetected, NearPlayer;
        public double Confidence, ClosingPixelsPerSecond;
        public string Reason = "sem observação visual suficiente";
    }
    public sealed class PossessionDetector
    {
        long sequence = -1;
        double ownerSince = -1;
        int ownerHits;
        bool wasControlled;
        public PossessionState State { get; private set; }
        public void Reset() { sequence = -1; ownerSince = -1; ownerHits = 0; wasControlled = false; State = PossessionState.BALL_UNKNOWN; }
        public PossessionState Update(BallFrame frame, double now, bool approaching)
        {
            if (frame == null || !frame.Fresh(now) || !frame.OwnerKnown || !frame.BallKnown)
                return State = PossessionState.BALL_UNKNOWN;
            bool owned = frame.OwnerId == frame.LocalId && frame.LocalId > 0 && frame.ConstraintIntact;
            if (frame.Motion.Sequence != sequence)
            {
                sequence = frame.Motion.Sequence;
                if (owned) { if (ownerSince < 0) ownerSince = frame.Time; ownerHits++; }
                else { ownerSince = -1; ownerHits = 0; }
            }
            if (owned && ownerHits >= 2 && frame.Time - ownerSince >= .15)
            { wasControlled = true; return State = PossessionState.BALL_CONTROLLED; }
            if (!owned && wasControlled) { wasControlled = false; return State = PossessionState.BALL_LOST; }
            return State = approaching ? PossessionState.BALL_APPROACHING : PossessionState.BALL_FREE;
        }
        // Pixels cannot prove ownership of a physical lab constraint or identify an authoritative owner.
        public static PossessionState FromVision(GameState imageState)
        {
            return EstimateVision(imageState).State;
        }
        public static VisualBallEvidence EstimateVision(GameState imageState)
        {
            var result = new VisualBallEvidence();
            if (imageState == null) return result;
            var ball = imageState.Best(EntityType.BALL);
            var local = imageState.Best(EntityType.LOCAL_PLAYER);
            if (ball == null || ball.Ambiguous || ball.Confidence < .7 || imageState.Camera.Discontinuity) return result;
            result.BallDetected = true;
            result.Confidence = ball.Confidence * .55;
            if (local == null || !local.Measured || local.Ambiguous)
            { result.Reason = "bola visível; posição local não medida"; return result; }
            result.LocalDetected = true;
            Vec delta = ball.Position - local.Position, velocity = ball.Velocity - local.Velocity;
            result.NearPlayer = delta.Length <= ball.Radius + local.Radius + 24;
            result.ClosingPixelsPerSecond = delta.Length < .001 ? 0 : -(delta.X * velocity.X + delta.Y * velocity.Y) / delta.Length;
            if (result.ClosingPixelsPerSecond > 1)
            { result.State = PossessionState.BALL_APPROACHING; result.Reason = "aproximação relativa observada; dono desconhecido"; return result; }
            bool nearAnyone = result.NearPlayer;
            foreach (var obj in imageState.Objects)
                if ((obj.Type == EntityType.ALLY || obj.Type == EntityType.OPPONENT) && !obj.Lost && Vec.Distance(ball.Position,obj.Position) <= ball.Radius + obj.Radius + 24) nearAnyone = true;
            if (!nearAnyone) { result.State = PossessionState.BALL_FREE; result.Reason = "estimativa de bola livre entre jogadores visíveis"; }
            else result.Reason = "proximidade visual sem evidência de dono ou vínculo";
            return result;
        }
    }
}
