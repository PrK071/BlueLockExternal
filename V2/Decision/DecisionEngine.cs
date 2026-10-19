// C# | Decision/DecisionEngine.cs | .NET Framework 4.8 | Confidence-gated FSM and observed action feedback.
using System;
using BlueLockV2.Core;
using BlueLockV2.Prediction;

namespace BlueLockV2.Decision
{
    public sealed class DecisionEngine : IDecisionEngine
    {
        public PlayState State { get; private set; }

        double lastShot = -10, lastPass = -10, progressTime, lastDistance = double.MaxValue;
        int ballId;
        public string Feedback { get; private set; }

        public ActionIntent Decide(GameState game, Forecast[] forecasts, double now)
        {
            if (game == null)
            {
                State = PlayState.IDLE;
                return ActionIntent.Wait(now, "sem estado");
            }

            if (now - game.Timestamp > .6 || game.Camera.Discontinuity)
            {
                State = PlayState.UNCERTAIN;
                return ActionIntent.Wait(now, "estado antigo ou câmera instável");
            }

            var ball = game.Best(EntityType.BALL);
            var local = game.Best(EntityType.LOCAL_PLAYER);
            if (ball == null)
            {
                State = PlayState.SEARCHING_BALL;
                return ActionIntent.Wait(now, "bola perdida; sem movimento cego");
            }

            if (ball.Confidence < .65 || ball.Ambiguous || local == null || local.Confidence < .5)
            {
                State = PlayState.UNCERTAIN;
                return ActionIntent.Wait(now, "identidade, posição local ou confiança insuficiente");
            }

            if (ball.Hits < 2)
            {
                State = PlayState.TRACKING_BALL;
                return ActionIntent.Wait(now, "confirmando observação");
            }

            double distance = Vec.Distance(local.Position, ball.Position);
            if (ballId != ball.Id)
            {
                ballId = ball.Id;
                lastDistance = distance;
                progressTime = now;
            }

            if (distance < lastDistance - 3)
            {
                progressTime = now;
                lastDistance = distance;
                Feedback = "aproximação observada";
            }

            if (ball.Velocity.Length > 100 && now - lastShot < 2)
                Feedback = "movimento da bola após chute observado";
            if (now - progressTime > 2.5 && distance > 60)
            {
                State = PlayState.RECOVERING;
                progressTime = now;
                lastDistance = distance;
                Feedback = "sem progresso; aguardando reestimativa";
                return new ActionIntent(IntentKind.RECOVER, ball.Position, new Vec(), now, .3, .5, 80, Feedback);
            }

            var goal = game.Best(EntityType.GOAL);
            var opponent = game.Best(EntityType.OPPONENT);
            var ally = game.Best(EntityType.ALLY);
            if (opponent != null && opponent.Confidence > .7 && ball.Position.X < game.Width * .23 && game.PossessionConfidence < .5)
            {
                State = PlayState.DEFENDING;
                return new ActionIntent(IntentKind.DEFEND, ball.Position, (ball.Position - local.Position).Normalized, now, .22, ball.Confidence, 70, "interceptar ameaça no cenário calibrado");
            }

            if (game.PossessionConfidence > .72 && goal != null && goal.Confidence > .7)
            {
                State = PlayState.PREPARING_SHOT;
                if (ally != null && opponent != null && Vec.Distance(opponent.Position, local.Position) < 80 && now - lastPass > 3)
                {
                    lastPass = now;
                    return new ActionIntent(IntentKind.PASS, ally.Position, (ally.Position - local.Position).Normalized, now, .2, game.PossessionConfidence, 60, "aliado calibrado disponível sob pressão");
                }

                if (now - lastShot > 3)
                {
                    lastShot = now;
                    return new ActionIntent(IntentKind.SHOOT, goal.Position, (goal.Position - local.Position).Normalized, now, .25, game.PossessionConfidence, 70, "proximidade/co-movimento estável e gol calibrado");
                }

                return new ActionIntent(IntentKind.AIM, goal.Position, (goal.Position - local.Position).Normalized, now, .2, game.PossessionConfidence, 50, "aguardando nova finalização");
            }

            Vec target = ball.Position;
            double t;
            Vec interception;
            if (PredictionEngine.Intercept(local.Position, ball.Position, ball.Velocity, 180, 1.5, out t, out interception))
                target = interception;
            foreach (var f in forecasts)
                if (f.TrackId == ball.Id && f.Valid && f.Confidence > .6 && t < 0)
                    target = f.Position;
            State = distance < 45 ? PlayState.ATTACKING : PlayState.MOVING_TO_TARGET;
            return new ActionIntent(IntentKind.MOVE, target, (target - local.Position).Normalized, now, .22, Math.Min(ball.Confidence, local.Confidence), 40, "aproximação pela posição/interceptação estimada em pixels");
        }
    }
}
