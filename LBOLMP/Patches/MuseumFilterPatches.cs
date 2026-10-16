using System;
using System.Collections.Generic;
using HarmonyLib;
using LBoL.Presentation.UI.Panels;

namespace LBOLMP.Patches
{
    /// <summary>
    /// Add a multiplayer-exclusive filter in the cards Collection in the main menu.
    /// </summary>
    [HarmonyPatch(typeof(MuseumPanel))]
    public static class MuseumMultiplayerFilterPatch
    {
        [HarmonyPatch(nameof(MuseumPanel.ResetCardTab))]
        [HarmonyPrefix]
        private static void Reset(MuseumPanel __instance) =>
            MpSafe.Run("MuseumMultiplayerFilter.Reset", () => UI.MpMuseumFilter.Attach(__instance));

        [HarmonyPatch("FilterCardByType")]
        [HarmonyPostfix]
        private static void Filter(ref IEnumerable<Type> __result)
        {
            if (!UI.MpMuseumFilter.Active)
            {
                return;
            }

            var all = __result;
            __result = MpSafe.Run("MuseumMultiplayerFilter.Filter", () => UI.MpMuseumFilter.Narrow(all), all);
        }
    }
}
