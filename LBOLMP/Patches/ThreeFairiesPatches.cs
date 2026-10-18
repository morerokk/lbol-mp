using HarmonyLib;
using LBOLMP.Session;
using LBoL.Core;
using LBoL.Core.Battle;
using LBoL.Core.Battle.BattleActions;
using LBoL.Core.Units;

namespace LBOLMP.Patches
{
    /// <summary>
    /// "No one" means "no one"
    /// (TFoL has effects that prevent everyone from gaining certain statuses. These patches apply it to all players)
    /// </summary>
    [HarmonyPatch(typeof(ApplyStatusEffectAction), "MainPhase")]
    public static class SilenceSignStatusPatch
    {
        [HarmonyPrefix]
        private static bool Prefix(ApplyStatusEffectAction __instance)
        {
            bool blocked = MpSafe.Run("SilenceSignStatusPatch",
                () => ThreeFairiesInterop.BlocksStatus(__instance.Args), false);
            if (!blocked)
            {
                return true;
            }

            __instance.Args.ForceCancelBecause(CancelCause.Reaction);
            return false;
        }
    }

    /// <summary>
    /// Sync Phase of Measured Walls
    /// </summary>
    [HarmonyPatch(typeof(BattleController), "CastBlockShield")]
    public static class SilenceSignBlockCapPatch
    {
        [HarmonyPrefix]
        private static void Prefix(Unit target, ref float block, ref float shield)
        {
            float b = block;
            float s = shield;
            MpSafe.Run("SilenceSignBlockCapPatch", () => ThreeFairiesInterop.CapBlockShield(target, ref b, ref s));
            block = b;
            shield = s;
        }
    }
}
