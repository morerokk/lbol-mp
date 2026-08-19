using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using LBOLMP.Session;
using LBOLMP.Session.Battle;
using LBoL.Core;
using LBoL.Core.Battle;
using LBoL.Core.Battle.BattleActions;
using LBoL.Core.StatusEffects;
using LBoL.Core.Units;
using LBoL.EntityLib.StatusEffects.Enemy;
using LBoL.EntityLib.StatusEffects.Neutral.TwoColor;

namespace LBOLMP.Patches
{
    /// <summary>
    /// Prevent players from gaining +3 block from Great Tengu's Decree if they are not the one doing the attack.
    /// If this is someone else's damage instance, skip the block gain.
    /// </summary>
    [HarmonyPatch(typeof(TiangouOrderSe), "OnDamageReceived")]
    internal static class TiangouOrderBlockPatch
    {
        private static bool Prefix(DamageEventArgs args, ref IEnumerable<BattleAction> __result)
        {
            bool theirHit = MpSafe.Run("TiangouOrderBlockPatch",
                () => MpSession.IsActive && MpBattleSync.InBattle && UI.MpAllyUnits.IsMirror(args?.Source),
                false);

            if (!theirHit)
            {
                return true;
            }

            __result = Enumerable.Empty<BattleAction>();
            return false;
        }
    }

    /// <summary>
    /// Makes Sanae's "I'm Really Curious!" count everyone's ability card plays.
    /// This is intentionally not globally applied to all enemy firepower gain, just the ones that come from Curiosity.
    /// </summary>
    [HarmonyPatch(typeof(ApplyStatusEffectAction), "MainPhase")]
    internal static class CuriosityReplicationPatch
    {
        [HarmonyPostfix]
        private static void Postfix(ApplyStatusEffectAction __instance)
        {
            MpSafe.Run("CuriosityReplicationPatch", () =>
            {
                if (!MpSession.IsActive || !MpBattleSync.InBattle)
                {
                    return;
                }

                if (!(__instance.Source is Curiosity curiosity))
                {
                    return;
                }

                var args = __instance.Args;
                if (args?.AddResult == null || !(args.Effect is Firepower)
                    || !(args.Unit is EnemyUnit enemy))
                {
                    return;
                }

                // This will likely never come up, but who knows (Eiki Shiki mirror nonsense).
                if (MpPrivateEnemies.IsPrivate(enemy))
                {
                    return;
                }

                if (!ReferenceEquals(curiosity.Owner, enemy))
                {
                    return;
                }

                MpBattleSync.ReportCuriosity(enemy, args.Level ?? curiosity.Level);
            });
        }
    }
}
