// C# | Capture/Simulation.cs | .NET Framework 4.8 x64 | Deterministic pixel-rendered closed-loop training field.
using System;
using OpenCvSharp;
using BlueLockV2.Core;

namespace BlueLockV2.Capture
{
    public sealed class Simulation : IFrameSource, IActionExecutor
    {
        readonly object gate = new object ();
        readonly IClock clock;
        readonly Mat raw = new Mat(360, 640, MatType.CV_8UC3);
        double last = -1, lastShot = -10;
        Vec player = new Vec(130, 230), ball = new Vec(335, 160), velocity, move;
        bool owns, closed;
        long id;
        public int Shots, Moves;
        public double Travelled;
        public SourceKind Kind
        {
            get
            {
                return SourceKind.Simulation;
            }
        }

        public bool Completed
        {
            get
            {
                return false;
            }
        }

        public string Description
        {
            get
            {
                return "Campo sintético: entrada → pixels → percepção → decisão → ação";
            }
        }

        public Simulation(IClock time)
        {
            clock = time;
        }

        public Vec Player
        {
            get
            {
                lock (gate)
                    return player;
            }
        }

        public Vec Ball
        {
            get
            {
                lock (gate)
                    return ball;
            }
        }

        public void Execute(ActionIntent intent)
        {
            lock (gate)
            {
                if (closed)
                    return;
                move = intent.Kind == IntentKind.MOVE ? intent.Direction.Normalized : new Vec();
                if (move.Length > 0)
                    Moves++;
                if ((intent.Kind == IntentKind.SHOOT || intent.Kind == IntentKind.PASS) && owns)
                {
                    owns = false;
                    velocity = (new Vec(600, 180) - ball).Normalized * 290;
                    lastShot = clock.Now;
                    Shots++;
                }
            }
        }

        public void EmergencyStop()
        {
            lock (gate)
                move = new Vec();
        }

        public Frame Read(MatPool pool, int width, Rect? roi)
        {
            lock (gate)
            {
                if (closed)
                    return null;
                double now = clock.Now, dt = last < 0 ? 0 : Numeric.Clamp(now - last, 0, .25);
                last = now;
                Vec step = move * (150 * dt);
                player += step;
                player = new Vec(Numeric.Clamp(player.X, 25, 610), Numeric.Clamp(player.Y, 30, 330));
                Travelled += step.Length;
                if (!owns)
                {
                    ball += velocity * dt;
                    velocity *= Math.Exp(-dt * .3);
                    if (now - lastShot > 2 && Vec.Distance(player, ball) < 31)
                        owns = true;
                }

                if (owns)
                    ball = player + new Vec(18, 0);
                if (ball.X > 625 || ball.X < 15 || ball.Y < 15 || ball.Y > 345)
                {
                    ball = new Vec(335, 160);
                    velocity = new Vec();
                }

                raw.SetTo(new Scalar(30, 145, 35));
                for (int x = 0; x < 640; x += 80)
                    Cv2.Rectangle(raw, new Rect(x, 0, 40, 360), new Scalar(35, 155, 40), -1);
                Cv2.Rectangle(raw, new Rect(15, 15, 610, 330), new Scalar(210, 210, 210), 1);
                Cv2.Line(raw, new OpenCvSharp.Point(320, 15), new OpenCvSharp.Point(320, 345), new Scalar(190, 190, 190), 1);
                Cv2.Rectangle(raw, new Rect(590, 125, 25, 110), new Scalar(210, 0, 210), -1);
                Cv2.Circle(raw, new OpenCvSharp.Point(570, 180), 11, new Scalar(0, 150, 245), -1);
                Cv2.Circle(raw, new OpenCvSharp.Point(440, 80), 12, new Scalar(0, 0, 235), -1);
                Cv2.Circle(raw, new OpenCvSharp.Point(230, 300), 12, new Scalar(220, 220, 0), -1);
                Cv2.Circle(raw, new OpenCvSharp.Point(player.X, player.Y), 12, new Scalar(245, 70, 0), -1);
                Cv2.Circle(raw, new OpenCvSharp.Point(ball.X, ball.Y), 8, new Scalar(245, 245, 245), -1);
                Cv2.Circle(raw, new OpenCvSharp.Point(ball.X, ball.Y), 3, new Scalar(35, 35, 35), -1);
                return Pixels.Resize(raw, pool, width, roi, now, ++id);
            }
        }

        public void Dispose()
        {
            lock (gate)
            {
                if (closed)
                    return;
                closed = true;
                move = new Vec();
                raw.Dispose();
            }
        }
    }
}
