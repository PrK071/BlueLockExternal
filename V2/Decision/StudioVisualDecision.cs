// C# | Decision/StudioVisualDecision.cs | .NET Framework 4.8 x64 | Pixel-calibrated lab navigation; dynamic Studio truth is never an argument.
using System;
using BlueLockV2.Core;
using BlueLockV2.Navigation;
namespace BlueLockV2.Decision
{
    public sealed class StudioVisualDecision : IDecisionEngine
    {
        readonly NavigationController controller=new NavigationController(true);
        public Vec Right,Forward;
        public Vec MeasuredPlayer,MeasuredBall;
        public bool CurrentBall,CurrentPlayer,Calibrated;
        public PlayState State {get;private set;}
        public NavigationTrace Trace {get{return controller.Trace;}}
        public string Feedback {get;private set;}
        public ActionIntent Decide(GameState game,Forecast[] predictions,double now)
        {
            var ball=game==null?null:game.Best(EntityType.BALL);
            var player=game==null?null:game.Best(EntityType.LOCAL_PLAYER);
            if(!Calibrated||!CurrentBall||!CurrentPlayer||ball==null||player==null||ball.Hits<2||player.Hits<2||ball.Ambiguous||player.Ambiguous)
            {State=PlayState.UNCERTAIN;Feedback="fresh measured visual ball, actor and fiducials required";return controller.Update(null,now);}
            Vec target=MeasuredBall;
            foreach(var forecast in predictions)if(forecast.TrackId==ball.Id&&forecast.Valid&&forecast.Confidence>.6&&Vec.Distance(player.Position,ball.Position)>3&&Vec.Distance(forecast.Position,ball.Position)<2)target=forecast.Position;
            var sample=new NavigationSample {Time=game.Timestamp,Sequence=game.FrameId,TargetId=ball.Id,Player=MeasuredPlayer,Target=target,Space=NavigationSpace.WorldXZ,Basis=new MotionBasis(Right*16,Forward*16,1),Confidence=Math.Min(ball.Confidence,player.Confidence),CameraConfidence=1,ArrivalRadius=1.2};
            var intent=controller.Update(sample,now);State=intent.Kind==IntentKind.MOVE?PlayState.MOVING_TO_TARGET:PlayState.RECOVERING;Feedback=controller.Trace.State;return intent;
        }
        public void Stop(){controller.EmergencyStop();}
    }
}
