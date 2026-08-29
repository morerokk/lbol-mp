using System.Collections.Generic;
using System.Linq;
using LBOLMP.Entities;
using LBOLMP.Net;
using LBOLMP.Session.Messages;
using LBoL.Core;
using LBoL.Core.Battle;
using LBoL.Core.Battle.BattleActions;
using LBoL.Core.Cards;
using LBoL.Core.Units;
using LBoL.EntityLib.Cards.Enemy;
using LBoL.EntityLib.EnemyUnits.Normal;
using LBoL.Presentation;

namespace LBOLMP.Session.Battle
{
    /// <summary>
    /// When an enemy runs away for 1 player, they run away for all players.
    /// </summary>
    internal static class MpEnemyEscape
    {
        /// <summary>
        /// Enemies already announced as gone, so a replayed escape is not re-announced.
        /// </summary>
        private static readonly HashSet<int> Announced = new HashSet<int>();

        internal static void RegisterHandlers() => MpNet.On<EnemyEscapedMessage>(OnRemoteEscape);

        internal static void Reset() => Announced.Clear();

        internal static void Report(EnemyUnit enemy)
        {
            if (enemy == null || !MpBattleSync.InBattle || !MpSession.IsActive
                || MpBattleSync.SpectatingOnly || MpPrivateEnemies.IsPrivate(enemy))
            {
                return;
            }

            if (!Announced.Add(enemy.Index))
            {
                return;
            }

            MpNet.Send(new EnemyEscapedMessage { EnemyIndex = enemy.Index });
        }

        private static void OnRemoteEscape(EnemyEscapedMessage message)
        {
            if (message.SenderId == MpNet.LocalPlayerId || !MpBattleSync.InBattle)
            {
                return;
            }

            var battle = GameMaster.Instance?.CurrentGameRun?.Battle;
            var enemy = battle?.EnemyGroup.FirstOrDefault(e => e.Index == message.EnemyIndex);
            if (enemy == null || !enemy.IsAlive || enemy.IsEscaped || MpPrivateEnemies.IsPrivate(enemy))
            {
                return;
            }

            // Noted before queueing, so our own escape combat trigger does not trigger another time.
            Announced.Add(enemy.Index);

            MpPlugin.Log.LogInfo($"Player {message.SenderId} saw off {enemy.Id}; letting ours go too");
            MpBattleSync.QueueReplicated(battle, new EscapeAction(enemy), "MP remote escape");

            if (enemy is FraudRabbit)
            {
                MpBattleSync.QueueReplicated(battle, new MpDeferredAction(PayOff), "MP remote escape cleanup");
            }
        }

        /// <summary>
        /// Remove the Trickster Rabbit's status from the hand
        /// </summary>
        private static IEnumerable<BattleAction> PayOff(BattleController battle)
        {
            if (battle.EnemyGroup.Any(e => e is FraudRabbit && e.IsAlive))
            {
                yield break;
            }

            var owed = battle.EnumerateAllCards()
                .Where(c => c is Payment && c.Zone != CardZone.Exile)
                .ToList();

            foreach (var card in owed)
            {
                yield return new ExileCardAction(card);
            }
        }
    }
}
