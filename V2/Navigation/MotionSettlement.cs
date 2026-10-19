// C# | Navigation/MotionSettlement.cs | .NET Framework 4.8 x64 | Fresh measured stillness between independent calibration pulses.
using System;
using BlueLockV2.Core;

namespace BlueLockV2.Navigation
{
    public sealed class MotionSettlement
    {
        readonly double notBefore;
        NavigationSample previous;
        long sequence = -1;
        double stableSince;
        int intervals;
        public MotionSettlement(double notBefore) { this.notBefore = notBefore; }
        public NavigationSample Observe(NavigationSample sample, double now)
        {
            if (sample == null || sample.Time < notBefore || now - sample.Time > .35 || sample.Time > now + .03 ||
                !sample.PlayerObserved || !sample.Grounded || sample.Confidence < .8 || sample.CameraConfidence < .8 ||
                sample.Basis == null || !sample.Basis.Valid || !MotionBasis.Finite(sample.Player.X) || !MotionBasis.Finite(sample.Player.Y))
            { previous = null; intervals = 0; return null; }
            // Re-reading the same network packet never counts as another observation.
            if (sample.Sequence <= sequence) return null;
            sequence = sample.Sequence;
            double dt = previous == null ? 0 : sample.Time - previous.Time;
            bool stable = previous != null && dt > 0 && dt <= .4 && sample.Space == previous.Space &&
                MotionBasis.Angle(sample.Basis.Forward, previous.Basis.Forward) < 1 &&
                MotionBasis.Angle(sample.Basis.Right, previous.Basis.Right) < 1 &&
                Vec.Distance(sample.Player, previous.Player) / dt <= .18;
            if (!stable) { stableSince = sample.Time; intervals = 0; }
            else intervals++;
            previous = sample;
            return intervals >= 3 && sample.Time - stableSince >= .55 ? sample : null;
        }
    }
}
