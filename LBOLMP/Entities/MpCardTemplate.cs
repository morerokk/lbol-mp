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
    /// Base for a card that does something to other players when it is played.
    ///
    /// The card itself decides what to put in the message and what to build when one arrives.
    /// Everything else, the fight id, who the message is for, whether the receiver is still in the
    /// fight, and how the actions reach their battle, is handled by <see cref="MpEffects"/>.
    ///
    /// Send from the card's own <c>Actions</c> with <c>MpEffects.Send</c>. Receive runs on the
    /// target's client, which has no instance of your card, so it lives here on the definition.
    /// </summary>
    public abstract class MpCardTemplate<TPayload> : CardTemplate, IMpEffect
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
