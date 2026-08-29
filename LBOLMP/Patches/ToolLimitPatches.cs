using HarmonyLib;
using LBOLMP.Session;
using LBoL.Core.Cards;
using LBoL.EntityLib.Cards.Adventure;
using UnityEngine;

namespace LBOLMP.Patches
{
    /// <summary>
    /// Make the EMP Device card have less uses in multiplayer.
    /// </summary>
    /// <remarks>
    /// Normally in singleplayer: Limited 3
    /// 2-3 player lobbies: Limited 2
    /// 4+ player lobbies: Limited 1
    /// </remarks>
    [HarmonyPatch(typeof(Card), nameof(Card.ToolPlayableTimes), MethodType.Getter)]
    public static class ToolPlayableTimesPatch
    {
        [HarmonyPostfix]
        private static void Postfix(Card __instance, ref int __result)
        {
            int vanillaLimitedCount = __result;
            __result = MpSafe.Run("ToolPlayableTimesPatch",
                () => __instance is EmpCard ? PartyUses(vanillaLimitedCount) : vanillaLimitedCount, vanillaLimitedCount);
        }

        private static int PartyUses(int vanillaLimitedCount)
        {
            if (!MpSession.IsActive)
            {
                return vanillaLimitedCount;
            }

            int players = MpSession.ConnectedCount;
            if (players >= 4)
            {
                return Mathf.Min(vanillaLimitedCount, 1);
            }

            return players >= 2 ? Mathf.Min(vanillaLimitedCount, 2) : vanillaLimitedCount;
        }
    }
}
