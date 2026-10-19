// C# | Navigation/NavigationController.cs | .NET Framework 4.8 x64 | Explicit control bases and closed-loop bounded navigation pulses.
using System;
using BlueLockV2.Core;

namespace BlueLockV2.Navigation
{
    public enum NavigationSpace { WorldXZ, CalibratedPlane }

    public sealed class MotionBasis
    {
        public readonly Vec Right, Forward;
        public readonly double Confidence;
        public MotionBasis(Vec right, Vec forward, double confidence)
        { Right = right; Forward = forward; Confidence = confidence; }
        public bool Valid
        {
            get { return Finite(Right.X) && Finite(Right.Y) && Finite(Forward.X) && Finite(Forward.Y) &&
                Confidence >= .8 && Right.Length > .01 && Forward.Length > .01 &&
                Math.Abs(Right.X * Forward.Y - Forward.X * Right.Y) / (Right.Length * Forward.Length) > .2; }
        }
        public static bool Finite(double value) { return !Double.IsNaN(value) && !Double.IsInfinity(value); }
        public Vec Displacement(Vec control) { return Right * control.X + Forward * control.Y; }
        public Vec Inverse(Vec displacement)
        {
            if (!Valid) return new Vec();
            double determinant = Right.X * Forward.Y - Forward.X * Right.Y;
            return new Vec((displacement.X * Forward.Y - Forward.X * displacement.Y) / determinant,
                (Right.X * displacement.Y - displacement.X * Right.Y) / determinant);
        }
        public double Heading { get { return Math.Atan2(Forward.Y, Forward.X); } }
        public static double Angle(Vec a, Vec b)
        {
            if (a.Length < .0001 || b.Length < .0001) return 0;
            return Math.Acos(Numeric.Clamp((a.X * b.X + a.Y * b.Y) / (a.Length * b.Length), -1, 1)) * 180 / Math.PI;
        }
        public static MotionBasis CameraXZ(Vec right, Vec look, double speed, double confidence)
        {
            // Flatten X/Z explicitly; image Y and world altitude never enter this transform.
            if (right.Length < .1 || look.Length < .1 || speed <= 0)
                return new MotionBasis(new Vec(), new Vec(), 0);
            Vec r = right.Normalized;
            Vec f = look - r * (look.X * r.X + look.Y * r.Y);
            return new MotionBasis(r * speed, f.Normalized * speed, f.Length < .1 ? 0 : confidence);
        }
    }

    public sealed class NavigationSample
    {
        public double Time, Confidence, CameraConfidence, ArrivalRadius = 1.2;
        public long Sequence;
        public int TargetId = 1;
        public Vec Player, Target, TargetVelocity;
        public bool PlayerObserved = true, TargetObserved = true, Grounded = true, CameraChanged;
        public NavigationSpace Space;
        public MotionBasis Basis;
    }

    public sealed class NavigationTrace
    {
        public long Revision;
        public double Time, Distance, DirectionError, MotionError;
        public Vec Player, Target, Intended, Expected, Observed;
        public double CameraHeading, CameraConfidence;
        public string State, Reason;
    }

    public sealed class NavigationController
    {
        ActionIntent pulse;
        NavigationSample start;
        Vec previousControl;
        double pulseEnd, settleUntil, lastIssued = -10;
        long consumed = -1, revision;
        int targetId, noMotion;
        readonly bool settledWorldFeedback;
        MotionSettlement responseSettlement;
        volatile bool stopped;
        bool fault;
        public NavigationTrace Trace { get; private set; }
        public bool Faulted { get { return fault; } }
        public NavigationController(bool settledWorldFeedback = false) { this.settledWorldFeedback = settledWorldFeedback; }
        public void EmergencyStop() { stopped = true; }
        public void Reset() { stopped = fault = false; noMotion = 0; pulse = null; consumed = -1; targetId = 0; previousControl = new Vec(); responseSettlement = null; start = null; settleUntil = 0; lastIssued = -10; }

        static Vec EffectiveVelocity(NavigationSample sample, Vec control)
        {
            // Roblox normalizes simultaneous movement keys; a diagonal is not sqrt(2) times WalkSpeed.
            if (sample.Space == NavigationSpace.WorldXZ && control.Length > 1) control = control.Normalized;
            return sample.Basis.Displacement(control);
        }

        ActionIntent Wait(double now, NavigationSample sample, string state, string reason)
        {
            Publish(now, sample, state, reason, new Vec(), new Vec(), new Vec(), 0, 0);
            return ActionIntent.Wait(now, reason);
        }
        void Publish(double now, NavigationSample sample, string state, string reason, Vec command, Vec expected, Vec observed, double angle, double error)
        {
            Trace = new NavigationTrace { Revision = ++revision, Time = now, State = state, Reason = reason,
                Player = sample == null ? new Vec() : sample.Player, Target = sample == null ? new Vec() : sample.Target,
                Distance = sample == null ? 0 : Vec.Distance(sample.Player, sample.Target), Intended = command,
                Expected = expected, Observed = observed, DirectionError = angle, MotionError = error,
                CameraHeading = sample == null || sample.Basis == null ? 0 : sample.Basis.Heading,
                CameraConfidence = sample == null ? 0 : sample.CameraConfidence };
        }

        public ActionIntent Update(NavigationSample sample, double now)
        {
            if (stopped) return Wait(now, sample, "STOPPED", "F12; controlador bloqueado até reiniciar");
            bool valid = sample != null && now - sample.Time <= .35 && sample.Time <= now + .03 &&
                sample.Confidence >= .8 && sample.CameraConfidence >= .8 && sample.PlayerObserved && sample.TargetObserved && sample.Grounded &&
                sample.Basis != null && sample.Basis.Valid && MotionBasis.Finite(sample.Player.X) && MotionBasis.Finite(sample.Player.Y) &&
                MotionBasis.Finite(sample.Target.X) && MotionBasis.Finite(sample.Target.Y);
            if (!valid)
            {
                pulse = null;
                consumed = sample == null ? consumed : Math.Max(consumed, sample.Sequence);
                string reason = sample == null ? "telemetria ausente" :
                    now - sample.Time > .35 ? "telemetria antiga; aguardar pacote novo" :
                    !sample.PlayerObserved ? "posição local não observada ou cenário sem foco" :
                    !sample.TargetObserved ? "alvo não observado" :
                    !sample.Grounded ? "personagem no ar; aguardar contato com chão" :
                    "posição, orientação ou confiança insuficiente";
                return Wait(now, sample, "UNCERTAIN", reason);
            }
            if (fault) return Wait(now, sample, "RECALIBRATE", "deslocamento incompatível; recalibração necessária");
            if (pulse != null && start.Space != sample.Space)
            {
                pulse = null; fault = true;
                return Wait(now, sample, "RECALIBRATE", "referencial mudou durante o movimento");
            }
            if (sample.CameraChanged || (start != null && pulse != null && MotionBasis.Angle(start.Basis.Forward, sample.Basis.Forward) > 8))
            {
                pulse = null;
                settleUntil = now + .2;
                return Wait(now, sample, "CAMERA_CHANGED", "câmera mudou; liberar e reobservar o referencial");
            }
            if (targetId != sample.TargetId)
            {
                targetId = sample.TargetId;
                pulse = null;
                previousControl = new Vec();
                settleUntil = now + .1;
            }
            if (pulse != null)
            {
                if (now < pulseEnd) return pulse;
                if (sample.Sequence <= start.Sequence || sample.Time < pulseEnd + .04)
                    return Wait(now, sample, "OBSERVING", "pulso terminado; aguardando deslocamento observado");
                Vec observed = sample.Player - start.Player;
                Vec control = new Vec(pulse.Direction.X, -pulse.Direction.Y);
                Vec expected = EffectiveVelocity(start, control) * (pulseEnd - pulse.Timestamp);
                double angle = MotionBasis.Angle(expected, observed);
                double error = (expected - observed).Length;
                if (settledWorldFeedback && sample.Space == NavigationSpace.WorldXZ)
                {
                    bool stationary = responseSettlement.Observe(sample, now) != null;
                    bool responded = observed.Length >= Math.Max(.03, expected.Length * .12);
                    // A fresh HTTP packet can still contain an avatar response that has not completed.
                    // Do not stack another pulse or diagnose inversion from that transient sample.
                    if (!stationary || (!responded && now < pulseEnd + 1.5))
                    {
                        if (now > pulseEnd + 6)
                        {
                            pulse = null; fault = true;
                            return Wait(now, sample, "RECALIBRATE", "resposta não estabilizou em seis segundos; entradas liberadas");
                        }
                        return Wait(now, sample, "OBSERVING", "entradas liberadas; aguardando resposta e estabilidade medidas após o pulso");
                    }
                }
                noMotion = observed.Length < Math.Max(.03, expected.Length * .12) ? noMotion + 1 : 0;
                bool incompatible = (observed.Length >= Math.Max(.03, expected.Length * .12) && angle > 70) || observed.Length > expected.Length * 3 + .5;
                if (incompatible || noMotion >= 3) fault = true;
                Publish(now, sample, fault ? "RECALIBRATE" : "FEEDBACK", settledWorldFeedback ? "comparação após resposta estabilizada" : "comparação após pulso", control, expected, observed, angle, error);
                pulse = null;
                consumed = sample.Sequence;
                settleUntil = now + .06;
                return ActionIntent.Wait(now, fault ? "erro de movimento; recalibrar" : "feedback observado; liberar antes do próximo pulso");
            }
            if (sample.Sequence <= consumed || now < settleUntil)
                return Wait(now, sample, "SETTLING", "aguardando observação nova; sem renovar comando antigo");
            consumed = sample.Sequence;
            Vec delta = sample.Target - sample.Player;
            if (delta.Length <= sample.ArrivalRadius)
                return Wait(now, sample, "REACHED", "alvo alcançado no referencial calibrado");
            // Modest lead uses real timestamps and velocity in this same calibrated space only.
            Vec desired = delta + sample.TargetVelocity * .1;
            Vec continuous = sample.Basis.Inverse(desired);
            double magnitude = Math.Max(Math.Abs(continuous.X), Math.Abs(continuous.Y));
            if (magnitude < .000001) return Wait(now, sample, "UNCERTAIN", "transformação degenerada");
            continuous /= magnitude;
            Vec command = new Vec(Math.Abs(continuous.X) > .42 ? Math.Sign(continuous.X) : 0,
                Math.Abs(continuous.Y) > .42 ? Math.Sign(continuous.Y) : 0);
            bool reversing = previousControl.X * command.X < 0 || previousControl.Y * command.Y < 0;
            if (reversing && now - lastIssued < .28)
                return Wait(now, sample, "HYSTERESIS", "intervalo neutro antes da inversão");
            Vec velocity = EffectiveVelocity(sample, command);
            if ((velocity.X * delta.X + velocity.Y * delta.Y) <= 0)
                return Wait(now, sample, "UNCERTAIN", "nenhum comando quantizado reduz o erro");
            double duration = Numeric.Clamp((delta.Length - sample.ArrivalRadius * .6) / Math.Max(.01, velocity.Length), .06, .12);
            start = sample;
            lastIssued = now;
            previousControl = command;
            pulseEnd = now + duration;
            responseSettlement = new MotionSettlement(pulseEnd + .04);
            // InputScheduler's contract is X=D+, Y=S+. The calibrated controller's second axis is W+.
            pulse = new ActionIntent(IntentKind.MOVE, sample.Target, new Vec(command.X, -command.Y), now,
                duration, sample.Confidence, 50, "pulso de navegação em malha fechada");
            Publish(now, sample, "PULSE", pulse.Reason, command, velocity * duration, new Vec(), 0, 0);
            return pulse;
        }
    }

    public sealed class MovementCalibration
    {
        readonly Vec[] velocities = new Vec[4];
        readonly bool[] present = new bool[4];
        public void Add(int index, Vec before, Vec after, double seconds, double confidence)
        {
            if (index < 0 || index > 3 || seconds < .05 || seconds > 1 || confidence < .8)
                throw new ArgumentException("Amostra de calibração inválida.");
            velocities[index] = (after - before) / seconds;
            present[index] = true;
        }
        public MotionBasis Build()
        {
            for (int i = 0; i < 4; i++) if (!present[i]) throw new InvalidOperationException("Teste W, A, S e D separadamente.");
            // Samples are W, A, S, D. Require inverse pairs and non-collinear axes.
            if (MotionBasis.Angle(velocities[0], velocities[2] * -1) > 15 || MotionBasis.Angle(velocities[1], velocities[3] * -1) > 15)
                throw new InvalidOperationException("Pares opostos inconsistentes; câmera/colisão durante calibração.");
            var basis = new MotionBasis((velocities[3] - velocities[1]) * .5, (velocities[0] - velocities[2]) * .5, .95);
            if (!basis.Valid) throw new InvalidOperationException("Movimento não observável ou eixos degenerados.");
            return basis;
        }
    }
}
