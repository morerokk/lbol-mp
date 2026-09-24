using HarmonyLib;
using LBOLMP.Session;
using LBOLMP.Session.Battle;
using LBoL.Base;
using LBoL.Core;
using LBoL.Core.Battle;
using LBoL.Core.Battle.BattleActions;
using LBoL.Core.StatusEffects;
using LBoL.Core.Units;
using UnityEngine;
using LBOLMP.Entities.StatusEffects;

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
        /// The unit's Resilient status, or null if it has none.
        /// </summary>
        internal static MpResilient Of(Unit unit)
        {
            var resilient = unit?.GetStatusEffect<MpResilient>();
            return resilient != null && resilient.Level > 0 ? resilient : null;
        }

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
    /// Whenever an enemy would lose Weak or Vulnerable to natural decay, they lose X more (1 for each stack of Resilient).
    /// </summary>
    /// Due to reasons, we cannot cleanly do this in the status effect itself, so we do it in a patch here.
    [HarmonyPatch(typeof(BattleController), "TurnEndDecreaseDuration")]
    public static class EnemyDebuffResiliencePatch
    {
        [HarmonyPrefix]
        private static void Prefix(Unit target)
        {
            MpSafe.Run("EnemyDebuffResiliencePatch", () =>
            {
                var resilient = MpResilience.Of(target);
                if (resilient == null)
                {
                    return;
                }

                bool extraTurn = target.HasStatusEffect<ExtraTurn>()
                                 || (target.HasStatusEffect<SuperExtraTurn>() && !target.IsExtraTurn);
                var timing = extraTurn ? DurationDecreaseTiming.EndTurnForExtra : DurationDecreaseTiming.EndTurnForRound;

                bool activated = false;
                foreach (var effect in target.StatusEffects)
                {
                    if (!(effect is Weak || effect is Vulnerable) || !effect.HasDuration || !effect.IsAutoDecreasing
                        || effect.Duration <= 1 || !effect.Config.DurationDecreaseTiming.HasFlag(timing))
                    {
                        continue;
                    }

                    // Down to 1 at most, because the game takes the last stack off and removes it.
                    effect.Duration = Mathf.Max(1, effect.Duration - resilient.Level);
                    activated = true;
                }

                if (activated)
                {
                    resilient.NotifyActivating();
                }
            });
        }
    }

    /// <summary>
    /// Whenever an enemy would lose Lock On to natural decay, they lose X more (1 for each stack of Resilient).
    /// </summary>
    /// Due to reasons, we cannot cleanly do this in the status effect itself, so we do it in a patch here.
    [HarmonyPatch(typeof(LockedOn), "OnOwnerTurnStarting")]
    public static class EnemyLockOnResiliencePatch
    {
        /// <summary>
        /// This has to happen before <see cref="YoumuModLockOnDecayPatch"/>.
        /// </summary>
        [HarmonyPrefix]
        [HarmonyPriority(Priority.High)]
        private static void Prefix(LockedOn __instance)
        {
            MpSafe.Run("EnemyLockOnResiliencePatch", () =>
            {
                var resilient = MpResilience.Of(__instance.Owner);
                if (resilient == null)
                {
                    return;
                }

                int floor = YoumuInterop.LockOnFloor(__instance.Owner as EnemyUnit);
                bool drops = floor > 0 ? __instance.Level > floor : __instance.IsAutoDecreasing;

                int level = Mathf.Max(floor + 1, __instance.Level - resilient.Level);
                if (!drops || level >= __instance.Level)
                {
                    return;
                }

                __instance.Level = level;
                resilient.NotifyActivating();
            });
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
