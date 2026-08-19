using HarmonyLib;
using LBOLMP.Net;
using LBOLMP.Session;
using LBoL.Presentation.UI;
using LBoL.Presentation.UI.Panels;
using UnityEngine;

namespace LBOLMP.Patches
{
    /// <summary>
    /// Force the host to be authoritative of the difficulty level that's chosen.
    /// </summary>
    [HarmonyPatch(typeof(StartGamePanel), "SelectDifficulty")]
    public static class LobbyDifficultyPatch
    {
        /// <summary>
        /// True if we're currently playing the difficulty change animation on the client.
        /// </summary>
        private static bool _applyingHostChoice;

        /// <summary>True when we are the one choosing the difficulty (singleplayer or host).</summary>
        private static bool WeAreChoosingDifficulty => !MpNet.IsOnline || MpNet.IsHost;

        /// <summary>
        /// Rewrite a client's difficulty to the host's.
        /// </summary>
        [HarmonyPrefix]
        private static void Prefix(ref int index)
        {
            // A ref parameter cannot be captured by a lambda, so decide first and assign after.
            int forced = MpSafe.Run("LobbyDifficultyPrefix", () =>
                _applyingHostChoice || WeAreChoosingDifficulty ? -1 : MpSession.HostDifficulty, -1);

            if (forced >= 0)
            {
                index = forced;
            }
        }

        [HarmonyPostfix]
        private static void Postfix(StartGamePanel __instance, int index)
        {
            MpSafe.Run("LobbyDifficultyPostfix", () =>
            {
                if (!MpNet.IsOnline)
                {
                    return;
                }

                if (MpNet.IsHost)
                {
                    MpSession.PublishHostDifficulty(index);
                    return;
                }

                LockForClient(__instance, index);
            });
        }

        /// <summary>
        /// Lock the difficulty panel for clients.
        /// </summary>
        private static void LockForClient(StartGamePanel panel, int index)
        {
            panel.difficultyLeftButton.interactable = false;
            panel.difficultyRightButton.interactable = false;

            // If the current player does not have the host's difficulty unlocked yet, stay on the current difficulty so that the Confirm button isn't locked.
            // Once the run actually starts, this will be resolved correctly anyway.
            if (!panel._isDifficultyLock)
            {
                return;
            }

            panel._isDifficultyLock = false;
            if (index >= 0 && index < panel.difficultyGroups.Length)
            {
                panel.difficultyGroups[index].SetLocked(locked: false);
            }

            panel.RefreshDifficultyConfirm();
        }

        /// <summary>
        /// Move the difficulty selector to the host's choice, even if we are currently locked into our character or not on the difficulty screen.
        /// </summary>
        public static void ApplyHostChoice(int difficulty)
        {
            var panel = UiManager.GetPanel<StartGamePanel>();
            if (panel == null)
            {
                return;
            }

            int index = Mathf.Clamp(difficulty, 0, MpConstants.DifficultyCount - 1);
            if (index == panel._difficultyIndex)
            {
                return;
            }

            bool watching = panel._currentPanelPhase == DifficultyPhase && panel.isActiveAndEnabled;

            _applyingHostChoice = true;
            try
            {
                panel.SelectDifficulty(index, immediate: !watching);
            }
            finally
            {
                _applyingHostChoice = false;
            }
        }

        /// <summary>The difficulty panel's phase number. (The character screen is 2.)</summary>
        private const int DifficultyPhase = 3;
    }
}
