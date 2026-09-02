using System;
using System.Collections.Generic;
using System.Linq;
using LBOLMP.Net;
using LBOLMP.Session;
using LBOLMP.Session.Battle;
using LBoL.Presentation;
using UnityEngine;

namespace LBOLMP.UI
{
    /// <summary>
    /// The little thingy that only shows up when F3 is pressed, in the top left. This is old and will be removed, since it conveys no more useful info AFAIK.
    /// </summary>
    public sealed class RemotePlayerBoard : MonoBehaviour
    {
        /// <summary>Narrowest a seat panel is allowed to be; it grows to fit a longer name.</summary>
        private const float PanelWidth = 190f;

        private const float PanelHeight = 62f;

        /// <summary>Left edge of the text column, clear of the portrait.</summary>
        private const float TextLeftInset = 50f;

        private static RemotePlayerBoard _instance;

        private GUIStyle _nameStyle;
        private GUIStyle _smallStyle;
        private Texture2D _white;

        private void Awake()
        {
            _instance = this;
            _white = new Texture2D(1, 1);
            _white.SetPixel(0, 0, Color.white);
            _white.Apply();
        }

        private void OnDestroy()
        {
            MpSafe.Run("RemotePlayerBoard.OnDestroy", () =>
            {
                Waiting.Hide();
                Standing.Hide();
            });

            if (_instance == this)
            {
                _instance = null;
            }
        }

        private void EnsureStyles()
        {
            if (_nameStyle != null)
            {
                return;
            }

            _nameStyle = MpGui.SingleLine(new GUIStyle(GUI.skin.label)
            {
                fontSize = 14,
                fontStyle = FontStyle.Bold,
                normal = { textColor = Color.white }
            });

            _smallStyle = MpGui.SingleLine(new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                normal = { textColor = new Color(0.85f, 0.85f, 0.85f) }
            });
        }

        private void OnGUI()
        {
            if (!MpBattleSync.InBattle || !MpSession.IsActive)
            {
                return;
            }

            EnsureStyles();
            DrawSeatPanels();
            DrawInspectBanner();
            DrawDiagnostics();
        }

        private static readonly MpBanner Waiting =
            new MpBanner("MpWaitingBanner", new Vector2(0.5f, 0.22f), 0.5f, 2.3f);

        private void Update()
        {
            MpSafe.Run("RemotePlayerBoard.Banners", () =>
            {
                if (!MpBattleSync.InBattle || !MpSession.IsActive)
                {
                    Waiting.Hide();
                    Standing.Hide();
                    return;
                }

                Waiting.Show(WaitingText());

                if (!MpDownedPlayers.OutOfFight)
                {
                    Standing.Hide();
                    return;
                }

                Standing.Show(
                    L10n.Get(MpDownedPlayers.LocalDown ? MpText.BoardDefeated : MpText.BoardSittingOut));
            });
        }

        private static readonly MpBanner Standing =
            new MpBanner("MpStandingBanner", new Vector2(0.5f, 0.30f), 0.5f, 2.3f);

        /// <summary>
        /// Say whose hand is on the board, and how to get your own back.
        /// </summary>
        private void DrawInspectBanner()
        {
            if (!MpHandView.Active)
            {
                return;
            }

            string text = L10n.Get(MpText.InspectBanner, MpHandInspect.TargetName);
            var size = MpGui.Measure(_nameStyle, text);
            var rect = new Rect((Screen.width - size.x) * 0.5f - 16f, 24f, size.x + 32f, size.y + 14f);

            GUI.color = new Color(0f, 0f, 0f, 0.75f);
            GUI.DrawTexture(rect, _white);
            GUI.color = new Color(1f, 0.85f, 0.4f);
            GUI.Label(new Rect(rect.x + 16f, rect.y + 7f, size.x, size.y), text, _nameStyle);
            GUI.color = Color.white;
        }

        private void DrawDiagnostics()
        {
            if (!MpPlugin.ShowDiagnostics)
            {
                return;
            }

            var battle = GameMaster.Instance?.CurrentGameRun?.Battle;
            var lines = new List<string>
            {
                $"waitingForInput : {battle?.IsWaitingPlayerInput.ToString() ?? "n/a"}",
                $"localComplete   : {MpBattleSync.LocalTurnComplete}",
                $"allComplete     : {MpBattleSync.AllSeatsCompleted(MpBattleSync.CurrentRound)}",
                $"round           : {battle?.RoundCounter.ToString() ?? "n/a"}"
            };

            foreach (var seat in MpBattleSync.AllSeats)
            {
                lines.Add($"#{seat.PlayerId} {seat.Name,-10} completed={seat.CompletedRound} " +
                          $"alive={seat.Alive} done={seat.Finished} down={seat.Down} " +
                          $"watching={seat.Spectating}");
            }

            float height = 8f + lines.Count * 16f;
            var area = new Rect(24f, Screen.height - height - 24f, 380f, height);

            GUI.color = new Color(0f, 0f, 0f, 0.75f);
            GUI.DrawTexture(area, _white);
            GUI.color = Color.white;

            for (int i = 0; i < lines.Count; i++)
            {
                GUI.Label(new Rect(area.x + 8f, area.y + 4f + i * 16f, area.width - 16f, 16f),
                    lines[i], _smallStyle);
            }
        }

        private static string WaitingText()
        {
            var silent = MpBattleSync.SilentSeats.ToList();
            if (silent.Count > 0)
            {
                return L10n.Get(MpText.BoardLostContact, string.Join(", ", silent));
            }

            if (MpBattleSync.AtEndOfBattleGate)
            {
                var fighting = MpBattleSync.SeatsStillFighting.ToList();
                if (fighting.Count == 0)
                {
                    return null;
                }

                return fighting.Count == 1
                    ? L10n.Get(MpText.BoardWaitingForOneToFinish, fighting[0])
                    : L10n.Get(MpText.BoardWaitingForMany, string.Join(", ", fighting));
            }

            // Anyone out of the fight has their own banner, and is not waiting on anybody.
            if (MpDownedPlayers.OutOfFight)
            {
                return null;
            }

            if (!MpBattleSync.LocalTurnComplete
                || MpBattleSync.AllSeatsCompleted(MpBattleSync.CurrentRound))
            {
                return null;
            }

            var pending = MpBattleSync.SeatsStillPlaying.ToList();
            if (pending.Count == 0)
            {
                return null;
            }

            return pending.Count == 1
                ? L10n.Get(MpText.BoardWaitingForOne, pending[0])
                : L10n.Get(MpText.BoardWaitingForMany, string.Join(", ", pending));
        }

        private void DrawSeatPanels()
        {
            if (!MpPlugin.ShowDiagnostics)
            {
                return;
            }

            var seats = MpBattleSync.RemoteSeats.ToList();

            float width = PanelWidth;
            foreach (var seat in seats)
            {
                width = Mathf.Max(width, TextLeftInset + MpGui.Measure(_nameStyle, seat.Name).x + 8f);
            }

            for (int slot = 0; slot < seats.Count; slot++)
            {
                var rect = new Rect(24f, 200f + slot * (PanelHeight + 8f), width, PanelHeight);
                DrawSeatPanel(rect, seats[slot]);
            }
        }

        private void DrawSeatPanel(Rect rect, MpBattleSeat seat)
        {
            GUI.color = new Color(0f, 0f, 0f, 0.6f);
            GUI.DrawTexture(rect, _white);
            GUI.color = Color.white;

            var portraitRect = new Rect(rect.x + 4f, rect.y + 4f, 40f, 40f);
            MpPortraits.Draw(portraitRect, seat.CharacterId);

            float textX = rect.x + TextLeftInset;
            float textWidth = rect.width - TextLeftInset - 4f;
            GUI.Label(new Rect(textX, rect.y + 2f, textWidth, 18f), seat.Name, _nameStyle);

            // HP bar.
            var barRect = new Rect(textX, rect.y + 22f, textWidth - 4f, 10f);
            GUI.color = new Color(0.15f, 0.15f, 0.15f, 0.9f);
            GUI.DrawTexture(barRect, _white);

            float fraction = seat.MaxHp > 0 ? Mathf.Clamp01(seat.Hp / (float)seat.MaxHp) : 0f;
            GUI.color = seat.Alive ? new Color(0.75f, 0.25f, 0.3f) : new Color(0.35f, 0.35f, 0.35f);
            GUI.DrawTexture(new Rect(barRect.x, barRect.y, barRect.width * fraction, barRect.height), _white);
            GUI.color = Color.white;

            string extras = $"{seat.Hp}/{seat.MaxHp}";
            if (seat.Block > 0)
            {
                extras += "   " + L10n.Get(MpText.BoardBlock, seat.Block);
            }
            if (seat.Shield > 0)
            {
                extras += "   " + L10n.Get(MpText.BoardShield, seat.Shield);
            }
            GUI.Label(new Rect(textX, rect.y + 34f, textWidth, 16f), extras, _smallStyle);

            string activity = seat.Spectating ? L10n.Get(MpText.ActivitySpectating)
                : seat.Down ? L10n.Get(MpText.ActivityDownSpectating)
                : seat.Finished ? L10n.Get(MpText.ActivityDone)
                : !seat.Alive ? L10n.Get(MpText.ActivityDown)
                : seat.HasCompleted(MpBattleSync.CurrentRound) ? L10n.Get(MpText.ActivityTurnOver)
                : L10n.Get(MpText.ActivityHand, seat.HandCount);
            GUI.Label(new Rect(textX, rect.y + 46f, textWidth, 16f), activity, _smallStyle);
        }

    }
}
