using System.Collections.Generic;
using LBOLMP.Net;
using LBoL.Core.Battle;
using LBoL.Core.Battle.BattleActions;

namespace LBOLMP.Entities
{
    /// <summary>
    /// Put this on a card's logic class to make its targeting arrow pick a partner instead of an enemy.
    /// The card's config still has to set <c>TargetType.SingleEnemy</c>, to borrow the arrow selector.
    /// </summary>
    /// <remarks>
    /// Read the chosen partner in <c>Actions</c> with <c>MpPartyTargeting.Consume()</c>.
    /// </remarks>
    public interface IMpPartnerTargeted
    {
    }

    /// <summary>
    /// Like <see cref="IMpPartnerTargeted"/>, except the local player is also a valid target.
    /// </summary>
    /// <remarks>
    /// <c>MpPartyTargeting.Consume()</c> can therefore hand back the local player's own id. Check
    /// for that before sending anything. An effect aimed at the local player should be resolved directly there.
    /// </remarks>
    public interface IMpAnyPlayerTargeted : IMpPartnerTargeted
    {
    }

    /// <summary>
    /// Put this on an MP effect that still needs to do something for downed players.
    /// </summary>
    public interface IMpReachesDownedPlayers
    {
    }

    /// <summary>
    /// Put this on a card definition to make it only ever show up in a multiplayer run.
    /// </summary>
    /// <remarks>
    /// This makes the mod set a debug flag on the card if we're not playing a multiplayer session.
    /// </remarks>
    public interface IMpOnlyCard
    {
    }

    /// <summary>
    /// Base for an LBOL MP card that does something to other players when it is played.
    /// </summary>
    /// <remarks>
    /// Only a shortcut for this mod's own cards. Other mods implement <see cref="IMpEffect{TPayload}"/>
    /// (and <see cref="IMpOnlyCard"/>) on their own card definitions instead.
    /// Send from the actual card class's own <c>Actions</c> with <c>MpEffects.Send</c>.
    /// </remarks>
    public abstract class LbolMpMultiplayerCardTemplate<TPayload> : LbolMpCardTemplate, IMpEffect<TPayload>, IMpOnlyCard
        where TPayload : MpEffectPayload, new()
    {
        /// <inheritdoc />
        public abstract IEnumerable<BattleAction> Receive(TPayload payload, BattleController battle, int senderId);
    }
}
