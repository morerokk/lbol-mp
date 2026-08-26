using System.Linq;
using LBOLMP.Entities;
using LBOLMP.Net;
using LBoL.Core.Cards;

namespace LBOLMP.Session.Battle
{
    /// <summary>
    /// Carries the partner picked with the targeting arrow from the play board to the card that
    /// is being played.
    ///
    /// The game's <c>UnitSelector</c> can only hold an enemy, so it cannot carry this for us. The
    /// selection is confirmed and stored here in the same frame the card is committed, then read
    /// once by the card's Actions.
    /// </summary>
    public static class MpPartyTargeting
    {
        private static int _pending = MpConstants.InvalidPlayerId;

        /// <summary>Whether a card wants the arrow to point at partners rather than enemies.</summary>
        public static bool WantsPartner(Card card) => card is IMpPartnerTargeted;

        /// <summary>Whether anybody is in a state to be pointed at right now.</summary>
        public static bool AnyValidPartner => MpEffects.CanSend && MpEffects.ValidPartners.Any();

        /// <summary>Whether a card is willing to be pointed back at the player holding it.</summary>
        public static bool IncludesSelf(Card card) => card is IMpAnyPlayerTargeted;

        /// <summary>True if this player can be picked as a target at the moment.</summary>
        public static bool IsValidPartner(int playerId) =>
            playerId != MpConstants.InvalidPlayerId
            && MpEffects.ValidPartners.Any(s => s.PlayerId == playerId);

        /// <summary>
        /// True if this player can be picked as a target for this particular card. Ourselves is
        /// only ever an option for a card that asked for it, and needs no liveness check: we are
        /// plainly still here, or we would not be playing a card.
        /// </summary>
        public static bool IsValidTarget(Card card, int playerId) =>
            playerId == MpNet.LocalPlayerId ? IncludesSelf(card) : IsValidPartner(playerId);

        internal static void Set(int playerId) => _pending = playerId;

        /// <summary>
        /// The partner the player pointed at, or <c>InvalidPlayerId</c>. Reading it clears it, so
        /// a stale pick can never leak into the next card.
        /// </summary>
        public static int Consume()
        {
            int id = _pending;
            _pending = MpConstants.InvalidPlayerId;
            return id;
        }

        internal static void Clear() => _pending = MpConstants.InvalidPlayerId;
    }
}
