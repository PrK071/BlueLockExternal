// C# | Tests/Navigation/FeedbackTests.cs | .NET Framework 4.8 x64 | Transient reversal, bounded response, normalized diagonals and delayed telemetry plant.
using System;
using System.IO;
using System.Text;
using System.Collections.Generic;
using BlueLockV2.Core;
using BlueLockV2.Configuration;
using BlueLockV2.Input;
using BlueLockV2.Navigation;

namespace BlueLockV2.NavigationAcceptance
{
    static class FeedbackTests
    {
        static void Check(bool value, string message) { if (!value) throw new Exception(message); }
        static NavigationSample Sample(double time, long sequence, Vec position, Vec target)
        { return new NavigationSample { Time=time, Sequence=sequence, Player=position, Target=target, Confidence=1, CameraConfidence=1, Basis=MotionBasis.CameraXZ(new Vec(1,0),new Vec(0,-1),16,1), Space=NavigationSpace.WorldXZ }; }
        static void Start(NavigationController nav, Vec target)
        { nav.Update(Sample(0,1,new Vec(),target),0); Check(nav.Update(Sample(.1,2,new Vec(),target),.1).Kind==IntentKind.MOVE,"no initial pulse"); }
        public static void Run(Action<string,Action> test, string folder)
        {
            test("transient reverse observation waits for settled response",delegate
            {
                Vec target=new Vec(-10,-10);
                var prior=new NavigationController(); Start(prior,target);
                prior.Update(Sample(.3,3,new Vec(.32,.25),target),.3);
                Check(prior.Faulted,"old early-evaluation failure not reproduced");
                var nav=new NavigationController(true); Start(nav,target);
                Check(nav.Update(Sample(.3,3,new Vec(.32,.25),target),.3).Kind==IntentKind.WAIT && !nav.Faulted,"transient response faulted");
                for(int i=0;i<4;i++) nav.Update(Sample(.5+i*.2,4+i,new Vec(-1.2,-1.2),target),.5+i*.2);
                Check(nav.Trace.State=="FEEDBACK" && !nav.Faulted,"settled correct response not accepted");
            });
            test("settled true inversion remains blocked",delegate
            {
                var nav=new NavigationController(true); Vec target=new Vec(20,0); Start(nav,target);
                for(int i=0;i<4;i++) nav.Update(Sample(.3+i*.2,3+i,new Vec(-1.9,0),target),.3+i*.2);
                Check(nav.Faulted,"persistent wrong direction accepted");
            });
            test("settled feedback no response has bounded retries",delegate
            {
                var nav=new NavigationController(true);
                for(int i=0;i<450;i++) { double time=i*.02; nav.Update(Sample(time,i,new Vec(),new Vec(20,0)),time); }
                Check(nav.Faulted,"no response never stopped");
            });
            test("feedback times out when player never stabilizes",delegate
            {
                var nav=new NavigationController(true); Vec target=new Vec(20,0); Start(nav,target);
                for(int i=0;i<35;i++) { double time=.3+i*.2; nav.Update(Sample(time,3+i,new Vec(time*.5,0),target),time); }
                Check(nav.Faulted,"unbounded response wait");
            });
            test("world diagonal expected speed is normalized",delegate
            {
                var nav=new NavigationController(true); Start(nav,new Vec(20,-20));
                Check(Math.Abs(nav.Trace.Expected.Length-1.92)<.001,"diagonal expected speed exceeds WalkSpeed");
                nav.EmergencyStop(); Check(nav.Update(Sample(.3,3,new Vec(),new Vec(20,-20)),.3).Kind==IntentKind.WAIT,"F12 failed while waiting");
            });
            var results=new StringBuilder("telemetry_delay_s,initial_distance,final_distance,simulated_seconds,feedback_windows\r\n");
            foreach(double lag in new[]{0.0,.2,.6,1.0})
            {
                double latency=lag;
                test("closed loop with telemetry delayed "+latency+" seconds",delegate
                {
                    var clock=new Clock(); var transport=new Transport(); var nav=new NavigationController(true);
                    var positions=new List<Vec>(); Vec player=new Vec(.8828125,-15.3515625), target=new Vec(0,-20);
                    double initial=Vec.Distance(player,target); NavigationSample sample=null; bool reached=false; int feedback=0;
                    using(var input=new InputScheduler(transport,clock,new Settings{Sprint=false},false))
                    {
                        for(int tick=0;tick<2500;tick++)
                        {
                            clock.Time=tick*.02; positions.Add(player);
                            if(tick%10==0) sample=Sample(clock.Time,tick/10+1,positions[Math.Max(0,tick-(int)Math.Round(latency/.02))],target);
                            input.Execute(nav.Update(sample,clock.Time)); input.Tick();
                            if(nav.Trace.State=="FEEDBACK") feedback++;
                            Vec command=transport.Control; if(command.Length>1) command=command.Normalized;
                            player+=new Vec(command.X*16,-command.Y*16)*.02;
                            Check(!nav.Faulted,"false fault with delayed observations");
                            if(nav.Trace.State=="OBSERVING") Check(transport.Keys.Count==0,"keys held while observing");
                            if(nav.Trace.State=="REACHED") { Check(Vec.Distance(player,target)<=1.25,"reported arrival while actual player far away"); reached=true; break; }
                        }
                        input.EmergencyStop(); Check(transport.Keys.Count==0,"keys not released");
                    }
                    Check(reached,"did not finish approach");
                    results.AppendLine(String.Format(System.Globalization.CultureInfo.InvariantCulture,"{0:R},{1:R},{2:R},{3:R},{4}",latency,initial,Vec.Distance(player,target),clock.Time,feedback));
                });
            }
            File.WriteAllText(Path.Combine(folder,"delayed-feedback-acceptance.csv"),results.ToString());
        }
    }
}
