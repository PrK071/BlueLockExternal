// C# | Tests/Navigation/SettlementTests.cs | .NET Framework 4.8 x64 | Reproduces residual W contaminating the old A measurement.
using System;
using BlueLockV2.Core;
using BlueLockV2.Navigation;

namespace BlueLockV2.NavigationAcceptance
{
    static class SettlementTests
    {
        static NavigationSample Sample(double time, long sequence, Vec position)
        { return new NavigationSample { Time = time, Sequence = sequence, Player = position, Confidence = 1, CameraConfidence = 1, Basis = MotionBasis.CameraXZ(new Vec(1,0),new Vec(0,-1),16,1) }; }
        static void Check(bool value, string message) { if (!value) throw new Exception(message); }
        public static void Run(Action<string, Action> test)
        {
            test("settling excludes residual W before independent A", delegate
            {
                // Real CSV: A contained (-2.8168,-1.6617); the previous W baseline was still changing.
                Vec wPartial = new Vec(-8.7811441421508789,-11.283663749694824);
                Vec wStopped = wPartial + new Vec(0,-1.66166877746582);
                var gate = new MotionSettlement(0);
                Check(gate.Observe(Sample(0,1,wPartial),0) == null, "first sample counted as stable");
                Check(gate.Observe(Sample(.2,2,wStopped),.2) == null, "residual W accepted");
                Check(gate.Observe(Sample(.4,3,wStopped),.4) == null, "short stationary window");
                Check(gate.Observe(Sample(.6,4,wStopped),.6) == null, "two intervals only");
                var baseline = gate.Observe(Sample(.8,5,wStopped),.8);
                Check(baseline != null, "no stationary baseline");
                Vec afterA = wStopped + new Vec(-2.816815376281738,0);
                Check(MotionBasis.Angle(afterA-baseline.Player,new Vec(-1,0)) < .001, "W leaked into A");
                Check(MotionBasis.Angle(afterA-wPartial,new Vec(-1,0)) > 30, "old contamination not reproduced");
            });
            test("settling rejects repeated stale and pre-release samples", delegate
            {
                var gate = new MotionSettlement(1);
                Check(gate.Observe(Sample(.9,1,new Vec()),1) == null, "old baseline accepted");
                var sample = Sample(1,2,new Vec());
                for (int i=0;i<20;i++) Check(gate.Observe(sample,1+i*.01)==null,"same packet advanced stability");
                Check(gate.Observe(sample,2)==null,"stale packet accepted");
            });
            test("settling resets when camera moves or observation is lost", delegate
            {
                var gate = new MotionSettlement(0);
                gate.Observe(Sample(0,1,new Vec()),0); gate.Observe(Sample(.2,2,new Vec()),.2);
                var sample = Sample(.4,3,new Vec()); sample.PlayerObserved=false;
                Check(gate.Observe(sample,.4)==null,"missing player accepted");
                Check(gate.Observe(Sample(.6,4,new Vec()),.6)==null,"did not reset");
                sample=Sample(.8,5,new Vec()); sample.Basis=MotionBasis.CameraXZ(new Vec(0,1),new Vec(1,0),16,1);
                Check(gate.Observe(sample,.8)==null,"camera rotation accepted");
            });
            test("settling requires speed below threshold across fresh packets", delegate
            {
                var gate = new MotionSettlement(0);
                for(int i=0;i<20;i++) Check(gate.Observe(Sample(i*.2,i,new Vec(i*.1,0)),i*.2)==null,"moving player accepted");
            });
        }
    }
}
