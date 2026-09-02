using HarmonyLib;
using LBoL.Presentation.UI.Panels;
using UnityEngine.UI;

namespace LBOLMP.Patches
{
    /// <summary>
    /// Builds the multiplayer tab the first time the options are opened, and refreshes its rows
    /// from the config every time after that.
    /// </summary>
    [HarmonyPatch(typeof(SettingPanel), "OnShowing")]
    public static class SettingsTabBuildPatch
    {
        [HarmonyPostfix]
        private static void Postfix(SettingPanel __instance) =>
            MpSafe.Run("SettingsTabBuildPatch", () => UI.MpSettingsTab.Attach(__instance));
    }

    /// <summary>
    /// Routes clicks on our own tab, which the game's handler cannot recognise.
    /// </summary>
    [HarmonyPatch(typeof(SettingPanel), nameof(SettingPanel.UI_OnTabToggleChanged))]
    public static class SettingsTabSwitchPatch
    {
        [HarmonyPrefix]
        private static bool Prefix(SettingPanel __instance, Toggle item)
        {
            bool ours = MpSafe.Run("SettingsTabSwitchPatch",
                () => UI.MpSettingsTab.Owns(item) && UI.MpSettingsTab.Index >= 0, false);

            if (!ours)
            {
                return true;
            }

            MpSafe.Run("SettingsTabSwitch", () => __instance.SwitchToTab(UI.MpSettingsTab.Index));
            return false;
        }
    }
}
