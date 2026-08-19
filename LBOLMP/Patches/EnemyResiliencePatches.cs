using HarmonyLib;
using LBOLMP.Entities;
using LBOLMP.Session;
using LBOLMP.Session.Battle;
using LBoL.Core;
using LBoL.Core.Battle.BattleActions;
using LBoL.Core.StatusEffects;
using LBoL.Core.Units;
using UnityEngine;

namespace LBOLMP.Patches
{
    /// <summary>
    /// Applies the new Resilient status to enemies, which makes them less vulnerable to debuff stacking.
    /// </summary>
    internal static class MpResilience
    {
        // Amount of players minus one, also deals with the setting being disabled
        internal static int LevelFor(Unit unit) =>
            MpSession.EnemyResilience ? MpEnemyScaling.ExtraFighters : 0;

        /// <summary>
        /// Applied straight onto the unit rather than through an <c>ApplyStatusEffectAction</c>, so we can apply it right away (deals with start-of-combat effects).
        /// </summary>
        internal static void Grant(EnemyUnit enemy)
        {
            if (enemy == null)
            {
                return;
            }

            int level = LevelFor(enemy);
            if (level <= 0)
            {
                return;
            }

            if (MpPrivateEnemies.IsPrivate(enemy))
            {
                return;
            }

            var battle = enemy.Battle;
            if (battle == null || enemy.HasStatusEffect<MpResilient>())
            {
                return;
            }

            var effect = Library.CreateStatusEffect<MpResilient>();

            effect.SetInitLevel(level);

            battle.TryAddStatusEffect(enemy, effect);

            // Force-add the status effect icon since we're kinda sorta hacking it in without notifying the game.
            (enemy.View as LBoL.Presentation.Units.UnitView)
                ?.OnAddStatusEffect(effect, StatusEffectAddResult.Added);
        }
    }

    /// <summary>
    /// Apply resilient to enemies
    /// </summary>
    [HarmonyPatch(typeof(Unit), "EnterBattle")]
    public static class EnemyResilienceApplyPatch
    {
        [HarmonyPostfix]
        private static void Postfix(Unit __instance)
        {
            MpSafe.Run("EnemyResilienceApplyPatch", () => MpResilience.Grant(__instance as EnemyUnit));
        }
    }

    /// <summary>
    /// If (Temporary) Firepower Down would be applied to an enemy, apply 1 less for each stack of Resilient. Minimum of 1.
    /// </summary>
    [HarmonyPatch(typeof(ApplyStatusEffectAction), "MainPhase")]
    public static class EnemyFirepowerDownResiliencePatch
    {
        [HarmonyPrefix]
        private static void Prefix(ApplyStatusEffectAction __instance)
        {
            MpSafe.Run("EnemyFirepowerDownResiliencePatch", () =>
            {
                var args = __instance.Args;
                var effect = args?.Effect;

                if (!(effect is FirepowerNegative || effect is TempFirepowerNegative)
                    || !(args.Unit is EnemyUnit enemy) || !effect.HasLevel)
                {
                    return;
                }

                if (MpBattleSync.IsInjected(__instance))
                {
                    return;
                }

                int resilience = enemy.GetStatusEffect<MpResilient>()?.Level ?? 0;
                if (resilience <= 0)
                {
                    return;
                }

                int reduced = Mathf.Max(1, effect.Level - resilience);
                if (reduced >= effect.Level)
                {
                    return;
                }

                effect.SetInitLevel(reduced);
                args.Level = reduced;
            });
        }
    }
}
