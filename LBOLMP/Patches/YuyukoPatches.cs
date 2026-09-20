using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using LBOLMP.Session;
using LBOLMP.Session.Battle;
using LBoL.Core.Battle;

namespace LBOLMP.Patches
{
    /// <summary>
    /// Notify other players when Yuyuko's Law of Mortality is triggered.
    /// </summary>
    [HarmonyPatch]
    public static class LawOfMortalityReplicationPatch
    {
        private static bool Prepare() => YuyukoInterop.Installed;

        private static MethodBase TargetMethod() => YuyukoInterop.GetPhasesMethod;

        [HarmonyPostfix]
        private static void Postfix(BattleAction __instance, ref IEnumerable<Phase> __result)
        {
            if (__result != null)
            {
                __result = Reported(__instance, __result);
            }
        }

        private static IEnumerable<Phase> Reported(BattleAction action, IEnumerable<Phase> phases)
        {
            foreach (var phase in phases)
            {
                yield return phase;
            }

            MpSafe.Run("LawOfMortalityReplicationPatch", () => MpYuyuko.Report(action));
        }
    }
}
