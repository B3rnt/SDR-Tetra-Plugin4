using System;
using System.Buffers;
using System.Collections.Generic;
using System.Threading;
using SDRSharp.Radio;

namespace SDRSharp.Tetra.MultiChannel
{
    public unsafe class WideIqSource : IDisposable
    {
        private readonly ISharpControl _control;
        private readonly WideIqProcessor _proc;

        private readonly object _lock = new();
        private readonly Queue<(Complex[] buf, int len, double fs)> _queue = new();
        private readonly List<IWideIqSink> _sinks = new();

        private Thread _worker;
        private volatile bool _running;

        /// <summary>
        /// Last observed IQ sample rate (Hz) from the wideband IQ stream.
        /// Not all SDR# builds expose a control-level SampleRate property,
        /// so we cache it here from the incoming IQ callback.
        /// </summary>
        public double LastSampleRate { get; private set; }

        public WideIqSource(ISharpControl control)
        {
            _control = control;
            _proc = new WideIqProcessor();
            _proc.IQReady += OnIqReady;
            _proc.Enabled = true;

            // Try to hook the widest available IQ.
            // NOTE: Some SDR# builds may not expose RawIQ; if you need to change this,
            // edit the ProcessorType below.
            _control.RegisterStreamHook(_proc, ProcessorType.RawIQ);

            _running = true;
            _worker = new Thread(Worker) { IsBackground = true, Name = "TetraWideIQ" };
            _worker.Start();
        }

        public void AddSink(IWideIqSink sink)
        {
            lock (_lock)
            {
                if (!_sinks.Contains(sink))
                    _sinks.Add(sink);
            }
        }

        public void RemoveSink(IWideIqSink sink)
        {
            lock (_lock)
            {
                _sinks.Remove(sink);
            }
        }

        /// <summary>
        /// Fallback sample-rate resolver.
        /// Some SDR# builds/forks (notably certain AIRSPY SDR# Studio builds)
        /// do not populate the IIQProcessor.SampleRate property for RawIQ stream hooks,
        /// resulting in a samplerate of 0 being passed to plugins. This breaks any
        /// downstream DDC/resampling.
        /// </summary>
        private double TryGetSampleRateFromControl()
        {
            try
            {
                var t = _control.GetType();

                // First try common well-known property names
                foreach (var name in new[] { "SampleRate", "SamplingRate", "IFSampleRate", "IfSampleRate", "InputSampleRate", "SampleRateHz", "SampleRateIn", "SampleRateOut" })
                {
                    var p = t.GetProperty(name);
                    if (p == null) continue;
                    var v = p.GetValue(_control, null);
                    if (v is double d && d > 0) return d;
                    if (v is float f && f > 0) return f;
                    if (v is int i && i > 0) return i;
                    if (v is long l && l > 0) return l;
                }

                // Some builds expose a nested Source/Frontend/Device object holding the sample rate
                foreach (var name in new[] { "Source", "Frontend", "Device", "Receiver", "Parent" })
                {
                    var p = t.GetProperty(name);
                    if (p == null) continue;
                    var src = p.GetValue(_control, null);
                    if (src == null) continue;
                    var st = src.GetType();

                    // Try common sample-rate property names on the nested object
                    foreach (var sn in new[] { "SampleRate", "SamplingRate", "SampleRateHz", "IFSampleRate" })
                    {
                        var sp = st.GetProperty(sn);
                        if (sp == null) continue;
                        var sv = sp.GetValue(src, null);
                        if (sv is double sd && sd > 0) return sd;
                        if (sv is float sf && sf > 0) return sf;
                        if (sv is int si && si > 0) return si;
                        if (sv is long sl && sl > 0) return sl;
                    }

                    // Generic one-level scan: any property name containing "sample", "rate" or "hz"
                    foreach (var sp in st.GetProperties())
                    {
                        var nm = sp.Name.ToLowerInvariant();
                        if (nm.Contains("sample") || nm.Contains("rate") || nm.Contains("hz"))
                        {
                            try
                            {
                                var sv = sp.GetValue(src, null);
                                if (sv is double sd2 && sd2 > 0) return sd2;
                                if (sv is float sf2 && sf2 > 0) return sf2;
                                if (sv is int si2 && si2 > 0) return si2;
                                if (sv is long sl2 && sl2 > 0) return sl2;
                            }
                            catch
                            {
                                // ignore property exceptions
                            }
                        }
                    }
                }

                // As a last resort, scan top-level properties generically
                foreach (var p in t.GetProperties())
                {
                    var n = p.Name.ToLowerInvariant();
                    if (n.Contains("sample") || n.Contains("rate") || n.Contains("hz"))
                    {
                        var v = p.GetValue(_control, null);
                        if (v is double d2 && d2 > 0) return d2;
                        if (v is float f2 && f2 > 0) return f2;
                        if (v is int i2 && i2 > 0) return i2;
                        if (v is long l2 && l2 > 0) return l2;
                    }
                }
            }
            catch
            {
                // swallow reflection exceptions - fallback to 0 below
            }

            return 0;
        }

        private void OnIqReady(Complex* samples, double samplerate, int length)
        {
            if (length <= 0) return;

            // Workaround for SDR# builds that pass 0Hz for RawIQ hooks.
            if (samplerate <= 0)
            {
                var fallback = TryGetSampleRateFromControl();
                if (fallback > 0)
                {
                    samplerate = fallback;
                }
                else if (LastSampleRate > 0)
                {
                    // Use cached value from previous good callbacks
                    samplerate = LastSampleRate;
                }
                else
                {
                    // No reliable sample-rate -> drop this block to avoid passing 0 to downstream DDC
                    // This prevents configuring the DDC with 0 and stops breaking resampling/decoding.
                    return;
                }
            }

            LastSampleRate = samplerate;

            var arr = ArrayPool<Complex>.Shared.Rent(length);
            fixed (Complex* dst = arr)
            {
                // Faster than element-by-element copy at high sample rates.
                Buffer.MemoryCopy(samples, dst, (long)length * sizeof(Complex), (long)length * sizeof(Complex));
            }

            lock (_lock)
            {
                _queue.Enqueue((arr, length, samplerate));
                Monitor.Pulse(_lock);
                // Limit backlog to avoid RAM spike
                while (_queue.Count > 32)
                {
                    var old = _queue.Dequeue();
                    ArrayPool<Complex>.Shared.Return(old.buf);
                }
            }
        }

        private void Worker()
        {
            while (_running)
            {
                (Complex[] buf, int len, double fs) item;
                List<IWideIqSink> sinksSnapshot;

                lock (_lock)
                {
                    while (_queue.Count == 0 && _running)
                        Monitor.Wait(_lock, 200);

                    if (!_running) break;
                    if (_queue.Count == 0) continue;

                    item = _queue.Dequeue();
                    sinksSnapshot = new List<IWideIqSink>(_sinks);
                }

                fixed (Complex* p = item.buf)
                {
                    for (int i = 0; i < sinksSnapshot.Count; i++)
                    {
                        try { sinksSnapshot[i].OnWideIq(p, item.fs, item.len); }
                        catch { /* isolate channel failures */ }
                    }
                }

                ArrayPool<Complex>.Shared.Return(item.buf);
            }
        }

        public void Dispose()
        {
            _running = false;
            lock (_lock) { Monitor.PulseAll(_lock); }
            try { _worker?.Join(500); } catch { }

            // No explicit unregister API in SDR# stream hooks, so we just stop dispatching.
        }
    }
}
