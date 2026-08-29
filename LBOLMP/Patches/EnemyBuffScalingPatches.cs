using System;
using System.Collections.Generic;
using HarmonyLib;
using LBOLMP.Net;
using LBOLMP.Session;
using LBOLMP.Session.Battle;
using LBoL.Core;
using LBoL.Core.Battle;
using LBoL.Core.Battle.BattleActions;
using LBoL.Core.StatusEffects;
using LBoL.Core.Units;
using LBoL.EntityLib.EnemyUnits.Character;
using LBoL.EntityLib.EnemyUnits.Normal;
using LBoL.EntityLib.StatusEffects.Enemy;
using LBoL.Presentation;
using UnityEngine;

namespace LBOLMP.Patches
{
    /// <summary>
    /// Determines enemy HP and buff scales.
    /// </summary>
    internal static class MpEnemyScaling
    {
        /// <summary>
        /// Players in this fight beyond the first, or zero when we are not scaling anything.
        /// Counted from the players actually fighting, in case there is someone spectating.
        /// </summary>
        internal static int ExtraFighters
        {
            get
            {
                if (!MpSession.IsActive)
                {
                    return 0;
                }

                int fighters = MpEventBattle.Active
                    ? MpEventBattle.FighterCount
                    : MpSession.ConnectedCount;

                return Mathf.Max(0, fighters - 1);
            }
        }

        /// <summary>
        /// Which act this unit's fight is currently in.
        /// </summary>
        internal static int ActOf(Unit unit)
        {
            var run = unit?.GameRun ?? GameMaster.Instance?.CurrentGameRun;
            return Mathf.Clamp(run?.CurrentStage?.Level ?? 1, 1, MpConstants.ActCount);
        }

        /// <summary>
        /// The pure moon rabbit duo is permanently Flawless and should skip the per-act "Escalation" HP modifier.
        /// </summary>
        internal static bool SkipsEscalation(Unit unit) => unit is HardworkRabbit || unit is LazyRabbit;

        /// <summary>
        /// The pure moon rabbit duo should only get 75% of the flat HP scaling, too.
        /// </summary>
        internal static bool HasReducedHpScale(Unit unit) => unit is HardworkRabbit || unit is LazyRabbit;

        /// <summary>
        /// Everything the party adds to an enemy beyond its single-player self, as a fraction.
        /// This is both the flat HP bonus per player, as well as additional escalating bonuses per act.
        /// With the shipped defaults, a 4-player party in Act 3 adds up to +390% enemy HP: 3x100% flat,
        /// plus 15% + 30% + 45% escalating.
        /// Moon Rabbits are exempt from the escalation.
        /// </summary>
        internal static float BonusFor(Unit unit)
        {
            int extra = ExtraFighters;
            if (extra <= 0)
            {
                return 0f;
            }

            float flat = MpSession.EnemyHpScalePerExtraPlayer * extra;

            if (HasReducedHpScale(unit))
            {
                flat *= 0.75f;
            }

            if (SkipsEscalation(unit))
            {
                return flat;
            }

            int amountOfExtraEscalationToStack = extra * (extra + 1) / 2;

            return flat + MpSession.EnemyHpEscalationForAct(ActOf(unit)) * amountOfExtraEscalationToStack;
        }

        /// <summary>The host's HP scale applied to the current party. 1.0 when nothing scales.</summary>
        internal static float MultiplierFor(Unit unit) => 1f + BonusFor(unit);

        /// <summary>
        /// Half of <see cref="MultiplierFor"/>, for buffs that should only scale half as fast to avoid frustrating players (Doremy barrier might be insane if it started at 200 and everyone draws badly).
        /// </summary>
        internal static float HalfMultiplierFor(Unit unit) => 1f + BonusFor(unit) * 0.5f;
    }

    /// <summary>
    /// Make Seija's per-turn damage cap grow with the amount of players, exactly as her health does.
    /// </summary>
    [HarmonyPatch(typeof(LimitedDamage), "OnAdded")]
    public static class EnemyDamageCapScalingPatch
    {
        [HarmonyPrefix]
        private static void Prefix(LimitedDamage __instance, Unit unit)
        {
            MpSafe.Run("EnemyDamageCapScalingPatch", () =>
            {
                if (!(unit is EnemyUnit) || MpEnemyScaling.ExtraFighters <= 0
                    || MpPrivateEnemies.IsPrivate(unit))
                {
                    return;
                }

                __instance.Limit = Mathf.Max(1,
                    Mathf.RoundToInt(__instance.Limit * MpEnemyScaling.MultiplierFor(unit)));
            });
        }
    }

    /// <summary>
    /// Scales up enemy Graze buff amounts.
    /// If they receive Graze from their own intents, they receive 1 more for each extra player, even on Lunatic.
    /// A crow that gains 2 graze now gains 3 graze in a 2-player party.
    /// Aya doesn't scale her graze at all, and instead scales Wind God Girl, so we check for that too.
    /// </summary>
    [HarmonyPatch(typeof(ApplyStatusEffectAction), "MainPhase")]
    public static class EnemyGrazeScalingPatch
    {
        [HarmonyPrefix]
        private static void Prefix(ApplyStatusEffectAction __instance)
        {
            MpSafe.Run("EnemyGrazeScalingPatch", () =>
            {
                int extra = MpEnemyScaling.ExtraFighters;
                if (extra <= 0)
                {
                    return;
                }

                var args = __instance.Args;
                var effect = args?.Effect;
                if (effect == null || !(args.Unit is EnemyUnit) || !effect.HasLevel
                    || MpPrivateEnemies.IsPrivate(args.Unit))
                {
                    return;
                }

                // A replay of somebody else's play, which was already scaled on their end.
                if (MpBattleSync.IsInjected(__instance))
                {
                    return;
                }

                if (effect is Graze)
                {
                    if (__instance.Source is WindGirl)
                    {
                        return;
                    }
                }
                else if (!(effect is WindGirl))
                {
                    return;
                }

                int scaled = effect.Level + extra;

                effect.SetInitLevel(scaled);
                args.Level = scaled;
            });
        }
    }

    /// <summary>
    /// Doremy's Barrier grows with the party, at half the rate her health does.
    /// This scales Sleep instead of her Barrier.
    /// </summary>
    [HarmonyPatch(typeof(ApplyStatusEffectAction), "MainPhase")]
    public static class EnemySleepScalingPatch
    {
        [HarmonyPrefix]
        private static void Prefix(ApplyStatusEffectAction __instance)
        {
            MpSafe.Run("EnemySleepScalingPatch", () =>
            {
                if (MpEnemyScaling.ExtraFighters <= 0)
                {
                    return;
                }

                var args = __instance.Args;
                var effect = args?.Effect;
                if (!(effect is Sleep) || !(args.Unit is EnemyUnit) || !effect.HasLevel
                    || MpPrivateEnemies.IsPrivate(args.Unit))
                {
                    return;
                }

                if (MpBattleSync.IsInjected(__instance))
                {
                    return;
                }

                int scaled = Mathf.Max(1,
                    Mathf.RoundToInt(effect.Level * MpEnemyScaling.HalfMultiplierFor(args.Unit)));

                effect.SetInitLevel(scaled);
                args.Level = scaled;
            });
        }
    }

    /// <summary>
    /// Scale up all enemy healing by their HP scale.
    /// So if Sanae has +390% HP, she heals +390% more than the usual 150.
    /// This only covers enemies that heal through actions or intents, so Tenshi's peach is exempted and so is Vampire, as those are already naturally scaled.
    /// </summary>
    [HarmonyPatch(typeof(HealAction), MethodType.Constructor,
        new Type[] { typeof(Unit), typeof(Unit), typeof(int), typeof(HealType), typeof(float) })]
    public static class EnemyHealScalingPatch
    {
        [HarmonyPostfix]
        private static void Postfix(HealAction __instance)
        {
            MpSafe.Run("EnemyHealScalingPatch", () =>
            {
                if (MpEnemyScaling.ExtraFighters <= 0)
                {
                    return;
                }

                var args = __instance.Args;
                if (args == null || args.HealType == HealType.Vampire)
                {
                    return;
                }

                if (!(args.Source is EnemyUnit) || !(args.Target is EnemyUnit target)
                    || MpPrivateEnemies.IsPrivate(target))
                {
                    return;
                }

                args.Amount = Mathf.Max(1f,
                    Mathf.Round(args.Amount * MpEnemyScaling.MultiplierFor(target)));
            });
        }
    }

    /// <summary>
    /// Prevent Tenshi's peach from healing her more than the usual.
    /// </summary>
    [HarmonyPatch(typeof(FlatPeach), "OnDamageReceived")]
    public static class FlatPeachHealScalingExemption
    {
        [HarmonyPostfix]
        private static void Postfix(FlatPeach __instance, ref IEnumerable<BattleAction> __result)
        {
            var original = __result;
            if (original == null)
            {
                return;
            }

            __result = Unscaled(__instance, original);
        }

        private static IEnumerable<BattleAction> Unscaled(FlatPeach peach, IEnumerable<BattleAction> actions)
        {
            foreach (var action in actions)
            {
                MpSafe.Run("FlatPeachHealScalingExemption", () => Restore(peach, action));
                yield return action;
            }
        }

        private static void Restore(FlatPeach peach, BattleAction action)
        {
            var heal = action as HealAction;
            if (heal?.Args == null || heal.Args.Target != peach.Owner)
            {
                return;
            }

            heal.Args.Amount = peach.Level;
        }
    }

    /// <summary>
    /// Seija's Barrier grows with her health, exactly as her health does.
    /// </summary>
    [HarmonyPatch(typeof(CastBlockShieldAction), MethodType.Constructor,
        new Type[] { typeof(Unit), typeof(Unit), typeof(int), typeof(int), typeof(BlockShieldType), typeof(bool) })]
    public static class SeijaBarrierScalingPatch
    {
        [HarmonyPostfix]
        private static void Postfix(CastBlockShieldAction __instance)
        {
            MpSafe.Run("SeijaBarrierScalingPatch", () =>
            {
                if (MpEnemyScaling.ExtraFighters <= 0)
                {
                    return;
                }

                var args = __instance.Args;
                if (args == null || !args.HasShield || args.HasBlock)
                {
                    return;
                }

                if (!(args.Target is LBoL.EntityLib.EnemyUnits.Character.Seija seija)
                    || args.Source != args.Target
                    || MpPrivateEnemies.IsPrivate(seija))
                {
                    return;
                }

                args.Shield = Mathf.Max(1f,
                    Mathf.Round(args.Shield * MpEnemyScaling.MultiplierFor(seija)));
            });
        }
    }

    /// <summary>
    /// A Terminator drone's Defense Matrix grows with the party, at half the rate its health does.
    /// 
    /// Similar to Doremy's sleep.
    /// </summary>
    [HarmonyPatch(typeof(ApplyStatusEffectAction), "MainPhase")]
    public static class DroneBlockScalingPatch
    {
        [HarmonyPrefix]
        private static void Prefix(ApplyStatusEffectAction __instance)
        {
            MpSafe.Run("DroneBlockScalingPatch", () =>
            {
                if (MpEnemyScaling.ExtraFighters <= 0)
                {
                    return;
                }

                var args = __instance.Args;
                var effect = args?.Effect;
                if (!(effect is DroneBlock) || !(args.Unit is EnemyUnit) || !effect.HasLevel
                    || MpPrivateEnemies.IsPrivate(args.Unit))
                {
                    return;
                }

                if (MpBattleSync.IsInjected(__instance))
                {
                    return;
                }

                int scaled = Mathf.Max(1,
                    Mathf.RoundToInt(effect.Level * MpEnemyScaling.HalfMultiplierFor(args.Unit)));

                effect.SetInitLevel(scaled);
                args.Level = scaled;
            });
        }
    }

    /// <summary>
    /// Scales up Tenshi's spell card costs based on her HP scale, so that she isn't going full terminator mode at 75% HP and wrecks the game.
    /// At +250% HP, that means she has to wait for 250 P to use her spellcard, and expends 250 P when she does.
    /// </summary>
    internal static class MpTianziSpell
    {
        /// <summary>The flat price the fight normally has, and the number both patches replace.</summary>
        internal const int VanillaCost = 100;

        /// <summary>
        /// What Tenshi should pay
        /// </summary>
        internal static int Cost(EnemyUnit tianzi)
        {
            if (MpEnemyScaling.ExtraFighters <= 0 || MpPrivateEnemies.IsPrivate(tianzi))
            {
                return VanillaCost;
            }

            return Mathf.Max(VanillaCost,
                Mathf.RoundToInt(VanillaCost * MpEnemyScaling.MultiplierFor(tianzi)));
        }
    }

    /// <summary>
    /// How much Power Tenshi has to be holding before she reaches for the spell card.
    /// This unfortunately has to effectively replace the entire method, since I don't want to use transpilers.
    /// Is another mod really going to mess with Tenshi's spell card thresholds? Maybe, but we can solve that when it happens.
    /// </summary>
    [HarmonyPatch(typeof(Tianzi), "UpdateMoveCounters")]
    public static class TianziSpellThresholdPatch
    {
        [HarmonyPrefix]
        private static bool Prefix(Tianzi __instance)
        {
            int cost = MpSafe.Run("TianziSpellThresholdPatch",
                () => MpTianziSpell.Cost(__instance), MpTianziSpell.VanillaCost);

            if (cost <= MpTianziSpell.VanillaCost)
            {
                return true;
            }

            __instance.CountDown--;
            __instance.DebuffCountDown--;

            if (__instance.CountDown <= 0)
            {
                __instance.Next = Tianzi.MoveType.DefendAndBuff;
                __instance.CountDown = 5;
                return false;
            }

            var energy = __instance.GetStatusEffect<EnemyEnergy>();
            if (energy != null && energy.Level >= cost)
            {
                __instance.Next = Tianzi.MoveType.SpellAttack;
                return false;
            }

            if (__instance.DebuffCountDown <= 0)
            {
                __instance.Next = Tianzi.MoveType.AttackAndDebuff;
                __instance.DebuffCountDown = 4;
                return false;
            }

            __instance.Next = Tianzi.MoveType.Shoot;
            return false;
        }
    }

    /// <summary>
    /// What Tenshi actually pays when she uses it.
    /// </summary>
    [HarmonyPatch(typeof(ApplyStatusEffectAction), "MainPhase")]
    public static class TianziSpellPaymentPatch
    {
        [HarmonyPrefix]
        private static void Prefix(ApplyStatusEffectAction __instance)
        {
            MpSafe.Run("TianziSpellPaymentPatch", () =>
            {
                var args = __instance.Args;
                var effect = args?.Effect;
                if (!(effect is EnemyEnergyNegative) || !(args.Unit is Tianzi tianzi)
                    || !effect.HasLevel)
                {
                    return;
                }

                if (MpBattleSync.IsInjected(__instance))
                {
                    return;
                }

                if (effect.Level != MpTianziSpell.VanillaCost)
                {
                    return;
                }

                int cost = MpTianziSpell.Cost(tianzi);
                if (cost <= MpTianziSpell.VanillaCost)
                {
                    return;
                }

                effect.SetInitLevel(cost);
                args.Level = cost;
            });
        }
    }

    /// <summary>
    /// Scales Lovesick Girl by playercount.
    /// More players adds more stacks of Lingering Regrets, but also reduces the damage reduction per stack of Lingering Regrets.
    /// This means she will be basically invulnerable at the start of the fight,
    /// but won't be *still* nearly untouchable if 1 player in a 4-man party still needs to play 2 Loveletter cards.
    /// </summary>
    internal static class MpLoveGirl
    {
        /// <summary>What one stack turns away in the shipped fight, as a percentage.</summary>
        internal const int ShippedRate = 20;

        /// <summary>How much of that each extra player takes off.</summary>
        private const int RateDropPerExtraPlayer = 5;

        /// <summary>What one stack is worth to this party.</summary>
        internal static int RatePerStack => Mathf.Max(1,
            ShippedRate - RateDropPerExtraPlayer * MpEnemyScaling.ExtraFighters);

        /// <summary>One player's worth of stacks per player.</summary>
        internal static int Stacks(int shipped) => shipped * (1 + MpEnemyScaling.ExtraFighters);
    }

    /// <summary>
    /// How many stacks of Lingering Regrets the Lovesick Girl opens with. See <see cref="MpLoveGirl"/>.
    /// </summary>
    [HarmonyPatch(typeof(ApplyStatusEffectAction), "MainPhase")]
    public static class LoveGirlRegretScalingPatch
    {
        [HarmonyPrefix]
        private static void Prefix(ApplyStatusEffectAction __instance)
        {
            MpSafe.Run("LoveGirlRegretScalingPatch", () =>
            {
                if (MpEnemyScaling.ExtraFighters <= 0)
                {
                    return;
                }

                var args = __instance.Args;
                var effect = args?.Effect;
                if (!(effect is LoveGirlDamageReduce) || !(args.Unit is EnemyUnit girl)
                    || !effect.HasLevel || MpPrivateEnemies.IsPrivate(girl))
                {
                    return;
                }

                if (MpBattleSync.IsInjected(__instance))
                {
                    return;
                }

                int scaled = MpLoveGirl.Stacks(effect.Level);

                effect.SetInitLevel(scaled);
                args.Level = scaled;
            });
        }
    }

    /// <summary>
    /// What each of her Lingering Regrets is worth. See <see cref="MpLoveGirl"/>.
    /// </summary>
    [HarmonyPatch(typeof(LoveGirlDamageReduce), nameof(LoveGirlDamageReduce.Rate), MethodType.Getter)]
    public static class LoveGirlRegretRatePatch
    {
        [HarmonyPostfix]
        private static void Postfix(LoveGirlDamageReduce __instance, ref int __result)
        {
            int returnValue = __result;

            __result = MpSafe.Run("LoveGirlRegretRatePatch", () =>
            {
                int perStack = MpLoveGirl.RatePerStack;
                if (perStack >= MpLoveGirl.ShippedRate || MpPrivateEnemies.IsPrivate(__instance.Owner))
                {
                    return returnValue;
                }

                // Cap it at 100% to prevent her from healing from damage
                return Mathf.Min(100, __instance.Level * perStack);
            }, returnValue);
        }
    }
}
