// C# | Tracking/TemporalTracker.cs | .NET Framework 4.8 | Alpha-beta estimates, gated associations, bounded histories.
using System;
using System.Collections.Generic;
using BlueLockV2.Core;

namespace BlueLockV2.Tracking
{
    public sealed class TemporalTracker : ITracker
    {
        sealed class Track
        {
            public int Id, Hits;
            public EntityType Type;
            public Vec Position, Velocity;
            public double Time, Seen, Confidence, Radius, Width, Height;
            public bool Ambiguous, Changed, Measured;
            public readonly Queue<Vec> History = new Queue<Vec>();
        }

        readonly List<Track> tracks = new List<Track>();
        readonly int capacity, historyLimit;
        int nextId = 1;
        double lastTime = -1, nearSince = -1;
        public TemporalTracker(int capacity = 24, int history = 12)
        {
            this.capacity = capacity;
            historyLimit = history;
        }

        public int Count
        {
            get
            {
                return tracks.Count;
            }
        }

        public int HistoryCount
        {
            get
            {
                int n = 0;
                foreach (var t in tracks)
                    n += t.History.Count;
                return n;
            }
        }

        public void Reset()
        {
            tracks.Clear();
            lastTime = -1;
            nearSince = -1;
        }

        public GameState Update(Detection[] observations, double time, long frame, int width, int height, CameraMotion camera)
        {
            if (time <= lastTime)
                return Snapshot(lastTime, frame, width, height, camera);
            lastTime = time;
            var used = new HashSet<int>();
            foreach (var track in tracks)
            {
                double dt = Math.Max(.001, time - track.Time);
                Vec predicted = track.Position + track.Velocity * dt + camera.Translation;
                int index = -1;
                double best = double.MaxValue, second = double.MaxValue;
                for (int i = 0; i < observations.Length; i++)
                {
                    var obs = observations[i];
                    if (used.Contains(i) || obs.Type != track.Type || obs.Confidence < .35)
                        continue;
                    double distance = Vec.Distance(predicted, obs.Position);
                    if (distance < best)
                    {
                        second = best;
                        best = distance;
                        index = i;
                    }
                    else
                        second = Math.Min(second, distance);
                }

                double gate = Math.Max(35, Math.Min(width * .24, 55 + track.Velocity.Length * dt));
                track.Changed = false;
                track.Ambiguous = false;
                if (index >= 0 && best <= gate)
                {
                    Detection obs = observations[index];
                    used.Add(index);
                    Vec residual = obs.Position - predicted;
                    Vec raw = (obs.Position - track.Position - camera.Translation) / dt;
                    track.Changed = track.Hits > 2 && (raw - track.Velocity).Length > Math.Max(180, track.Velocity.Length * 1.6);
                    track.Ambiguous = second - best < 12;
                    if (camera.Discontinuity)
                    {
                        track.Velocity = new Vec();
                        track.Position = obs.Position;
                    }
                    else
                    {
                        track.Position = predicted + residual * .85;
                        track.Velocity = track.Velocity + residual * (.22 / dt);
                        if (track.Hits == 1)
                            track.Velocity = raw;
                    }

                    if (track.Changed)
                        track.Velocity = track.Velocity * .4 + raw * .6;
                    if (track.Velocity.Length > width * 3)
                        track.Velocity = track.Velocity.Normalized * width * 3;
                    track.Seen = time;
                    track.Hits++;
                    track.Radius = obs.Radius;
                    track.Width = obs.Width;
                    track.Height = obs.Height;
                    track.Measured = obs.Measured;
                    track.Confidence = Numeric.Clamp(track.Confidence * .25 + obs.Confidence * .75, 0, 1) * (track.Ambiguous ? .55 : 1) * (camera.Discontinuity ? .6 : 1);
                    track.History.Enqueue(obs.Position);
                    while (track.History.Count > historyLimit)
                        track.History.Dequeue();
                }
                else
                {
                    track.Position = predicted;
                    track.Confidence *= Math.Exp(-dt * 2.8);
                }

                track.Time = time;
            }

            tracks.RemoveAll(delegate (Track t)
            {
                return time - t.Seen > 1.2 || t.Confidence < .06;
            });
            for (int i = 0; i < observations.Length && tracks.Count < capacity; i++)
                if (!used.Contains(i) && observations[i].Confidence >= .5)
                {
                    var o = observations[i];
                    var t = new Track
                    {
                        Id = nextId++,
                        Type = o.Type,
                        Position = o.Position,
                        Time = time,
                        Seen = time,
                        Confidence = o.Confidence,
                        Radius = o.Radius,
                        Width = o.Width,
                        Height = o.Height,
                        Hits = 1,
                        Measured = o.Measured
                    };
                    t.History.Enqueue(o.Position);
                    tracks.Add(t);
                }

            return Snapshot(time, frame, width, height, camera);
        }

        GameState Snapshot(double time, long frame, int width, int height, CameraMotion camera)
        {
            var objects = new TrackedObject[tracks.Count];
            TrackedObject ball = null, local = null;
            for (int i = 0; i < tracks.Count; i++)
            {
                var t = tracks[i];
                bool lost = time - t.Seen > .001;
                var o = new TrackedObject(t.Id, t.Type, t.Position, t.Velocity, time, t.Seen, t.Confidence, t.Radius, lost, t.Ambiguous, t.Changed, t.Hits, t.Measured, t.Width, t.Height);
                objects[i] = o;
                if (o.Type == EntityType.BALL && !lost)
                    ball = o;
                if (o.Type == EntityType.LOCAL_PLAYER && !lost)
                    local = o;
            }

            double possession = 0;
            if (ball != null && local != null && Vec.Distance(ball.Position, local.Position) < ball.Radius + local.Radius + 24 && (ball.Velocity - local.Velocity).Length < 90)
            {
                if (nearSince < 0)
                    nearSince = time;
                possession = Math.Min(ball.Confidence, local.Confidence) * Numeric.Clamp((time - nearSince) / .35, 0, 1) * (local.Measured ? 1 : .55);
            }
            else
                nearSince = -1;
            return new GameState(time, frame, width, height, objects, camera, possession);
        }
    }
}
