using System;
using HarmonyLib;
using LBOLMP.Session;
using LBoL.Core;

namespace LBOLMP.Patches
{
    /// <summary>
    /// Fixes Reisen exhibit RNG so that 2 players aren't always offered the exact same exhibits in Act 2/3.
    /// </summary>
    [HarmonyPatch(typeof(Stage), nameof(Stage.GetSupplyExhibit))]
    public static class SupplyExhibitPatch
    {
        [HarmonyPrefix]
        private static bool Prefix(Stage __instance, ref Exhibit __result)
        {
            var rolled = MpSafe.Run("SupplyExhibitPatch", () =>
            {
                var rng = MpPersonalRng.Supply;
                var gameRun = __instance?.GameRun;
                if (rng == null || gameRun == null)
                {
                    return null;
                }

                return gameRun.RollNormalExhibit(rng, __instance.SupplyExhibitWeightTable,
                    new Func<Exhibit>(__instance.GetSentinelExhibit), null);
            }, null);

            if (rolled == null)
            {
                return true;
            }

            __result = rolled;
            return false;
        }
    }
}
