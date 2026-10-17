using System;
using HarmonyLib;
using LBoL.Core;
using LBoL.Core.Battle;

namespace LBOLMP.Patches
{
    /// <summary>
    /// Fix status effects like Burst causing errors when leaving combat.
    /// For example: entering Burst after your turn has already ended, then winning combat without taking another turn would otherwise cause errors in the game.
    /// </summary>
    [HarmonyPatch(typeof(BattleController), nameof(BattleController.Leave))]
    public static class LeavingBattlePatch
    {
        internal static bool Leaving { get; private set; }

        [HarmonyPrefix]
        private static void Prefix() => Leaving = true;

        [HarmonyFinalizer]
        private static Exception Finalizer(Exception __exception)
        {
            Leaving = false;
            return __exception;
        }
    }

    /// <inheritdoc cref="LeavingBattlePatch"/>
    [HarmonyPatch(typeof(ActionResolver), nameof(ActionResolver.React))]
    public static class LeaveBattleReactPatch
    {
        [HarmonyPrefix]
        private static bool Prefix(ActionResolver __instance, GameEntity source)
        {
            if (!LeavingBattlePatch.Leaving || __instance._reactors != null)
            {
                return true;
            }

            MpPlugin.Log.LogWarning(
                $"Dropped a reaction from {(source == null ? "nothing" : source.GetType().Name)} "
                + $"(owner: {Describe(source)}) while leaving the battle; it would have thrown and stalled the battle end");
            return false;
        }

        private static string Describe(GameEntity source)
        {
            if (source is LBoL.Core.StatusEffects.StatusEffect effect && effect.Owner != null)
            {
                return effect.Owner.GetType().Name;
            }

            return "n/a";
        }
    }
}
