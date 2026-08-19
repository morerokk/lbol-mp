using System.Collections.Generic;
using HarmonyLib;
using LBOLMP.Session.Battle;
using LBoL.Core;
using LBoL.Core.Battle;
using LBoL.EntityLib.StatusEffects.Enemy;

namespace LBOLMP.Patches
{
    /// <summary>
    /// Count Junko's "Overflowing Blemishes" rainbow/philosopher's mana across the whole party instead of per-player.
    /// </summary>
    [HarmonyPatch(typeof(JunkoColor), "OnManaGained")]
    public static class JunkoImpurityPoolPatch
    {
        [HarmonyPrefix]
        private static bool Prefix(JunkoColor __instance, ManaEventArgs args,
                                   ref IEnumerable<BattleAction> __result)
        {
            bool pooled = MpSafe.Run("JunkoImpurityPoolPatch", () => MpJunko.Active, false);
            if (!pooled)
            {
                return true;
            }

            __result = MpJunko.OnManaGained(__instance, args);
            return false;
        }
    }
}
