using System.Collections.Generic;
using LBOLMP.Net;
using LBoL.Core;
using LBoL.Core.Battle;
using LBoL.Core.Battle.BattleActions;

namespace LBOLMP.Entities
{
    /// <summary>
    /// Payload used to play a card on behalf of another player.
    /// </summary>
    public sealed class MpProxyCardPayload : MpEffectPayload
    {
        public string CardId;
        public bool Upgraded;

        /// <summary>
        /// Plays the card named here on the local client.
        /// </summary>
        /// <remarks>
        /// The copy is a PlayTwiceToken, so that it can't be double-played itself, and so that a
        /// non-Ability cards don't litter the receiver's discard/exile pile.
        /// </remarks>
        public IEnumerable<BattleAction> Play(int senderId)
        {
            var copy = Library.TryCreateCard(CardId, Upgraded);
            if (copy == null)
            {
                MpPlugin.Log.LogWarning($"Cannot play '{CardId}' for player {senderId}; unknown card");
                yield break;
            }

            copy.IsPlayTwiceToken = true;

            yield return new PlayCardAction(copy);
        }
    }
}
