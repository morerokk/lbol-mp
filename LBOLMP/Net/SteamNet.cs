using System;
using System.Globalization;
using Steamworks;
using UnityEngine;

namespace LBOLMP.Net
{
    /// <summary>
    /// The Steam side of finding each other: the lobby, the overlay invite, accepting an invite, or joining directly on a friend.
    ///
    /// <see cref="SteamTransport"/> moves bytes between two machines that already know each other's
    /// Steam accounts. This class handles how they come to know.
    ///
    /// Nothing here calls <c>SteamAPI.RunCallbacks</c>. The game already checks and runs it every frame from
    /// its own platform handler, so registering a <c>Callback</c> is enough.
    /// </summary>
    public static class SteamNet
    {
        private static bool _available;
        private static bool _callbacksReady;

        /// <summary>When a "Steam is not there" answer may be re-checked again, by unscaled time.</summary>
        private static float _nextAvailabilityCheck;

        /// <summary>How long that answer is trusted for.</summary>
        private const float UnavailableRecheckSeconds = 2f;

        private static CSteamID _lobby = CSteamID.Nil;

        /// <summary>What we last told Steam, so an unchanged group is not republished every second.</summary>
        private static string _publishedGroup = string.Empty;
        private static int _publishedGroupSize;

        private static Callback<GameLobbyJoinRequested_t> _joinRequested;
        private static CallResult<LobbyCreated_t> _lobbyCreated;
        private static CallResult<LobbyEnter_t> _lobbyEntered;

        /// <summary>Raised when a lobby has been entered and its host is known. Argument is the host.</summary>
        public static event Action<CSteamID> JoinRequested;

        /// <summary>Raised when our own lobby is up and invitations can be sent.</summary>
        public static event Action LobbyReady;

        /// <summary>
        /// Whether Steam is actually usable in this process.
        ///
        /// Rechecked until it succeeds rather than cached.
        /// The mod loads before the game initialises the Steam API, so the first answer would otherwise be a permanent no. Once
        /// true it cannot become false, so this is fine.
        /// (And besides, would you really feel comfortable pirating this game?)
        /// This is only checked every so often, because otherwise it can lag the F2 window while it's up.
        /// </summary>
        public static bool IsAvailable
        {
            get
            {
                if (_available)
                {
                    return true;
                }

                if (Time.unscaledTime < _nextAvailabilityCheck)
                {
                    return false;
                }

                _nextAvailabilityCheck = Time.unscaledTime + UnavailableRecheckSeconds;

                try
                {
                    _available = SteamAPI.IsSteamRunning() && SteamUser.GetSteamID().IsValid();
                }
                catch (Exception)
                {
                    // This catch is a bit dirty, but I cannot foresee what kinds of nonsense the Steam networking API might try to pull.
                    // At worst, this makes Steam unavailable if an exception is thrown trying to access it, which is working as intended.
                    _available = false;
                }

                return _available;
            }
        }

        public static bool InLobby => _lobby.IsValid();

        /// <summary>
        /// Start listening for invitations. Should be called once at startup, but is safe to call again.
        /// </summary>
        public static void EnsureCallbacks()
        {
            if (_callbacksReady || !IsAvailable)
            {
                return;
            }

            try
            {
                _joinRequested = Callback<GameLobbyJoinRequested_t>.Create(OnJoinRequestedFromOverlay);
                _lobbyCreated = CallResult<LobbyCreated_t>.Create(OnLobbyCreated);
                _lobbyEntered = CallResult<LobbyEnter_t>.Create(OnLobbyEntered);
                _callbacksReady = true;
                MpPlugin.Log.LogInfo("Steam invites are being listened for");
            }
            catch (Exception e)
            {
                MpPlugin.Log.LogWarning("Could not register Steam callbacks: " + e.Message);
            }
        }

        // ---------------------------------------------------------------- hosting

        /// <summary>
        /// Open a friends-only lobby so the host has something to invite people to.
        /// Currently always makes a Friends-only lobby rather than a private invite-only one. Invite-only will be handled later.
        /// Public is not going to happen.
        /// </summary>
        public static void CreateLobby()
        {
            if (!IsAvailable)
            {
                return;
            }

            EnsureCallbacks();
            LeaveLobby();

            var call = SteamMatchmaking.CreateLobby(ELobbyType.k_ELobbyTypeFriendsOnly, MpInfo.MaxPlayers);
            _lobbyCreated.Set(call);
        }

        private static void OnLobbyCreated(LobbyCreated_t result, bool failed)
        {
            MpSafe.Run("SteamLobbyCreated", () =>
            {
                if (failed || result.m_eResult != EResult.k_EResultOK)
                {
                    MpPlugin.Log.LogError("Could not create a Steam lobby: " +
                                          (failed ? "the call failed" : result.m_eResult.ToString()));
                    Session.MpSession.StatusLine = L10n.Get(MpText.StatusSteamLobbyFailed);
                    return;
                }

                _lobby = new CSteamID(result.m_ulSteamIDLobby);

                // Stamped so a joiner can tell this is a modded session before it tries to connect,
                // and which protocol it speaks. The handshake checks the version properly; this is
                // for a clearer message when it will obviously fail.
                SteamMatchmaking.SetLobbyData(_lobby, "lbolmp", "1");
                SteamMatchmaking.SetLobbyData(_lobby, "protocol", MpInfo.ProtocolVersion.ToString());
                SteamMatchmaking.SetLobbyData(_lobby, "version", MpInfo.Version);

                MpPlugin.Log.LogInfo("Steam lobby open; friends can be invited");
                LobbyReady?.Invoke();
            });
        }

        /// <summary>Opens the Steam overlay on its invite dialog for our lobby.</summary>
        public static bool OpenInviteDialog()
        {
            if (!IsAvailable || !_lobby.IsValid())
            {
                return false;
            }

            SteamFriends.ActivateGameOverlayInviteDialog(_lobby);
            return true;
        }

        // ---------------------------------------------------------------- joining

        private static void OnJoinRequestedFromOverlay(GameLobbyJoinRequested_t callback)
        {
            MpSafe.Run("SteamJoinRequested", () =>
            {
                MpPlugin.Log.LogInfo($"Accepting a Steam invite from {NameOf(callback.m_steamIDFriend)}");
                JoinLobby(callback.m_steamIDLobby);
            });
        }

        public static void JoinLobby(CSteamID lobby)
        {
            if (!IsAvailable || !lobby.IsValid())
            {
                return;
            }

            EnsureCallbacks();
            _lobbyEntered.Set(SteamMatchmaking.JoinLobby(lobby));
        }

        private static void OnLobbyEntered(LobbyEnter_t result, bool failed)
        {
            MpSafe.Run("SteamLobbyEntered", () =>
            {
                if (failed)
                {
                    MpPlugin.Log.LogError("Could not enter the Steam lobby");
                    Session.MpSession.StatusLine = L10n.Get(MpText.StatusSteamJoinFailed);
                    return;
                }

                var lobby = new CSteamID(result.m_ulSteamIDLobby);
                var owner = SteamMatchmaking.GetLobbyOwner(lobby);

                if (owner == SteamUser.GetSteamID())
                {
                    // Our own lobby coming back to us. Nothing to join.
                    _lobby = lobby;
                    return;
                }

                _lobby = lobby;
                MpPlugin.Log.LogInfo($"Entered {NameOf(owner)}'s Steam lobby");
                JoinRequested?.Invoke(owner);
            });
        }

        /// <summary>
        /// Tell Steam that the people here are playing together, so their friends lists draw them
        /// as one group instead of as unrelated people who happen to own the same game.
        /// </summary>
        /// <remarks>
        /// steam_player_group is a reserved rich presence key: friends reporting the same value are
        /// listed nested under one another. The lobby id is already unique to this session and
        /// everybody in it knows the same one, so it doubles as the group id without a thing having
        /// to be agreed over the wire.
        ///
        /// steam_display is deliberately left alone. It takes a localization token out of the
        /// game's own Steamworks configuration, which a mod has no way to add to, so setting it
        /// would put a raw #token in front of everybody's friends.
        /// </remarks>
        public static void PublishPlayerGroup(int size)
        {
            if (!IsAvailable)
            {
                return;
            }

            if (!_lobby.IsValid())
            {
                ClearPlayerGroup();
                return;
            }

            string group = _lobby.m_SteamID.ToString(CultureInfo.InvariantCulture);
            if (group == _publishedGroup && size == _publishedGroupSize)
            {
                return;
            }

            MpSafe.Run("SteamNet.PublishPlayerGroup", () =>
            {
                SteamFriends.SetRichPresence("steam_player_group", group);
                SteamFriends.SetRichPresence("steam_player_group_size",
                    size.ToString(CultureInfo.InvariantCulture));

                _publishedGroup = group;
                _publishedGroupSize = size;
                MpPlugin.Log.LogInfo($"Steam friends list group published ({size} player(s))");
            });
        }

        /// <summary>
        /// Stop advertising the group.
        /// </summary>
        /// <remarks>
        /// Our own two keys are emptied one at a time rather than calling ClearRichPresence, which
        /// drops every key this game has set for the account, the game's own included.
        /// </remarks>
        public static void ClearPlayerGroup()
        {
            bool hadGroup = !string.IsNullOrEmpty(_publishedGroup);

            _publishedGroup = string.Empty;
            _publishedGroupSize = 0;

            if (!hadGroup || !IsAvailable)
            {
                return;
            }

            MpSafe.Run("SteamNet.ClearPlayerGroup", () =>
            {
                SteamFriends.SetRichPresence("steam_player_group", string.Empty);
                SteamFriends.SetRichPresence("steam_player_group_size", string.Empty);
            });
        }

        public static void LeaveLobby()
        {
            // Before the validity check below: the lobby may already be gone while the presence
            // keys are still up.
            ClearPlayerGroup();

            if (!_lobby.IsValid())
            {
                return;
            }

            try { SteamMatchmaking.LeaveLobby(_lobby); } catch (Exception) { }
            _lobby = CSteamID.Nil;
        }

        // ---------------------------------------------------------------- helpers

        /// <summary>A Steam persona name, for the log and the lobby list.</summary>
        public static string NameOf(CSteamID id)
        {
            if (!IsAvailable || !id.IsValid())
            {
                return string.Empty;
            }

            try
            {
                string name = SteamFriends.GetFriendPersonaName(id);
                return string.IsNullOrEmpty(name) ? id.m_SteamID.ToString() : name;
            }
            catch (Exception)
            {
                return id.m_SteamID.ToString();
            }
        }

        public static string LocalName()
        {
            if (!IsAvailable)
            {
                return string.Empty;
            }

            try { return SteamFriends.GetPersonaName(); }
            catch (Exception) { return string.Empty; }
        }
    }
}
