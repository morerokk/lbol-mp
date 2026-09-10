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
    /// Publish the Firepower and Spirit that Perfect Crime shaves off an enemy.
    /// </summary>
    [HarmonyPatch(typeof(PerfectCrime), "Actions")]
    public static class PerfectCrimeStealPatch
    {
        /// <summary>The status effects it reduces rather than removes.</summary>
        private static readonly Type[] Reduced =
        {
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

        private static Dictionary<string, int> Snapshot(EnemyUnit enemy)
        {
            var levels = new Dictionary<string, int>();
            if (enemy == null)
            {
                return levels;
            }

            foreach (var type in Reduced)
            {
                var effect = enemy.GetStatusEffect(type);
                if (effect != null && effect.HasLevel)
                {
                    levels[effect.Id] = effect.Level;
                }
            }

            return levels;
        }

        private static void Publish(EnemyUnit enemy, Dictionary<string, int> before)
        {
            if (enemy == null || before == null || !MpSession.IsActive || !MpBattleSync.InBattle)
            {
                return;
            }

            foreach (var drained in before)
            {
                var effect = enemy.StatusEffects.FirstOrDefault(s => s.Id == drained.Key);
                if (effect != null && effect.HasLevel && effect.Level < drained.Value)
                {
                    MpBattleSync.ReportEnemyStatusLevel(enemy, effect);
                }
            }
        }
    }
}
