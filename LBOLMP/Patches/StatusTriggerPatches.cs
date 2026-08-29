using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using LBOLMP.Session.Battle;
using LBoL.Core.StatusEffects;

namespace LBOLMP.Patches
{
    /// <summary>
    /// Send a network message when a card sets off an enemy's status effects early.
    /// </summary>
    [HarmonyPatch]
    public static class StatusTriggerReplicationPatch
    {
        private static IEnumerable<MethodBase> TargetMethods() => MpStatusTriggers.TriggerMethods();

        [HarmonyPostfix]
        private static void Postfix(StatusEffect __instance)
        {
            MpSafe.Run("StatusTriggerReplicationPatch", () => MpStatusTriggers.Report(__instance));
        }
    }
}
