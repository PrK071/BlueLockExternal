// C# | BallControl/BallProtocol.cs | .NET Framework 4.8 x64 | Bounded typed protocol for the dedicated Studio ball laboratory.
using System;
using System.Collections.Generic;
using System.Web.Script.Serialization;
using BlueLockV2.Core;
using BlueLockV2.Navigation;
namespace BlueLockV2.BallControl
{
    public static class BallProtocol
    {
        public static BallFrame Parse(string json, double received)
        {
            var data = new JavaScriptSerializer { MaxJsonLength = 16384, RecursionLimit = 4 }.Deserialize<Dictionary<string, object>>(json);
            if (Text(data,"kind") != "ball-lab-v22") throw new ArgumentException("Wrong laboratory protocol");
            var motion = StudioTelemetry.Parse(json, received);
            Integer(data,"seq",0,1000000);
            var frame = new BallFrame { Motion = motion, Session = Text(data,"session"), Trial = (int)Integer(data,"trial",1,Int32.MaxValue), CaseId = (int)Integer(data,"caseId",1,10),
                LocalId = Integer(data,"local_id",1,1000000000000), OwnerId = Integer(data,"owner_id",0,1000000000000), OwnerKnown = Flag(data,"owner_known"), ConstraintIntact = Flag(data,"constraint"),
                BallKnown = Flag(data,"ball_known"), BallVisible = Flag(data,"visible"), Ball = new Vec(Number(data,"tx"),Number(data,"tz")),
                BallY = Number(data,"ball_y"), BallVelocity = new Vec(Number(data,"vx"),Number(data,"vz")), BallVy = Number(data,"ball_vy"),
                GoalKnown = Flag(data,"goal_known"), Goal = new Vec(Number(data,"goal_x"),Number(data,"goal_z")), GoalWidth = Number(data,"goal_width"),
                GoalHeight = Number(data,"goal_height"), GoalAimY = Number(data,"goal_aim_y"), Gravity = Number(data,"gravity"), Radius = Number(data,"radius"),
                Ack = Integer(data,"ack",0,1000000000000), PreparedId = Integer(data,"prepared_id",0,1000000000000), ShotId = Integer(data,"shot_id",0,1000000000000), ShotAge = Number(data,"shot_age"),
                Result = Text(data,"result"), ResultReason = Text(data,"result_reason"), ResultTrial = (int)Integer(data,"result_trial",0,Int32.MaxValue),
                ResultShotId = Integer(data,"result_shot_id",0,1000000000000), GoalCounter = Integer(data,"goal_counter",0,1000000000000),
                CrossX = Number(data,"cross_x"), CrossY = Number(data,"cross_y"), CrossZ = Number(data,"cross_z"), Blocked = Flag(data,"blocked") };
            if (String.IsNullOrWhiteSpace(frame.Session) || frame.Radius <= 0 || frame.Gravity <= 0 || frame.ShotAge < 0 ||
                (frame.GoalKnown && (frame.GoalWidth <= 2 * frame.Radius || frame.GoalHeight <= 2 * frame.Radius || frame.GoalAimY < frame.Radius || frame.GoalAimY > frame.GoalHeight - frame.Radius)) ||
                (frame.Result != "NONE" && frame.Result != "GOAL" && frame.Result != "MISS"))
                throw new ArgumentException("Invalid laboratory frame domain");
            return frame;
        }
        static string Text(Dictionary<string,object> data,string key)
        { object value; if(!data.TryGetValue(key,out value) || !(value is string) || ((string)value).Length>256) throw new ArgumentException("Invalid text: "+key); return (string)value; }
        static bool Flag(Dictionary<string,object> data,string key)
        { object value; if(!data.TryGetValue(key,out value) || !(value is bool)) throw new ArgumentException("Invalid flag: "+key); return (bool)value; }
        static double Number(Dictionary<string,object> data,string key)
        {
            object value; if(!data.TryGetValue(key,out value) || (!(value is int) && !(value is long) && !(value is double) && !(value is decimal))) throw new ArgumentException("Invalid number: "+key);
            double number = Convert.ToDouble(value,System.Globalization.CultureInfo.InvariantCulture);
            if(!MotionBasis.Finite(number) || Math.Abs(number)>1000000000000) throw new ArgumentException("Out of range: "+key);
            return number;
        }
        static long Integer(Dictionary<string,object> data,string key,long minimum,long maximum)
        {
            double number=Number(data,key);
            if(number<minimum || number>maximum || Math.Truncate(number)!=number) throw new ArgumentException("Invalid integer: "+key);
            return (long)number;
        }
    }
}
