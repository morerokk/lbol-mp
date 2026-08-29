using HarmonyLib;
using LBOLMP.Net;
using LBOLMP.Session.Battle;
using LBoL.Core;
using LBoL.Core.Battle.BattleActions;
using LBoL.Core.Units;
using LBoL.Presentation.Units;
using LBoL.Presentation.UI.ExtraWidgets;
using LBoL.Presentation.UI.Widgets;
using UnityEngine;

namespace LBOLMP.Patches
{
    /// <summary>
    /// Lets a card point its targeting arrow at a partner instead of an enemy.
    ///
    /// Most of the selection flow is already unit-agnostic: the arrow, the highlight, the mouse
    /// raycast and gamepad cycling all just walk the selector's list of views. Only two spots
    /// insist on an EnemyUnit and both live inside GetConfirmUseSelector, which is why that one is
    /// replaced outright rather than postfixed.
    /// </summary>
    internal static class PartyTarget
    {
        /// <summary>Whether the selector is currently pointing at partners.</summary>
        internal static bool IsPartnerSelection(TargetSelector selector) =>
            selector != null
            && selector._activeHand != null
            && selector._activeHand.Card != null
            && MpPartyTargeting.WantsPartner(selector._activeHand.Card);

        /// <summary>
        /// The unit the player is indicating. UpdateSingleEnemy resolves the mouse raycast into
        /// PendingTarget every frame, so reading it keeps the commit and the highlight in
        /// agreement. The gamepad tracks its own target separately.
        /// </summary>
        internal static Unit Pointed(TargetSelector selector)
        {
            var gamepadTarget = selector._currentGamepadTarget;
            return gamepadTarget != null ? gamepadTarget.Unit : selector._activeHand?.Card?.PendingTarget;
        }
    }

    /// <summary>
    /// Offer the party as the arrow's targets instead of the enemies.
    /// Anyone out of the fight or gone silent never enters the list, so they cannot be hovered,
    /// cycled to with a controller, or picked. That is the entire validity check.
    /// </summary>
    [HarmonyPatch(typeof(TargetSelector), "SetPotentialTargets")]
    public static class PartyPotentialTargetsPatch
    {
        [HarmonyPostfix]
        private static void Postfix(TargetSelector __instance)
        {
            MpSafe.Run("PartyPotentialTargetsPatch", () =>
            {
                if (!PartyTarget.IsPartnerSelection(__instance))
                {
                    return;
                }

                __instance._potentialTargets.Clear();

                foreach (var view in UI.MpAllyUnits.LoadedViews)
                {
                    if (MpPartyTargeting.IsValidPartner(UI.MpAllyUnits.PlayerFor(view)))
                    {
                        __instance._potentialTargets.Add(view);
                    }
                }

                // Our own unit is real rather than a mirror, so it is added by hand and never
                // appears in MpAllyUnits. Only cards that asked to be aimed at their holder get it.
                if (MpPartyTargeting.IncludesSelf(__instance._activeHand.Card)
                    && GameDirector.Player != null)
                {
                    __instance._potentialTargets.Add(GameDirector.Player);
                }
            });
        }
    }

    /// <summary>
    /// A fresh selection starts with nobody chosen.
    ///
    /// Deliberately not done on DisableSelector: the selector is torn down as soon as the card is
    /// committed, which is well before the card's Actions run and read the pick.
    /// </summary>
    [HarmonyPatch(typeof(TargetSelector), nameof(TargetSelector.EnableSelector), typeof(HandCard))]
    public static class PartyTargetResetPatch
    {
        [HarmonyPrefix]
        private static void Prefix() => MpSafe.Run("PartyTargetResetPatch", MpPartyTargeting.Clear);
    }

    /// <summary>
    /// Turn the pointed-at partner into a selection the game will accept.
    ///
    /// UnitSelector is sealed and can only carry an enemy, so the partner is stashed in
    /// <see cref="MpPartyTargeting"/> and a Nobody selector goes back instead. Skipping the
    /// original also avoids its two casts to EnemyUnit, which would throw on a player unit.
    /// </summary>
    [HarmonyPatch(typeof(TargetSelector), nameof(TargetSelector.GetConfirmUseSelector))]
    public static class PartyConfirmSelectorPatch
    {
        [HarmonyPrefix]
        private static bool Prefix(TargetSelector __instance, Vector2 screenPosition, ref UnitSelector __result)
        {
            int playerId = MpSafe.Run("PartyConfirmSelectorPatch", () =>
            {
                if (!PartyTarget.IsPartnerSelection(__instance))
                {
                    return int.MinValue;
                }

                return UI.MpAllyUnits.PlayerForIncludingLocal(PartyTarget.Pointed(__instance));
            }, int.MinValue);

            // Not one of ours, let the game do its thing.
            if (playerId == int.MinValue)
            {
                return true;
            }

            if (!MpPartyTargeting.IsValidTarget(__instance._activeHand?.Card, playerId))
            {
                // Nothing valid under the pointer. Null keeps us in selecting mode, the same as
                // the game already does when you point an attack at empty space.
                MpPartyTargeting.Clear();
                __result = null;
                return false;
            }

            MpPartyTargeting.Set(playerId);
            __result = UnitSelector.Nobody;
            return false;
        }
    }

    /// <summary>
    /// Let card auto-targeting work for partner-played cards
    /// </summary>
    [HarmonyPatch(typeof(PlayCardAction), "ReTargeting")]
    public static class PartyAutoTargetPatch
    {
        [HarmonyPrefix]
        private static void Prefix(PlayCardAction __instance)
        {
            MpSafe.Run("PartyAutoTargetPatch", () =>
            {
                var card = __instance.Args?.Card;
                if (!MpPartyTargeting.WantsPartner(card))
                {
                    return;
                }

                MpPartyTargeting.PickMissingRandomTarget(card, __instance.Battle.GameRun.BattleRng);
            });
        }
    }
}
