using System;
using SDRSharp.Radio;

namespace SDRSharp.Tetra.MultiChannel
{
    /// <summary>
    /// Geoptimaliseerde DDC voor hoge bandbreedtes (Airspy/SDRPlay).
    /// Gebruikt een 2-traps aanpak:
    /// 1. Mix + Boxcar Decimation (CIC-achtig) om de snelheid grof en efficient te verlagen.
    /// 2. Scherp FIR Filter + Lineaire Interpolatie voor de laatste stap naar de doel-rate.
    /// </summary>
    public unsafe class ComplexDdcResampler
    {
        private readonly double _targetFs;
        private double _inputFs;

        // NCO (De frequentie verschuiver)
        private double _phase;
        private double _phaseInc;

        // Stage 1: Boxcar Decimator (Grof)
        private int _decim1; 
        private int _decim1Counter;
        private float _boxcarAccumR;
        private float _boxcarAccumI;

        // Stage 2: FIR Filter (Fijn)
        private float[] _taps = Array.Empty<float>();
        private Complex[] _delay = Array.Empty<Complex>();
        private int _delayPos;

        // Stage 3: Fractional Resampler (Finetuning)
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
            
            // NCO instellen
            _phase = 0;
            _phaseInc = -2.0 * Math.PI * (freqOffsetHz / _inputFs);

            // STAP 1: Bereken Boxcar Decimatie
            // We willen de snelheid omlaag brengen naar een tussen-niveau dat het FIR filter aankan.
            // Richtwaarde: 4x tot 8x de doelsnelheid. (bijv. 300k - 500k Hz)
            double desiredIntermediate = _targetFs * 4.0;
            _decim1 = (int)Math.Floor(_inputFs / desiredIntermediate);
            if (_decim1 < 1) _decim1 = 1;

            _intermediateFs = _inputFs / _decim1;
            _decim1Counter = 0;
            _boxcarAccumR = 0;
            _boxcarAccumI = 0;

            // STAP 2: FIR Design (werkt nu op de veel lagere intermediate rate)
            // Hierdoor kunnen we een perfecte cut-off maken voor 25kHz TETRA.
            double cutoff = 14000.0;     // 14 kHz doorlaat (helft van 28k)
            double transition = 10000.0; // 10 kHz overgangsgebied

            // Omdat de sample rate nu laag is, is dit filter heel scherp zonder duizenden taps.
            var taps = DesignLowpass((cutoff / _intermediateFs), (transition / _intermediateFs));
            _taps = taps;
            _delay = new Complex[_taps.Length];
            _delayPos = 0;

            // Reset resampler
            _fracPos = 0;
            _last = default;
        }

        private static float[] DesignLowpass(double normCutoff, double normTransition)
        {
            // Bereken benodigd aantal taps
            var width = Math.Max(1e-6, normTransition);
            var nTaps = (int)Math.Ceiling(4.0 / width);
            
            // Zorg voor oneven aantal (symmetrie)
            if (nTaps % 2 == 0) nTaps++;
            
            // Veiligheidsgrenzen (hoewel we met stage 1 zelden de limiet raken)
            nTaps = Math.Clamp(nTaps, 15, 511);

            var taps = new float[nTaps];
            int m = nTaps / 2;
            double sum = 0;
            
            for (int i = 0; i < nTaps; i++)
            {
                int k = i - m;
                // Sinc functie
                double sinc = k == 0 ? 2.0 * Math.PI * normCutoff : Math.Sin(2.0 * Math.PI * normCutoff * k) / k;
                if (k == 0) sinc /= Math.PI;
                else sinc /= Math.PI;

                // Blackman window voor betere demping van ruis (beter dan Hamming)
                double w = 0.42 - 0.5 * Math.Cos(2.0 * Math.PI * i / (nTaps - 1)) + 0.08 * Math.Cos(4.0 * Math.PI * i / (nTaps - 1));
                
                double v = sinc * w;
                taps[i] = (float)v;
                sum += v;
            }

            // Normaliseren voor unity gain (geen volume verlies)
            for (int i = 0; i < nTaps; i++)
                taps[i] /= (float)sum;

            return taps;
        }

        public int Process(Complex* input, int length, Complex* output, int outputCapacity)
        {
            if (length <= 0 || outputCapacity <= 0) return 0;

            int outCount = 0;
            double ratio = _intermediateFs / _targetFs;

            // Lokale variabelen voor snelheid
            int d1 = _decim1;
            float[] taps = _taps;
            int tapsLen = taps.Length;
            int delayLen = _delay.Length;

            for (int i = 0; i < length; i++)
            {
                // 1. NCO Mixer (Frequentie shift)
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

                // 2. Boxcar Decimator (Integrate & Dump)
                // Dit telt samples bij elkaar op en dumpt ze 1x per d1 samples.
                // Dit werkt als een simpel low-pass filter en decimeert tegelijk.
                _boxcarAccumR += mixR;
                _boxcarAccumI += mixI;
                _decim1Counter++;

                if (_decim1Counter < d1) continue;

                // We hebben een "grof" sample: gemiddelde berekenen
                float sampleR = _boxcarAccumR / d1;
                float sampleI = _boxcarAccumI / d1;
                
                _boxcarAccumR = 0;
                _boxcarAccumI = 0;
                _decim1Counter = 0;

                // 3. FIR Filter (Scherp filteren op tussen-snelheid)
                _delay[_delayPos].Real = sampleR;
                _delay[_delayPos].Imag = sampleI;
                _delayPos++;
                if (_delayPos >= delayLen) _delayPos = 0;

                double firR = 0;
                double firI = 0;
                int idx = _delayPos;

                // Convolutie (het eigenlijke filteren)
                for (int t = 0; t < tapsLen; t++)
                {
                    idx--;
                    if (idx < 0) idx = delayLen - 1;
                    float tap = taps[t];
                    firR += _delay[idx].Real * tap;
                    firI += _delay[idx].Imag * tap;
                }

                // 4. Fractional Resampler (Lineaire Interpolatie naar eind-snelheid)
                Complex current;
                current.Real = (float)firR;
                current.Imag = (float)firI;

                while (_fracPos <= 1.0 && outCount < outputCapacity)
                {
                    float alpha = (float)_fracPos;
                    // Interpoleer tussen vorige (_last) en huidige (current)
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
