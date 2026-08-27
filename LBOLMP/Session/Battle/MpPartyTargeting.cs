using System.Linq;
using LBOLMP.Entities;
using LBOLMP.Net;
using LBoL.Core.Cards;

namespace LBOLMP.Session.Battle
{
    /// <summary>
    /// Utility functions to let cards target individual players instead of enemies.
    /// </summary>
    public static class MpPartyTargeting
    {
        private static int _pending = MpConstants.InvalidPlayerId;

        /// <summary>Whether a card wants the arrow to point at partners rather than enemies.</summary>
        public static bool WantsPartner(Card card) => card is IMpPartnerTargeted;

        /// <summary>Whether anybody is in a state to be pointed at right now.</summary>
        public static bool AnyValidPartner => MpEffects.CanSend && MpEffects.ValidPartners.Any();

        /// <summary>Whether a card can be pointed at the local player (such as Ice Block).</summary>
        public static bool IncludesSelf(Card card) => card is IMpAnyPlayerTargeted;

        /// <summary>True if the specified player can be picked as a target at the moment.</summary>
        public static bool IsValidPartner(int playerId) =>
            playerId != MpConstants.InvalidPlayerId
            && MpEffects.ValidPartners.Any(s => s.PlayerId == playerId);

        /// <summary>
        /// True if this player can be picked as a target for this particular card.
        /// </summary>
        public static bool IsValidTarget(Card card, int playerId) =>
            playerId == MpNet.LocalPlayerId ? IncludesSelf(card) : IsValidPartner(playerId);

        internal static void Set(int playerId) => _pending = playerId;

        /// <summary>
        /// Returns the player currently being targeted by a card, or <c>InvalidPlayerId</c>.
        /// This is cleared by reading it.
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
