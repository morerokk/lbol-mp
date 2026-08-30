using System.Linq;
using LBOLMP.Net;
using LBOLMP.Session.Messages;
using LBoL.Core;
using LBoL.Core.Battle.BattleActions;
using LBoL.Core.Units;
using LBoL.Presentation;

namespace LBOLMP.Session.Battle
{
    internal static class MpVampire
    {
        internal static void RegisterHandlers() => MpNet.On<EnemyVampireHealMessage>(OnRemoteHeal);

        internal static void Report(EnemyUnit enemy, int amount)
        {
            if (enemy == null || amount <= 0 || !MpBattleSync.InBattle || !MpSession.IsActive
                || MpBattleSync.SpectatingOnly || MpPrivateEnemies.IsPrivate(enemy))
            {
                return;
            }

            MpNet.Send(new EnemyVampireHealMessage
            {
                EnemyIndex = enemy.Index,
                Amount = amount
            });
        }

        private static void OnRemoteHeal(EnemyVampireHealMessage message)
        {
            if (message.SenderId == MpNet.LocalPlayerId || !MpBattleSync.InBattle
                || message.Amount <= 0)
            {
                return;
            }

            var battle = GameMaster.Instance?.CurrentGameRun?.Battle;
            var enemy = battle?.EnemyGroup.FirstOrDefault(e => e.Index == message.EnemyIndex);
            if (enemy == null || !enemy.IsAlive || MpPrivateEnemies.IsPrivate(enemy))
            {
                return;
            }

            var source = UI.MpAllyUnits.GetUnit(message.SenderId) ?? (Unit)enemy;

            MpBattleSync.QueueReplicated(
                battle,
                new HealAction(source, enemy, message.Amount, HealType.Vampire, 0f),
                "MP remote vampire drain");
        }
    }
}
