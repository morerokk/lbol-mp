using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace LBOLMP.Net
{
    /// <summary>
    /// One TCP link, with a reader thread and a writer thread. Neither thread touches game state:
    /// received frames land in the inbox and are drained by the main thread.
    ///
    /// TCP is a stream, so frames are length-prefixed on the way out and reassembled on the way in.
    /// (The Steam transport needs none of that, its messages already have boundaries.)
    /// </summary>
    public sealed class TcpNetConnection : NetConnection
    {
        private const int MaxFrameBytes = 4 * 1024 * 1024;

        private readonly TcpClient _client;
        private readonly NetworkStream _stream;
        private readonly BlockingCollection<byte[]> _outbox =
            new BlockingCollection<byte[]>(new ConcurrentQueue<byte[]>());

        public TcpNetConnection(TcpClient client)
        {
            _client = client;
            _client.NoDelay = true;
            _stream = client.GetStream();
            RemoteEndPoint = client.Client.RemoteEndPoint?.ToString() ?? "unknown";

            new Thread(ReadLoop) { IsBackground = true, Name = "LBOLMP-Read" }.Start();
            new Thread(WriteLoop) { IsBackground = true, Name = "LBOLMP-Write" }.Start();
        }

        protected override void SendCore(byte[] payload)
        {
            try
            {
                _outbox.Add(payload);
            }
            catch (InvalidOperationException)
            {
                // Outbox was completed by a concurrent Close(); nothing to do.
            }
        }

        private void WriteLoop()
        {
            try
            {
                foreach (var payload in _outbox.GetConsumingEnumerable())
                {
                    var header = new byte[4];
                    header[0] = (byte)(payload.Length & 0xFF);
                    header[1] = (byte)((payload.Length >> 8) & 0xFF);
                    header[2] = (byte)((payload.Length >> 16) & 0xFF);
                    header[3] = (byte)((payload.Length >> 24) & 0xFF);

                    _stream.Write(header, 0, 4);
                    _stream.Write(payload, 0, payload.Length);
                    _stream.Flush();
                }
            }
            catch (Exception e)
            {
                Close(L10n.Encode(MpText.ReasonWriteFailed, e.Message));
            }
        }

        private void ReadLoop()
        {
            var header = new byte[4];
            try
            {
                while (!IsClosed)
                {
                    if (!ReadExactly(header, 4))
                    {
                        Close(L10n.Encode(MpText.ReasonRemoteClosed));
                        return;
                    }

                    int length = header[0] | (header[1] << 8) | (header[2] << 16) | (header[3] << 24);
                    if (length < 0 || length > MaxFrameBytes)
                    {
                        Close($"Bad frame length {length}");
                        return;
                    }

                    var payload = new byte[length];
                    if (!ReadExactly(payload, length))
                    {
                        Close("Remote closed mid-frame");
                        return;
                    }

                    Inbox.Enqueue(payload);
                }
            }
            catch (Exception e)
            {
                Close(L10n.Encode(MpText.ReasonReadFailed, e.Message));
            }
        }

        private bool ReadExactly(byte[] buffer, int count)
        {
            int offset = 0;
            while (offset < count)
            {
                int read = _stream.Read(buffer, offset, count - offset);
                if (read <= 0)
                {
                    return false;
                }
                offset += read;
            }
            return true;
        }

        protected override void CloseCore(string reason)
        {
            try { _outbox.CompleteAdding(); } catch (Exception) { /* already completed */ }
            try { _stream.Close(); } catch (Exception) { /* already torn down */ }
            try { _client.Close(); } catch (Exception) { /* already torn down */ }
        }
    }

    /// <summary>
    /// Direct IP: a listening socket on the host, an outbound connect on the client.
    ///
    /// The original and still the default. It needs nothing but a reachable address, which is
    /// exactly its problem — reachable usually means a forwarded port. <see cref="SteamTransport"/>
    /// exists for the players who cannot arrange that.
    /// </summary>
    public sealed class TcpTransport : INetTransport
    {
        private TcpListener _listener;

        // Empty until the link is actually up: the sentence depends on whether this client is
        // hosting or connecting, which is not known before then.
        public string Describe { get; private set; } = string.Empty;

        public string LastError { get; private set; }

        public bool StartHost(int port)
        {
            try
            {
                _listener = new TcpListener(IPAddress.Any, port);
                _listener.Start();
                Describe = L10n.Encode(MpText.LobbyHostingDirectIp, port);
                MpPlugin.Log.LogInfo($"Hosting on port {port}");
                return true;
            }
            catch (Exception e)
            {
                LastError = e.Message;
                MpPlugin.Log.LogError($"Failed to host on port {port}: {e.Message}");
                return false;
            }
        }

        public bool StartClient(string address, int port)
        {
            try
            {
                var tcp = new TcpClient();
                // Blocking connect with a short budget; the lobby is a modal screen so a brief
                // hitch is preferable to the bookkeeping an async connect would need.
                var result = tcp.BeginConnect(address, port, null, null);
                if (!result.AsyncWaitHandle.WaitOne(TimeSpan.FromSeconds(5)))
                {
                    tcp.Close();
                    throw new TimeoutException(L10n.Encode(MpText.ReasonTimedOut, address, port));
                }
                tcp.EndConnect(result);

                Describe = L10n.Encode(MpText.LobbyConnectedDirectIp, address, port);
                MpNet.SetServerLink(new TcpNetConnection(tcp));
                MpPlugin.Log.LogInfo($"Connected to {address}:{port}");
                return true;
            }
            catch (Exception e)
            {
                LastError = e.Message;
                MpPlugin.Log.LogError($"Failed to connect to {address}:{port}: {L10n.DecodeEn(e.Message)}");
                return false;
            }
        }

        public void Poll()
        {
            try
            {
                while (_listener != null && _listener.Pending())
                {
                    var tcp = _listener.AcceptTcpClient();
                    MpNet.RegisterIncoming(new TcpNetConnection(tcp));
                }
            }
            catch (Exception e)
            {
                MpPlugin.Log.LogError("Accept failed: " + e.Message);
            }
        }

        public void Shutdown(string reason)
        {
            try { _listener?.Stop(); } catch (Exception) { /* already stopped */ }
            _listener = null;
        }
    }
}
