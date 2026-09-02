using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using LBOLMP.Entities.JadeBoxes;
using LBOLMP.Net;
using LBOLMP.Session;
using LBoL.Core;
using LBoL.EntityLib.Adventures;

namespace LBOLMP.Patches
{
    // Makes shared money work with the Share The Wealth jadebox
    [HarmonyPatch]
    public static class SharedMoneyPatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(GameRunController), nameof(GameRunController.InternalGainMoney));
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

    // Eirin's [Trade] option asks for all your money.
    // If money is shared, this becomes a problem.
    // So therefore, the trade is now priced at "this character's initial money" rather than "ALL your money".
    [HarmonyPatch(typeof(Debut), "InitVariables")]
    public static class DebutTradePricePatch
    {
        private const string YarnPriceVar = "$allMoney";

        [HarmonyPostfix]
        private static void Postfix(Debut __instance) =>
            MpSafe.Run("DebutTradePricePatch", () => Reprice(__instance));

        private static void Reprice(Debut debut)
        {
            var gameRun = debut?.GameRun;
            if (gameRun == null || !MpNet.IsOnline || !gameRun.HasJadeBox<MpShareTheWealth>())
            {
                return;
            }

            var storage = debut.Storage;
            if (storage == null)
            {
                return;
            }

            int share = Math.Max(0, Math.Min(gameRun.Player?.Config.InitialMoney ?? 0, gameRun.Money));
            storage.SetValue(YarnPriceVar, share);
        }
    }
}
