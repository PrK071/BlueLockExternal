// C# | Prediction/PredictionEngine.cs | .NET Framework 4.8 | Time-based extrapolation, interception, observed error.
using System;
using System.Collections.Generic;
using BlueLockV2.Core;

namespace BlueLockV2.Prediction
{
    public sealed class PredictionEngine : IPredictor
    {
        readonly Queue<Forecast> pending = new Queue<Forecast>();
        readonly Dictionary<int, TrackedObject> previous = new Dictionary<int, TrackedObject>();
        double squared, total, ballSquared;
        public int ErrorSamples { get; private set; }
        public int BallErrorSamples { get; private set; }

        public double BallRmse
        {
            get
            {
                return BallErrorSamples == 0 ? 0 : Math.Sqrt(ballSquared / BallErrorSamples);
            }
        }

        public double Rmse
        {
            get
            {
                return ErrorSamples == 0 ? 0 : Math.Sqrt(squared / ErrorSamples);
            }
        }

        public double Mae
        {
            get
            {
                return ErrorSamples == 0 ? 0 : total / ErrorSamples;
            }
        }

        public Forecast[] Predict(GameState state, double horizon)
        {
            horizon = Numeric.Clamp(horizon, .02, 2);
            var results = new List<Forecast>();
            foreach (var o in state.Objects)
            {
                double confidence = o.Confidence * Math.Exp(-horizon * .8) * (o.TrajectoryChanged ? .45 : 1) * (o.Ambiguous ? .4 : 1) * (state.Camera.Discontinuity ? .2 : 1) * (state.Camera.Translation.Length > 5 ? .7 : 1) * Math.Max(.3, state.Camera.Confidence);
                bool valid = !o.Lost && o.Hits >= 2 && confidence >= .35 && !state.Camera.Discontinuity;
                results.Add(new Forecast(o.Id, o.Position + o.Velocity * horizon, state.Timestamp, state.Timestamp + horizon, confidence, valid, valid ? "velocidade constante em pixels; câmera futura desconhecida" : "oclusão, câmera, identidade ou confiança insuficiente"));
            }

            return results.ToArray();
        }

        public void ObserveAndQueue(GameState state, Forecast[] forecasts)
        {
            int count = pending.Count;
            for (int i = 0; i < count; i++)
            {
                var f = pending.Dequeue();
                if (f.TargetTime > state.Timestamp)
                {
                    pending.Enqueue(f);
                    continue;
                }

                TrackedObject current = null;
                foreach (var o in state.Objects)
                    if (o.Id == f.TrackId && !o.Lost)
                        current = o;
                TrackedObject old;
                if (current != null && previous.TryGetValue(f.TrackId, out old) && old.Timestamp <= f.TargetTime && current.Timestamp > old.Timestamp && current.Timestamp - f.TargetTime < .4 && !state.Camera.Discontinuity)
                {
                    double blend = (f.TargetTime - old.Timestamp) / (current.Timestamp - old.Timestamp);
                    double error = Vec.Distance(old.Position + (current.Position - old.Position) * blend, f.Position);
                    squared += error * error;
                    total += error;
                    ErrorSamples++;
                    if (current.Type == EntityType.BALL)
                    {
                        ballSquared += error * error;
                        BallErrorSamples++;
                    }
                }
            }

            previous.Clear();
            foreach (var o in state.Objects)
                if (!o.Lost)
                    previous[o.Id] = o;
            foreach (var f in forecasts)
                if (f.Valid)
                {
                    pending.Enqueue(f);
                    while (pending.Count > 128)
                        pending.Dequeue();
                }
        }

        public static bool Intercept(Vec origin, Vec target, Vec velocity, double speed, double maxHorizon, out double time, out Vec point)
        {
            Vec delta = target - origin;
            double a = velocity.X * velocity.X + velocity.Y * velocity.Y - speed * speed, b = 2 * (delta.X * velocity.X + delta.Y * velocity.Y), c = delta.X * delta.X + delta.Y * delta.Y;
            time = -1;
            point = target;
            if (speed <= 0)
                return false;
            if (Math.Abs(a) < 1e-8)
            {
                if (Math.Abs(b) < 1e-8)
                {
                    if (c < 1e-8)
                        time = 0;
                }
                else
                    time = -c / b;
            }
            else
            {
                double disc = b * b - 4 * a * c;
                if (disc < 0)
                    return false;
                double t1 = (-b - Math.Sqrt(disc)) / (2 * a), t2 = (-b + Math.Sqrt(disc)) / (2 * a);
                if (t1 >= 0)
                    time = t1;
                if (t2 >= 0 && (time < 0 || t2 < time))
                    time = t2;
            }

            if (time < 0 || time > maxHorizon)
                return false;
            point = target + velocity * time;
            return true;
        }
    }
}
