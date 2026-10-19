// C# | Core/Models.cs | .NET Framework 4.8 x64 | Shared immutable pipeline contracts.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

namespace BlueLockV2.Core
{
    public enum EntityType
    {
        BALL,
        LOCAL_PLAYER,
        ALLY,
        OPPONENT,
        GOAL,
        GOALKEEPER,
        FIELD_REFERENCE
    }

    public enum RunMode
    {
        MANUAL,
        ASSISTIDO,
        AUTONOMO_TESTE,
        PILOTO_ROBLOX,
        NAVEGACAO_STUDIO
    }

    public enum SourceKind
    {
        Simulation,
        Images,
        Video,
        RobloxWindow,
        StudioWindow
    }

    public enum PerformanceProfile
    {
        ECONOMICO,
        EQUILIBRADO,
        AVANCADO
    }

    public enum PlayState
    {
        IDLE,
        SEARCHING_BALL,
        TRACKING_BALL,
        MOVING_TO_TARGET,
        ATTACKING,
        DEFENDING,
        PREPARING_SHOT,
        RECOVERING,
        UNCERTAIN
    }

    public enum IntentKind
    {
        MOVE,
        AIM,
        SHOOT,
        PASS,
        DEFEND,
        WAIT,
        RECOVER,
        DRIBBLE
    }

    public struct Vec
    {
        public readonly double X, Y;
        public Vec(double x, double y)
        {
            X = x;
            Y = y;
        }

        public double Length
        {
            get
            {
                return Math.Sqrt(X * X + Y * Y);
            }
        }

        public Vec Normalized
        {
            get
            {
                return Length < 1e-9 ? new Vec() : this / Length;
            }
        }

        public static Vec operator +(Vec a, Vec b)
        {
            return new Vec(a.X + b.X, a.Y + b.Y);
        }

        public static Vec operator -(Vec a, Vec b)
        {
            return new Vec(a.X - b.X, a.Y - b.Y);
        }

        public static Vec operator *(Vec a, double b)
        {
            return new Vec(a.X * b, a.Y * b);
        }

        public static Vec operator /(Vec a, double b)
        {
            return new Vec(a.X / b, a.Y / b);
        }

        public static double Distance(Vec a, Vec b)
        {
            return (a - b).Length;
        }

        public override string ToString()
        {
            return X.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + "/" + Y.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);
        }
    }

    public sealed class Detection
    {
        public readonly EntityType Type;
        public readonly Vec Position;
        public readonly double Radius, Confidence, Width, Height;
        public readonly bool Measured;
        public Detection(EntityType type, Vec position, double radius, double confidence, bool measured = true, double width = 0, double height = 0)
        {
            Type = type;
            Position = position;
            Radius = radius;
            Confidence = confidence;
            Measured = measured;
            Width = width > 0 ? width : radius * 2;
            Height = height > 0 ? height : radius * 2;
        }
    }

    public sealed class CameraMotion
    {
        public readonly Vec Translation;
        public readonly double Confidence;
        public readonly bool Discontinuity;
        public CameraMotion(Vec translation, double confidence, bool discontinuity)
        {
            Translation = translation;
            Confidence = confidence;
            Discontinuity = discontinuity;
        }

        public static CameraMotion Stable
        {
            get
            {
                return new CameraMotion(new Vec(), 1, false);
            }
        }
    }

    public sealed class TrackedObject
    {
        public readonly int Id;
        public readonly EntityType Type;
        public readonly Vec Position, Velocity;
        public readonly double Timestamp, LastSeen, Confidence, Radius, Width, Height;
        public readonly bool Lost, Ambiguous, TrajectoryChanged, Measured;
        public readonly int Hits;
        public TrackedObject(int id, EntityType type, Vec pos, Vec velocity, double timestamp, double seen, double confidence, double radius, bool lost, bool ambiguous, bool changed, int hits, bool measured, double width = 0, double height = 0)
        {
            Id = id;
            Type = type;
            Position = pos;
            Velocity = velocity;
            Timestamp = timestamp;
            LastSeen = seen;
            Confidence = confidence;
            Radius = radius;
            Lost = lost;
            Ambiguous = ambiguous;
            TrajectoryChanged = changed;
            Hits = hits;
            Measured = measured;
            Width = width > 0 ? width : radius * 2;
            Height = height > 0 ? height : radius * 2;
        }
    }

    public sealed class GameState
    {
        public readonly double Timestamp;
        public readonly long FrameId;
        public readonly int Width, Height;
        public readonly TrackedObject[] Objects;
        public readonly CameraMotion Camera;
        public readonly double PossessionConfidence;
        public GameState(double time, long frame, int width, int height, TrackedObject[] objects, CameraMotion camera, double possession)
        {
            Timestamp = time;
            FrameId = frame;
            Width = width;
            Height = height;
            Objects = objects;
            Camera = camera;
            PossessionConfidence = possession;
        }

        public TrackedObject Best(EntityType type)
        {
            TrackedObject best = null;
            foreach (var obj in Objects)
                if (obj.Type == type && !obj.Lost && (best == null || obj.Confidence > best.Confidence))
                    best = obj;
            return best;
        }
    }

    public sealed class Forecast
    {
        public readonly int TrackId;
        public readonly Vec Position;
        public readonly double Created, TargetTime, Confidence;
        public readonly bool Valid;
        public readonly string Reason;
        public Forecast(int id, Vec position, double created, double target, double confidence, bool valid, string reason)
        {
            TrackId = id;
            Position = position;
            Created = created;
            TargetTime = target;
            Confidence = confidence;
            Valid = valid;
            Reason = reason;
        }
    }

    public sealed class ActionIntent
    {
        public readonly IntentKind Kind;
        public readonly Vec Target, Direction;
        public readonly double Timestamp, Expires, Confidence;
        public readonly int Priority;
        public readonly string Reason;
        public ActionIntent(IntentKind kind, Vec target, Vec direction, double timestamp, double ttl, double confidence, int priority, string reason)
        {
            Kind = kind;
            Target = target;
            Direction = direction;
            Timestamp = timestamp;
            Expires = timestamp + ttl;
            Confidence = confidence;
            Priority = priority;
            Reason = reason;
        }

        public static ActionIntent Wait(double time, string reason)
        {
            return new ActionIntent(IntentKind.WAIT, new Vec(), new Vec(), time, .25, 0, 100, reason);
        }
    }

    public interface IClock
    {
        double Now { get; }
    }

    public sealed class MonotonicClock : IClock
    {
        readonly Stopwatch watch = Stopwatch.StartNew();
        public double Now
        {
            get
            {
                return watch.Elapsed.TotalSeconds;
            }
        }
    }

    public interface ITracker
    {
        GameState Update(Detection[] observations, double time, long frame, int width, int height, CameraMotion camera);
        void Reset();
    }

    public interface IPredictor
    {
        Forecast[] Predict(GameState state, double horizon);
    }

    public interface IDecisionEngine
    {
        ActionIntent Decide(GameState state, Forecast[] predictions, double now);
        PlayState State { get; }
    }

    public interface IActionExecutor : IDisposable
    {
        void Execute(ActionIntent intent);
        void EmergencyStop();
    }

    public sealed class LatestSlot<T> : IDisposable where T : class, IDisposable
    {
        T value;
        int closed;
        public long Dropped;
        public readonly AutoResetEvent Ready = new AutoResetEvent(false);
        public void Put(T item)
        {
            if (Volatile.Read(ref closed) != 0)
            {
                item.Dispose();
                return;
            }

            T old = Interlocked.Exchange(ref value, item);
            if (old != null)
            {
                old.Dispose();
                Interlocked.Increment(ref Dropped);
            }

            Ready.Set();
            if (Volatile.Read(ref closed) != 0)
            {
                old = Interlocked.Exchange(ref value, null);
                if (old != null)
                    old.Dispose();
            }
        }

        public T Take()
        {
            return Interlocked.Exchange(ref value, null);
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref closed, 1) != 0)
                return;
            T old = Take();
            if (old != null)
                old.Dispose();
            Ready.Set();
        }
    }

    public static class Numeric
    {
        public static double Clamp(double x, double lo, double hi)
        {
            return Math.Max(lo, Math.Min(hi, x));
        }

        public static double Median(List<double> x)
        {
            if (x.Count == 0)
                return 0;
            var sorted = new List<double>(x);
            sorted.Sort();
            return sorted[sorted.Count / 2];
        }
    }
}
