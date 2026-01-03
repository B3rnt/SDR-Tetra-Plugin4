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

            LastSampleRate = samplerate;

            var arr = ArrayPool<Complex>.Shared.Rent(length);
            for (int i = 0; i < length; i++)
                arr[i] = samples[i];

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
