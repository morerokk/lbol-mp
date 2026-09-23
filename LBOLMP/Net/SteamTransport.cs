using System;
using System.Runtime.InteropServices;
using Steamworks;

namespace LBOLMP.Net
{
    /// <summary>
    /// One Steam peer-to-peer connection.
    /// Steam keeps message boundaries, so one send is one frame, with no length prefix or reassembly like TCP needs.
    /// There is no reader thread: messages are collected in <see cref="Poll"/> on the main thread, because the Steam API
    /// is not safe to call from anywhere else.
    /// </summary>
    public sealed class SteamNetConnection : NetConnection
    {
        /// <summary>A larger frame closes the link here, rather than being silently rejected by Steam.</summary>
        private const int MaxFrameBytes = 480 * 1024;

        /// <summary>
        /// Messages per <c>ReceiveMessagesOnConnection</c> call. <see cref="Poll"/> calls until the queue is empty,
        /// so this only sets how many calls a frame takes, never how much arrives.
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

        private const int UnreliableFlags = Constants.k_nSteamNetworkingSend_UnreliableNoNagle;

        protected override void SendCore(byte[] payload, bool reliable)
        {
            if (payload.Length > MaxFrameBytes)
            {
                Close($"Frame of {payload.Length} bytes is too large for Steam messaging");
                return;
            }

            // Steam copies the buffer before returning, so it can be freed straight away.
            var buffer = Marshal.AllocHGlobal(payload.Length);
            try
            {
                Marshal.Copy(payload, 0, buffer, payload.Length);

                int flags = reliable ? Constants.k_nSteamNetworkingSend_Reliable : UnreliableFlags;

                var result = SteamNetworkingSockets.SendMessageToConnection(
                    Handle, buffer, (uint)payload.Length, flags, out _);

                if (result == EResult.k_EResultOK)
                {
                    return;
                }

                // A frame that was allowed to go missing has gone missing, that's fine.
                if (!reliable)
                {
                    return;
                }

                Close(L10n.Encode(MpText.ReasonSendFailed, result.ToString()));
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        /// <summary>
        /// Take everything Steam is holding for this link, in as many batches as it takes.
        /// Never cap this at one batch per frame: a client's one link carries the whole party's traffic (the host relays
        /// everything), and a capped read silently falls further behind until the end-of-fight gate deadlocks. A backlog
        /// past Steam's receive queue also gets dropped and retransmitted, which only makes it worse.
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
                // Negative means the handle is gone. The drop itself arrives through the status callback.
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
                        // The message is ours now. Not releasing it leaks native memory.
                        SteamNetworkingMessage_t.Release(pointer);
                    }
                }
            }
            while (count >= ReceiveBatch);
        }

        /// <summary>What Steam thinks of this link right now, for the log. Empty if it cannot say.</summary>
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
        /// A link quality as a percentage, or "?" when Steam has no figure yet (it reports -1, which would print as "-100 %").
        /// </summary>
        private static string Quality(float fraction) =>
            fraction < 0f ? "?" : fraction.ToString("P0");

        /// <summary>
        /// Closed with linger on, so anything still queued goes out first. Without it, the rejection the host sends right
        /// before closing on a refused join was usually lost, and the joiner saw "Link to host lost" instead of the reason.
        /// </summary>
        protected override void CloseCore(string reason)
        {
            SteamNetworkingSockets.CloseConnection(Handle, 0, reason, true);
        }
    }

    /// <summary>
    /// Peer-to-peer over Steam, for players who cannot forward a port. Steam handles NAT punching and relaying,
    /// and neither player learns the other's IP address.
    /// Unlike TCP, connecting is asynchronous: the link is only usable once <see cref="OnConnectionStatusChanged"/> says so,
    /// which is why <see cref="MpNet.SetServerLink"/> raises an event that the join handshake is sent from.
    /// </summary>
    public sealed class SteamTransport : INetTransport
    {
        /// <summary>Both ends must agree on this. It only separates unrelated P2P listeners within one app.</summary>
        private const int VirtualPort = 0;

        private HSteamListenSocket _listenSocket = HSteamListenSocket.Invalid;
        private HSteamNetConnection _outgoing = HSteamNetConnection.Invalid;
        private Callback<SteamNetConnectionStatusChangedCallback_t> _statusChanged;

        /// <summary>
        /// Headroom for a party's traffic while a frame runs long. Steam's defaults (half a megabyte each way, a thousand
        /// messages) are thin for four players: a full receive queue drops and retransmits packets, and a full send queue
        /// fails the send, which drops the player. <see cref="SteamNetConnection.Poll"/> is what keeps the queues short.
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
        /// Widen Steam's send and receive queues, once per process. Global, because a connection copies these when it is
        /// created and the host's inbound ones are created inside a callback. Failures are only logged, since the defaults work.
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
            // Refused at the socket when full, which is cheaper than a handshake and a rejection. The handshake checks it too.
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

            // MpNet notices the closed link on its next pump and reports the disconnect, as it does for a dropped socket.
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
