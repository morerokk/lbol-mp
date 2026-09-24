using HarmonyLib;
using LBOLMP.Session;
using LBOLMP.Session.Battle;
using LBoL.Core.Battle.BattleActions;
using LBoL.Core.StatusEffects;
using LBoL.Core.Units;

namespace LBOLMP.Patches
{
    /// <summary>
    /// See <see cref="YoumuInterop"/>. Youmu's green exhibit applies a minimum lock on level, so we have to account for that.
    /// </summary>
    [HarmonyPatch(typeof(LockedOn), "OnOwnerTurnStarting")]
    public static class YoumuModLockOnDecayPatch
    {
        [HarmonyPrefix]
        private static bool Prefix(LockedOn __instance)
        {
            int floor = MpSafe.Run("YoumuLockOnDecayPatch",
                () => YoumuInterop.LockOnFloor(__instance.Owner as EnemyUnit), 0);
            if (floor <= 0)
            {
                return true;
            }

            if (__instance.Level > floor)
            {
                __instance.Level--;
            }
            return false;
        }
    }

    /// <summary>
    /// Fixes Youmu's green exhibit. It halves lock-on whenever it would be fully cleansed, so we have to replicate that effect here.
    /// </summary>
    [HarmonyPatch(typeof(RemoveStatusEffectAction), "PreEventPhase")]
    public static class YoumuModLockOnRemovalPatch
    {
        [HarmonyPostfix]
        private static void Postfix(RemoveStatusEffectAction __instance)
        {
            MpSafe.Run("YoumuLockOnRemovalPatch", () =>
            {
                var args = __instance.Args;
                YoumuInterop.StandInForRemoval(args, __instance.Battle);

                if (args == null || !YoumuInterop.IsLockOnFloorCancel(args) || MpBattleSync.ConsumeInjected(__instance))
                {
                    return;
                }

                if (args.Unit is EnemyUnit enemy
                    && StatusReplicationPatch.IsLocalPlayerSource(__instance.Source, __instance.Battle))
                {
                    MpBattleSync.ReportEnemyStatusRemoved(enemy, args.Effect.Id);
                }
            });
        }
    }
}
