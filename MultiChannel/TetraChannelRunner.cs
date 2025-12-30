using SDRSharp.Common;
using SDRSharp.Radio;
using System;
using System.Windows.Forms;

namespace SDRSharp.Tetra.MultiChannel
{
    public unsafe class TetraChannelRunner : IWideIqSink, ITetraDecoderHost, IDisposable
    {
        private readonly ISharpControl _control;
        private readonly TetraPanel _panel;
        private ChannelSettings _settings;

        private readonly ComplexDdcResampler _ddc;
        private readonly SimpleAgc _agc;

        private double _lastFs;
        private long _lastCenterHz;

        // Small reusable buffer for resampled output
        private UnsafeBuffer _outBuf;
        private Complex* _outPtr;

        public Guid Id => _settings.Id;
        public string Name => _settings.Name;
        public long FrequencyHz => _settings.FrequencyHz;
        public bool Enabled => _settings.Enabled;

        public bool MmOnlyMode => _settings.MmOnlyMode;

        public UserControl Gui => _panel;

        public TetraChannelRunner(ISharpControl control, ChannelSettings settings)
        {
            _control = control;
            _settings = settings;

            _panel = new TetraPanel(_control, externalIq: true);

            // Apply decoder host linkage (TetraPanel already exposes MmOnlyMode itself,
            // but TetraDecoder was patched to depend only on ITetraDecoderHost)
            // Nothing else needed here.

            _ddc = new ComplexDdcResampler(targetSampleRate: 72000.0);
            _agc = new SimpleAgc
            {
                Enabled = settings.AgcEnabled,
                TargetRms = settings.AgcTargetRms,
                Attack = settings.AgcAttack,
                Decay = settings.AgcDecay
            };

            EnsureOutBuffer(8192);
        }

        public void UpdateSettings(ChannelSettings settings)
        {
            _settings = settings;
            _agc.Enabled = settings.AgcEnabled;
            _agc.TargetRms = settings.AgcTargetRms;
            _agc.Attack = settings.AgcAttack;
            _agc.Decay = settings.AgcDecay;
        }

        private void EnsureOutBuffer(int complexCount)
        {
            if (_outBuf != null && _outBuf.Length >= complexCount)
                return;

            _outBuf?.Dispose();
            _outBuf = UnsafeBuffer.Create(complexCount, sizeof(Complex));
            _outPtr = (Complex*)_outBuf;
        }

        public void OnWideIq(Complex* samples, double samplerate, int length)
        {
            if (!_settings.Enabled) return;
            if (_settings.FrequencyHz <= 0) return;

            var centerHz = _control.Frequency;

            // (Re)configure when sample rate or center changes significantly
            if (Math.Abs(samplerate - _lastFs) > 1 || centerHz != _lastCenterHz)
            {
                _lastFs = samplerate;
                _lastCenterHz = centerHz;
                var offset = (double)(_settings.FrequencyHz - centerHz);
                _ddc.Configure(samplerate, offset);
            }

            EnsureOutBuffer(8192);

            // Process in chunks to keep stackalloc small in ddc
            const int chunk = 4096;
            int idx = 0;
            while (idx < length)
            {
                int n = Math.Min(chunk, length - idx);
                int produced = _ddc.Process(samples + idx, n, _outPtr, 8192);
                if (produced > 0)
                {
                    _agc.Process(_outPtr, produced);
                    _panel.FeedIq(_outPtr, _ddc.TargetSampleRate, produced);
                }
                idx += n;
            }
        }

        public void Dispose()
        {
            try { _panel?.SaveSettings(); } catch { }
            try { _outBuf?.Dispose(); } catch { }
        }
    }
}
