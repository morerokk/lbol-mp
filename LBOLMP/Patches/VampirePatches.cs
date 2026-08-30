using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using LBOLMP.Session;
using LBOLMP.Session.Battle;
using LBoL.Core.Battle;
using LBoL.Core.Battle.BattleActions;
using LBoL.Core.Units;
using LBoL.EntityLib.StatusEffects.Basic;
using UnityEngine;

namespace LBOLMP.Patches
{
    /// <summary>
    /// Enemies with Vampire now lifesteal from all players that they hit for life damage.
    /// They also do not lifesteal from downed players.
    /// </summary>
    [HarmonyPatch(typeof(Vampire), "OnStatisticalDamageDealt")]
    public static class VampireDrainPatch
    {
        [HarmonyPrefix]
        private static bool Prefix(ref IEnumerable<BattleAction> __result)
        {
            bool spectating = MpSafe.Run("VampireDrainPatch.Down",
                () => MpSession.IsActive && MpBattleSync.InBattle && MpDownedPlayers.OutOfFight,
                false);

            if (!spectating)
            {
                return true;
            }

            __result = Enumerable.Empty<BattleAction>();
            return false;
        }

        [HarmonyPostfix]
        private static void Postfix(Vampire __instance, ref IEnumerable<BattleAction> __result)
        {
            var original = __result;
            if (original != null)
            {
                __result = Share(__instance, original);
            }
        }

        private static IEnumerable<BattleAction> Share(Vampire vampire, IEnumerable<BattleAction> actions)
        {
            foreach (var action in actions)
            {
                MpSafe.Run("VampireDrainPatch", () => Publish(vampire, action));
                yield return action;
            }
        }

        private static void Publish(Vampire vampire, BattleAction action)
        {
            var heal = action as HealAction;
            if (heal?.Args == null || !(vampire.Owner is EnemyUnit enemy) || heal.Args.Target != enemy)
            {
                return;
            }

            MpVampire.Report(enemy, Mathf.RoundToInt(heal.Args.Amount));
        }
    }
}
