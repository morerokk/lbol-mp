using System;
using System.Runtime.InteropServices;
using Steamworks;

namespace LBOLMP.Net
{
    /// <summary>
    /// One Steam peer-to-peer connection.
    ///
    /// Steam's reliable messages are delivered in order and with their boundaries intact, so unlike
    /// the TCP connection this needs no length prefix and no reassembly on our end. One send is one frame.
    /// There is no reader thread either: messages are collected in <see cref="Poll"/>, on the main
    /// thread, because the Steam API is not safe to call from anywhere else.
    ///
    /// That last part is a potential problem here, and <see cref="Poll"/> is where it is answered:
    /// with no thread of its own, this connection only moves as often as the game's frames do, so a frame
    /// must take everything waiting rather than "whatever I feel like taking right now".
    /// Read that comment carefully before changing anything in there.
    /// 
    /// The point is that Steam's internal buffers have to be completely emptied out ASAP into our own buffer.
    /// Really bad things happen if the buffer is full and not read.
    /// </summary>
    public sealed class SteamNetConnection : NetConnection
    {
        /// <summary>
        /// Steam's reliable send caps out well below this, but the real point of the limit is to
        /// notice a frame that would be silently rejected rather than to allow one.
        /// </summary>
        private const int MaxFrameBytes = 480 * 1024;

        /// <summary>
        /// How many messages one <c>ReceiveMessagesOnConnection</c> call collects. A batch size and
        /// nothing more — <see cref="Poll"/> keeps calling until the queue is empty, so this decides
        /// how many round trips into Steam a frame's traffic costs and never how much of it arrives.
        /// </summary>
        private const int ReceiveBatch = 64;

        private readonly IntPtr[] _received = new IntPtr[ReceiveBatch];

        public HSteamNetConnection Handle { get; }

        public CSteamID RemoteId { get; }

        public SteamNetConnection(HSteamNetConnection handle, CSteamID remoteId)
        {
            Handle = handle;
            RemoteId = remoteId;

            string name = SteamNet.NameOf(remoteId);
            RemoteEndPoint = string.IsNullOrEmpty(name) ? "Steam:" + remoteId.m_SteamID : name;
        }

        protected override void SendCore(byte[] payload)
        {
            if (payload.Length > MaxFrameBytes)
            {
                Close($"Frame of {payload.Length} bytes is too large for Steam messaging");
                return;
            }

            // SendMessageToConnection copies out of this buffer before it returns, so releasing it
            // straight afterwards is correct and keeps the send allocation-bounded.
            var buffer = Marshal.AllocHGlobal(payload.Length);
            try
            {
                Marshal.Copy(payload, 0, buffer, payload.Length);

                var result = SteamNetworkingSockets.SendMessageToConnection(
                    Handle, buffer, (uint)payload.Length,
                    Constants.k_nSteamNetworkingSend_Reliable, out _);

                if (result != EResult.k_EResultOK)
                {
                    Close(L10n.Encode(MpText.ReasonSendFailed, result.ToString()));
                }
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        /// <summary>
        /// Take everything Steam is holding for this link, however much that is.
        ///
        /// The loop is the whole point. This used to be a single <c>ReceiveMessagesOnConnection</c>
        /// call for up to 64 messages, which quietly made the link a 64-messages-per-frame pipe —
        /// and a client's single link to the host is not one player's traffic but the whole party's,
        /// because the host relays everything to everyone (see <see cref="MpNet.Send"/>). Four
        /// players push roughly twice as much down it as two, and the frames it has to fit through
        /// are whatever the game is managing at the time.
        ///
        /// What that produced was not a disconnect and not a stutter. Every client went on running
        /// perfectly smoothly, sending perfectly normally and hearing *something* from everyone —
        /// so nothing looked broken and the silence watchdog in <c>MpBattleSync.IsUnresponsive</c>
        /// never fired — while its picture of everybody else fell further and further behind. The
        /// end of a fight is where that surfaces, because it is the one place everyone has to agree
        /// before anyone may move: each player has finished, each player has said so, and each
        /// player is still working through a backlog in which nobody else has. Everyone waits for
        /// everyone, forever, and every screen blames the others.
        ///
        /// Worse than the delay, a backlog that outgrows Steam's own receive queue stops being a
        /// delay at all: Steam drops the packets it cannot buffer and has the sender retransmit
        /// them, so falling behind generates the extra traffic that keeps you behind, until the
        /// connection dies of it. <see cref="SteamTransport.ConfigureBuffers"/> widens those queues;
        /// this stops them filling in the first place.
        ///
        /// The TCP transport never had any of this. Its reader thread pulls the socket dry into an
        /// unbounded queue no matter what the main thread is doing, which is exactly the behaviour
        /// restored here — and exactly why the fault was only ever seen over Steam.
        ///
        /// Terminating: every call takes the messages it returns out of Steam's queue, so a queue
        /// that is not being refilled faster than a tight loop can drain it always reaches empty.
        /// </summary>
        public override void Poll()
        {
            if (IsClosed)
            {
                return;
            }

            int count;
            do
            {
                // Negative means the handle is no longer valid, which the loop condition treats as
                // "nothing more to take" — the drop itself arrives through the status callback.
                count = SteamNetworkingSockets.ReceiveMessagesOnConnection(Handle, _received, ReceiveBatch);

                for (int i = 0; i < count; i++)
                {
                    var pointer = _received[i];
                    if (pointer == IntPtr.Zero)
                    {
                        continue;
                    }

                    try
                    {
                        var message = SteamNetworkingMessage_t.FromIntPtr(pointer);
                        if (message.m_cbSize > 0)
                        {
                            var payload = new byte[message.m_cbSize];
                            Marshal.Copy(message.m_pData, payload, 0, message.m_cbSize);
                            Inbox.Enqueue(payload);
                        }
                    }
                    finally
                    {
                        // Steam hands us ownership of the message; not releasing it leaks native
                        // memory for the rest of the session.
                        SteamNetworkingMessage_t.Release(pointer);
                    }
                }
            }
            while (count >= ReceiveBatch);
        }

        /// <summary>
        /// What Steam thinks of this link right now, for the log. Empty if it cannot say.
        ///
        /// Here because the fault this transport was carrying could not be seen from anything the
        /// mod knew about itself: the session looked healthy from every angle it could inspect, and
        /// the evidence that it was not lived on the other side of the API — bytes accepted for
        /// sending and not yet acknowledged, and how far ahead of the wire the queue had got.
        /// </summary>
        public string DescribeLink()
        {
            var status = new SteamNetConnectionRealTimeStatus_t();
            var lanes = new SteamNetConnectionRealTimeLaneStatus_t();

            if (SteamNetworkingSockets.GetConnectionRealTimeStatus(Handle, ref status, 0, ref lanes)
                != EResult.k_EResultOK)
            {
                return string.Empty;
            }

            return $"ping={status.m_nPing}ms " +
                   $"quality={Quality(status.m_flConnectionQualityLocal)}/" +
                   $"{Quality(status.m_flConnectionQualityRemote)} " +
                   $"pending={status.m_cbPendingReliable}B unacked={status.m_cbSentUnackedReliable}B " +
                   $"queued={(long)status.m_usecQueueTime / 1000}ms rate={status.m_nSendRateBytesPerSecond}B/s";
        }

        /// <summary>
        /// A link quality as a percentage, or "?" for one Steam has not measured yet.
        ///
        /// Steam answers -1 for "no figure", which printed straight as a percentage came out as
        /// "-100 %" — a perfectly healthy link described in the log as the worst one possible, in
        /// the exact field somebody reads to decide whether the network is at fault.
        /// </summary>
        private static string Quality(float fraction) =>
            fraction < 0f ? "?" : fraction.ToString("P0");

        /// <summary>
        /// Closed with lingering enabled, so anything already queued gets one last chance to go out.
        ///
        /// This matters for exactly one thing, and that thing is the reason a joiner is told why
        /// they were refused. The host answers a join it cannot accept by sending a rejection and
        /// then immediately closing the link — and closing a Steam connection without lingering
        /// throws away whatever has not left yet, which is nearly always that rejection. The joiner
        /// was then told "Link to host lost", which reads as a network fault and sent people
        /// hunting for one, when the truth was "the run has already started". It was a race rather
        /// than a certainty, so the same refusal would occasionally come through correctly and make
        /// the whole thing look intermittent.
        /// </summary>
        protected override void CloseCore(string reason)
        {
            SteamNetworkingSockets.CloseConnection(Handle, 0, reason, true);
        }
    }

    /// <summary>
    /// Peer-to-peer over Steam, for players who cannot forward a port.
    ///
    /// Steam punches through NAT where it can and relays through Valve's datagram network where it
    /// cannot, so from here a link either exists or does not and the reason is somebody else's
    /// problem. It also means neither player learns the other's IP address.
    ///
    /// Connecting is asynchronous, which is the one real difference from TCP: <c>ConnectP2P</c>
    /// returns immediately and the link is not usable until Steam says so through
    /// <see cref="OnConnectionStatusChanged"/>. That is why <see cref="MpNet.SetServerLink"/> raises
    /// an event rather than the caller assuming a link exists once the call returns — the join
    /// handshake is sent from that event on both transports, so there is only one path.
    /// </summary>
    public sealed class SteamTransport : INetTransport
    {
        /// <summary>
        /// Both ends must agree on this or the connect goes nowhere. It is a port only in the sense
        /// that it lets one app hold several unrelated P2P listeners at once; zero is ours.
        /// </summary>
        private const int VirtualPort = 0;

        private HSteamListenSocket _listenSocket = HSteamListenSocket.Invalid;
        private HSteamNetConnection _outgoing = HSteamNetConnection.Invalid;
        private Callback<SteamNetConnectionStatusChangedCallback_t> _statusChanged;

        /// <summary>
        /// Room for a party's traffic to sit in while a frame takes longer than it should.
        ///
        /// Steam's defaults are half a megabyte each way and a thousand queued messages, which is
        /// generous for two players trading moves and thin for four in a fight where one of them is
        /// loading a station. Both limits have teeth: an overfull receive queue makes Steam drop
        /// packets and the sender retransmit them, and an overfull send queue makes
        /// <c>SendMessageToConnection</c> fail, which this transport can only answer by dropping the
        /// player. The TCP transport queues both directions in managed memory with no ceiling at
        /// all, so neither failure exists there; this is not quite that, but it is the same order of
        /// forgiveness.
        ///
        /// Headroom rather than a fix. What actually keeps these queues short is
        /// <see cref="SteamNetConnection.Poll"/> emptying the receive side every frame; this is what
        /// stops a hitch that outlasts a frame from turning into lost packets.
        /// </summary>
        private const int BufferBytes = 4 * 1024 * 1024;

        private const int BufferMessages = 8192;

        private static bool _buffersConfigured;

        public string Describe { get; private set; } = string.Empty;

        public string LastError { get; private set; }

        public bool StartHost()
        {
            if (!SteamNet.IsAvailable)
            {
                LastError = L10n.Encode(MpText.ErrorSteamUnavailable);
                return false;
            }

            try
            {
                // Warms up the relay network. Without it the first connection pays for measuring
                // ping times to Valve's relays, which can add seconds to a join.
                SteamNetworkingUtils.InitRelayNetworkAccess();

                // Before the listen socket: inbound connections inherit these as they are created.
                ConfigureBuffers();

                _statusChanged = Callback<SteamNetConnectionStatusChangedCallback_t>.Create(OnConnectionStatusChanged);
                _listenSocket = SteamNetworkingSockets.CreateListenSocketP2P(VirtualPort, 0, null);

                if (_listenSocket == HSteamListenSocket.Invalid)
                {
                    LastError = L10n.Encode(MpText.ErrorSteamListenFailed);
                    return false;
                }

                Describe = L10n.Encode(MpText.LobbyHostingSteam);
                MpPlugin.Log.LogInfo("Listening for Steam connections");
                return true;
            }
            catch (Exception e)
            {
                LastError = e.Message;
                MpPlugin.Log.LogError("Failed to host over Steam: " + e);
                return false;
            }
        }

        public bool StartClient(CSteamID host)
        {
            if (!SteamNet.IsAvailable)
            {
                LastError = L10n.Encode(MpText.ErrorSteamUnavailable);
                return false;
            }

            try
            {
                SteamNetworkingUtils.InitRelayNetworkAccess();

                // Before ConnectP2P, for the reason given in StartHost.
                ConfigureBuffers();

                _statusChanged = Callback<SteamNetConnectionStatusChangedCallback_t>.Create(OnConnectionStatusChanged);

                var identity = new SteamNetworkingIdentity();
                identity.SetSteamID(host);

                _outgoing = SteamNetworkingSockets.ConnectP2P(ref identity, VirtualPort, 0, null);
                if (_outgoing == HSteamNetConnection.Invalid)
                {
                    LastError = L10n.Encode(MpText.ErrorSteamConnectFailed);
                    return false;
                }

                Describe = L10n.Encode(MpText.LobbyConnectedSteam, SteamNet.NameOf(host));
                MpPlugin.Log.LogInfo($"Connecting to {SteamNet.NameOf(host)} over Steam");
                return true;
            }
            catch (Exception e)
            {
                LastError = e.Message;
                MpPlugin.Log.LogError("Failed to connect over Steam: " + e);
                return false;
            }
        }

        /// <summary>
        /// Widen Steam's send and receive queues, once per process.
        ///
        /// Set globally rather than per connection because it has to be in force before a socket
        /// exists: a connection takes its copy of these when it is created, and the host's inbound
        /// links are created inside a callback with no convenient moment to configure them first.
        ///
        /// Failures are logged and otherwise ignored. Every one of these is a widening of a limit
        /// that already has a workable default, so a Steam build that does not recognise one leaves
        /// the session exactly as well off as it was before this method existed.
        /// </summary>
        private static void ConfigureBuffers()
        {
            if (_buffersConfigured)
            {
                return;
            }
            _buffersConfigured = true;

            SetGlobalInt(ESteamNetworkingConfigValue.k_ESteamNetworkingConfig_RecvBufferSize, BufferBytes);
            SetGlobalInt(ESteamNetworkingConfigValue.k_ESteamNetworkingConfig_RecvBufferMessages, BufferMessages);
            SetGlobalInt(ESteamNetworkingConfigValue.k_ESteamNetworkingConfig_SendBufferSize, BufferBytes);
        }

        private static void SetGlobalInt(ESteamNetworkingConfigValue setting, int amount)
        {
            // Steamworks.NET only exposes the raw form of this call, which takes the value by
            // pointer because the same entry point sets floats and strings too.
            var buffer = Marshal.AllocHGlobal(sizeof(int));
            try
            {
                Marshal.WriteInt32(buffer, amount);

                bool ok = SteamNetworkingUtils.SetConfigValue(
                    setting,
                    ESteamNetworkingConfigScope.k_ESteamNetworkingConfig_Global,
                    IntPtr.Zero,
                    ESteamNetworkingConfigDataType.k_ESteamNetworkingConfig_Int32,
                    buffer);

                if (!ok)
                {
                    MpPlugin.Log.LogWarning($"Steam would not accept {setting} = {amount}; leaving it at its default");
                }
            }
            catch (Exception e)
            {
                MpPlugin.Log.LogWarning($"Could not set {setting}: {e.Message}");
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        private void OnConnectionStatusChanged(SteamNetConnectionStatusChangedCallback_t callback)
        {
            MpSafe.Run("SteamConnectionStatus", () =>
            {
                var handle = callback.m_hConn;
                var info = callback.m_info;
                bool incoming = info.m_hListenSocket != HSteamListenSocket.Invalid;

                switch (info.m_eState)
                {
                    case ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_Connecting:
                        if (incoming)
                        {
                            AcceptIncoming(handle, info);
                        }
                        break;

                    case ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_Connected:
                        if (incoming)
                        {
                            MpNet.RegisterIncoming(new SteamNetConnection(handle, info.m_identityRemote.GetSteamID()));
                        }
                        else if (handle == _outgoing)
                        {
                            MpPlugin.Log.LogInfo("Steam connection established");
                            MpNet.SetServerLink(new SteamNetConnection(handle, info.m_identityRemote.GetSteamID()));
                        }
                        break;

                    case ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_ClosedByPeer:
                    case ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_ProblemDetectedLocally:
                        HandleDrop(handle, info);
                        break;
                }
            });
        }

        private void AcceptIncoming(HSteamNetConnection handle, SteamNetConnectionInfo_t info)
        {
            // Only the host listens, and only until the session is full. Steam will happily let a
            // stranger who knows the host's account try their luck, so the seat count is enforced
            // here as well as in the join handshake — a refused connection is cheaper than one that
            // gets a protocol check and a rejection message.
            if (MpNet.Connections.Count >= MpInfo.MaxPlayers - 1)
            {
                SteamNetworkingSockets.CloseConnection(
                    handle, 0, L10n.Encode(MpText.ReasonSessionFull), false);
                MpPlugin.Log.LogInfo($"Refused a Steam connection from {SteamNet.NameOf(info.m_identityRemote.GetSteamID())}: session is full");
                return;
            }

            var result = SteamNetworkingSockets.AcceptConnection(handle);
            if (result != EResult.k_EResultOK)
            {
                MpPlugin.Log.LogWarning("Could not accept a Steam connection: " + result);
                SteamNetworkingSockets.CloseConnection(handle, 0, "Accept failed", false);
            }
        }

        private void HandleDrop(HSteamNetConnection handle, SteamNetConnectionInfo_t info)
        {
            string reason = string.IsNullOrEmpty(info.m_szEndDebug)
                ? L10n.Encode(MpText.ReasonSteamClosed)
                : info.m_szEndDebug;

            // Tell the rest of the mod first, then hand the handle back. MpNet notices the closed
            // link on its next pump and reports the disconnect from there, exactly as it does for a
            // dropped socket.
            var existing = MpNet.FindBySteamHandle(handle);
            if (existing != null)
            {
                existing.Close(reason);
                return;
            }

            // Nothing was ever built around this handle, so this is a connect that never landed.
            MpPlugin.Log.LogWarning("Steam connection failed: " + reason);
            SteamNetworkingSockets.CloseConnection(handle, 0, reason, false);

            if (handle == _outgoing)
            {
                _outgoing = HSteamNetConnection.Invalid;
                MpNet.ReportConnectFailure(reason);
            }
        }

        public void Poll()
        {
            // Connections poll themselves; MpNet walks them. Nothing to accept here, because Steam
            // delivers new links through the status callback rather than a listen queue.
        }

        public void Shutdown(string reason)
        {
            if (_listenSocket != HSteamListenSocket.Invalid)
            {
                try { SteamNetworkingSockets.CloseListenSocket(_listenSocket); } catch (Exception) { }
                _listenSocket = HSteamListenSocket.Invalid;
            }

            _outgoing = HSteamNetConnection.Invalid;

            try { _statusChanged?.Dispose(); } catch (Exception) { }
            _statusChanged = null;

            SteamNet.LeaveLobby();
        }
    }
}
