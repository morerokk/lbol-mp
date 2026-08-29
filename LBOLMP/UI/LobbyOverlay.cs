using System;
using System.Collections.Generic;
using System.Linq;
using LBOLMP.Net;
using LBOLMP.Session;
using UnityEngine;

namespace LBOLMP.UI
{
    /// <summary>
    /// Immediate-mode overlay for hosting/joining and for showing who else is in the run.
    /// </summary>
    public sealed class LobbyOverlay : MonoBehaviour
    {
        // Unique window ID to avoid clashing with other mods, do not reuse this specific ID
        private const int WindowId = 0x4D41;

        private static readonly Color HostColour = new Color(1f, 0.85f, 0.4f);
        private static readonly Color LocalColour = new Color(0.6f, 1f, 0.7f);
        private static readonly Color DeadColour = new Color(0.7f, 0.35f, 0.35f);
        private static readonly Color WarningColour = new Color(1f, 0.72f, 0.3f);

        // Margin from the right edge of the screen when opened, so that players stop clicking "through" the window in the main menu
        private const float Margin = 40f;

        // Roughly one line of the roster.
        private const float RosterRowHeight = 20f;

        private bool _visible;
        private Rect _window = new Rect(Margin, Margin, 460f,
            380f + MpInfo.MaxPlayers * RosterRowHeight);
        private string _address = "127.0.0.1";
        private string _port = "7777";
        private string _name = "Player";
        private GUIStyle _headerStyle;
        private GUIStyle _rowStyle;
        private GUIStyle _hudStyle;

        /// <summary>
        /// The Balance section of the config, opened from the button at the bottom of this window.
        /// </summary>
        private readonly BalanceSettingsWindow _balance = new BalanceSettingsWindow();

        private void Start()
        {
            _address = MpPlugin.LastJoinAddress.Value;
            _port = MpPlugin.DefaultPort.Value.ToString();
            _name = MpPlugin.PlayerName.Value;
        }

        private void Update()
        {
            if (Input.GetKeyDown(MpPlugin.LobbyHotkey.Value))
            {
                if (!_visible)
                {
                    AnchorRight();
                }
                _visible = !_visible;
            }
        }

        private void AnchorRight()
        {
            _window.x = Mathf.Max(Margin, Screen.width - _window.width - Margin);
            _window.y = Margin;
        }

        private void EnsureStyles()
        {
            if (_headerStyle != null)
            {
                return;
            }

            _headerStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 15,
                fontStyle = FontStyle.Bold,
                wordWrap = true
            };

            _rowStyle = new GUIStyle(GUI.skin.label) { fontSize = 13, wordWrap = true };

            _hudStyle = MpGui.SingleLine(new GUIStyle(GUI.skin.label)
            {
                fontSize = 13,
                alignment = TextAnchor.UpperLeft,
                normal = { textColor = Color.white }
            });
        }

        private void OnGUI()
        {
            EnsureStyles();
            DrawCornerHud();
            DrawVoteBanner();
            DrawNotice();

            if (!_visible)
            {
                return;
            }

            _window = GUI.Window(WindowId, _window, DrawWindow,
                L10n.Get(MpText.LobbyWindowTitle, MpInfo.Version), MpGui.Window);
            _balance.Draw();
        }

        //--
        // always-on HUD
        //--

        /// <summary>
        /// Says why the map has not moved yet.
        /// </summary>
        private void DrawVoteBanner()
        {
            if (!MpSession.IsActive || !MpSession.IsInRun
                || !MapSync.VoteInProgress || MapSync.PartyAgrees)
            {
                return;
            }

            string text = MapSync.DescribeVoteState();
            var size = MpGui.Measure(_hudStyle, text);
            var rect = new Rect((Screen.width - size.x) * 0.5f - 14f, 64f, size.x + 28f, size.y + 12f);

            GUI.color = new Color(0f, 0f, 0f, 0.7f);
            GUI.Box(rect, GUIContent.none);
            GUI.color = Color.white;
            GUI.Label(new Rect(rect.x + 14f, rect.y + 6f, size.x, size.y), text, _hudStyle);
        }

        /// <summary>
        /// Whatever the mod last had to say to this player in terms of messages.
        /// </summary>
        private void DrawNotice()
        {
            string text = MpNotice.Current;
            if (text.Length == 0)
            {
                return;
            }

            var size = MpGui.Measure(_hudStyle, text);
            var rect = new Rect((Screen.width - size.x) * 0.5f - 14f, 96f, size.x + 28f, size.y + 12f);

            GUI.color = new Color(0f, 0f, 0f, 0.8f);
            GUI.Box(rect, GUIContent.none);
            GUI.color = new Color(1f, 0.85f, 0.4f);
            GUI.Label(new Rect(rect.x + 14f, rect.y + 6f, size.x, size.y), text, _hudStyle);
            GUI.color = Color.white;
        }

        /// <summary>Padding between the party panel's text and its edges.</summary>
        private const float HudPadding = 8f;

        /// <summary>Gap between the party panel and the corner of the screen.</summary>
        private const float HudMargin = 8f;

        private void DrawCornerHud()
        {
            if (!MpSession.IsActive)
            {
                return;
            }

            var others = MpSession.Players.Where(p => !p.IsLocal).ToList();
            if (others.Count == 0)
            {
                return;
            }

            var lines = new List<string>
            {
                L10n.Get(MpText.HudParty, MpSession.ConnectedCount)
            };
            var colours = new List<Color> { Color.white };

            foreach (var player in others)
            {
                string vote = string.Empty;
                if (MapSync.CurrentVotes.TryGetValue(player.Id, out var node))
                {
                    vote = L10n.Get(MpText.HudVoteNode, node.X, node.Y);
                }
                else if (MapSync.VoteInProgress)
                {
                    vote = L10n.Get(MpText.HudVoteStillChoosing);
                }

                lines.Add(L10n.Get(MpText.HudPlayerRow, player.Name, player.Hp, player.MaxHp, vote));
                colours.Add(player.State == MpPlayerState.Disconnected ? DeadColour : Color.white);
            }

            var sizes = new Vector2[lines.Count];
            float widest = 0f;
            float total = 0f;
            for (int i = 0; i < lines.Count; i++)
            {
                sizes[i] = MpGui.Measure(_hudStyle, lines[i]);
                widest = Mathf.Max(widest, sizes[i].x);
                total += sizes[i].y;
            }

            var area = new Rect(
                Mathf.Max(HudMargin, Screen.width - widest - HudPadding * 2f - HudMargin),
                HudMargin,
                widest + HudPadding * 2f,
                total + HudPadding * 2f);

            GUI.color = new Color(0f, 0f, 0f, 0.55f);
            GUI.Box(area, GUIContent.none);
            GUI.color = Color.white;

            float y = area.y + HudPadding;
            for (int i = 0; i < lines.Count; i++)
            {
                var previous = GUI.contentColor;
                GUI.contentColor = colours[i];
                GUI.Label(new Rect(area.x + HudPadding, y, widest, sizes[i].y), lines[i], _hudStyle);
                GUI.contentColor = previous;
                y += sizes[i].y;
            }
        }

        //--
        // lobby window
        //--

        private void DrawWindow(int id)
        {
            GUILayout.Space(4f);

            if (!MpNet.IsOnline)
            {
                DrawOfflineControls();
            }
            else
            {
                DrawOnlineControls();
            }

            GUILayout.Space(8f);
            if (GUILayout.Button(L10n.Get(MpText.LobbyBalanceSettings), GUILayout.Width(180f)))
            {
                _balance.Visible = !_balance.Visible;
            }

            if (!string.IsNullOrEmpty(MpSession.StatusLine))
            {
                GUILayout.Space(6f);
                GUILayout.Label(MpSession.StatusLine, _rowStyle);
            }

            GUI.DragWindow(new Rect(0f, 0f, 10000f, 20f));
        }

        private void DrawOfflineControls()
        {
            GUILayout.Label(L10n.Get(MpText.LobbyNotConnected), _headerStyle);
            GUILayout.Space(6f);

            GUILayout.BeginHorizontal();
            GUILayout.Label(L10n.Get(MpText.LobbyYourName), GUILayout.Width(90f));
            var newName = GUILayout.TextField(_name, 24);
            if (newName != _name)
            {
                _name = newName;
                MpPlugin.PlayerName.Value = newName;
            }
            GUILayout.EndHorizontal();

            DrawSteamControls();

            GUILayout.Space(8f);
            GUILayout.Label(L10n.Get(MpText.LobbyDirectConnection), _headerStyle);
            GUILayout.Space(2f);

            GUILayout.BeginHorizontal();
            GUILayout.Label(L10n.Get(MpText.LobbyPort), GUILayout.Width(90f));
            _port = GUILayout.TextField(_port, 6, GUILayout.Width(80f));
            if (GUILayout.Button(L10n.Get(MpText.LobbyHostSession), GUILayout.Width(140f)))
            {
                if (int.TryParse(_port, out int port))
                {
                    MpPlugin.DefaultPort.Value = port;
                    MpSession.Host(port);
                }
                else
                {
                    MpSession.StatusLine = L10n.Get(MpText.LobbyPortNotANumber);
                }
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(4f);

            GUILayout.BeginHorizontal();
            GUILayout.Label(L10n.Get(MpText.LobbyHostAddress), GUILayout.Width(90f));
            _address = GUILayout.TextField(_address, 64);
            GUILayout.EndHorizontal();

            if (GUILayout.Button(L10n.Get(MpText.LobbyJoinSession), GUILayout.Width(140f)))
            {
                if (int.TryParse(_port, out int port))
                {
                    MpPlugin.LastJoinAddress.Value = _address;
                    MpPlugin.DefaultPort.Value = port;
                    MpSession.Join(_address, port);
                }
                else
                {
                    MpSession.StatusLine = L10n.Get(MpText.LobbyPortNotANumber);
                }
            }

            GUILayout.Space(10f);
            GUILayout.Label(L10n.Get(MpText.LobbyOfflineHelp), _rowStyle);
        }

        private void DrawSteamControls()
        {
            GUILayout.Space(8f);
            GUILayout.Label(L10n.Get(MpText.LobbySteam), _headerStyle);
            GUILayout.Space(2f);

            if (!SteamNet.IsAvailable)
            {
                GUILayout.Label(L10n.Get(MpText.LobbySteamUnavailable), _rowStyle);
                return;
            }

            if (GUILayout.Button(L10n.Get(MpText.LobbyHostOverSteam), GUILayout.Width(180f)))
            {
                MpSession.HostSteam();
            }

            GUILayout.Label(L10n.Get(MpText.LobbySteamHelp), _rowStyle);
        }

        private void DrawOnlineControls()
        {
            string link = L10n.Decode(MpNet.TransportName);
            if (string.IsNullOrEmpty(link))
            {
                link = L10n.Get(MpNet.IsHost ? MpText.LobbyHosting : MpText.LobbyConnected);
            }

            GUILayout.Label(link, _headerStyle);
            GUILayout.Space(4f);

            if (MpNet.IsSteamSession && !MpSession.IsInRun && SteamNet.InLobby
                && GUILayout.Button(L10n.Get(MpText.LobbyInviteFriends), GUILayout.Width(180f)))
            {
                SteamNet.OpenInviteDialog();
            }

            foreach (var player in MpSession.Players)
            {
                var previous = GUI.contentColor;
                if (player.State == MpPlayerState.Disconnected)
                {
                    GUI.contentColor = DeadColour;
                }
                else if (player.IsLocal)
                {
                    GUI.contentColor = LocalColour;
                }
                else if (player.IsHost)
                {
                    GUI.contentColor = HostColour;
                }

                string tags = string.Empty;
                if (player.IsHost) tags += L10n.Get(MpText.LobbyTagHost);
                if (player.IsLocal) tags += L10n.Get(MpText.LobbyTagYou);

                string character = string.IsNullOrEmpty(player.CharacterId)
                    ? L10n.Get(MpText.LobbyChoosing)
                    : player.CharacterId;
                GUILayout.Label(
                    L10n.Get(MpText.LobbyPlayerRow, player.Id, player.Name, tags, character, DescribeState(player)),
                    _rowStyle);

                GUI.contentColor = previous;
            }

            DrawModMismatch();

            GUILayout.Space(8f);

            if (MpSession.State == MpSessionState.WaitingForPlayers)
            {
                GUILayout.Label(L10n.Get(MpText.LobbyLockedIn), _rowStyle);
                GUILayout.Label(MpSession.DescribeRunWait(), _rowStyle);
            }

            if (MpSession.IsInRun)
            {
                GUILayout.Label(L10n.Get(MpText.LobbySeed, MpSession.RunSeed), _rowStyle);
                GUILayout.Label(L10n.Get(MpText.LobbyMapVoteHint), _rowStyle);
            }

            GUILayout.Space(8f);
            if (GUILayout.Button(L10n.Get(MpText.LobbyLeaveSession), GUILayout.Width(140f)))
            {
                MpSession.Leave(MpText.ReasonYouLeft);
            }
        }

        /// <summary>
        /// Lists content-adding mods that are not the same on everyone's installation.
        /// </summary>
        private void DrawModMismatch()
        {
            var differences = MpSafe.Run("DrawModMismatch", MpModContent.Differences, null);
            if (differences == null || differences.Count == 0)
            {
                return;
            }

            GUILayout.Space(8f);

            var previous = GUI.contentColor;
            GUI.contentColor = WarningColour;
            GUILayout.Label(L10n.Get(MpText.ModMismatchTitle), _headerStyle);
            GUI.contentColor = previous;

            GUILayout.Label(L10n.Get(MpText.ModMismatchHelp), _rowStyle);
            GUILayout.Space(2f);

            foreach (var difference in differences)
            {
                GUILayout.Label(
                    L10n.Get(MpText.ModMismatchRow, difference.Mod, difference.Detail, difference.Who),
                    _rowStyle);
            }
        }

        private static string DescribeState(MpPlayer player)
        {
            switch (player.State)
            {
                case MpPlayerState.Lobby: return L10n.Get(MpText.StateInLobby);
                case MpPlayerState.Ready: return L10n.Get(MpText.StateReady);
                case MpPlayerState.Resuming: return L10n.Get(MpText.StateResuming);
                case MpPlayerState.InRun: return L10n.Get(MpText.StateHp, player.Hp, player.MaxHp);
                case MpPlayerState.Disconnected: return L10n.Get(MpText.StateDisconnected);
                default: return string.Empty;
            }
        }
    }
}
