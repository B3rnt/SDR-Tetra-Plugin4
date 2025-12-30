using SDRSharp.Radio;

namespace SDRSharp.Tetra.MultiChannel
{
    public unsafe class WideIqProcessor : IIQProcessor
    {
        public delegate void IQReadyDelegate(Complex* buffer, double samplerate, int length);
        public event IQReadyDelegate IQReady;

        private double _sampleRate;
        private bool _enabled;

        public double SampleRate
        {
            get => _sampleRate;
            set => _sampleRate = value;
        }

        public bool Enabled
        {
            get => _enabled;
            set => _enabled = value;
        }

        public void Process(Complex* buffer, int length)
        {
            IQReady?.Invoke(buffer, _sampleRate, length);
        }
    }
}
