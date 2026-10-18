using HarmonyLib;
using LBOLMP.Net;
using LBoL.Presentation.UI.Panels;
using LBoL.Presentation.UI.Widgets;
using UnityEngine;
using UnityEngine.UI;

namespace LBOLMP.Patches
{
    /// <summary>
    /// Greys out Lost Branch Companion's "Quick Start" and "Reset Run" main menu buttons while in a lobby.
    /// </summary>
    [HarmonyPatch(typeof(MainMenuPanel), "OnShowing")]
    internal static class CompanionMenuLockPatch
    {
        // The Lost Branch Companion button names
        private static readonly string[] Names = { "QuickStartGame", "ResetGame" };

        private static Transform _group;
        private static bool _locked;

        [HarmonyPostfix]
        private static void Postfix(MainMenuPanel __instance)
        {
            var group = __instance.mainMenuButtonGroup;
            if (group != _group)
            {
                // A new panel comes with fresh buttons
                _group = group;
                _locked = false;
            }
        }

        internal static void Tick()
        {
            if (_group == null || MpNet.IsOnline == _locked)
            {
                return;
            }

            MpSafe.Run("CompanionMenuLockPatch", () =>
            {
                _locked = MpNet.IsOnline;

                foreach (var name in Names)
                {
                    var child = _group.Find(name);
                    if (child == null)
                    {
                        continue;
                    }

                    var button = child.GetComponent<Button>();
                    if (button != null)
                    {
                        button.interactable = !_locked;
                    }

                    // Disable the widget to prevent hover effects
                    var widget = child.GetComponentInChildren<CommonButtonWidget>(true);
                    if (widget != null)
                    {
                        widget.enabled = !_locked;
                    }
                }
            });
        }
    }

    /// <summary>
    /// Greys out Lost Branch Companion's "Reset Run" pause menu button during a multiplayer run.
    /// </summary>
    [HarmonyPatch(typeof(SettingPanel))]
    internal static class CompanionResetRunLockPatch
    {
        // Button name
        private const string Name = "ResetGame";

        private static Button _button;

        /// <summary>
        /// What the button was set to before we touched it.
        /// Is null if we didn't touch it yet.
        /// </summary>
        private static bool? _restore;

        [HarmonyPostfix]
        [HarmonyPatch("OnShowing")]
        private static void AfterShowing(SettingPanel __instance, SettingsPanelType payload)
        {
            MpSafe.Run("CompanionResetRunLockPatch.Show", () =>
            {
                _button = null;
                _restore = null;

                if (payload != SettingsPanelType.InGame || !MpNet.IsOnline || __instance.inGameGroup == null)
                {
                    return;
                }

                var child = __instance.inGameGroup.transform.Find(Name);
                var button = child == null ? null : child.GetComponent<Button>();
                if (button == null)
                {
                    return;
                }

                _button = button;
                _restore = button.interactable;
                button.interactable = false;
            });
        }

        [HarmonyPostfix]
        [HarmonyPatch("OnHiding")]
        private static void AfterHiding()
        {
            MpSafe.Run("CompanionResetRunLockPatch.Hide", () =>
            {
                if (_restore == null)
                {
                    return;
                }

                bool was = _restore.Value;
                _restore = null;

                if (_button != null)
                {
                    _button.interactable = was;
                }
                _button = null;
            });
        }
    }
}
