// C# | Tests/BallControl/TestBallLab.cs | .NET Framework 4.8 x64 | Deterministic independent ball plant; not a substitute for physical Studio trials.
using System;
using System.IO;
using System.Diagnostics;
using System.Collections.Generic;
using System.Globalization;
using BlueLockV2.Core;
using BlueLockV2.Navigation;
using BlueLockV2.BallControl;
using BlueLockV2.Input;
using BlueLockV2.Configuration;

namespace BlueLockV2.BallTests
{
    sealed class Clock : IClock { public double Time; public double Now { get { return Time; } } }
    sealed class Keys : IInputTransport
    {
        public bool Focus=true;
        readonly HashSet<int> held=new HashSet<int>();
        public bool Focused { get { return Focus; } }
        public void Send(int key,bool down){if(down)held.Add(key);else held.Remove(key);}
        public int Count { get { return held.Count; } }
        public Vec Control { get {return new Vec((held.Contains('D')?1:0)-(held.Contains('A')?1:0),(held.Contains('W')?1:0)-(held.Contains('S')?1:0));} }
    }
    sealed class Plant
    {
        public Vec Player,Ball,Velocity,Facing=new Vec(0,-1);
        public double Y=.8,Vy,ShotAt,Speed=16;
        public int Trial=1,Scenario,Owner;
        public long Prepared,Shot,Goals,Ack,ResultShot;
        public int ResultTrial;
        public string Result="NONE";
        public double CrossX,CrossY,CrossZ;
        public bool Lost,ReleasedBlock;
        LabCommand plan;
        double acquired;
        public Plant(int scenario) { Scenario=scenario; Reset(0); }
        public void Reset(double now)
        {
            Player=new Vec();Ball=Scenario==1?new Vec(0,-5):Scenario==2?new Vec(8,-25):Scenario==3?new Vec(6,-12):new Vec(0,-7);
            Facing=Scenario==4?new Vec(0,1):new Vec(0,-1);Velocity=new Vec();Y=.8;Vy=0;Owner=0;Shot=Prepared=ResultShot=0;
            Result="NONE";ResultTrial=0;Lost=ReleasedBlock=false; acquired=0;
        }
        public MotionBasis Basis()
        { return Scenario==5?MotionBasis.CameraXZ(new Vec(0,1),new Vec(1,0),Speed,1):MotionBasis.CameraXZ(new Vec(1,0),new Vec(0,-1),Speed,1); }
        public BallFrame Frame(double now,long seq)
        {
            var basis=Basis();if(Scenario==10&&!ReleasedBlock)basis=MotionBasis.CameraXZ(new Vec(1,0),new Vec(0,-1),0,1);
            return new BallFrame { Motion=new NavigationSample { Time=now,Sequence=seq,Player=Player,Target=Ball,Confidence=1,CameraConfidence=1,Basis=basis,Space=NavigationSpace.WorldXZ },
                Session="synthetic-reference",Trial=Trial,CaseId=Scenario,LocalId=100,OwnerId=Owner,OwnerKnown=true,ConstraintIntact=Owner==100,
                BallKnown=true,BallVisible=!(Scenario==9 && now%5<2),Ball=Ball,BallY=Y,BallVelocity=Velocity,BallVy=Vy,
                GoalKnown=true,Goal=new Vec(0,-55),GoalWidth=16,GoalHeight=8,GoalAimY=3,Gravity=196.2,Radius=.8,
                Ack=Ack,PreparedId=Prepared,ShotId=Shot,ShotAge=Shot==0?0:now-ShotAt,Result=Result,ResultReason=Result,
                ResultTrial=ResultTrial,ResultShotId=ResultShot,GoalCounter=Goals,CrossX=CrossX,CrossY=CrossY,CrossZ=CrossZ,Blocked=Scenario==10&&!ReleasedBlock };
        }
        public void Command(LabCommand cmd,double now)
        {
            if(cmd==null || cmd.id<=Ack || cmd.trial!=Trial)return; Ack=cmd.id;
            if(cmd.action=="acquire" && Owner==0 && Vec.Distance(Player,Ball)<=2.6){Owner=100;acquired=now;}
            if(cmd.action=="recover")ReleasedBlock=true;
            if(cmd.action=="prepare" && Owner==100){plan=cmd;Prepared=cmd.id;}
            if(cmd.action=="shoot" && Owner==100 && plan!=null && cmd.plan==Prepared)
            {Owner=0;Shot=cmd.id;ShotAt=now;Velocity=new Vec(plan.dx*plan.speed+(Scenario==8?75:0),plan.dz*plan.speed);Vy=plan.lift;}
            if(cmd.action=="reset"){Trial++;Scenario=cmd.scenario;Speed=cmd.speed==24?24:16;Reset(now);}
        }
        public void Step(Vec control,double now,double dt)
        {
            if(control.Length>1)control=control.Normalized;
            Vec movement=Basis().Displacement(control);
            if(Scenario==10&&!ReleasedBlock)movement=new Vec();
            Player+=movement*dt;if(movement.Length>.01)Facing=movement.Normalized;
            if(Owner==100)
            {
                Ball=Player+Facing*2;Y=.8;Velocity=movement;Vy=0;
                if(Scenario==6&&!Lost&&now-acquired>1&&Player.Y<-10){Owner=0;Lost=true;Velocity=new Vec(4,0);}
                return;
            }
            if(Shot==0)
            {
                if(Scenario==3)Velocity=new Vec(3*Math.Cos(now*.5),0);
                else Velocity*=Math.Exp(-dt*4);
                Ball+=Velocity*dt;return;
            }
            Vec old=Ball;double oldY=Y;
            Ball+=Velocity*dt;Y+=Vy*dt-.5*196.2*dt*dt;Vy-=196.2*dt;
            if(Y<.8){Y=.8;Vy=Math.Abs(Vy)*.2;}
            double plane=-55.8;
            if(Result=="NONE"&&old.Y>plane&&Ball.Y<=plane)
            {
                double blend=(plane-old.Y)/(Ball.Y-old.Y);CrossX=old.X+(Ball.X-old.X)*blend;CrossY=oldY+(Y-oldY)*blend;CrossZ=plane;
                bool goal=Math.Abs(CrossX)<=7.2&&CrossY>=.8&&CrossY<=7.2;
                Result=goal?"GOAL":"MISS";ResultShot=Shot;ResultTrial=Trial;if(goal)Goals++;
            }
            if(Result=="NONE"&&now-ShotAt>6){Result="MISS";ResultShot=Shot;ResultTrial=Trial;}
        }
    }
    static class Tests
    {
        static int passed,failed;
        static readonly string Folder=Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"..","reports","ball-v22"));
        static void Check(bool value,string message){if(!value)throw new Exception(message);}
        static void Test(string name,Action action){try{action();passed++;Console.WriteLine("PASS "+name);}catch(Exception e){failed++;Console.WriteLine("FAIL "+name+" | "+e.Message);}}
        static BallFrame Frame(double now=0,long seq=1){return new Plant(1).Frame(now,seq);}
        static string RunScenario(int scenario,int count=1,double speed=16)
        {
            var clock=new Clock();var keys=new Keys();var plant=new Plant(scenario){Speed=speed};var cycle=new BallCycleController {MaximumTrials=count,AutoReset=count>1,MovementSpeed=speed};
            var states=new HashSet<CycleState>();bool lost=false,blocked=false;BallFrame frame=null;CycleReport report=null;int recorded=0;
            using(var input=new InputScheduler(keys,clock,new Settings{Sprint=false},false))
            {
                for(int tick=0;tick<15000;tick++)
                {
                    clock.Time=tick*.02;
                    if(tick%10==0)frame=plant.Frame(clock.Now,tick/10+1);
                    var action=cycle.Update(frame,clock.Now);states.Add(cycle.State);lost|=cycle.Possession==PossessionState.BALL_LOST;blocked|=frame.Blocked;
                    input.Execute(action.Movement);input.Tick();plant.Command(action.Command,clock.Now);plant.Step(keys.Control,clock.Now,.02);
                    if(cycle.LastReport!=null&&cycle.LastReport!=report){report=cycle.LastReport;recorded++;}
                    if(cycle.State==CycleState.STOPPED)break;
                }
                input.EmergencyStop();Check(keys.Count==0,"held keys after stop");
            }
            Check(report!=null&&recorded==count,"no completed report; "+cycle.State+" "+cycle.Reason);
            Check(report.Outcome==(scenario==8?ShotOutcome.SHOT_FAILED:ShotOutcome.GOAL_CONFIRMED),"unexpected result "+report.Outcome+" "+report.Reason);
            Check(states.Contains(CycleState.VERIFY_POSSESSION)&&states.Contains(CycleState.CARRY_BALL)&&states.Contains(CycleState.PREPARE_SHOT)&&states.Contains(CycleState.SHOOT)&&states.Contains(CycleState.VERIFY_GOAL),"cycle skipped required stage");
            if(scenario==6)Check(lost&&report.Recoveries>0,"loss was not recovered");
            if(scenario==10)Check(blocked&&report.Recoveries>0&&plant.ReleasedBlock,"navigation failure not recovered");
            if(count>1)Check(plant.Trial==count,"automatic reset not observed");
            return String.Format(CultureInfo.InvariantCulture,"{0},{1},{2:R},{3:R},{4},{5}",scenario,report.Outcome,clock.Now,report.TrajectoryRmse,report.Recoveries,recorded);
        }
        public static int Main(string[] args)
        {
            Directory.CreateDirectory(Folder);CultureInfo.CurrentCulture=CultureInfo.InvariantCulture;
            if(args.Length==3&&args[0]=="--generate"){Console.WriteLine(BallLabProject.Write(args[1],args[2]));return 0;}
            var watch=Stopwatch.StartNew();var process=Process.GetCurrentProcess();double cpu=process.TotalProcessorTime.TotalSeconds;
            BallProtocolTests.Run(Test);
            Test("panel restart continues above the server command acknowledgement",delegate {
                var c=new BallCycleController(1){ResetOnStart=true};var f=Frame();f.Ack=900000;
                var a=c.Update(f,0);Check(a.Command!=null&&a.Command.action=="reset"&&a.Command.id>f.Ack,"reset would be rejected after a panel restart");
            });
            Test("focus pause does not consume the trial timeout",delegate {
                var c=new BallCycleController();c.Update(Frame(),0);c.CompensateFocusPause(300,300);
                c.Update(Frame(300.2,2),300.2);Check(c.Completed==0&&c.State!=CycleState.STOPPED,"focus pause expired the trial");
                c.EmergencyStop();c.CompensateFocusPause(10,310.2);Check(c.State==CycleState.STOPPED,"focus resume rearmed F12");
            });
            Test("running setting is carried by the laboratory reset command",delegate {
                var c=new BallCycleController(){ResetOnStart=true,MovementSpeed=24};var a=c.Update(Frame(),0);
                Check(a.Command!=null&&a.Command.action=="reset"&&a.Command.speed==24,"running was not configured in the lab");
            });
            Test("nearby ball without owner is not possession",delegate{var detector=new PossessionDetector();var f=Frame();f.Ball=new Vec();Check(detector.Update(f,0,false)!=PossessionState.BALL_CONTROLLED,"proximity declared ownership");});
            Test("authoritative owner needs intact constraint and two observations",delegate
            {
                var d=new PossessionDetector();var f=Frame();f.OwnerId=f.LocalId;f.ConstraintIntact=true;
                Check(d.Update(f,0,false)!=PossessionState.BALL_CONTROLLED,"single observation");f.Motion.Sequence=2;f.Motion.Time=.2;
                Check(d.Update(f,.2,false)==PossessionState.BALL_CONTROLLED,"ownership not confirmed");f.OwnerId=0;f.ConstraintIntact=false;f.Motion.Sequence=3;f.Motion.Time=.4;
                Check(d.Update(f,.4,false)==PossessionState.BALL_LOST,"loss not detected");
            });
            Test("vision proximity never confirms ownership",delegate
            {
                var d=new[]{new TrackedObject(1,EntityType.BALL,new Vec(),new Vec(),0,0,1,8,false,false,false,5,true),new TrackedObject(2,EntityType.LOCAL_PLAYER,new Vec(),new Vec(),0,0,1,8,false,false,false,5,true)};
                Check(PossessionDetector.FromVision(new GameState(0,1,640,480,d,CameraMotion.Stable,1))!=PossessionState.BALL_CONTROLLED,"pixel proximity granted ownership");
            });
            Test("ballistic plan lands inside goal aperture",delegate
            {
                var f=Frame();f.Ball=new Vec(2,-43);var p=ShotPlanner.Prepare(f,PossessionState.BALL_CONTROLLED,.7);
                Check(p!=null&&Math.Abs(p.Height(p.Flight)-3)<1e-8&&Vec.Distance(p.Position(p.Flight),new Vec(0,-55.8))<1e-8,"trajectory equation incorrect");
                Check(ShotPlanner.Prepare(f,PossessionState.BALL_FREE,.7)==null,"shot without possession");
            });
            Test("disappearance alone cannot confirm goal",delegate{var f=Frame();f.BallVisible=false;Check(GoalDetector.Observe(f,1,12,0)==ShotOutcome.RESULT_UNKNOWN,"disappearance became goal");});
            Test("goal requires matching trial shot counter and geometric crossing",delegate
            {
                var f=Frame();f.Result="GOAL";f.ShotId=12;f.ResultTrial=1;f.ResultShotId=12;f.GoalCounter=1;f.CrossZ=-55.8;f.CrossY=3;
                Check(GoalDetector.Observe(f,1,12,0)==ShotOutcome.GOAL_CONFIRMED,"valid crossing rejected");
                Check(GoalDetector.Observe(f,2,12,0)==ShotOutcome.RESULT_UNKNOWN,"wrong trial accepted");
                Check(GoalDetector.Observe(f,1,13,0)==ShotOutcome.RESULT_UNKNOWN,"wrong shot accepted");
                Check(GoalDetector.Observe(f,1,12,1)==ShotOutcome.RESULT_UNKNOWN,"old counter accepted");
                f.CrossX=15;Check(GoalDetector.Observe(f,1,12,0)==ShotOutcome.RESULT_UNKNOWN,"outside aperture accepted");
            });
            Test("F12 blocks movement commands and restarts",delegate
            {
                var c=new BallCycleController();var f=Frame();c.Update(f,0);c.EmergencyStop();
                Check(c.State==CycleState.STOPPED,"emergency status waits for focus or a fresh frame");
                for(int i=1;i<100;i++){f=Frame(i*.1,i);var a=c.Update(f,i*.1);Check(a.Movement.Kind==IntentKind.WAIT&&a.Command==null&&c.State==CycleState.STOPPED,"rearmed after F12");}
            });
            Test("missing reference data cannot shoot",delegate
            {
                var c=new BallCycleController();var f=Frame();f.BallKnown=false;
                for(int i=0;i<20;i++){f.Motion.Time=i*.2;f.Motion.Sequence=i;var a=c.Update(f,i*.2);Check(a.Command==null&&a.Movement.Kind==IntentKind.WAIT,"acted without observation");}
            });
            var csv=new System.Text.StringBuilder("scenario,outcome,simulated_seconds,trajectory_rmse,recoveries,completed_trials\r\n");
            for(int s=1;s<=10;s++){int scenario=s;Test("full synthetic cycle scenario "+s,delegate{csv.AppendLine(RunScenario(scenario));});}
            for(int s=1;s<=10;s++){int scenario=s;Test("full running synthetic cycle scenario "+s,delegate{RunScenario(scenario,1,24);});}
            Test("automatic scenario reset completes two trials",delegate{RunScenario(1,2);});
            Test("generated physical place validates and preserves navigation place",delegate
            {
                string output=Path.Combine(Folder,"generated");
                string navigation=StudioLabProject.Write(output,"http://127.0.0.1:12345/test");
                string before=Convert.ToBase64String(File.ReadAllBytes(navigation));
                string path=BallLabProject.Write(output,"http://127.0.0.1:12345/test");
                var doc=StudioLabFormat.Validate(path);string server=StudioLabFormat.Source(doc,true);
                Check(server.Contains("ApplyImpulse")&&server.Contains("VerifiedPossession")&&server.Contains("verified full-ball goal-plane crossing"),"physical mechanics missing");
                Check(server.Contains("align.ApplyAtCenterOfMass")&&!server.Contains("ForceApplyAtCenterOfMass"),"invalid AlignPosition property");
                Check(server.Contains("BALLLAB_COMMAND_ERROR")&&server.Contains("xpcall"),"command error can terminate telemetry loop");
                Check(server.Contains("if ball.Parent~=workspace then ball=createBall() end"),"reset cannot recover a destroyed ball");
                Check(server.Contains("CollisionGroupSetCollidable(\"BallLabActor\",\"BallLabBall\",false)")&&server.Contains("RearBoundary"),"uncontrolled avatar collisions or missing field containment");
                Check(!server.Contains("__ENDPOINT__"),"unsubstituted endpoint");
                Check(Convert.ToBase64String(File.ReadAllBytes(navigation))==before,"navigation lab overwritten");
            });
            File.WriteAllText(Path.Combine(Folder,"synthetic-cycles.csv"),csv.ToString());
            process.Refresh();watch.Stop();
            File.WriteAllText(Path.Combine(Folder,"synthetic-resources.txt"),"wall_s="+watch.Elapsed.TotalSeconds.ToString("F3")+"\r\ncpu_s="+(process.TotalProcessorTime.TotalSeconds-cpu).ToString("F3")+"\r\nprivate_MiB="+(process.PrivateMemorySize64/1048576d).ToString("F3")+"\r\nworking_MiB="+(process.WorkingSet64/1048576d).ToString("F3"));
            Console.WriteLine("TOTAL "+passed+" passed / "+failed+" failed");return failed==0?0:1;
        }
    }
}
