using System;
using System.Collections.Generic;
using System.Linq;
using LBOLMP.Net;
using LBOLMP.Session.Messages;
using UnityEngine;

namespace LBOLMP.Session
{
    public enum MpSessionState
    {
        Offline,

        /// <summary>Connected peers, nobody has locked in a run yet.</summary>
        Lobby,

        /// <summary>Local player has locked in and is waiting for the rest.</summary>
        WaitingForPlayers,

        /// <summary>Everyone is playing.</summary>
        InRun
    }

    /// <summary>
    /// The lobby and run-level bookkeeping: who is in the session, what they picked, and the handshake that gets everybody into a run with the same seed.
    /// </summary>
    public static class MpSession
    {
        private static readonly Dictionary<int, MpPlayer> PlayersById = new Dictionary<int, MpPlayer>();

        private static float _nextStatusBroadcast;
        private static bool _handlersRegistered;

        public static MpSessionState State { get; private set; } = MpSessionState.Offline;

        /// <summary>Seed shared by every participant, so all maps and stations line up.</summary>
        public static ulong RunSeed { get; private set; }

        /// <summary>
        /// The host's enemy health scaling, as sent with the seed. Null until a run has started.
        /// </summary>
        private static float? _runEnemyHpScale;

        /// <summary>
        /// How much extra max HP each additional player gives an enemy, for the run in progress.
        /// </summary>
        public static float EnemyHpScalePerExtraPlayer =>
            _runEnemyHpScale ?? MpPlugin.EnemyHpScalePerExtraPlayer.Value;

        /// <summary>The host's per-act escalation, as sent with the seed. Null until a run starts.</summary>
        private static float[] _runEnemyHpEscalation;

        /// <summary>
        /// How much a given act steepens the scaling above, for the run in progress.
        /// </summary>
        public static float EnemyHpEscalationForAct(int act)
        {
            if (act < 1 || act > MpConstants.ActCount)
            {
                return 0f;
            }

            return _runEnemyHpEscalation != null
                ? _runEnemyHpEscalation[act - 1]
                : MpPlugin.EnemyHpEscalationByAct[act - 1].Value;
        }

        /// <summary>The host's revive fraction, as sent with the seed. Null until a run starts.</summary>
        private static float? _runReviveHpFraction;

        /// <summary>
        /// How much of their max HP a knocked-out player comes back with, for the run in progress.
        /// </summary>
        public static float ReviveHpFraction =>
            _runReviveHpFraction ?? MpPlugin.ReviveHpFraction.Value;

        /// <summary>The host's Resilient toggle, as sent with the seed. Null until a run starts.</summary>
        private static bool? _runEnemyResilience;

        /// <summary>
        /// Whether enemies carry Resilient for the run in progress.
        /// </summary>
        public static bool EnemyResilience =>
            _runEnemyResilience ?? MpPlugin.EnableEnemyResilience.Value;

        /// <summary>The host's multiplayer card toggle, as sent with the seed. Null until a run starts.</summary>
        private static bool? _runMultiplayerCards;

        /// <summary>
        /// Whether this mod's multiplayer cards can be found in the run in progress.
        /// </summary>
        public static bool MultiplayerCards =>
            _runMultiplayerCards ?? MpPlugin.MultiplayerCardsEnabled.Value;

        /// <summary>
        /// The host's difficulty as it stands in the lobby, as a <c>GameDifficulty</c> ordinal.
        /// </summary>
        public static int HostDifficulty { get; private set; } = MpConstants.DefaultDifficulty;

        /// <summary>The difficulty the run in progress actually started on. Null outside a run.</summary>
        private static int? _runDifficulty;

        /// <summary>
        /// The difficulty every client must start with, decided by the host and delivered with the
        /// seed. Falls back to the lobby value, and then to Normal.
        /// </summary>
        public static int RunDifficulty => _runDifficulty ?? HostDifficulty;

        /// <summary>
        /// Jadebox ID's that are enabled by the host.
        /// </summary>
        public static IReadOnlyList<string> HostJadeBoxes { get; private set; } = new List<string>();

        /// <summary>The jade boxes the run in progress actually began with. Null outside a run.</summary>
        private static List<string> _runJadeBoxes;

        /// <summary>
        /// The jade boxes every client must start with, decided by the host and delivered with the
        /// seed. Falls back to the lobby list, and then to none.
        /// </summary>
        public static IReadOnlyList<string> RunJadeBoxes => _runJadeBoxes ?? HostJadeBoxes;

        /// <summary>
        /// Our name in the party.
        /// </summary>
        /// <remarks>
        /// When in a Steam lobby, if the player name is empty/whitespace or exactly "Player", their actual Steam name is sent instead.
        /// </remarks>
        /// This way, most users don't *have to* change the name away from the default.
        public static string LocalName
        {
            get
            {
                string configured = MpPlugin.PlayerName?.Value ?? string.Empty;

                string untouched = MpPlugin.PlayerName?.DefaultValue as string;
                bool isDefault = string.IsNullOrWhiteSpace(configured)
                    || (untouched != null && configured == untouched);

                if (!isDefault || !MpNet.IsSteamSession)
                {
                    return configured;
                }

                string persona = SteamNet.LocalName();
                return string.IsNullOrWhiteSpace(persona) ? configured : persona;
            }
        }

        public static string StatusLine { get; internal set; } = string.Empty;

        /// <summary>True when there is more than one participant, which means multiplayer rules apply.</summary>
        public static bool IsActive => MpNet.IsOnline && PlayersById.Count > 1;

        public static bool IsInRun => State == MpSessionState.InRun;

        public static IEnumerable<MpPlayer> Players => PlayersById.Values.OrderBy(p => p.Id);

        public static IEnumerable<MpPlayer> ConnectedPlayers =>
            PlayersById.Values.Where(p => p.State != MpPlayerState.Disconnected).OrderBy(p => p.Id);

        public static int ConnectedCount => ConnectedPlayers.Count();

        /// <summary>
        /// How long a player has to say *nothing whatsoever* before the rest stop waiting on them.
        /// This fixes deadlocks caused by weird ass internet
        /// </summary>
        public const float UnresponsiveSeconds = 45f;

        /// <summary>True when we have heard nothing at all from this player for a long time.</summary>
        public static bool IsUnresponsive(int playerId) =>
            playerId != MpNet.LocalPlayerId
            && MpNet.SilenceFor(playerId) > UnresponsiveSeconds;

        /// <summary>Connected players who are still talking to us.</summary>
        public static IEnumerable<MpPlayer> RespondingPlayers =>
            ConnectedPlayers.Where(p => !IsUnresponsive(p.Id));

        public static MpPlayer LocalPlayer => Get(MpNet.LocalPlayerId);

        public static MpPlayer Get(int id) => PlayersById.TryGetValue(id, out var player) ? player : null;

        /// <summary>
        /// Stable seat order for a battle: ascending player id, connected players only.
        /// </summary>
        public static List<MpPlayer> SeatOrder => ConnectedPlayers.ToList();

        public static int SeatIndexOf(int playerId)
        {
            var seats = SeatOrder;
            for (int i = 0; i < seats.Count; i++)
            {
                if (seats[i].Id == playerId)
                {
                    return i;
                }
            }
            return -1;
        }

        public static int LocalSeatIndex => SeatIndexOf(MpNet.LocalPlayerId);

        //--
        // setup
        //--

        public static void EnsureHandlers()
        {
            if (_handlersRegistered)
            {
                return;
            }
            _handlersRegistered = true;

            MpNet.ClientConnected += OnClientConnected;
            MpNet.PeerDisconnected += OnPeerDisconnected;
            MpNet.Disconnected += OnDisconnectedFromHost;
            MpNet.ServerLinkReady += OnServerLinkReady;
            MpNet.ConnectFailed += OnConnectFailed;
            Net.SteamNet.JoinRequested += OnSteamJoinRequested;
            Net.SteamNet.LobbyReady += OnSteamLobbyReady;

            MpNet.On<JoinRequestMessage>(OnJoinRequest);
            MpNet.On<JoinAcceptedMessage>(OnJoinAccepted);
            MpNet.On<JoinRejectedMessage>(OnJoinRejected);
            MpNet.On<PlayerListMessage>(OnPlayerList);
            MpNet.On<PlayerReadyMessage>(OnPlayerReady);
            MpNet.On<ResumeReadyMessage>(OnResumeReady);
            MpNet.On<RunStartMessage>(OnRunStart);
            MpNet.On<RunResumeMessage>(OnRunResume);
            MpNet.On<RunStartCancelledMessage>(OnRunStartCancelled);
            MpNet.On<BackToLobbyMessage>(OnBackToLobby);
            MpNet.On<LobbyDifficultyMessage>(OnLobbyDifficulty);
            MpNet.On<LobbyJadeBoxMessage>(OnLobbyJadeBoxes);
            MpNet.On<PlayerStatusMessage>(OnPlayerStatus);
            MpNet.On<PlayerLeftMessage>(OnPlayerLeft);

            MapSync.RegisterHandlers();
            MpHandInspect.RegisterHandlers();
            MpBorderSensor.RegisterHandlers();
            MpRunFlags.RegisterHandlers();
            MpRestart.RegisterHandlers();
            Battle.MpBattleSync.RegisterHandlers();
        }

        public static bool Host(int port)
        {
            EnsureHandlers();

            if (!MpNet.StartHost(port))
            {
                StatusLine = L10n.Get(MpText.StatusHostFailed, L10n.Decode(MpNet.LastError));
                return false;
            }

            PlayersById.Clear();
            PlayersById[MpConstants.HostPlayerId] = new MpPlayer
            {
                Id = MpConstants.HostPlayerId,
                Name = LocalName,
                State = MpPlayerState.Lobby
            };

            State = MpSessionState.Lobby;
            StatusLine = L10n.Get(MpText.StatusHostingOnPort, port);
            return true;
        }

        /// <summary>
        /// Host over Steam by starting a Steam lobby that can be joined through the overlay or an invite.
        /// </summary>
        public static bool HostSteam()
        {
            EnsureHandlers();

            if (!MpNet.StartSteamHost())
            {
                StatusLine = L10n.Get(MpText.StatusHostSteamFailed, L10n.Decode(MpNet.LastError));
                return false;
            }

            PlayersById.Clear();
            PlayersById[MpConstants.HostPlayerId] = new MpPlayer
            {
                Id = MpConstants.HostPlayerId,
                Name = LocalName,
                State = MpPlayerState.Lobby
            };

            State = MpSessionState.Lobby;
            StatusLine = L10n.Get(MpText.StatusOpeningSteamLobby);
            Net.SteamNet.CreateLobby();
            return true;
        }

        private static void OnSteamLobbyReady()
        {
            if (MpNet.IsHost)
            {
                StatusLine = L10n.Get(MpText.StatusHostingOverSteam);
            }
        }

        public static bool Join(string address, int port)
        {
            EnsureHandlers();

            PlayersById.Clear();
            State = MpSessionState.Lobby;
            StatusLine = L10n.Get(MpText.StatusConnecting);

            if (!MpNet.StartClient(address, port))
            {
                State = MpSessionState.Offline;
                StatusLine = L10n.Get(MpText.StatusConnectFailed, L10n.Decode(MpNet.LastError));
                return false;
            }

            return true;
        }

        /// <summary>
        /// A friend's invitation was accepted, or their lobby was joined from the friends list.
        /// Can be called outside of our own code.
        /// </summary>
        private static void OnSteamJoinRequested(Steamworks.CSteamID host)
        {
            if (MpNet.IsOnline)
            {
                MpPlugin.Log.LogWarning("Ignoring a Steam invite: already in a session");
                StatusLine = L10n.Get(MpText.StatusAlreadyInSession);
                return;
            }

            EnsureHandlers();

            PlayersById.Clear();
            State = MpSessionState.Lobby;
            StatusLine = L10n.Get(MpText.StatusConnectingSteam);

            if (!MpNet.StartSteamClient(host))
            {
                State = MpSessionState.Offline;
                StatusLine = L10n.Get(MpText.StatusConnectSteamFailed, L10n.Decode(MpNet.LastError));
            }
        }

        /// <summary>
        /// The connection to the host is up, introduce ourselves.
        /// </summary>
        private static void OnServerLinkReady()
        {
            MpNet.SendToHostDirect(new JoinRequestMessage
            {
                ProtocolVersion = MpInfo.ProtocolVersion,
                PlayerName = LocalName
            });
        }

        private static void OnConnectFailed(string reason)
        {
            MpPlugin.Log.LogWarning(L10n.En(MpText.StatusConnectFailed, L10n.DecodeEn(reason)));
            Leave(L10n.Get(MpText.StatusConnectFailed, L10n.Decode(reason)),
                  L10n.En(MpText.StatusConnectFailed, L10n.DecodeEn(reason)));
        }

        /// <summary>Leave for a reason with a name.</summary>
        public static void Leave(MpText reason) => Leave(L10n.Get(reason), L10n.En(reason));

        public static void Leave(string statusLine, string shutdownReason = null)
        {
            if (MpNet.IsOnline)
            {
                MpNet.Shutdown(shutdownReason ?? statusLine);
            }

            Net.SteamNet.LeaveLobby();

            PlayersById.Clear();
            State = MpSessionState.Offline;
            RunSeed = 0;
            _runEnemyHpScale = null;
            _runEnemyHpEscalation = null;
            _runReviveHpFraction = null;
            _runEnemyResilience = null;
            _runDifficulty = null;
            HostDifficulty = MpConstants.DefaultDifficulty;
            _runJadeBoxes = null;
            HostJadeBoxes = new List<string>();
            StatusLine = statusLine;

            // Anything this client was holding for a start that is now never going to happen.
            Patches.StartGameInterceptPatch.Cancel();
            Patches.RestoreGameInterceptPatch.Cancel();

            MapSync.Reset();
            MpRestart.Reset();
            MpHandInspect.Reset();
            MpBorderSensor.Reset();
            MpPersonalRng.Reset();
            MpRunFlags.Reset();
            MpRunCredit.Reset();
            Battle.MpBattleSync.Reset();
        }

        public static void Update()
        {
            if (State == MpSessionState.Offline)
            {
                return;
            }

            if (Time.unscaledTime >= _nextStatusBroadcast)
            {
                _nextStatusBroadcast = Time.unscaledTime + 1f;
                BroadcastLocalStatus();

                // Cheap and self-cancelling: it only reaches Steam when the lobby or the number of
                // people in it has actually changed.
                SteamNet.PublishPlayerGroup(ConnectedPlayers.Count());

                if (State == MpSessionState.WaitingForPlayers)
                {
                    StatusLine = DescribeRunWait();
                }
            }

            MapSync.Update();
            MpRestart.Update();
            MpHandInspect.Update();
            MpBorderSensor.Tick();
            MpPersonalRng.Tick();
            MpRunFlags.Tick();
            MpRunCredit.Tick();
        }

        //--
        // host handshake
        //--

        private static void OnClientConnected(NetConnection connection)
        {
            // Nothing to do until the client identifies itself with a JoinRequest.
        }

        private static void OnJoinRequest(JoinRequestMessage message)
        {
            if (!MpNet.IsHost)
            {
                return;
            }

            var connection = MpNet.CurrentSource;
            if (connection == null || connection.HandshakeComplete)
            {
                return;
            }

            // Rejections travel as translation keys so the client sees it in their language.
            if (message.ProtocolVersion != MpInfo.ProtocolVersion)
            {
                string reason = L10n.Encode(MpText.ReasonProtocolMismatch,
                    MpInfo.ProtocolVersion, message.ProtocolVersion);
                MpNet.SendToConnection(connection, new JoinRejectedMessage { Reason = reason });
                connection.Close(reason);
                return;
            }

            // Disallow mid-run joining
            if (State != MpSessionState.Lobby)
            {
                string reason = L10n.Encode(MpText.ReasonRunInProgress);
                MpNet.SendToConnection(connection, new JoinRejectedMessage { Reason = reason });
                connection.Close(reason);
                return;
            }

            // The lobby is full
            if (PlayersById.Count >= MpInfo.MaxPlayers)
            {
                string reason = L10n.Encode(MpText.ReasonSessionFull);
                MpNet.SendToConnection(connection, new JoinRejectedMessage { Reason = reason });
                connection.Close(reason);
                return;
            }

            int playerId = MpNet.AllocatePlayerId();
            connection.PlayerId = playerId;
            connection.HandshakeComplete = true;

            PlayersById[playerId] = new MpPlayer
            {
                Id = playerId,
                Name = string.IsNullOrWhiteSpace(message.PlayerName) ? "Player " + playerId : message.PlayerName,
                State = MpPlayerState.Lobby
            };

            MpNet.SendToConnection(connection, new JoinAcceptedMessage { AssignedPlayerId = playerId });
            BroadcastPlayerList();

            // The difficulty and jade boxes are only broadcast when they change, so this client
            // has to be told where they stand. The panel is asked first, in case the host ticked
            // something before there was anybody to tell.
            MpNet.SendToConnection(connection, new LobbyDifficultyMessage { Difficulty = HostDifficulty });
            MpSafe.Run("PublishHostJadeBoxes", Patches.LobbyJadeBoxPatch.PublishLocalSelection);
            MpNet.SendToConnection(connection,
                new LobbyJadeBoxMessage { JadeBoxes = new List<string>(HostJadeBoxes) });

            MpPlugin.Log.LogInfo($"{PlayersById[playerId]} joined from {connection.RemoteEndPoint}");
        }

        private static void OnJoinAccepted(JoinAcceptedMessage message)
        {
            MpNet.LocalPlayerId = message.AssignedPlayerId;
            StatusLine = L10n.Get(MpText.StatusConnectedAsPlayer, message.AssignedPlayerId);
            MpPlugin.Log.LogInfo(L10n.En(MpText.StatusConnectedAsPlayer, message.AssignedPlayerId));
        }

        private static void OnJoinRejected(JoinRejectedMessage message)
        {
            MpPlugin.Log.LogWarning(L10n.En(MpText.StatusRejected, L10n.DecodeEn(message.Reason)));
            Leave(L10n.Get(MpText.StatusRejected, L10n.Decode(message.Reason)),
                  L10n.En(MpText.StatusRejected, L10n.DecodeEn(message.Reason)));
        }

        private static void OnPlayerList(PlayerListMessage message)
        {
            if (MpNet.IsHost)
            {
                return;
            }

            var seen = new HashSet<int>();
            foreach (var incoming in message.Players)
            {
                seen.Add(incoming.Id);
                if (PlayersById.TryGetValue(incoming.Id, out var existing))
                {
                    existing.Name = incoming.Name;
                    existing.State = incoming.State;
                    existing.CharacterId = incoming.CharacterId;
                    existing.PlayerTypeIndex = incoming.PlayerTypeIndex;
                    existing.InitExhibitId = incoming.InitExhibitId;
                    existing.StartingDeck = incoming.StartingDeck;
                    existing.Hp = incoming.Hp;
                    existing.MaxHp = incoming.MaxHp;
                    existing.Money = incoming.Money;
                    existing.Power = incoming.Power;
                }
                else
                {
                    PlayersById[incoming.Id] = incoming;
                }
            }

            foreach (var id in PlayersById.Keys.Where(id => !seen.Contains(id)).ToList())
            {
                PlayersById.Remove(id);
            }
        }

        public static void BroadcastPlayerList()
        {
            if (!MpNet.IsHost)
            {
                return;
            }

            var message = new PlayerListMessage { Players = Players.ToList() };
            var payload = MessageRegistry.Serialize(message);
            foreach (var connection in MpNet.Connections)
            {
                if (connection.HandshakeComplete)
                {
                    connection.Send(payload);
                }
            }
        }

        //--
        // lobby difficulty
        //--

        /// <summary>
        /// The host has moved the difficulty selection.
        /// Tell the rest of the lobby, so their panels also move.
        /// </summary>
        public static void PublishHostDifficulty(int difficulty)
        {
            if (!MpNet.IsOnline || !MpNet.IsHost)
            {
                return;
            }

            difficulty = ClampDifficulty(difficulty);
            if (difficulty == HostDifficulty)
            {
                return;
            }

            HostDifficulty = difficulty;
            MpPlugin.Log.LogInfo($"Difficulty for the party is now {DescribeDifficulty(difficulty)}");
            MpNet.Send(new LobbyDifficultyMessage { Difficulty = difficulty });
        }

        private static void OnLobbyDifficulty(LobbyDifficultyMessage message)
        {
            if (message.SenderId != MpConstants.HostPlayerId || MpNet.IsHost)
            {
                return;
            }

            HostDifficulty = ClampDifficulty(message.Difficulty);
            MpPlugin.Log.LogInfo($"The host set the difficulty to {DescribeDifficulty(HostDifficulty)}");
            MpSafe.Run("ApplyHostDifficulty", () => Patches.LobbyDifficultyPatch.ApplyHostChoice(HostDifficulty));
        }

        private static int ClampDifficulty(int difficulty) =>
            Mathf.Clamp(difficulty, 0, MpConstants.DifficultyCount - 1);

        public static string DescribeDifficulty(int difficulty)
        {
            switch (ClampDifficulty(difficulty))
            {
                case 0: return "Easy";
                case 1: return "Normal";
                case 2: return "Hard";
                default: return "Lunatic";
            }
        }

        //--
        // lobby jade boxes
        //--

        /// <summary>
        /// The host has ticked or unticked a jade box.
        /// Tell the rest of the lobby, so their panels also change.
        /// </summary>
        public static void PublishHostJadeBoxes(IEnumerable<string> jadeBoxes)
        {
            if (!MpNet.IsOnline || !MpNet.IsHost)
            {
                return;
            }

            var chosen = jadeBoxes?.ToList() ?? new List<string>();
            if (chosen.SequenceEqual(HostJadeBoxes))
            {
                return;
            }

            HostJadeBoxes = chosen;
            MpPlugin.Log.LogInfo($"Jade boxes for the party are now {DescribeJadeBoxes(chosen)}");
            MpNet.Send(new LobbyJadeBoxMessage { JadeBoxes = new List<string>(chosen) });
        }

        private static void OnLobbyJadeBoxes(LobbyJadeBoxMessage message)
        {
            if (message.SenderId != MpConstants.HostPlayerId || MpNet.IsHost)
            {
                return;
            }

            HostJadeBoxes = message.JadeBoxes ?? new List<string>();
            MpPlugin.Log.LogInfo($"The host set the jade boxes to {DescribeJadeBoxes(HostJadeBoxes)}");
            MpSafe.Run("ApplyHostJadeBoxes", Patches.LobbyJadeBoxPatch.ApplyHostChoice);
        }

        public static string DescribeJadeBoxes(IEnumerable<string> jadeBoxes)
        {
            var names = jadeBoxes?.ToList() ?? new List<string>();
            return names.Count == 0 ? "none" : string.Join(", ", names);
        }

        //--
        // run start
        //--

        /// <summary>
        /// Called from the Start Game screen patch once the local player has configured a run.
        /// Publishes the choice and waits. The actual run begins when the host sends the seed.
        /// </summary>
        public static void SubmitLocalReady(string characterId, int playerTypeIndex, string initExhibitId,
            List<string> deck, int difficulty, List<string> jadeBoxes)
        {
            State = MpSessionState.WaitingForPlayers;
            StatusLine = L10n.Get(MpText.StatusWaitingForPlayers);

            MpNet.Send(new PlayerReadyMessage
            {
                CharacterId = characterId,
                PlayerTypeIndex = playerTypeIndex,
                InitExhibitId = initExhibitId,
                Deck = deck,
                Difficulty = difficulty,
                JadeBoxes = jadeBoxes ?? new List<string>()
            });
        }

        private static void OnPlayerReady(PlayerReadyMessage message)
        {
            var player = Get(message.SenderId);
            if (player == null)
            {
                return;
            }

            player.CharacterId = message.CharacterId;
            player.PlayerTypeIndex = message.PlayerTypeIndex;
            player.InitExhibitId = message.InitExhibitId;
            player.StartingDeck = message.Deck;
            player.Difficulty = ClampDifficulty(message.Difficulty);
            player.JadeBoxes = message.JadeBoxes ?? new List<string>();
            player.State = MpPlayerState.Ready;

            if (!MpNet.IsHost)
            {
                return;
            }

            BroadcastPlayerList();
            TryBeginRun();
        }

        //--
        // resuming a saved run
        //--

        /// <summary>
        /// Called from the Continue patch once the local player has a saved run loaded.
        /// </summary>
        public static void SubmitLocalResume(ulong seed, int stageIndex, int x, int y,
            int difficulty, string characterId)
        {
            State = MpSessionState.WaitingForPlayers;
            StatusLine = L10n.Get(MpText.StatusWaitingToResume);

            MpNet.Send(new ResumeReadyMessage
            {
                Seed = seed,
                StageIndex = stageIndex,
                X = x,
                Y = y,
                Difficulty = difficulty,
                CharacterId = characterId ?? string.Empty
            });
        }

        private static void OnResumeReady(ResumeReadyMessage message)
        {
            var player = Get(message.SenderId);
            if (player == null)
            {
                return;
            }

            player.ResumeSeed = message.Seed;
            player.ResumeStage = message.StageIndex;
            player.ResumeX = message.X;
            player.ResumeY = message.Y;
            player.Difficulty = ClampDifficulty(message.Difficulty);
            player.CharacterId = message.CharacterId ?? string.Empty;
            player.State = MpPlayerState.Resuming;

            if (!MpNet.IsHost)
            {
                return;
            }

            BroadcastPlayerList();
            TryBeginRun();
        }

        //--
        //  beginning the run
        //--

        private static void TryBeginRun()
        {
            var party = ConnectedPlayers.ToList();
            if (party.Count == 0)
            {
                return;
            }

            bool anyNew = false;
            bool anyResume = false;
            foreach (var player in party)
            {
                if (player.State == MpPlayerState.Ready)
                {
                    anyNew = true;
                }
                else if (player.State == MpPlayerState.Resuming)
                {
                    anyResume = true;
                }
                else
                {
                    return;
                }
            }

            if (anyNew && anyResume)
            {
                CancelRunStart(L10n.Encode(MpText.ReasonStartSplit));
                return;
            }

            if (anyResume)
            {
                BeginResume(party);
            }
            else
            {
                BeginNewRun();
            }
        }

        private static void BeginNewRun()
        {
            ulong seed = (ulong)Environment.TickCount * 6364136223846793005UL + 1442695040888963407UL;
            seed ^= (ulong)DateTime.UtcNow.Ticks;
            if (seed == 0)
            {
                seed = 1;
            }

            // The host's own answers, rather than the last player to press Start. Taken from what
            // they actually confirmed with, which the lobby mirror can be a screen behind on.
            var host = Get(MpConstants.HostPlayerId);
            int difficulty = host?.Difficulty ?? HostDifficulty;
            var jadeBoxes = host?.JadeBoxes ?? new List<string>(HostJadeBoxes);

            MpPlugin.Log.LogInfo($"The party is starting with jade boxes: {DescribeJadeBoxes(jadeBoxes)}");

            // The host's balance settings go out with the seed.
            MpNet.Send(new RunStartMessage
            {
                Seed = seed,
                Difficulty = difficulty,
                JadeBoxes = new List<string>(jadeBoxes),
                EnemyHpScalePerExtraPlayer = MpPlugin.EnemyHpScalePerExtraPlayer.Value,
                EnemyHpEscalationByAct = LocalEscalationSettings(),
                ReviveHpFraction = MpPlugin.ReviveHpFraction.Value,
                EnemyResilience = MpPlugin.EnableEnemyResilience.Value,
                MultiplayerCards = MpPlugin.MultiplayerCardsEnabled.Value
            });
        }

        /// <summary>
        /// Host-only. Called when everybody wants to carry on a saved run.
        /// </summary>
        private static void BeginResume(List<MpPlayer> party)
        {
            ulong seed = party[0].ResumeSeed;
            var odd = party.FirstOrDefault(p => p.ResumeSeed != seed);
            if (odd != null)
            {
                MpPlugin.Log.LogWarning(
                    "Refusing to continue: these are not saves of the same run — "
                    + string.Join(", ", party.Select(p => $"{p.Name} has seed {p.ResumeSeed}")));
                CancelRunStart(L10n.Encode(MpText.ReasonResumeDifferentRuns));
                return;
            }

            string positions = string.Join(", ", party.Select(DescribeSavedPosition));
            MpPlugin.Log.LogInfo($"Continuing run {seed}; saves are at {positions}");

            var first = party[0];
            bool staggered = party.Any(p =>
                p.ResumeStage != first.ResumeStage || p.ResumeX != first.ResumeX || p.ResumeY != first.ResumeY);
            if (staggered)
            {
                MpPlugin.Log.LogWarning(
                    "Not everybody saved at the same point in the run; the party will be out of "
                    + "step until whoever is behind catches up");
            }

            int difficulty = Get(MpConstants.HostPlayerId)?.Difficulty ?? HostDifficulty;

            MpNet.Send(new RunResumeMessage
            {
                Seed = seed,
                Difficulty = difficulty,
                EnemyHpScalePerExtraPlayer = MpPlugin.EnemyHpScalePerExtraPlayer.Value,
                EnemyHpEscalationByAct = LocalEscalationSettings(),
                ReviveHpFraction = MpPlugin.ReviveHpFraction.Value,
                EnemyResilience = MpPlugin.EnableEnemyResilience.Value,
                MultiplayerCards = MpPlugin.MultiplayerCardsEnabled.Value,
                Note = staggered ? L10n.Encode(MpText.NoticeResumeStaggered) : string.Empty
            });
        }

        /// <summary>
        /// Act and node a player's save will drop them on. For the host's log only, and English on purpose, for diagnosing issues later.
        /// </summary>
        private static string DescribeSavedPosition(MpPlayer player) =>
            $"{player.Name} at act {player.ResumeStage + 1}, node ({player.ResumeX}, {player.ResumeY})";

        public static string DescribeRunWait()
        {
            var missing = ConnectedPlayers
                .Where(p => p.State != MpPlayerState.Ready && p.State != MpPlayerState.Resuming)
                .Select(p => p.Name)
                .ToList();

            return missing.Count == 0
                ? L10n.Get(MpText.StatusWaitingForPlayers)
                : L10n.Get(MpText.StatusWaitingForNames, string.Join(", ", missing));
        }

        /// <summary>Host-only. Cancel a start the party asked for, and say why.</summary>
        private static void CancelRunStart(string encodedReason)
        {
            MpPlugin.Log.LogWarning("Not starting the run: " + L10n.DecodeEn(encodedReason));

            foreach (var player in PlayersById.Values)
            {
                if (player.State == MpPlayerState.Ready || player.State == MpPlayerState.Resuming)
                {
                    player.State = MpPlayerState.Lobby;
                }
            }

            BroadcastPlayerList();
            MpNet.Send(new RunStartCancelledMessage { Reason = encodedReason });
        }

        /// <summary>
        /// The host will not start the run this client is waiting on.
        /// </summary>
        private static void OnRunStartCancelled(RunStartCancelledMessage message)
        {
            string text = L10n.Decode(message.Reason);
            StatusLine = text;
            UI.MpNotice.Show(text);
            MpPlugin.Log.LogInfo(L10n.DecodeEn(message.Reason));

            if (State != MpSessionState.WaitingForPlayers)
            {
                return;
            }

            Patches.StartGameInterceptPatch.Cancel();
            Patches.RestoreGameInterceptPatch.Cancel();
            State = MpSessionState.Lobby;
        }

        /// <summary>This machine's per-act escalation settings (balance options).</summary>
        private static float[] LocalEscalationSettings()
        {
            var values = new float[MpConstants.ActCount];
            for (int i = 0; i < values.Length; i++)
            {
                values[i] = MpPlugin.EnemyHpEscalationByAct[i].Value;
            }

            return values;
        }

        private static void OnRunStart(RunStartMessage message)
        {
            RunSeed = message.Seed;
            State = MpSessionState.InRun;
            StatusLine = L10n.Get(MpText.StatusRunStarted, message.Seed);

            AdoptRunRules(message.Difficulty, message.EnemyHpScalePerExtraPlayer,
                message.EnemyHpEscalationByAct, message.ReviveHpFraction, message.EnemyResilience,
                message.MultiplayerCards);

            // Only a new run carries these; a resumed one already has its jade boxes in the save.
            _runJadeBoxes = message.JadeBoxes ?? new List<string>();

            foreach (var player in PlayersById.Values)
            {
                if (player.State == MpPlayerState.Ready)
                {
                    player.State = MpPlayerState.InRun;
                }
            }

            ForgetLastRun();
            Patches.StartGameInterceptPatch.BeginPendingRun(message.Seed);
        }

        /// <summary>
        /// The host says the party's saves match. Load ours.
        /// </summary>
        private static void OnRunResume(RunResumeMessage message)
        {
            if (!Patches.RestoreGameInterceptPatch.HasPending)
            {
                MpPlugin.Log.LogWarning("The party is continuing a run, but this client had no save held");
                return;
            }

            RunSeed = message.Seed;
            State = MpSessionState.InRun;
            StatusLine = L10n.Get(MpText.StatusRunResumed, message.Seed);

            AdoptRunRules(message.Difficulty, message.EnemyHpScalePerExtraPlayer,
                message.EnemyHpEscalationByAct, message.ReviveHpFraction, message.EnemyResilience,
                message.MultiplayerCards);

            foreach (var player in PlayersById.Values)
            {
                if (player.State == MpPlayerState.Resuming)
                {
                    player.State = MpPlayerState.InRun;
                }
            }

            if (!string.IsNullOrEmpty(message.Note))
            {
                UI.MpNotice.Show(L10n.Decode(message.Note));
            }

            ForgetLastRun();
            Patches.RestoreGameInterceptPatch.BeginPendingResume(message.Seed);
        }

        private static void ForgetLastRun()
        {
            MapSync.Reset();
            MpRestart.Reset();
            MpHandInspect.Reset();
            MpBorderSensor.Reset();
            MpPersonalRng.Reset();
            MpRunFlags.Reset();
            MpRunCredit.Reset();
        }

        private static void AdoptRunRules(int difficulty, float enemyHpScale, float[] escalation,
            float reviveHpFraction, bool enemyResilience, bool multiplayerCards)
        {
            _runDifficulty = ClampDifficulty(difficulty);
            _runEnemyHpScale = enemyHpScale;

            _runEnemyHpEscalation = escalation != null && escalation.Length == MpConstants.ActCount
                ? escalation
                : new float[MpConstants.ActCount];

            if (!MpNet.IsHost &&
                !Mathf.Approximately(enemyHpScale, MpPlugin.EnemyHpScalePerExtraPlayer.Value))
            {
                MpPlugin.Log.LogInfo(
                    $"Using the host's enemy health scaling ({enemyHpScale:0.##} per extra player) " +
                    $"instead of this machine's ({MpPlugin.EnemyHpScalePerExtraPlayer.Value:0.##})");
            }

            _runReviveHpFraction = reviveHpFraction;
            if (!MpNet.IsHost &&
                !Mathf.Approximately(reviveHpFraction, MpPlugin.ReviveHpFraction.Value))
            {
                MpPlugin.Log.LogInfo(
                    $"Using the host's revive fraction ({reviveHpFraction:0.##}) " +
                    $"instead of this machine's ({MpPlugin.ReviveHpFraction.Value:0.##})");
            }

            _runEnemyResilience = enemyResilience;
            if (!MpNet.IsHost && enemyResilience != MpPlugin.EnableEnemyResilience.Value)
            {
                MpPlugin.Log.LogInfo(
                    $"Using the host's Resilient setting ({(enemyResilience ? "on" : "off")}) " +
                    $"instead of this machine's ({(MpPlugin.EnableEnemyResilience.Value ? "on" : "off")})");
            }

            _runMultiplayerCards = multiplayerCards;
            if (!MpNet.IsHost && multiplayerCards != MpPlugin.MultiplayerCardsEnabled.Value)
            {
                MpPlugin.Log.LogInfo(
                    $"Using the host's multiplayer card setting ({(multiplayerCards ? "on" : "off")}) " +
                    $"instead of this machine's ({(MpPlugin.MultiplayerCardsEnabled.Value ? "on" : "off")})");
            }

            if (!MpNet.IsHost)
            {
                for (int act = 1; act <= MpConstants.ActCount; act++)
                {
                    float host = _runEnemyHpEscalation[act - 1];
                    float mine = MpPlugin.EnemyHpEscalationByAct[act - 1].Value;
                    if (!Mathf.Approximately(host, mine))
                    {
                        MpPlugin.Log.LogInfo(
                            $"Using the host's Act {act} escalation ({host:0.##}) instead of this machine's ({mine:0.##})");
                    }
                }
            }
        }

        //--
        // back to the menu
        //--

        /// <summary>
        /// The local player has saved and quit their run and gone back to the main menu.
        /// </summary>
        public static void BackToLobby()
        {
            if (State == MpSessionState.Offline)
            {
                return;
            }

            bool wasPlaying = State != MpSessionState.Lobby;

            State = MpSessionState.Lobby;
            RunSeed = 0;
            _runDifficulty = null;
            _runEnemyHpScale = null;
            _runEnemyHpEscalation = null;
            _runReviveHpFraction = null;
            _runEnemyResilience = null;
            _runMultiplayerCards = null;
            _runJadeBoxes = null;

            Patches.StartGameInterceptPatch.Cancel();
            Patches.RestoreGameInterceptPatch.Cancel();

            Patches.EnemyDamageHook.UnhookAll();
            Patches.PlayerDamageHook.Unhook();
            Patches.CardPlayHook.Unhook();
            Battle.MpPrivateEnemies.Reset();
            Battle.MpBattleSync.LeaveBattle();
            Battle.MpBattleSync.Reset();

            ForgetLastRun();

            if (!wasPlaying)
            {
                return;
            }

            StatusLine = L10n.Get(MpText.StatusBackInLobby);
            MpNet.Send(new BackToLobbyMessage());
        }

        /// <summary>
        /// Somebody is back at the main menu. Purely for the lobby list.
        /// </summary>
        private static void OnBackToLobby(BackToLobbyMessage message)
        {
            var player = Get(message.SenderId);
            if (player == null || player.State == MpPlayerState.Disconnected)
            {
                return;
            }

            player.State = MpPlayerState.Lobby;

            if (MpNet.IsHost)
            {
                BroadcastPlayerList();
            }
        }

        //--
        // status mirror
        //--

        private static void BroadcastLocalStatus()
        {
            var gameRun = LBoL.Presentation.GameMaster.Instance?.CurrentGameRun;
            if (gameRun?.Player == null)
            {
                return;
            }

            var local = LocalPlayer;
            if (local != null)
            {
                local.Hp = gameRun.Player.Hp;
                local.MaxHp = gameRun.Player.MaxHp;
                local.Money = gameRun.Money;
                local.Power = gameRun.Player.Power;
            }

            MpNet.Send(new PlayerStatusMessage
            {
                Hp = gameRun.Player.Hp,
                MaxHp = gameRun.Player.MaxHp,
                Money = gameRun.Money,
                Power = gameRun.Player.Power
            });
        }

        private static void OnPlayerStatus(PlayerStatusMessage message)
        {
            var player = Get(message.SenderId);
            if (player == null)
            {
                return;
            }

            player.Hp = message.Hp;
            player.MaxHp = message.MaxHp;
            player.Money = message.Money;
            player.Power = message.Power;
            if (!Battle.MpBattleSync.InBattle)
            {
                UI.MpAllyUnits.SyncOutOfBattle(player);
            }
        }

        private static void OnPeerDisconnected(int playerId, string reason)
        {
            if (!MpNet.IsHost)
            {
                return;
            }

            if (PlayersById.TryGetValue(playerId, out var player))
            {
                player.State = MpPlayerState.Disconnected;
            }

            MpNet.Send(new PlayerLeftMessage { PlayerId = playerId, Reason = reason });

            if (State == MpSessionState.Lobby)
            {
                PlayersById.Remove(playerId);
            }

            BroadcastPlayerList();
            TryBeginRun();
        }

        private static void OnPlayerLeft(PlayerLeftMessage message)
        {
            if (PlayersById.TryGetValue(message.PlayerId, out var player))
            {
                player.State = MpPlayerState.Disconnected;
                StatusLine = L10n.Get(MpText.StatusPlayerLeft, player.Name, L10n.Decode(message.Reason));
            }

            Battle.MpBattleSync.OnPlayerLeft(message.PlayerId);
            MapSync.OnPlayerLeft(message.PlayerId);
        }

        private static void OnDisconnectedFromHost(string reason)
        {
            StatusLine = L10n.Get(MpText.StatusDisconnected, L10n.Decode(reason));
            MpPlugin.Log.LogWarning(L10n.En(MpText.StatusDisconnected, L10n.DecodeEn(reason)));
            PlayersById.Clear();
            State = MpSessionState.Offline;
            _runEnemyHpScale = null;
            _runEnemyHpEscalation = null;
            _runReviveHpFraction = null;
            _runEnemyResilience = null;
            _runDifficulty = null;
            HostDifficulty = MpConstants.DefaultDifficulty;
            _runJadeBoxes = null;
            HostJadeBoxes = new List<string>();
            MapSync.Reset();
            MpRestart.Reset();
            MpHandInspect.Reset();
            MpBorderSensor.Reset();
            MpPersonalRng.Reset();
            MpRunFlags.Reset();
            MpRunCredit.Reset();
            Battle.MpBattleSync.Reset();
        }
    }
}
