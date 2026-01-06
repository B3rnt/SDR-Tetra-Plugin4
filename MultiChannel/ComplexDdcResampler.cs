using System;
using SDRSharp.Radio;

namespace SDRSharp.Tetra.MultiChannel
{
    /// <summary>
    /// Optimized Downconverter for high-bandwidth inputs (Airspy/SDRPlay).
    /// Uses a 2-stage approach:
    /// 1. Mix + Boxcar Decimation (CIC-like) to reduce rate efficiently.
    /// 2. Sharp FIR + Linear Interpolation for final sample rate alignment.
    /// </summary>
    public unsafe class ComplexDdcResampler
    {
        private readonly double _targetFs;
        private double _inputFs;

        // NCO (Oscillator)
        private double _phase;
        private double _phaseInc;

        // Stage 1: Boxcar Decimator
        private int _decim1; // Primary decimation factor
        private int _decim1Counter;
        private float _boxcarAccumR;
        private float _boxcarAccumI;

        // Stage 2: FIR Filter
        private float[] _taps = Array.Empty<float>();
        private Complex[] _delay = Array.Empty<Complex>();
        private int _delayPos;

        // Fractional Resampler
        private double _intermediateFs;
        private double _fracPos;
        private Complex _last;

        public double TargetSampleRate => _targetFs;

        public ComplexDdcResampler(double targetSampleRate)
        {
            _targetFs = targetSampleRate;
        }

        public void Configure(double inputSampleRate, double freqOffsetHz)
        {
            _inputFs = inputSampleRate;
            
            // NCO Setup
            _phase = 0;
            _phaseInc = -2.0 * Math.PI * (freqOffsetHz / _inputFs);

            // STAGE 1: Calculate Boxcar Decimation
            // We want to bring the rate down to something manageable for the FIR filter,
            // typically around 4x - 8x the target rate.
            // E.g., Target 72k. We aim for intermediate ~300k-500k.
            // Airspy 6M / 20 = 300k.
            double desiredIntermediate = _targetFs * 4.0;
            _decim1 = (int)Math.Floor(_inputFs / desiredIntermediate);
            if (_decim1 < 1) _decim1 = 1;

            _intermediateFs = _inputFs / _decim1;
            _decim1Counter = 0;
            _boxcarAccumR = 0;
            _boxcarAccumI = 0;

            // STAGE 2: FIR Design (running at intermediate rate)
            // We need a clean cut for a 25kHz TETRA channel.
            // Cutoff ~15kHz, Stopband ~25kHz.
            double cutoff = 14000.0;     // 14 kHz pass
            double transition = 10000.0; // 10 kHz transition width

            var taps = DesignLowpass((cutoff / _intermediateFs), (transition / _intermediateFs));
            _taps = taps;
            _delay = new Complex[_taps.Length];
            _delayPos = 0;

            // Resampler state
            _fracPos = 0;
            _last = default;
        }

        private static float[] DesignLowpass(double normCutoff, double normTransition)
        {
            // At the intermediate rate (e.g. 300k), a transition of 10k is 0.033.
            // 4 / 0.033 = ~120 taps. This is very lightweight for the CPU.
            var width = Math.Max(1e-6, normTransition);
            var nTaps = (int)Math.Ceiling(4.0 / width);
            
            // Ensure odd number for symmetry
            if (nTaps % 2 == 0) nTaps++;
            
            // Clamp to reasonable limits (though with stage 1, we rarely hit the limit)
            nTaps = Math.Clamp(nTaps, 15, 511);

            var taps = new float[nTaps];
            int m = nTaps / 2;
            double sum = 0;
            
            for (int i = 0; i < nTaps; i++)
            {
                int k = i - m;
                // Sinc function
                double sinc = k == 0 ? 2.0 * Math.PI * normCutoff : Math.Sin(2.0 * Math.PI * normCutoff * k) / k;
                if (k == 0) sinc /= Math.PI; // normalize peak
                else sinc /= Math.PI;

                // Blackman window for better stopband attenuation
                double w = 0.42 - 0.5 * Math.Cos(2.0 * Math.PI * i / (nTaps - 1)) + 0.08 * Math.Cos(4.0 * Math.PI * i / (nTaps - 1));
                
                double v = sinc * w;
                taps[i] = (float)v;
                sum += v;
            }

            // Normalize for unity gain
            for (int i = 0; i < nTaps; i++)
                taps[i] /= (float)sum;

            return taps;
        }

        public int Process(Complex* input, int length, Complex* output, int outputCapacity)
        {
            if (length <= 0 || outputCapacity <= 0) return 0;

            int outCount = 0;
            double ratio = _intermediateFs / _targetFs;

            // Local cache for performance
            int d1 = _decim1;
            float[] taps = _taps;
            int tapsLen = taps.Length;
            int delayLen = _delay.Length;

            for (int i = 0; i < length; i++)
            {
                // 1. NCO Mixer
                double c = Math.Cos(_phase);
                double s = Math.Sin(_phase);
                _phase += _phaseInc;
                if (_phase > Math.PI) _phase -= 2 * Math.PI;
                else if (_phase < -Math.PI) _phase += 2 * Math.PI;

                float xr = input[i].Real;
                float xi = input[i].Imag;

                // Mix
                float mixR = (float)(xr * c - xi * s);
                float mixI = (float)(xr * s + xi * c);

                // 2. Boxcar Decimator (Sum and Dump)
                _boxcarAccumR += mixR;
                _boxcarAccumI += mixI;
                _decim1Counter++;

                if (_decim1Counter < d1) continue;

                // Decimation point reached: Average and push to FIR
                // (Normalization happens implicitly via FIR gain, but averaging keeps values sane)
                float sampleR = _boxcarAccumR / d1;
                float sampleI = _boxcarAccumI / d1;
                
                _boxcarAccumR = 0;
                _boxcarAccumI = 0;
                _decim1Counter = 0;

                // 3. FIR Filter
                _delay[_delayPos].Real = sampleR;
                _delay[_delayPos].Imag = sampleI;
                _delayPos++;
                if (_delayPos >= delayLen) _delayPos = 0;

                double firR = 0;
                double firI = 0;
                int idx = _delayPos;

                // Convolution
                for (int t = 0; t < tapsLen; t++)
                {
                    idx--;
                    if (idx < 0) idx = delayLen - 1;
                    float tap = taps[t];
                    firR += _delay[idx].Real * tap;
                    firI += _delay[idx].Imag * tap;
                }

                // 4. Fractional Resampler (Linear Interpolation)
                // We have a new "clean" sample at intermediate rate: (firR, firI)
                Complex current;
                current.Real = (float)firR;
                current.Imag = (float)firI;

                while (_fracPos <= 1.0 && outCount < outputCapacity)
                {
                    float alpha = (float)_fracPos;
                    // Interpolate between _last and current
                    output[outCount].Real = _last.Real + (current.Real - _last.Real) * alpha;
                    output[outCount].Imag = _last.Imag + (current.Imag - _last.Imag) * alpha;
                    outCount++;
                    _fracPos += ratio;
                }

                _fracPos -= 1.0;
                _last = current;
            }

            return outCount;
        }
    }
}
