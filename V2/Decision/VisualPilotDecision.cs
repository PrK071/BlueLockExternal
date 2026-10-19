// C# | Decision/VisualPilotDecision.cs | .NET Framework 4.8 x64 | Navigation only with a measured, calibrated ground plane and control basis.
using System;
using BlueLockV2.Core;
using BlueLockV2.Configuration;
using BlueLockV2.Navigation;
using BlueLockV2.Vision;
namespace BlueLockV2.Decision
{
    public sealed class VisualPilotDecision : IDecisionEngine
    {
        readonly Settings settings;
        readonly NavigationController navigation = new NavigationController();
        public PlayState State { get; private set; }
        public string Feedback { get; private set; }
        public VisualPilotDecision(Settings configuration) { settings = configuration.Copy(); }
        public ActionIntent Decide(GameState game, Forecast[] forecasts, double now)
        {
            State = PlayState.UNCERTAIN;
            var ball = game == null ? null : game.Best(EntityType.BALL);
            var local = game == null ? null : game.Best(EntityType.LOCAL_PLAYER);
            if (!settings.NavigationCalibrated || settings.ImageCorners.Length != 8 || game == null ||
                local == null || !local.Measured || local.Ambiguous || ball == null || ball.Ambiguous ||
                game.Camera.Discontinuity || game.Camera.Confidence < .8)
            {
                Feedback = "Navegação requer chão e movimento calibrados, posição local medida e câmera confiável. Use o laboratório Studio.";
                return navigation.Update(null, now);
            }
            int references = 0;
            foreach (var obj in game.Objects)
                if (obj.Type == EntityType.FIELD_REFERENCE && obj.Measured && !obj.Lost && obj.Confidence >= .8)
                    references++;
            if (references < 4)
            {
                Feedback = "Quatro referências medidas do chão ausentes; sem converter pixels em WASD.";
                return navigation.Update(null, now);
            }
            // A stored homography is accepted only while all four corresponding scene references remain in its calibrated pose.
            for (int corner = 0; corner < 4; corner++)
            {
                bool matches = false;
                foreach (var obj in game.Objects)
                    if (obj.Type == EntityType.FIELD_REFERENCE && obj.Measured && !obj.Lost &&
                        Vec.Distance(obj.Position, new Vec(settings.ImageCorners[corner * 2], settings.ImageCorners[corner * 2 + 1])) < 10)
                        matches = true;
                if (!matches) { Feedback = "Câmera saiu da pose da homografia; recalibrar."; return navigation.Update(null, now); }
            }
            using (var plane = new FieldProjector(settings.ImageCorners, settings.FieldWidth, settings.FieldHeight))
            {
                var sample = new NavigationSample { Time = game.Timestamp, Sequence = game.FrameId, TargetId = ball.Id,
                    Space = NavigationSpace.CalibratedPlane, Player = plane.Project(local.Position), Target = plane.Project(ball.Position),
                    Confidence = Math.Min(ball.Confidence, local.Confidence), CameraConfidence = game.Camera.Confidence,
                    Basis = new MotionBasis(new Vec(settings.NavigationRightX, settings.NavigationRightY),
                        new Vec(settings.NavigationForwardX, settings.NavigationForwardY), .95), ArrivalRadius = 1.2 };
                ActionIntent result = navigation.Update(sample, now);
                Feedback = result.Reason;
                State = result.Kind == IntentKind.MOVE ? PlayState.MOVING_TO_TARGET : PlayState.RECOVERING;
                return result;
            }
        }
    }
}
