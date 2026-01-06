using SDRSharp.Common;
using SDRSharp.Radio;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Threading;

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
                // Common property names on ISharpControl implementations.
                var t = _control.GetType();
                foreach (var name in new[] { "SampleRate", "SamplingRate", "IFSampleRate", "IfSampleRate", "InputSampleRate" })
                {
                    var p = t.GetProperty(name);
                    if (p == null) continue;
                    var v = p.GetValue(_control, null);
                    if (v is double d) return d;
                    if (v is float f) return f;
                    if (v is int i) return i;
                    if (v is long l) return l;
                }

                // Some builds expose a Source object that holds the SampleRate.
                foreach (var name in new[] { "Source", "Frontend", "Device", "Receiver" })
                {
                    var p = t.GetProperty(name);
                    if (p == null) continue;
                    var src = p.GetValue(_control, null);
                    if (src == null) continue;
                    var st = src.GetType();
                    var sp = st.GetProperty("SampleRate") ?? st.GetProperty("SamplingRate");
                    if (sp == null) continue;
                    var sv = sp.GetValue(src, null);
                    if (sv is double sd) return sd;
                    if (sv is float sf) return sf;
                    if (sv is int si) return si;
                    if (sv is long sl) return sl;
                }
            }
            catch
            {
                // ignore
            }
            return 0;
        }

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

        private void OnIqReady(Complex* samples, double samplerate, int length)
        {
            if (length <= 0) return;

            // Workaround for SDR# builds that pass 0Hz for RawIQ hooks.
            if (samplerate <= 0)
            {
                var fallback = TryGetSampleRateFromControl();
                if (fallback > 0)
                    samplerate = fallback;
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
                while (_queue.Count > 8)
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

    public unsafe interface IWideIqSink
    {
        void OnWideIq(Complex* samples, double samplerate, int length);
    }
}
