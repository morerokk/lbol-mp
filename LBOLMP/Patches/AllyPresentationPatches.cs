using System;
using System.Collections.Generic;
using HarmonyLib;
using LBOLMP.Session.Battle;
using LBoL.Presentation.Units;

namespace LBOLMP.Patches
{
    /// <summary>
    /// Shared helper for the patches below, all of which have to answer the same question: is this
    /// view the character this client is actually playing, or one of the other player's mirror units?
    /// </summary>
    internal static class LocalPlayerView
    {
        internal static bool Is(UnitView view)
        {
            var director = GameDirector.Instance;
            return view != null && director != null && ReferenceEquals(view, director.PlayerUnitView);
        }
    }

    /// <summary>
    /// Play an entry/debut animation on everyone else if it plays on you.
    /// </summary>
    [HarmonyPatch(typeof(GameDirector), nameof(GameDirector.PlayerDebutAnimation))]
    public static class AllyDebutPatch
    {
        [HarmonyPostfix]
        private static void Postfix()
        {
            UI.MpAllyUnits.PlayDebut();
        }
    }

    /// <summary>
    /// Hide everyone else's player units if yours is hidden (to fix players appearing at events or gaps).
    /// </summary>
    [HarmonyPatch(typeof(GameDirector), nameof(GameDirector.HidePlayer))]
    public static class AllyHidePatch
    {
        [HarmonyPostfix]
        private static void Postfix()
        {
            UI.MpAllyUnits.SetHidden(true, false);
        }
    }

    /// <summary><see cref="AllyHidePatch"/> but the opposite, re-show players if yours is re-shown.</summary>
    [HarmonyPatch(typeof(GameDirector), nameof(GameDirector.RevealPlayer))]
    public static class AllyRevealPatch
    {
        [HarmonyPostfix]
        private static void Postfix(bool withStatus)
        {
            UI.MpAllyUnits.SetHidden(false, withStatus);
        }
    }

    /// <summary>
    /// When you play certain animations for any reason, also play it for other clients (skill/ability uses, taking damage, etc).
    /// This does NOT handle attack cards, since those are synced separately through the gun.
    /// Does not handle certain spellcards yet, because most of them are already synced through their guns (including the portrait popup).
    /// </summary>
    [HarmonyPatch(typeof(UnitView), nameof(UnitView.PlayAnimation))]
    public static class AllyCardAnimationPatch
    {
        /// <summary>
        /// The animations to skip because they're already played for other reasons.
        /// </summary>
        private static readonly HashSet<string> NotACardBeingPlayed =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "debut", "hit", "graze", "guard", "crash", "die"
            };

        [HarmonyPostfix]
        private static void Postfix(UnitView __instance, string animationName)
        {
            MpSafe.Run("AllyCardAnimationPatch", () =>
            {
                if (string.IsNullOrEmpty(animationName)
                    || NotACardBeingPlayed.Contains(animationName)
                    || !LocalPlayerView.Is(__instance))
                {
                    return;
                }

                MpBattleSync.ReportAnimation(animationName);
            });
        }
    }

    /// <summary>
    /// Replicate one-shot effects the game pops up on your own character, so other players see them too.
    /// TODO: this may be a little disruptive but testing will tell
    /// </summary>
    [HarmonyPatch(typeof(UnitView), nameof(UnitView.PlayEffectOneShot))]
    public static class AllyEffectPatch
    {
        [HarmonyPostfix]
        private static void Postfix(UnitView __instance, string effectName, float delay)
        {
            MpSafe.Run("AllyEffectPatch", () =>
            {
                if (string.IsNullOrEmpty(effectName) || !LocalPlayerView.Is(__instance))
                {
                    return;
                }

                MpBattleSync.ReportPerformEffect(effectName, delay);
            });
        }
    }

    /// <summary>
    /// Plays the block animation on you for other clients, since the card play itself doesn't necessarily handle this.
    /// </summary>
    [HarmonyPatch(typeof(UnitView), "DefendAnimation")]
    public static class AllyDefendAnimationPatch
    {
        [HarmonyPostfix]
        private static void Postfix(UnitView __instance)
        {
            MpSafe.Run("AllyDefendAnimationPatch", () =>
            {
                if (!LocalPlayerView.Is(__instance))
                {
                    return;
                }

                MpBattleSync.ReportAnimation("defend");
            });
        }
    }
}
