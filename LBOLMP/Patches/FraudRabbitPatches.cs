using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using LBOLMP.Session;
using LBOLMP.Session.Battle;
using LBoL.Core;
using LBoL.Core.Battle;
using LBoL.Core.Battle.BattleActions;
using LBoL.EntityLib.EnemyUnits.Normal;

namespace LBOLMP.Patches
{
    /// <summary>
    /// Make the Trickster Rabbit leave at the same time for everyone.
    /// This makes her leave early only if everyone is out of money.
    /// </summary>
    [HarmonyPatch(typeof(FraudRabbit), "OnTurnStarted")]
    public static class FraudRabbitNoMoneyPatch
    {
        [HarmonyPrefix]
        private static bool Prefix(FraudRabbit __instance, ref IEnumerable<BattleAction> __result)
        {
            if (!MpSafe.Run("FraudRabbitNoMoneyPatch",
                    () => MpSession.IsActive && MpBattleSync.InBattle && !MpPrivateEnemies.IsPrivate(__instance), false))
            {
                return true;
            }

            __result = OnTurnStarted(__instance);
            return false;
        }

        /// <summary>
        /// The game's own TurnStarted, but with the party's money instead of just our own.
        /// </summary>
        private static IEnumerable<BattleAction> OnTurnStarted(FraudRabbit rabbit)
        {
            if (rabbit._escaping || rabbit.Next == FraudRabbit.MoveType.Start)
            {
                yield break;
            }

            string chat;
            if (rabbit.Battle.AllAliveEnemies.Count() == 1)
            {
                chat = "Chat.FraudRabbitOnlyOne";
            }
            else if (MpBattleSync.PartyOutOfMoney)
            {
                chat = "Chat.FraudRabbitNoMoney";
            }
            else if (rabbit.TurnCounter >= 4)
            {
                chat = "Chat.FraudRabbitLongTurn";
            }
            else
            {
                yield break;
            }

            yield return PerformAction.Chat(rabbit, chat.LocalizeWithChar(rabbit.GameRun, true), 3f, 0f, 0f, true);
            rabbit._escaping = true;
        }
    }
}
