using HarmonyLib;
using LBOLMP.Net;
using LBOLMP.Session;
using LBOLMP.UI;
using LBoL.Presentation.UI.Panels;
using LBoL.Presentation.Units;

namespace LBOLMP.Patches
{
    /// <summary>
    /// Which player's character unit the mouse is currently over, if any.
    /// This lets you select players to preview them, and hopefully later also allow you to select players to use multiplayer cards on.
    /// </summary>
    public static class MpHoveredUnit
    {
        private static UnitView _view;

        /// <summary>The id of the ally under the pointer, or <c>InvalidPlayerId</c>.</summary>
        public static int HoveredPlayer =>
            _view == null ? MpConstants.InvalidPlayerId : MpAllyUnits.PlayerFor(_view);

        internal static void Entered(UnitView view) => _view = view;

        internal static void Exited(UnitView view)
        {
            if (ReferenceEquals(_view, view))
            {
                _view = null;
            }
        }
    }

    [HarmonyPatch(typeof(UnitView), nameof(UnitView.Event_OnPointerEnter))]
    public static class UnitHoverEnterPatch
    {
        [HarmonyPostfix]
        private static void Postfix(UnitView __instance) => MpHoveredUnit.Entered(__instance);
    }

    [HarmonyPatch(typeof(UnitView), nameof(UnitView.Event_OnPointerExit))]
    public static class UnitHoverExitPatch
    {
        [HarmonyPostfix]
        private static void Postfix(UnitView __instance) => MpHoveredUnit.Exited(__instance);
    }

    /// <summary>
    /// While somebody else's hand is on the board, the pile buttons open their piles.
    /// </summary>
    internal static class InspectRedirect
    {
        /// <summary>
        /// True if the watched player's pile was shown instead, in which case the game's own
        /// version of the call must not run.
        /// </summary>
        internal static bool Took(System.Action show)
        {
            if (!MpHandView.Active)
            {
                return false;
            }

            show();
            return true;
        }
    }

    /// <summary>
    /// Your own hand stops being hovered while you are reading somebody else's, so you can't accidentally play your own hidden cards underneath.
    /// </summary>
    [HarmonyPatch(typeof(PlayBoard), "GetHoveringIndex")]
    public static class HoveringIndexPatch
    {
        [HarmonyPrefix]
        private static bool Prefix(ref int? __result)
        {
            if (!MpHandView.Active)
            {
                return true;
            }

            __result = null;
            return false;
        }
    }

    [HarmonyPatch(typeof(PlayBoard), nameof(PlayBoard.ShowDrawZone))]
    public static class ShowDrawZonePatch
    {
        [HarmonyPrefix]
        private static bool Prefix() => !InspectRedirect.Took(MpInspectedPiles.ShowDraw);
    }

    [HarmonyPatch(typeof(PlayBoard), nameof(PlayBoard.ShowDiscardZone))]
    public static class ShowDiscardZonePatch
    {
        [HarmonyPrefix]
        private static bool Prefix() => !InspectRedirect.Took(MpInspectedPiles.ShowDiscard);
    }

    [HarmonyPatch(typeof(PlayBoard), nameof(PlayBoard.ShowExileZone))]
    public static class ShowExileZonePatch
    {
        [HarmonyPrefix]
        private static bool Prefix() => !InspectRedirect.Took(MpInspectedPiles.ShowExile);
    }

    [HarmonyPatch(typeof(SystemBoard), nameof(SystemBoard.ShowBaseDeck))]
    public static class ShowBaseDeckPatch
    {
        [HarmonyPrefix]
        private static bool Prefix() => !InspectRedirect.Took(MpInspectedPiles.ShowDeck);
    }

    /// <summary>
    /// When opening someone else's piles or library, show that person's character portrait on the left rather than your own.
    /// </summary>
    [HarmonyPatch(typeof(ShowCardsPanel), "OnShowing", typeof(ShowCardsPayload))]
    public static class InspectedPilePortraitPatch
    {
        [HarmonyPostfix]
        private static void Postfix(ShowCardsPanel __instance)
        {
            MpSafe.Run("InspectedPilePortraitPatch", () =>
            {
                string character = MpInspectedPiles.ShowingFor;
                if (string.IsNullOrEmpty(character)
                    || __instance.portrait == null
                    || __instance.characterPortraits == null)
                {
                    return;
                }

                if (!__instance.characterPortraits.TryGetValue(character, out var sprite)
                    || sprite == null)
                {
                    MpPlugin.Log.LogWarning($"The pile viewer has no illustration for '{character}', can't show portrait.");
                    return;
                }

                __instance.portrait.sprite = sprite;
                __instance._currentCharacterIndex = -1;
            });
        }
    }
}
