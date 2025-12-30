using System;
using SDRSharp.Radio;

namespace SDRSharp.Tetra.MultiChannel
{
    /// <summary>
    /// Downconverts a wide IQ stream to a narrow channel and resamples to a target rate.
    /// Not a perfect SDR-grade resampler, but good enough for typical TETRA monitoring when
    /// the SDR# input is clean and not too close to Nyquist.
    /// </summary>
    public unsafe class ComplexDdcResampler
    {
        private readonly double _targetFs;
        private double _inputFs;

        // NCO
        private double _phase;
        private double _phaseInc;

        // FIR
        private float[] _taps = Array.Empty<float>();
        private Complex[] _delay = Array.Empty<Complex>();
        private int _delayPos;

        // Intermediate decimation
        private int _decim = 1;
        private int _decimCounter;

        // Fractional resampler state
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
            _phase = 0;
            _phaseInc = -2.0 * Math.PI * (freqOffsetHz / _inputFs);

            // Choose an intermediate decimation to reduce CPU.
            // We aim for ~200 kHz intermediate if possible.
            var desiredMid = 200000.0;
            _decim = Math.Max(1, (int)Math.Floor(_inputFs / desiredMid));
            // Keep decim reasonable
            if (_decim > 32) _decim = 32;

            // FIR lowpass before decimation
            // TETRA is 25 kHz channel. We'll keep 15 kHz cutoff.
            var cutoff = 15000.0;
            var transition = 5000.0;
            var taps = DesignLowpass((cutoff / _inputFs), (transition / _inputFs));
            _taps = taps;
            _delay = new Complex[_taps.Length];
            _delayPos = 0;
            _decimCounter = 0;

            _fracPos = 0;
            _last = default;
        }

        private static float[] DesignLowpass(double normCutoff, double normTransition)
        {
            // Windowed-sinc (Hamming). Keep taps modest to control CPU.
            // tap count grows as transition narrows.
            var width = Math.Max(1e-6, normTransition);
            var nTaps = (int)Math.Ceiling(4.0 / width);
            nTaps = Math.Clamp(nTaps, 63, 255);
            if (nTaps % 2 == 0) nTaps++;

            var taps = new float[nTaps];
            int m = nTaps / 2;
            double sum = 0;
            for (int i = 0; i < nTaps; i++)
            {
                int k = i - m;
                double sinc = k == 0 ? 2 * normCutoff : Math.Sin(2 * Math.PI * normCutoff * k) / (Math.PI * k);
                // Hamming window
                double w = 0.54 - 0.46 * Math.Cos(2 * Math.PI * i / (nTaps - 1));
                double v = sinc * w;
                taps[i] = (float)v;
                sum += v;
            }
            // Normalize DC gain
            for (int i = 0; i < nTaps; i++)
                taps[i] = (float)(taps[i] / sum);

            return taps;
        }

        /// <summary>
        /// Process input block and produce resampled baseband output into provided buffer.
        /// Returns number of produced samples.
        /// </summary>
        public int Process(Complex* input, int length, Complex* output, int outputCapacity)
        {
            if (length <= 0 || outputCapacity <= 0) return 0;

            // 1) Mix + FIR + decimate to mid-rate
            // We'll write decimated samples directly into a small temp buffer (stack alloc).
            int maxMid = Math.Min(outputCapacity * 4, length); // rough
            Complex* mid = stackalloc Complex[maxMid];
            int midCount = 0;

            for (int i = 0; i < length; i++)
            {
                // NCO multiply: x * e^{j phase}
                double c = Math.Cos(_phase);
                double s = Math.Sin(_phase);
                _phase += _phaseInc;
                if (_phase > Math.PI) _phase -= 2 * Math.PI;
                else if (_phase < -Math.PI) _phase += 2 * Math.PI;

                var xr = input[i].Real;
                var xi = input[i].Imag;

                Complex mixed;
                mixed.Real = (float)(xr * c - xi * s);
                mixed.Imag = (float)(xr * s + xi * c);

                // Push into FIR delay
                _delay[_delayPos] = mixed;
                _delayPos++;
                if (_delayPos >= _delay.Length) _delayPos = 0;

                // FIR output for every sample, but keep only every _decim
                // Decim counter
                if ((_decimCounter++ % _decim) != 0)
                    continue;

                // Convolve
                double accR = 0, accI = 0;
                int di = _delayPos;
                for (int t = 0; t < _taps.Length; t++)
                {
                    di--;
                    if (di < 0) di = _delay.Length - 1;
                    var tap = _taps[t];
                    accR += _delay[di].Real * tap;
                    accI += _delay[di].Imag * tap;
                }

                mid[midCount].Real = (float)accR;
                mid[midCount].Imag = (float)accI;
                midCount++;
                if (midCount >= maxMid) break;
            }

            if (midCount <= 1) return 0;

            // 2) Fractional resample from midFs to targetFs (linear)
            double midFs = _inputFs / _decim;
            double ratio = midFs / _targetFs; // input steps per output sample

            int outCount = 0;
            for (int i = 0; i < midCount && outCount < outputCapacity; i++)
            {
                var current = mid[i];
                // Generate output samples between last and current depending on _fracPos
                while (_fracPos <= 1.0 && outCount < outputCapacity)
                {
                    float a = (float)_fracPos;
                    output[outCount].Real = _last.Real + (current.Real - _last.Real) * a;
                    output[outCount].Imag = _last.Imag + (current.Imag - _last.Imag) * a;
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
