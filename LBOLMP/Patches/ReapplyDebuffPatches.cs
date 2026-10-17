using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using LBOLMP.Session;
using LBOLMP.Session.Battle;
using LBoL.Core;
using LBoL.Core.Battle;
using LBoL.Core.Battle.BattleActions;
using LBoL.Core.Units;
using LBoL.EntityLib.EnemyUnits.Normal;
using LBoL.EntityLib.StatusEffects.Basic;
using LBoL.EntityLib.StatusEffects.Enemy;

namespace LBOLMP.Patches
{
    /// <summary>
    /// Fox Youkai and Drowned Girl now re-apply debuffs to everyone if at least 50% of players do not have the debuff anymore.
    /// </summary>
    internal static class MpReapplyDebuff
    {
        internal static bool? Reapply(EnemyUnit enemy, string statusId)
        {
            if (!MpSession.IsActive || !MpBattleSync.InBattle || MpPrivateEnemies.IsPrivate(enemy))
            {
                return null;
            }

            var reapply = MpBattleSync.DoEnoughPlayersLackStatus(statusId);
            if (reapply != null)
            {
                MpPlugin.Log.LogInfo($"{enemy.GetType().Name} {(reapply.Value ? "re-applies" : "does not re-apply")} {statusId}, by the party's turn-end reports");
            }

            return reapply;
        }

        internal static IEnumerable<BattleAction> SkipAlreadyAffectedPlayers(IEnumerable<BattleAction> actions)
        {
            foreach (var action in actions)
            {
                if (MpSession.IsActive
                    && action is ApplyStatusEffectAction apply
                    && apply.Args?.Unit is PlayerUnit player
                    && apply.Args.Effect != null
                    && player.StatusEffects.Any(effect => effect.Id == apply.Args.Effect.Id))
                {
                    MpPlugin.Log.LogInfo($"Already has debuff {apply.Args.Effect.Id}, not applying the status");
                    continue;
                }

                yield return action;
            }
        }
    }

    [HarmonyPatch(typeof(Fox))]
    public static class FoxYoukaiCharmPartyPatch
    {
        [HarmonyPatch("UpdateMoveCounters")]
        [HarmonyPostfix]
        private static void Decide(Fox __instance)
        {
            MpSafe.Run("FoxYoukaiCharmPartyPatch.Decide", () =>
            {
                if (__instance.Next == Fox.MoveType.Attack)
                {
                    return;
                }

                var reapply = MpReapplyDebuff.Reapply(__instance, nameof(FoxCharm));
                if (reapply != null)
                {
                    __instance.Next = reapply.Value ? Fox.MoveType.Debuff : Fox.MoveType.AttackLarge;
                }
            });
        }

        [HarmonyPatch("CharmActions")]
        [HarmonyPostfix]
        private static void Spare(ref IEnumerable<BattleAction> __result)
        {
            if (__result != null)
            {
                __result = MpReapplyDebuff.SkipAlreadyAffectedPlayers(__result);
            }
        }
    }

    [HarmonyPatch(typeof(WaterGirl))]
    public static class DrownedGirlDrowningPartyPatch
    {
        [HarmonyPatch("UpdateMoveCounters")]
        [HarmonyPostfix]
        private static void Decide(WaterGirl __instance)
        {
            MpSafe.Run("DrownedGirlDrowningPartyPatch.Decide", () =>
            {
                if (__instance.Next == WaterGirl.MoveType.Attack)
                {
                    return;
                }

                var reapply = MpReapplyDebuff.Reapply(__instance, nameof(Drowning));
                if (reapply != null)
                {
                    __instance.Next = reapply.Value ? WaterGirl.MoveType.Debuff : WaterGirl.MoveType.AttackLarge;
                }
            });
        }

        [HarmonyPatch("DrownActions")]
        [HarmonyPostfix]
        private static void Spare(ref IEnumerable<BattleAction> __result)
        {
            if (__result != null)
            {
                __result = MpReapplyDebuff.SkipAlreadyAffectedPlayers(__result);
            }
        }
    }
}
