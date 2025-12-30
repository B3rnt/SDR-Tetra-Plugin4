using System;
using SDRSharp.Radio;

namespace SDRSharp.Tetra.MultiChannel
{
    /// <summary>
    /// Very lightweight complex AGC for per-channel leveling.
    /// </summary>
    public unsafe class SimpleAgc
    {
        public bool Enabled { get; set; } = true;
        public float TargetRms { get; set; } = 0.25f;
        public float Attack { get; set; } = 0.02f;
        public float Decay { get; set; } = 0.002f;

        private float _gain = 1.0f;

        public void Process(Complex* buf, int length)
        {
            if (!Enabled || length <= 0) return;

            // Estimate RMS (cheap)
            double acc = 0;
            for (int i = 0; i < length; i++)
            {
                var re = buf[i].Real;
                var im = buf[i].Imag;
                acc += (re * re + im * im);
            }
            var rms = (float)Math.Sqrt(acc / length);
            if (rms <= 1e-12f) return;

            var desired = TargetRms / rms;
            // Smooth gain
            if (desired < _gain)
                _gain += (desired - _gain) * Attack;
            else
                _gain += (desired - _gain) * Decay;

            for (int i = 0; i < length; i++)
            {
                buf[i].Real *= _gain;
                buf[i].Imag *= _gain;
            }
        }
    }
}
