using System.Collections.Generic;
using HarmonyLib;
using LBOLMP.Session.Battle;
using LBoL.Core;
using LBoL.Core.Battle;
using LBoL.EntityLib.EnemyUnits.Character;

namespace LBOLMP.Patches
{
    /// <summary>
    /// Let Doremy sleep through another player's Black Notebook, exactly as she sleeps through ours.
    /// </summary>
    [HarmonyPatch(typeof(Doremy), "OnDamageReceived")]
    public static class DoremySleepReplicationPatch
    {
        [HarmonyPrefix]
        private static bool Prefix(DamageEventArgs __0, ref IEnumerable<BattleAction> __result)
        {
            if (!MpSafe.Run("DoremySleepReplicationPatch", () => MpDoremy.IsUndisturbed(__0), false))
            {
                return true;
            }

            __result = null;
            return false;
        }
    }
}
