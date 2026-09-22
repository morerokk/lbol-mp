using System.Collections.Generic;
using HarmonyLib;
using LBOLMP.Session.Battle;
using LBoL.Core;
using LBoL.Core.Battle;
using LBoL.Core.Battle.BattleActions;
using LBoL.EntityLib.Cards.Adventure;
using LBoL.EntityLib.EnemyUnits.Normal.Drones;

namespace LBOLMP.Patches
{
    /// <summary>
    /// Send a message when an EMP Device knocks out a drone.
    /// Patched on the drone's own reaction.
    /// </summary>
    [HarmonyPatch(typeof(Drone), "OnDamageReceived")]
    public static class DroneStunReplicationPatch
    {
        [HarmonyPostfix]
        private static void Postfix(Drone __instance, DamageEventArgs __0, ref IEnumerable<BattleAction> __result)
        {
            // The same condition the drone uses
            if (__result == null || __0 == null || __0.Cause != ActionCause.Card
                || !(__0.ActionSource is EmpCard))
            {
                return;
            }

            __result = Reported(__instance, __result);
        }

        private static IEnumerable<BattleAction> Reported(Drone drone, IEnumerable<BattleAction> actions)
        {
            foreach (var action in actions)
            {
                yield return action;
            }

            MpSafe.Run("DroneStunReplicationPatch", () => MpDrones.Report(drone));
        }
    }
}
