using System.Collections.Generic;
using LBOLMP.Net;
using LBoL.Core.Battle;
using LBoL.Core.Battle.BattleActions;
using LBoLEntitySideloader.Entities;

namespace LBOLMP.Entities
{
    /// <summary>
    /// Put this on a card's logic class to make its targeting arrow pick a partner instead of an
    /// enemy. The card's config still has to say <c>TargetType.SingleEnemy</c>, which is the only
    /// target type that gives you the drag-out-and-point interaction; the arrow is borrowed, the
    /// meaning is not.
    ///
    /// Read the chosen partner in <c>Actions</c> with <c>MpPartyTargeting.Consume()</c>.
    /// </summary>
    public interface IMpPartnerTargeted
    {
    }

    /// <summary>
    /// Put this on a card definition that should only turn up in a multiplayer run.
    ///
    /// Card pools are fixed sets, and a card that does nothing in single player still has to live
    /// in one of them, so <see cref="Session.MpCardAvailability"/> hides these by raising their
    /// debug level for the duration of a solo run instead. The card keeps its normal pool, colour
    /// and owner; the roller just never sees it. See that class for the details.
    /// </summary>
    public interface IMpOnlyCard
    {
    }

    /// <summary>
    /// Base for a card that does something to other players when it is played.
    ///
    /// The card itself decides what to put in the message and what to build when one arrives.
    /// Everything else, the fight id, who the message is for, whether the receiver is still in the
    /// fight, and how the actions reach their battle, is handled by <see cref="MpEffects"/>.
    ///
    /// Send from the card's own <c>Actions</c> with <c>MpEffects.Send</c>. Receive runs on the
    /// target's client, which has no instance of your card, so it lives here on the definition.
    /// </summary>
    public abstract class MpCardTemplate<TPayload> : CardTemplate, IMpEffect, IMpOnlyCard
        where TPayload : MpEffectPayload, new()
    {
        /// <summary>
        /// Namespaced by assembly so two mods cannot collide. Override only if you have to keep a
        /// key stable across a rename; changing it breaks compatibility with older versions.
        /// </summary>
        public virtual string Key => GetType().Assembly.GetName().Name + "." + GetId();

        MpEffectPayload IMpEffect.NewPayload() => new TPayload();

        IEnumerable<BattleAction> IMpEffect.Receive(MpEffectPayload payload, BattleController battle, int senderId)
            => Receive((TPayload)payload, battle, senderId);

        /// <summary>
        /// What this card does on the receiving player's client. Return the actions and let the
        /// framework queue them; never touch player state directly, because the receiver may be
        /// mid-action or parked at a gate when this runs.
        /// </summary>
        public abstract IEnumerable<BattleAction> Receive(TPayload payload, BattleController battle, int senderId);
    }
}
