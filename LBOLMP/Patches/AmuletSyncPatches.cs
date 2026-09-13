using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using LBOLMP.Session;
using LBOLMP.Session.Battle;
using LBoL.Core;
using LBoL.Core.Battle;
using LBoL.Core.Battle.BattleActions;
using LBoL.Core.Units;
using LBoL.EntityLib.StatusEffects.Basic;

namespace LBOLMP.Patches
{
    // Sync amulet properly
    [HarmonyPatch(typeof(ApplyStatusEffectAction), "PreEventPhase")]
    public static class StatusOriginPreEventPatch
    {
        [HarmonyPrefix]
        private static void Prefix(ApplyStatusEffectAction __instance, out bool __state)
        {
            __state = MpStatusOrigin.Replaying;
            MpStatusOrigin.Replaying = MpSafe.Run("StatusOriginPreEventPatch",
                () => MpSession.IsActive && MpBattleSync.IsInjected(__instance), false);
        }

        [HarmonyPostfix]
        private static void Postfix(bool __state)
        {
            MpStatusOrigin.Replaying = __state;
        }
    }

    [HarmonyPatch(typeof(Amulet), "OnStatusEffectAdding")]
    public static class AmuletReplayPatch
    {
        [HarmonyPrefix]
        private static bool Prefix(ref IEnumerable<BattleAction> __result)
        {
            // If it reached us, it got through their Amulet.
            if (!MpSafe.Run("AmuletReplayPatch.Prefix", () => MpStatusOrigin.Replaying, false))
            {
                return true;
            }

            __result = Enumerable.Empty<BattleAction>();
            return false;
        }

        [HarmonyPostfix]
        private static void Postfix(Amulet __instance, StatusEffectApplyEventArgs args,
                                    ref IEnumerable<BattleAction> __result)
        {
            if (__result == null || !MpSession.IsActive || MpStatusOrigin.Replaying)
            {
                return;
            }

            __result = PublishSpent(__instance, args, __result);
        }

        private static IEnumerable<BattleAction> PublishSpent(
            Amulet amulet, StatusEffectApplyEventArgs args, IEnumerable<BattleAction> blocking)
        {
            var enemy = amulet.Owner as EnemyUnit;
            int before = amulet.Level;
            bool ours = MpSafe.Run("AmuletReplayPatch.Source", () =>
                enemy != null && amulet.Battle != null
                && StatusReplicationPatch.IsLocalPlayerSource(args?.ActionSource, amulet.Battle), false);

            foreach (var action in blocking)
            {
                yield return action;
            }

            if (!ours || amulet.Level >= before)
            {
                yield break;
            }

            MpSafe.Run("AmuletReplayPatch.Publish", () =>
            {
                if (amulet.Level <= 0)
                {
                    MpBattleSync.ReportEnemyStatusRemoved(enemy, amulet.Id);
                }
                else
                {
                    MpBattleSync.ReportEnemyStatusLevel(enemy, amulet);
                }
            });
        }
    }
}
