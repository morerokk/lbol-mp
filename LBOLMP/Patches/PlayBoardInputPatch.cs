using HarmonyLib;
using LBOLMP.Session;
using LBOLMP.Session.Battle;
using LBoL.Presentation.UI.Panels;

namespace LBOLMP.Patches
{
    /// <summary>
    /// Ensures that if someone else plays a card while you're busy (in a card select dialog, or already waiting for your own card to resolve),
    /// that card is queued for later and still played properly once you're done.
    /// This also hopefully fixes *some* cases where a player tries to attack an enemy that just now died by refunding their card and mana.
    /// </summary>
    [HarmonyPatch(typeof(PlayBoard), "EnqueueRequest")]
    public static class PlayBoardInputPatch
    {
        private enum Decision
        {
            /// <summary>Let the game handle it normally.</summary>
            RunOriginal,

            /// <summary>Refuse the click without spending anything.</summary>
            Reject,

            /// <summary>Put on the play board's queue for later.</summary>
            Parked
        }

        [HarmonyPrefix]
        private static bool Prefix(PlayBoard __instance, PlayBoard.RequestEntry request, ref bool __result)
        {
            // Ref parameter nonsense (can't be captured by lambdas, boooooo)
            var decision = MpSafe.Run("PlayBoardInputPatch",
                () => Decide(__instance, request), Decision.RunOriginal);

            switch (decision)
            {
                case Decision.Reject:
                    __result = false;
                    return false;

                case Decision.Parked:
                    __result = true;
                    return false;

                default:
                    return true;
            }
        }

        private static Decision Decide(PlayBoard playBoard, PlayBoard.RequestEntry request)
        {
            if (!MpSession.IsActive || !MpBattleSync.InBattle)
            {
                return Decision.RunOriginal;
            }

            // Can't play something if you're downed.
            if (MpDownedPlayers.OutOfFight)
            {
                return Decision.Reject;
            }

            // Can't play something if you're watching someone else's hand (Marisa get your grubby hands off my Unmoving Great Library)
            if (UI.MpHandView.Active)
            {
                return Decision.Reject;
            }

            if (!MpBattleSync.ShouldDeferPlayerInput)
            {
                return Decision.RunOriginal;
            }

            // Mirrors the original method's "battle is busy" branch.
            request.PlayBoard = playBoard;
            if (!request.Verify(false))
            {
                return Decision.Reject;
            }

            request.Prepay();
            playBoard._requests.Enqueue(request);

            MpPlugin.Log.LogInfo($"Temporarily parked {request.GetType().Name} while replaying another player's move");
            return Decision.Parked;
        }
    }
}
