using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using LBOLMP.Session;
using LBOLMP.Session.Battle;
using LBoL.Core;
using LBoL.Core.Battle;
using LBoL.Core.StatusEffects;
using LBoL.Core.Units;
using LBoL.EntityLib.Cards.Character.Koishi;

namespace LBOLMP.Patches
{
    /// <summary>
    /// Send to other players what Perfect Crime took off the enemy (removes all Barrier/Graze/Flawless from the enemy, and some firepower/spirit).
    /// </remarks>
    [HarmonyPatch(typeof(PerfectCrime), "Actions")]
    public static class PerfectCrimeStealPatch
    {
        private static readonly Type[] Stolen =
        {
            typeof(Graze), typeof(GuangxueMicai), typeof(Invincible), typeof(InvincibleEternal),
            typeof(Firepower), typeof(TempFirepower), typeof(Spirit), typeof(TempSpirit)
        };

        [HarmonyPostfix]
        private static void Postfix(ref IEnumerable<BattleAction> __result, UnitSelector selector)
        {
            var original = __result;
            if (original != null)
            {
                __result = Watch(original, selector);
            }
        }

        private static IEnumerable<BattleAction> Watch(
            IEnumerable<BattleAction> actions, UnitSelector selector)
        {
            var enemy = MpSafe.Run("PerfectCrimeStealPatch.Target", () => selector?.SelectedEnemy, null);
            var before = MpSafe.Run("PerfectCrimeStealPatch.Before", () => Snapshot(enemy), null);

            foreach (var action in actions)
            {
                yield return action;
            }

            MpSafe.Run("PerfectCrimeStealPatch", () => Publish(enemy, before));
        }

        private sealed class Loot
        {
            internal int Shield;

            /// <summary>Level by effect id, or -1 for one that has no level.</summary>
            internal readonly Dictionary<string, int> Effects = new Dictionary<string, int>();
        }

        private static Loot Snapshot(EnemyUnit enemy)
        {
            var loot = new Loot();
            if (enemy == null)
            {
                return loot;
            }

            loot.Shield = enemy.Shield;

            foreach (var type in Stolen)
            {
                var effect = enemy.GetStatusEffect(type);
                if (effect != null)
                {
                    loot.Effects[effect.Id] = effect.HasLevel ? effect.Level : -1;
                }
            }

            return loot;
        }

        private static void Publish(EnemyUnit enemy, Loot before)
        {
            if (enemy == null || before == null || !MpSession.IsActive || !MpBattleSync.InBattle)
            {
                return;
            }

            if (enemy.Shield < before.Shield)
            {
                MpBattleSync.ReportEnemyBlockShieldLoss(enemy, 0, before.Shield - enemy.Shield);
            }

            foreach (var stolen in before.Effects)
            {
                var effect = enemy.StatusEffects.FirstOrDefault(s => s.Id == stolen.Key);
                if (effect == null)
                {
                    MpBattleSync.ReportEnemyStatusRemoved(enemy, stolen.Key);
                    continue;
                }

                if (effect.HasLevel && effect.Level < stolen.Value)
                {
                    MpBattleSync.ReportEnemyStatusLevel(enemy, effect);
                }
            }
        }
    }
}
