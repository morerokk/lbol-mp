using System;
using HarmonyLib;
using LBoL.Core;
using LBoL.Core.Battle;

namespace LBOLMP.Patches
{
    /// <summary>
    /// Leaving a battle clears every status effect, and a few of them queue an action when they
    /// are removed (Burst plays its "burst ends" effect). Vanilla never has one of those left over:
    /// the last EndPlayerTurnAction always runs first and takes Burst off.
    ///
    /// In multiplayer it can be left over. A partner's card reaching this client after the local
    /// turn already ended (like aoe event horizon from the ability pile jadebox) gives the local
    /// player a Burst that no turn end will ever remove. If that same play wins the fight, the
    /// removal reacts outside of any action resolution, ActionResolver throws, and the exception
    /// kills GameMaster's battle flow: this player sits on the battle forever while everyone else
    /// moves on to the rewards.
    ///
    /// While the battle is being left, such reactions are dropped instead. There is nothing left
    /// to resolve them in anyway.
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
