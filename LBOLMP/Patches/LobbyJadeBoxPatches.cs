using System.Linq;
using HarmonyLib;
using LBOLMP.Net;
using LBOLMP.Session;
using LBoL.Presentation.UI;
using LBoL.Presentation.UI.Panels;
using UnityEngine;

namespace LBOLMP.Patches
{
    /// <summary>
    /// Force the host to be authoritative of the jade boxes the party plays with.
    /// Clients can open the list and read it, but every toggle mirrors the host's.
    /// </summary>
    [HarmonyPatch(typeof(StartGamePanel))]
    public static class LobbyJadeBoxPatch
    {
        /// <summary>True when we are the one choosing the jade boxes (singleplayer or host).</summary>
        private static bool WeAreChoosingJadeBoxes => !MpNet.IsOnline || MpNet.IsHost;

        /// <summary>
        /// Insert the host's choices into the panel's selections
        /// </summary>
        [HarmonyPrefix]
        [HarmonyPatch("RefreshJadeBoxIcon")]
        private static void RefreshPrefix(StartGamePanel __instance)
        {
            MpSafe.Run("LobbyJadeBoxRefreshPrefix", () => LockForClient(__instance));
        }

        /// <summary>
        /// Broadcast the host's jadebox selections to everyone else
        /// </summary>
        [HarmonyPostfix]
        [HarmonyPatch("RefreshJadeBoxIcon")]
        private static void RefreshPostfix(StartGamePanel __instance)
        {
            MpSafe.Run("LobbyJadeBoxRefreshPostfix", () => Publish(__instance));
        }

        /// <summary>
        /// Let clients into the list even if they have not unlocked jade boxes themselves.
        /// They are only reading it, and they are going to be playing with these either way.
        /// </summary>
        [HarmonyPostfix]
        [HarmonyPatch("SetJadeBoxStatus")]
        private static void StatusPostfix(StartGamePanel __instance)
        {
            MpSafe.Run("LobbyJadeBoxStatus", () =>
            {
                if (WeAreChoosingJadeBoxes)
                {
                    Publish(__instance);
                    return;
                }

                __instance.jadeBoxButton.interactable = true;
                __instance._jadeBoxTooltip.enabled = false;
                __instance.jadeBoxLockImage.gameObject.SetActive(false);
                __instance.jadeBoxText.color = Color.white;

                LockForClient(__instance);
                ShowHostSelection(__instance);
            });
        }

        /// <summary>
        /// Send a network message to the lobby what the host's panel says, if we are the host.
        /// Only sends if anything changed.
        /// </summary>
        private static void Publish(StartGamePanel panel)
        {
            if (!MpNet.IsOnline || !MpNet.IsHost)
            {
                return;
            }

            MpSession.PublishHostJadeBoxes(panel._jadeBoxToggles
                .Where(pair => pair.Value.IsOn)
                .OrderBy(pair => pair.Key.Config.Index)
                .Select(pair => pair.Key.Id));
        }

        /// <summary>
        /// Publish whatever the host's panel is showing right now, for a client that has just joined.
        /// </summary>
        public static void PublishLocalSelection()
        {
            var panel = UiManager.GetPanel<StartGamePanel>();
            if (panel != null && panel._jadeBoxToggles.Count > 0)
            {
                Publish(panel);
            }
        }

        /// <summary>
        /// Prevent clients from changing jadeboxes
        /// </summary>
        private static void LockForClient(StartGamePanel panel)
        {
            bool locked = !WeAreChoosingJadeBoxes;

            foreach (var pair in panel._jadeBoxToggles)
            {
                if (locked)
                {
                    pair.Value.Toggle.SetIsOnWithoutNotify(MpSession.HostJadeBoxes.Contains(pair.Key.Id));
                }

                pair.Value.Toggle.interactable = !locked;
            }
        }

        /// <summary>
        /// Enable the difficulty screen's hints about the run being modified and achievements being disabled.
        /// This normally only gets updated when the jadebox panel is closed.
        /// </summary>
        private static void ShowHostSelection(StartGamePanel panel)
        {
            panel.jadeBoxSetImage.fillAmount = MpSession.HostJadeBoxes.Count > 0 ? 1f : 0f;
            panel.SetNoClearHint();
        }

        /// <summary>
        /// Apply the host choices, regardless of whether the jadebox panel is currently open.
        /// </summary>
        public static void ApplyHostChoice()
        {
            var panel = UiManager.GetPanel<StartGamePanel>();

            // Nothing built yet: the toggles pick the host's list up when the panel is first shown.
            if (panel == null || panel._jadeBoxToggles.Count == 0)
            {
                return;
            }

            // The prefix above does the moving; this is what makes the panel read them back.
            panel.RefreshJadeBoxIcon();
            ShowHostSelection(panel);
        }
    }
}
