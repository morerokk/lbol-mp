using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using LBOLMP.Entities;
using LBOLMP.Net;
using LBOLMP.Session.Messages;
using LBoL.Core;
using LBoL.Core.Battle;
using LBoL.Core.StatusEffects;
using LBoL.Core.Units;
using LBoL.EntityLib.StatusEffects.Others;
using LBoL.Presentation;

namespace LBOLMP.Session.Battle
{
    /// <summary>
    /// Replicates a card effect that triggers an enemy's status effects right away (such as Larva).
    /// </summary>
    internal static class MpStatusTriggers
    {
        /// <summary>The convention these effects follow for "go off now".</summary>
        private const string TriggerName = "TakeEffect";

        /// <summary>Set while replaying somebody else's trigger, so it is not sent back to them.</summary>
        private static bool _replaying;

        private static MethodInfo[] _triggers;

        internal static void RegisterHandlers() => MpNet.On<EnemyStatusTriggerMessage>(OnRemoteTrigger);

        /// <summary>
        /// Every status effect that can be told to go off early, found by the name they all use.
        /// </summary>
        internal static IEnumerable<MethodBase> TriggerMethods()
        {
            if (_triggers != null)
            {
                return _triggers;
            }

            _triggers = typeof(Poison).Assembly
                .GetTypes()
                .Where(type => type.IsSubclassOf(typeof(StatusEffect)))
                .Select(Trigger)
                .Where(method => method != null)
                .ToArray();

            MpPlugin.Log.LogInfo(
                $"Watching {_triggers.Length} status effects for an early {TriggerName}()");

            return _triggers;
        }

        /// <summary>The public, parameterless "go off now" method on an effect, if it has one.</summary>
        private static MethodInfo Trigger(Type type)
        {
            var method = type.GetMethod(TriggerName, BindingFlags.Instance | BindingFlags.Public,
                null, Type.EmptyTypes, null);

            return method != null && method.DeclaringType == type ? method : null;
        }

        /// <summary>
        /// Publish an effect the local player has just triggered on a shared enemy.
        /// </summary>
        internal static void Report(StatusEffect effect)
        {
            if (_replaying || effect == null || !MpBattleSync.InBattle
                || !MpSession.IsActive || MpBattleSync.SpectatingOnly)
            {
                return;
            }

            if (!(effect.Owner is EnemyUnit enemy) || MpPrivateEnemies.IsPrivate(enemy))
            {
                return;
            }

            // If this is the enemy's turn then it's whatever
            if (MpBattleSync.EnemyTurnRunning)
            {
                return;
            }

            MpNet.Send(new EnemyStatusTriggerMessage
            {
                EnemyIndex = enemy.Index,
                StatusId = effect.Id
            });
        }

        private static void OnRemoteTrigger(EnemyStatusTriggerMessage message)
        {
            if (message.SenderId == MpNet.LocalPlayerId || !MpBattleSync.InBattle)
            {
                return;
            }

            var battle = GameMaster.Instance?.CurrentGameRun?.Battle;
            var enemy = battle?.EnemyGroup.FirstOrDefault(e => e.Index == message.EnemyIndex);
            if (enemy == null || !enemy.IsAlive || MpPrivateEnemies.IsPrivate(enemy))
            {
                return;
            }

            MpBattleSync.QueueReplicated(
                battle,
                new MpDeferredAction(b => Fire(b, message.EnemyIndex, message.StatusId)),
                "MP remote status trigger");
        }

        private static IEnumerable<BattleAction> Fire(BattleController battle, int enemyIndex, string statusId)
        {
            var enemy = battle.EnemyGroup.FirstOrDefault(e => e.Index == enemyIndex);
            var effect = enemy?.StatusEffects.FirstOrDefault(s => s.Id == statusId);
            if (effect == null || !enemy.IsAlive || battle.BattleShouldEnd)
            {
                // Already gone here, which is the same place the sender ended up.
                yield break;
            }

            var trigger = Trigger(effect.GetType());
            if (trigger == null)
            {
                MpPlugin.Log.LogWarning(
                    $"'{statusId}' was set off remotely but has no {TriggerName}() to run here");
                yield break;
            }

            object result;
            _replaying = true;
            try
            {
                result = trigger.Invoke(effect, null);
            }
            catch (Exception e)
            {
                MpPlugin.Log.LogError($"Could not set off '{statusId}': {e}");
                yield break;
            }
            finally
            {
                _replaying = false;
            }

            // The two shapes these methods come in.
            if (result is IEnumerable<BattleAction> actions)
            {
                foreach (var action in actions)
                {
                    yield return action;
                }
            }
            else if (result is BattleAction single)
            {
                yield return single;
            }
        }
    }
}
