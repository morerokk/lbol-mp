using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using LBOLMP.Entities.JadeBoxes;
using LBOLMP.Net;
using LBOLMP.Session;
using LBoL.Core;
using LBoL.Core.Randoms;
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

    // Disable Eirin's "Lose all money, gain a random rare exhibit" option with share the wealth
    [HarmonyPatch(typeof(Debut), nameof(Debut.RollBonus))]
    public static class DebutTradeOptionPatch
    {
        /// <summary>[Trade], the third of the six options, which the yarn refers to as Bonus3.</summary>
        private const int TradeOption = 2;

        private const int OptionCount = 6;
        private const int BonusCount = 2;

        [HarmonyPrefix]
        private static bool Prefix(Debut __instance) =>
            !MpSafe.Run("DebutTradeOption", () => RollWithoutTrade(__instance), false);

        private static bool RollWithoutTrade(Debut debut)
        {
            var gameRun = debut?.GameRun;
            if (gameRun == null || !MpNet.IsOnline || !gameRun.HasJadeBox<MpShareTheWealth>())
            {
                return false;
            }

            var pool = new UniqueRandomPool<int>(false);
            for (int option = 0; option < OptionCount; option++)
            {
                if (option != TradeOption)
                {
                    pool.Add(option, 1f);
                }
            }

            var picks = pool.SampleMany(gameRun.DebutRng, BonusCount, true);
            debut._bonusNos = picks;

            var storage = debut.Storage;
            storage.SetValue("$bonusNo1", picks[0]);
            storage.SetValue("$bonusNo2", picks[1]);

            for (int option = 0; option < OptionCount; option++)
            {
                debut._optionTitles[option] =
                    storage.TryGetValue($"$option{option + 1}Source", out string title) ? title : string.Empty;
            }

            storage.SetValue("$bonusOption1", debut._optionTitles[picks[0]]);
            storage.SetValue("$bonusOption2", debut._optionTitles[picks[1]]);

            for (int slot = 0; slot < BonusCount; slot++)
            {
                storage.SetValue($"$bonusTarget{slot + 1}", $"Bonus{picks[slot] + 1}");

                switch (picks[slot])
                {
                    case 0:
                        storage.SetValue("$tipUncommonCard", slot + 3);
                        break;
                    case 1:
                        storage.SetValue("$tipRareCard", slot + 3);
                        break;
                    case 5:
                        storage.SetValue("$tipTransformCard", slot + 3);
                        break;
                }
            }

            return true;
        }
    }
}
