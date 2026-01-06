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
        private readonly bool _persistSettingsOnDispose;

        private double _lastFs;
        private long _lastCenterHz;
	    private double _afcHz; // per-channel AFC correction in Hz
	    private readonly object _afcLock = new object();

        // Small reusable buffer for resampled output
        private UnsafeBuffer _outBuf;
        private Complex* _outPtr;

        public Guid Id => _settings.Id;
        public string Name => _settings.Name;
        public long FrequencyHz => _settings.FrequencyHz;
        public bool Enabled => _settings.Enabled;

        public bool MmOnlyMode => _settings.MmOnlyMode;

        public UserControl Gui => _panel;

        public TetraPanel Panel => _panel;

        public TetraChannelRunner(ISharpControl control, ChannelSettings settings, bool persistSettingsOnDispose = true)
        {
            _control = control;
            _settings = settings;
            _persistSettingsOnDispose = persistSettingsOnDispose;

	        _panel = new TetraPanel(_control, externalIq: true);
	        _panel.AfcCorrectionRequested = ApplyAfcCorrection;
            _panel.SetExternalFrequency(settings.FrequencyHz);

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

	    /// <summary>
	    /// Apply fine AFC correction for this channel only. The value passed is in Hz and is
	    /// derived from the decoder's frequency error estimate. We clamp and smooth it to
	    /// avoid instability.
	    /// </summary>
	    private void ApplyAfcCorrection(double hz)
	    {
	        // Clamp step to avoid sudden jumps
	        const double maxStep = 50.0; // Hz per tick
	        if (hz > maxStep) hz = maxStep;
	        else if (hz < -maxStep) hz = -maxStep;

	        lock (_afcLock)
	        {
	            // Integrate with a little smoothing
	            _afcHz = (_afcHz * 0.9) + (hz * 0.1);

	            // Reconfigure DDC immediately if we're already configured
	            if (_lastFs > 0 && _lastCenterHz != 0)
	            {
	                var offset = (double)(_settings.FrequencyHz - _lastCenterHz) - _afcHz;
	                _ddc.Configure(_lastFs, offset);
	            }
	        }
	    }

        public void UpdateSettings(ChannelSettings settings)
        {
            _settings = settings;
            _panel.SetExternalFrequency(settings.FrequencyHz);
            _agc.Enabled = settings.AgcEnabled;
            _agc.TargetRms = settings.AgcTargetRms;
            _agc.Attack = settings.AgcAttack;
            _agc.Decay = settings.AgcDecay;
        }


        private static long GetCenterFrequencyHz(ISharpControl control)
        {
            try
            {
                var t = control.GetType();

                // Try common property names used across SDR# builds/forks.
                foreach (var name in new[]
                {
                    "CenterFrequency",
                    "LOFrequency",
                    "RfFrequency",
                    "RFFrequency",
                    "RadioFrequency",
                    "DeviceFrequency",
                    "HardwareFrequency"
                })
                {
                    var p = t.GetProperty(name);
                    if (p == null) continue;
                    var v = p.GetValue(control, null);
                    if (v is long l) return l;
                    if (v is int i) return i;
                    if (v is double d) return (long)d;
                }

                // Heuristic: any property containing "Center" and "Frequency".
                foreach (var p in t.GetProperties())
                {
                    var n = p.Name;
                    if (n.IndexOf("Center", StringComparison.OrdinalIgnoreCase) >= 0 &&
                        n.IndexOf("Freq", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        var v = p.GetValue(control, null);
                        if (v is long l) return l;
                        if (v is int i) return i;
                        if (v is double d) return (long)d;
                    }
                }

                // Fall back to the currently tuned frequency.
                return control.Frequency;
            }
            catch
            {
                return control.Frequency;
            }
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

            var centerHz = GetCenterFrequencyHz(_control);


            // (Re)configure when sample rate or center changes significantly
	            if (Math.Abs(samplerate - _lastFs) > 1 || centerHz != _lastCenterHz)
            {
                _lastFs = samplerate;
                _lastCenterHz = centerHz;
	                double afc;
	                lock (_afcLock) afc = _afcHz;
	                var offset = (double)(_settings.FrequencyHz - centerHz) - afc;
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
            if (_persistSettingsOnDispose)
            {
                try { _panel?.SaveSettings(); } catch { }
            }
            try { _outBuf?.Dispose(); } catch { }
        }
    }
}