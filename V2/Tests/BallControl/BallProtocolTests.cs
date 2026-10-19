// C# | Tests/BallControl/BallProtocolTests.cs | .NET Framework 4.8 x64 | Real localhost transport and strict reference-frame validation.
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Web.Script.Serialization;
using BlueLockV2.BallControl;
using BlueLockV2.Navigation;

namespace BlueLockV2.BallTests
{
    static class BallProtocolTests
    {
        static readonly JavaScriptSerializer Json = new JavaScriptSerializer();
        static void Check(bool value,string message) { if(!value)throw new Exception(message); }
        static Dictionary<string,object> Packet(long seq=1,string session="reference-a")
        {
            return new Dictionary<string,object> {
                {"kind","ball-lab-v22"},{"studio",true},{"session",session},{"seq",seq},{"trial",1},{"caseId",1},
                {"px",0},{"pz",0},{"tx",0},{"tz",-5},{"vx",0},{"vz",0},{"ball_y",.8},{"ball_vy",0},
                {"rx",1},{"rz",0},{"fx",0},{"fz",-1},{"speed",16},{"grounded",true},{"active",true},{"visible",true},
                {"local_id",1},{"owner_id",0},{"owner_known",true},{"constraint",false},{"ball_known",true},{"goal_known",true},
                {"goal_x",0},{"goal_z",-55},{"goal_width",16},{"goal_height",8},{"goal_aim_y",3},{"gravity",196.2},{"radius",.8},
                {"ack",0},{"prepared_id",0},{"shot_id",0},{"shot_age",0},{"result","NONE"},{"result_reason",""},
                {"result_trial",0},{"result_shot_id",0},{"goal_counter",0},{"cross_x",0},{"cross_y",0},{"cross_z",0},{"blocked",false}
            };
        }
        static string Post(StudioTelemetry source,Dictionary<string,object> packet)
        {
            byte[] bytes=Encoding.UTF8.GetBytes(Json.Serialize(packet));
            var request=(HttpWebRequest)WebRequest.Create(source.Endpoint);
            request.Method="POST";request.ContentType="application/json";request.Proxy=null;
            request.Timeout=request.ReadWriteTimeout=2000;request.ContentLength=bytes.Length;request.KeepAlive=false;
            using(var stream=request.GetRequestStream())stream.Write(bytes,0,bytes.Length);
            using(var response=request.GetResponse())using(var reader=new StreamReader(response.GetResponseStream()))return reader.ReadToEnd();
        }
        static void Rejected(string key,object value)
        {
            var packet=Packet();packet[key]=value;bool rejected=false;
            try { BallProtocol.Parse(Json.Serialize(packet),.2); } catch(ArgumentException){rejected=true;}
            Check(rejected,"invalid field accepted: "+key);
        }
        public static void Run(Action<string,Action> test)
        {
            test("persistent laboratory endpoint survives panel restart",delegate {
                string file=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"..","reports","ball-v22","endpoint-test-"+Guid.NewGuid().ToString("N")+".txt");
                string endpoint;
                try {
                    using(var first=StudioTelemetry.OpenPersistent(new Clock(),file))endpoint=first.Endpoint;
                    using(var second=StudioTelemetry.OpenPersistent(new Clock(),file))Check(second.Endpoint==endpoint,"laboratory endpoint changed on restart");
                } finally {if(File.Exists(file))File.Delete(file);}
            });
            test("persistent endpoint rejects remote routes and malformed tokens",delegate {
                string[] invalid={"http://example.com:12345/0123456789abcdef0123456789abcdef","http://127.0.0.1:12345/x","http://127.0.0.1:12345/0123456789abcdef0123456789abcdef?x=1"};
                foreach(string endpoint in invalid) {
                    bool rejected=false;try {using(var source=new StudioTelemetry(new Clock(),endpoint)){} }catch(ArgumentException){rejected=true;}
                    Check(rejected,"invalid persistent endpoint accepted");
                }
            });
            test("typed ball protocol preserves reference and timestamp",delegate {
                var frame=BallProtocol.Parse(Json.Serialize(Packet()),.2);
                Check(frame.Time==.2 && frame.Ball.Y==-5 && frame.Motion.Sequence==1 && frame.LocalId==1,"reference corrupted");
            });
            test("ball protocol rejects fractional identifiers and invalid geometry",delegate {
                Rejected("trial",1.5);Rejected("seq",1.1);Rejected("caseId",11);Rejected("local_id",0);
                Rejected("shot_id",-1);Rejected("radius",0);Rejected("goal_width",1);Rejected("gravity",0);
                Rejected("session","");Rejected("result","ASSUMED");Rejected("visible","true");Rejected("tx","5");
            });
            test("localhost command is scoped to current laboratory trial",delegate {
                using(var source=new StudioTelemetry(new Clock())) {
                    Check(Post(source,Packet())=="{}","unexpected initial command");
                    source.SetBallCommand(new LabCommand{id=25,action="acquire",session="reference-a",trial=1});
                    var reply=Json.Deserialize<Dictionary<string,object>>(Post(source,Packet(2)));
                    var command=(Dictionary<string,object>)reply["command"];
                    Check(Convert.ToInt64(command["id"])==25 && (string)command["action"]=="acquire","command not delivered");
                    source.SetBallCommand(new LabCommand{id=26,action="shoot",session="reference-a",trial=2});
                    Check(Post(source,Packet(3))=="{}","wrong-trial command delivered");
                }
            });
            test("new laboratory session discards command and requires recalibration",delegate {
                using(var source=new StudioTelemetry(new Clock())) {
                    Post(source,Packet(10));source.ControlValidated=true;
                    source.SetBallCommand(new LabCommand{id=25,action="shoot",session="reference-a",trial=1});
                    Check(Post(source,Packet(1,"reference-b"))=="{}","old command crossed sessions");
                    Check(!source.ControlValidated && source.LatestBall.Session=="reference-b" && source.Latest.Sequence==1,"new session not reset");
                }
            });
            test("repeated frame is rejected and transport recovers",delegate {
                using(var source=new StudioTelemetry(new Clock())) {
                    Post(source,Packet());bool rejected=false;
                    try {Post(source,Packet());}catch(WebException){rejected=true;}
                    Check(rejected && source.Accepted==1,"repeated frame published");
                    Post(source,Packet(2));Check(source.Accepted==2 && source.Latest.Sequence==2 && source.LastError=="","transport did not recover");
                }
            });
        }
    }
}
