using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using LBOLMP.Session;
using LBoL.Core;

namespace LBOLMP.Patches
{
    /// <summary>
    /// Watch the local purse for the Share the Wealth jade box.
    /// </summary>
    /// <remarks>
    /// The run raises MoneyGained, MoneyConsumed and MoneyLost, but none of them carry the amount,
    /// and all three clamp what they were asked for. So the reading is taken on the way in and out
    /// instead, which gives what the purse actually did rather than what somebody wanted from it.
    ///
    /// Loading a save assigns Money directly and so never comes through here.
    /// </remarks>
    [HarmonyPatch]
    public static class SharedMoneyPatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(GameRunController), nameof(GameRunController.GainMoney));
            yield return AccessTools.Method(typeof(GameRunController), nameof(GameRunController.ConsumeMoney));
            yield return AccessTools.Method(typeof(GameRunController), nameof(GameRunController.LoseMoney));
        }

        [HarmonyPrefix]
        private static void Prefix(GameRunController __instance, out int __state)
        {
            __state = __instance.Money;
        }

        [HarmonyPostfix]
        private static void Postfix(GameRunController __instance, int __state)
        {
            MpSafe.Run("SharedMoneyPatch",
                () => MpSharedMoney.OnLocalChange(__instance, __instance.Money - __state));
        }
    }
}
