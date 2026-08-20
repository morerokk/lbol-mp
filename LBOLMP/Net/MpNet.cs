using System;
using System.Collections.Generic;
using System.Linq;
using Steamworks;

namespace LBOLMP.Net
{
    public enum NetRole
    {
        Offline,
        Host,
        Client
    }

    /// <summary>
    /// Class that helps manage the multiplayer details and the transport.
    /// Note: the host is authoritative over a lot of things. Network messages sent by clients do not directly apply to themselves. Instead, they send them to the host, and the host echoes them back.
    /// This ensures the correct order and keeps the game mostly in sync, since each client has their own simulation of the game.
    /// </summary>
    public static class MpNet
    {
        private static INetTransport _transport;
        private static readonly List<NetConnection> _clients = new List<NetConnection>();
        private static NetConnection _serverLink;
        private static int _nextPlayerId = MpConstants.HostPlayerId + 1;

        private static readonly Dictionary<Type, List<Action<NetMessage>>> Handlers =
            new Dictionary<Type, List<Action<NetMessage>>>();

        public static NetRole Role { get; private set; } = NetRole.Offline;

        public static bool IsOnline => Role != NetRole.Offline;
        public static bool IsHost => Role == NetRole.Host;
        public static bool IsClient => Role == NetRole.Client;

        public static int LocalPlayerId { get; internal set; } = MpConstants.InvalidPlayerId;

        /// <summary>Raised on the main thread when a player disconnects. Argument is the reason for disconnecting.</summary>
        public static event Action<int, string> PeerDisconnected;

        /// <summary>Raised on the main thread when our connection to the host drops.</summary>
        public static event Action<string> Disconnected;

        /// <summary>Raised on the host when a new client connection comes up (pre-handshake).</summary>
        public static event Action<NetConnection> ClientConnected;

        /// <summary>
        /// Raised on a client once the connection to the host is usable.
        /// </summary>
        /// <remarks>
        /// This exists because Steam connects asynchronously. A TCP connect either succeeds or
        /// throws before the call returns, so the join handshake could simply follow it right away.
        /// But Steam's <c>ConnectP2P</c> returns a handle to a connection that does not exist yet, and might even never exist.
        /// Therefore, we send the handshake from here in both cases to keep it simple.
        /// </remarks>
        public static event Action ServerLinkReady;

        /// <summary>Raised on a client when the connection could not be established at all.</summary>
        public static event Action<string> ConnectFailed;

        public static string LastError { get; private set; }

        /// <summary>What kind of connection is used for the current session. Used in the F2 lobby window. Empty when offline.</summary>
        public static string TransportName => _transport?.Describe ?? string.Empty;

        /// <summary>True when the session is running over Steam rather than a direct address.</summary>
        public static bool IsSteamSession => _transport is SteamTransport;

        // ---------------------------------------------------------------- lifecycle

        public static bool StartHost(int port)
        {
            Shutdown("Restarting as host");

            var transport = new TcpTransport();
            if (!transport.StartHost(port))
            {
                LastError = transport.LastError;
                Shutdown("Host start failed");
                return false;
            }

            BecomeHost(transport);
            return true;
        }

        /// <summary>
        /// Host over Steam. Players can be invited, or join directly via Join Game through the friends list.
        /// "Invite-only" functionality does not yet exist.
        /// </summary>
        public static bool StartSteamHost()
        {
            Shutdown("Restarting as host");

            var transport = new SteamTransport();
            if (!transport.StartHost())
            {
                LastError = transport.LastError;
                Shutdown("Host start failed");
                return false;
            }

            BecomeHost(transport);
            return true;
        }

        private static void BecomeHost(INetTransport transport)
        {
            _transport = transport;
            Role = NetRole.Host;
            LocalPlayerId = MpConstants.HostPlayerId;
            _nextPlayerId = MpConstants.HostPlayerId + 1;
            LastError = null;
        }

        public static bool StartClient(string address, int port)
        {
            Shutdown("Restarting as client");

            var transport = new TcpTransport();
            _transport = transport;
            Role = NetRole.Client;
            LocalPlayerId = MpConstants.InvalidPlayerId;

            // Connect synchronously, which could raise ServerLinkReady before this returns (for TCP connections).
            // This is deliberate.
            if (!transport.StartClient(address, port))
            {
                LastError = transport.LastError;
                Shutdown("Connect failed");
                return false;
            }

            LastError = null;
            return true;
        }

        /// <summary>
        /// Connect to a Steam host. Returns as soon as the attempt is underway!
        /// This connection may not be usable until <see cref="ServerLinkReady"/> is called, and may instead fail with <see cref="ConnectFailed"/>.
        /// </summary>
        public static bool StartSteamClient(CSteamID host)
        {
            Shutdown("Restarting as client");

            var transport = new SteamTransport();
            _transport = transport;
            Role = NetRole.Client;
            LocalPlayerId = MpConstants.InvalidPlayerId;

            if (!transport.StartClient(host))
            {
                LastError = transport.LastError;
                Shutdown("Connect failed");
                return false;
            }

            LastError = null;
            return true;
        }

        /// <summary>Called by a transport once an inbound connection is usable.</summary>
        public static void RegisterIncoming(NetConnection connection)
        {
            if (connection == null)
            {
                return;
            }

            _clients.Add(connection);
            MpPlugin.Log.LogInfo($"Incoming connection from {connection.RemoteEndPoint}");
            ClientConnected?.Invoke(connection);
        }

        /// <summary>Called by a transport once the outbound connection to the host is usable.</summary>
        public static void SetServerLink(NetConnection connection)
        {
            _serverLink = connection;
            ServerLinkReady?.Invoke();
        }

        private static string _pendingConnectFailure;

        /// <summary>
        /// Called by a transport when an outbound connection could not be established.
        ///
        /// Recorded rather than raised, because Steam reports this from inside a callback and the
        /// handler's response is to leave the session immediately. This disposes the callback object
        /// being dispatched. Raising it from the next attempt keeps the shutdown off Steam's stack.
        /// (Trying to do it right away caused issues with leaving the session or wanting to rejoin it)
        /// </summary>
        public static void ReportConnectFailure(string reason)
        {
            LastError = reason;
            _pendingConnectFailure = reason ?? L10n.Encode(MpText.ReasonConnectionFailed);
        }

        /// <summary>Finds the connection wrapping a Steam handle, if there is one.</summary>
        public static NetConnection FindBySteamHandle(HSteamNetConnection handle)
        {
            if (_serverLink is SteamNetConnection outgoing && outgoing.Handle == handle)
            {
                return _serverLink;
            }

            for (int i = 0; i < _clients.Count; i++)
            {
                if (_clients[i] is SteamNetConnection client && client.Handle == handle)
                {
                    return _clients[i];
                }
            }

            return null;
        }

        public static void Shutdown(string reason)
        {
            if (Role == NetRole.Offline && _transport == null && _serverLink == null && _clients.Count == 0)
            {
                return;
            }

            MpPlugin.Log?.LogInfo($"Network shutdown: {reason}");

            foreach (var client in _clients)
            {
                client.Close(reason);
            }
            _clients.Clear();

            _serverLink?.Close(reason);
            _serverLink = null;

            // Done after the connections, so a transport that has to dispose of Steam stuff only does it once everything using it has already gone.
            try { _transport?.Shutdown(reason); } catch (Exception e) { MpPlugin.Log?.LogError("Transport shutdown failed: " + e.Message); }
            _transport = null;

            Role = NetRole.Offline;
            LocalPlayerId = MpConstants.InvalidPlayerId;
            _pendingConnectFailure = null;
            LastHeard.Clear();
            _lastPumpTime = -1f;
        }

        // ---------------------------------------------------------------- sending

        /// <summary>
        /// Submit a message to the session.
        /// On the host it is applied immediately and relayed to everyone else.
        /// On a client it is sent to the host and only applied when the host has echoed it back to the client.
        /// </summary>
        public static void Send(NetMessage message)
        {
            if (!IsOnline)
            {
                return;
            }

            message.SenderId = LocalPlayerId;

            bool reliable = !MessageRegistry.IsUnreliable(message);

            if (IsHost)
            {
                Dispatch(message);
                if (MessageRegistry.IsRelayed(message))
                {
                    BroadcastRaw(MessageRegistry.Serialize(message), null, reliable);
                }
            }
            else
            {
                _serverLink?.Send(MessageRegistry.Serialize(message), reliable);
            }
        }

        /// <summary>Host-only: send straight to one peer without relaying (such as for handshake traffic).</summary>
        public static void SendToConnection(NetConnection connection, NetMessage message)
        {
            message.SenderId = LocalPlayerId;
            connection?.Send(MessageRegistry.Serialize(message), !MessageRegistry.IsUnreliable(message));
        }

        /// <summary>Client-only: send straight to the host without waiting for an echo.</summary>
        public static void SendToHostDirect(NetMessage message)
        {
            message.SenderId = LocalPlayerId;
            _serverLink?.Send(MessageRegistry.Serialize(message), !MessageRegistry.IsUnreliable(message));
        }

        /// <summary>
        /// A message's payload without its header, for comparing one tick against the last.
        /// Header left out because it's always the same, and if it isn't the same we don't care and it would just be noise (such as for reconnections)
        /// </summary>
        public static byte[] BodyOf(NetMessage message)
        {
            var writer = new NetWriter();
            message.Write(writer);
            return writer.ToArray();
        }

        /// <summary>True if the two payloads are byte-for-byte the same.</summary>
        public static bool SameBytes(byte[] a, byte[] b)
        {
            if (a == null || b == null || a.Length != b.Length)
            {
                return false;
            }

            for (int i = 0; i < a.Length; i++)
            {
                if (a[i] != b[i])
                {
                    return false;
                }
            }
            return true;
        }

        private static void BroadcastRaw(byte[] payload, NetConnection except, bool reliable = true)
        {
            for (int i = 0; i < _clients.Count; i++)
            {
                var client = _clients[i];
                if (client == except || !client.HandshakeComplete)
                {
                    continue;
                }
                client.Send(payload, reliable);
            }
        }

        // ---------------------------------------------------------------- receiving

        public static void Pump()
        {
            if (!IsOnline)
            {
                return;
            }

            AbsorbPumpStall();

            if (_pendingConnectFailure != null)
            {
                var reason = _pendingConnectFailure;
                _pendingConnectFailure = null;
                ConnectFailed?.Invoke(reason);
                return;
            }

            // Accepts pending connections, and lets a transport that has no reader thread of its own do
            // its receiving on the main thread. Before the inboxes are drained, so anything that
            // arrived this frame is handled this frame.
            try { _transport?.Poll(); } catch (Exception e) { MpPlugin.Log.LogError("Transport poll failed: " + e.Message); }

            if (IsHost)
            {
                PumpClients();
            }
            else
            {
                PumpServerLink();
            }
        }

        private static void PumpClients()
        {
            for (int i = _clients.Count - 1; i >= 0; i--)
            {
                var connection = _clients[i];
                connection.Poll();

                while (connection.Inbox.TryDequeue(out var payload))
                {
                    NetMessage message;
                    try
                    {
                        message = MessageRegistry.Deserialize(payload);
                    }
                    catch (Exception e)
                    {
                        MpPlugin.Log.LogError($"Dropping malformed message from {connection.RemoteEndPoint}: {e.Message}");
                        continue;
                    }

                    // Let's not trust a client's claim about who it is. Paranoid? I don't know
                    message.SenderId = connection.PlayerId;

                    HostReceived(connection, message);
                }

                if (connection.IsClosed)
                {
                    _clients.RemoveAt(i);
                    MpPlugin.Log.LogInfo($"Client {connection.PlayerId} disconnected: {L10n.DecodeEn(connection.DisconnectReason)}");
                    PeerDisconnected?.Invoke(connection.PlayerId, connection.DisconnectReason);
                }
            }
        }

        /// <summary>
        /// The connection that a message is currently being handled for.
        /// Only valid inside a host-side handler, and only useful before a player id has been assigned. That is, during the
        /// join handshake, where two clients could otherwise be confused for each other.
        /// </summary>
        public static NetConnection CurrentSource { get; private set; }

        private static void HostReceived(NetConnection connection, NetMessage message)
        {
            LogVerbose($"host <- {message}");

            CurrentSource = connection;
            try
            {
                Dispatch(message);
            }
            finally
            {
                CurrentSource = null;
            }

            if (MessageRegistry.IsRelayed(message) && connection.HandshakeComplete)
            {
                // If the message came from a client, echo it back to everyone including the sender, so all clients see the messages arrive in the same order.
                BroadcastRaw(MessageRegistry.Serialize(message), null, !MessageRegistry.IsUnreliable(message));
            }
        }

        private static void PumpServerLink()
        {
            var link = _serverLink;
            if (link == null)
            {
                return;
            }

            link.Poll();

            while (link.Inbox.TryDequeue(out var payload))
            {
                NetMessage message;
                try
                {
                    message = MessageRegistry.Deserialize(payload);
                }
                catch (Exception e)
                {
                    MpPlugin.Log.LogError($"Dropping malformed message from host: {e.Message}");
                    continue;
                }

                LogVerbose($"client <- {message}");
                Dispatch(message);
            }

            if (link.IsClosed)
            {
                var reason = link.DisconnectReason;
                _serverLink = null;
                Shutdown("Link to host lost");
                Disconnected?.Invoke(reason);
            }
        }

        // ---------------------------------------------------------------- dispatch

        public static void On<T>(Action<T> handler) where T : NetMessage
        {
            if (!Handlers.TryGetValue(typeof(T), out var list))
            {
                list = new List<Action<NetMessage>>();
                Handlers[typeof(T)] = list;
            }
            list.Add(m => handler((T)m));
        }

        // ---------------------------------------------------------------- liveness

        /// <summary>
        /// The last time we heard anything at all from each player, by Time.unscaledTime.
        /// This is used to identify cases where the connection is dead.
        /// This is directly in here rather than on the connection, because clients only connect to the host.
        /// If A is the host and B and C are clients, then now B can also know immediately that C is gone.
        /// </summary>
        private static readonly Dictionary<int, float> LastHeard = new Dictionary<int, float>();

        private static float _lastPumpTime = -1f;

        /// <summary>
        /// Dirty hack: a frame gap longer than this is taken as our own machine being too busy to handle connection data (such as when restarting or loading a level),
        /// rather than taking it as "oh, all the other players are suddenly gone at the same time".
        /// </summary>
        private const float PumpStallGraceSeconds = 2f;

        /// <summary>
        /// Seconds since anything last arrived from this player, or zero if nothing ever has.
        /// This may be at zero for some time while the player is still connecting or loading,
        /// a "silence" is only counted if we have heard from the player before and now suddenly no more.
        /// </summary>
        public static float SilenceFor(int playerId)
        {
            if (playerId == LocalPlayerId || !LastHeard.TryGetValue(playerId, out float heard))
            {
                return 0f;
            }
            return Math.Max(0f, UnityEngine.Time.unscaledTime - heard);
        }

        /// <summary>
        /// Used to describe exact connection states, useful for logging or debug purposes.
        /// Only used for Steam connections since we already know the exact state in our own TCP connections.
        /// </summary>
        public static string DescribeLinks()
        {
            var parts = new List<string>();

            if (_serverLink is SteamNetConnection host)
            {
                string described = host.DescribeLink();
                if (!string.IsNullOrEmpty(described))
                {
                    parts.Add("host: " + described);
                }
            }

            for (int i = 0; i < _clients.Count; i++)
            {
                if (!(_clients[i] is SteamNetConnection client))
                {
                    continue;
                }

                string described = client.DescribeLink();
                if (!string.IsNullOrEmpty(described))
                {
                    parts.Add($"#{client.PlayerId}: {described}");
                }
            }

            return string.Join(" | ", parts);
        }

        /// <summary>
        /// Stop a stall on *our* side from being read as everyone else losing connection.
        ///
        /// For example, if WE are loading a level for 50 seconds and then the connection comes alive again afterwards,
        /// the mod won't suddenly think "oh it's been more than 50 seconds since I last heard from anyone, I guess the connection is dead, cya later!"
        /// </summary>
        private static void AbsorbPumpStall()
        {
            float now = UnityEngine.Time.unscaledTime;
            float gap = _lastPumpTime < 0f ? 0f : now - _lastPumpTime;
            _lastPumpTime = now;

            if (gap <= PumpStallGraceSeconds || LastHeard.Count == 0)
            {
                return;
            }

            var players = new int[LastHeard.Count];
            LastHeard.Keys.CopyTo(players, 0);
            foreach (int player in players)
            {
                LastHeard[player] += gap;
            }
        }

        private static void Dispatch(NetMessage message)
        {
            LastHeard[message.SenderId] = UnityEngine.Time.unscaledTime;

            if (!Handlers.TryGetValue(message.GetType(), out var list))
            {
                MpPlugin.Log.LogWarning($"No handler registered for {message.GetType().Name}");
                return;
            }

            for (int i = 0; i < list.Count; i++)
            {
                try
                {
                    list[i](message);
                }
                catch (Exception e)
                {
                    MpPlugin.Log.LogError($"Handler for {message.GetType().Name} threw: {e}");
                }
            }
        }

        // ---------------------------------------------------------------- host helpers

        public static IReadOnlyList<NetConnection> Connections => _clients;

        public static int AllocatePlayerId() => _nextPlayerId++;

        public static NetConnection FindConnection(int playerId) =>
            _clients.FirstOrDefault(c => c.PlayerId == playerId);

        /// <summary>
        /// Not currently used, but potentially usable. Kicks a player out of the game.
        /// Will implement later.
        /// </summary>
        public static void Kick(int playerId, string reason)
        {
            FindConnection(playerId)?.Close(reason);
        }

        private static void LogVerbose(string text)
        {
            if (MpPlugin.VerboseLogging != null && MpPlugin.VerboseLogging.Value)
            {
                MpPlugin.Log.LogInfo("[net] " + text);
            }
        }
    }
}
