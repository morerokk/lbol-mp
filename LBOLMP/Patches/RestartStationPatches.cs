using HarmonyLib;
using LBOLMP.Session;
using LBoL.Presentation;
using LBoL.Presentation.UI.Panels;

namespace LBOLMP.Patches
{
    /// <summary>
    /// If the host restarts the level, everyone does.
    /// </summary>
    [HarmonyPatch(typeof(GameMaster), nameof(GameMaster.RequestReenterStation))]
    internal static class RestartStationPatch
    {
        [HarmonyPrefix]
        private static bool Prefix()
        {
            return MpSafe.Run("RestartStationPatch", MpRestart.OnLocalRequest, true);
        }
    }

    /// <summary>
    /// Greys "Restart Level" out for everyone except the host.
    /// </summary>
    [HarmonyPatch(typeof(SettingPanel))]
    internal static class RestartButtonLockPatch
    {
        /// <summary>What the button was set to before we touched it, or null if we did not.</summary>
        private static bool? _restore;

        [HarmonyPostfix]
        [HarmonyPatch("OnShowing")]
        private static void AfterShowing(SettingPanel __instance, SettingsPanelType payload)
        {
            MpSafe.Run("RestartButtonLockPatch.Show", () =>
            {
                _restore = null;

                if (payload != SettingsPanelType.InGame || MpRestart.LocalDecides)
                {
                    return;
                }

                var button = __instance.reenterStationButton;
                if (button == null)
                {
                    return;
                }

                _restore = button.interactable;
                __instance.SetReenterStationInteractable(false);
            });
        }

        [HarmonyPostfix]
        [HarmonyPatch("OnHiding")]
        private static void AfterHiding(SettingPanel __instance)
        {
            MpSafe.Run("RestartButtonLockPatch.Hide", () =>
            {
                if (_restore == null)
                {
                    return;
                }

                bool was = _restore.Value;
                _restore = null;

                if (__instance.reenterStationButton != null)
                {
                    __instance.SetReenterStationInteractable(was);
                }
            });
        }
    }
}
