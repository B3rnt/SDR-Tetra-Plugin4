using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace SDRSharp.Tetra
{
    public class TcpServer : IDisposable
    {
        private const int DefaultPortNumber = 47806;

        private TcpListener _listener;
        private int _port = DefaultPortNumber;
        private volatile bool _serverRunning;
        private CancellationTokenSource _cts;

        // VERBETERING: Thread-safe collection en async afhandeling
        private readonly ConcurrentDictionary<TcpClient, bool> _tcpClients = new ConcurrentDictionary<TcpClient, bool>();

        public int ConnectedClients => _tcpClients.Count;

        ~TcpServer()
        {
            Dispose();
        }

        public void Dispose()
        {
            Stop();
            GC.SuppressFinalize(this);
        }
        
        #region Public Methods

        public void FrameReady(byte[] frame, int actualLength)
        {
            if (_tcpClients.IsEmpty || !_serverRunning) return;

            // Fire-and-forget send to avoid blocking the demodulator
            foreach (var kvp in _tcpClients)
            {
                var client = kvp.Key;
                Task.Run(async () => 
                {
                    try
                    {
                        if (client.Connected)
                        {
                            var stream = client.GetStream();
                            await stream.WriteAsync(frame, 0, actualLength).ConfigureAwait(false);
                        }
                        else
                        {
                            RemoveClient(client);
                        }
                    }
                    catch
                    {
                        RemoveClient(client);
                    }
                });
            }
        }

        public void Start(int port)
        {
            Stop(); // Ensure clean state
            _port = port;
            _cts = new CancellationTokenSource();
            _serverRunning = true;

            Task.Run(async () => await ListenLoop(_cts.Token));
        }

        public void Stop()
        {
            _serverRunning = false;
            _cts?.Cancel();
            
            try
            {
                _listener?.Stop();
            }
            catch { }

            foreach (var client in _tcpClients.Keys)
            {
                try { client.Close(); } catch { }
            }
            _tcpClients.Clear();
        }

        #endregion

        #region Private Methods

        private async Task ListenLoop(CancellationToken token)
        {
            try
            {
                _listener = new TcpListener(IPAddress.Any, _port);
                _listener.Start();
                Console.WriteLine("Listening on {0}", _listener.LocalEndpoint);

                while (!token.IsCancellationRequested)
                {
                    try 
                    {
                        var client = await _listener.AcceptTcpClientAsync();
                        _tcpClients.TryAdd(client, true);
                        Console.WriteLine("New client from {0}. {1} clients connected.", client.Client.RemoteEndPoint, _tcpClients.Count);
                    }
                    catch (ObjectDisposedException) { break; }
                    catch (Exception ex) 
                    {
                         Console.WriteLine("Accept error: " + ex.Message);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Server loop error: " + ex.Message);
            }
            finally
            {
                Stop();
            }
        }

        private void RemoveClient(TcpClient client)
        {
            if (_tcpClients.TryRemove(client, out _))
            {
                try { client.Close(); } catch { }
            }
        }

        #endregion
    }
}
