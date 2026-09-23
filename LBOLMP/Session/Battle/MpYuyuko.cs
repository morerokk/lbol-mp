using System.Collections.Generic;
using LBOLMP.Entities;
using LBOLMP.Net;
using LBOLMP.Session.Messages;
using LBoL.Core;
using LBoL.Core.Battle;
using LBoL.Core.Battle.BattleActions;
using LBoL.EntityLib.StatusEffects.Enemy;
using LBoL.Presentation;

namespace LBOLMP.Session.Battle
{
    /// <summary>
    /// Interop with the Yuyuko character mod, for her Law of Mortality.
    /// </summary>
    internal static class MpYuyuko
    {
        internal static void RegisterHandlers() => MpNet.OnRemote<LawOfMortalityMessage>(OnRemoteTrigger);

        /// <summary>Publish a trigger that just went off locally.</summary>
        internal static void Report(BattleAction action)
        {
            if (!MpBattleSync.InBattle || !MpSession.IsActive || MpBattleSync.SpectatingOnly)
            {
                return;
            }

            var amount = YuyukoInterop.AmountOf(action);
            if (amount <= 0)
            {
                return;
            }

            MpNet.Send(new LawOfMortalityMessage { Amount = amount });
        }

        private static void OnRemoteTrigger(LawOfMortalityMessage message)
        {
            if (!MpBattleSync.InBattle)
            {
                return;
            }

            var battle = GameMaster.Instance?.CurrentGameRun?.Battle;
            if (battle == null || battle.BattleShouldEnd || message.Amount <= 0)
            {
                return;
            }

            MpBattleSync.QueueReplicated(
                battle,
                new MpDeferredAction(b => Fire(b, message.Amount)),
                "MP remote Law of Mortality");
        }

        private static IEnumerable<BattleAction> Fire(BattleController battle, int amount)
        {
            for (var i = 0; i < amount; i++)
            {
                foreach (var enemy in battle.AllAliveEnemies)
                {
                    if (battle.BattleShouldEnd)
                    {
                        yield break;
                    }

                    var mark = enemy.GetStatusEffect<YuyukoDeath>();
                    if (mark == null || MpPrivateEnemies.IsPrivate(enemy))
                    {
                        continue;
                    }

                    yield return PerformAction.Effect(enemy, "YuyukoDeathHit", 0f, "YuyukoDeathHit");
                    yield return new DamageAction(enemy, enemy, DamageInfo.HpLose(mark.Level));
                }
            }
        }
    }
}
