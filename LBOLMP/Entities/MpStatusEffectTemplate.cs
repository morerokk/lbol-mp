using System.Collections.Generic;
using LBOLMP.Net;
using LBoL.Core.Battle;
using LBoL.Core.Battle.BattleActions;
using LBoLEntitySideloader.Entities;

namespace LBOLMP.Entities
{
    /// <summary>
    /// Base for a status effect that does something to other players when it triggers.
    ///
    /// There is deliberately no Send method to override here. A status decides for itself when it
    /// fires, so hook whatever event you want in the logic class and call <c>MpEffects.Send</c>
    /// from there.
    ///
    /// If you are hooking a card play, use <c>Battle.CardUsed</c> and not <c>Battle.CardPlayed</c>.
    /// CardUsed only fires when a player actually plays a card from hand; CardPlayed also fires for
    /// free plays, follow-attack fillers, and cards replayed on your behalf by somebody else's
    /// effect. Hooking CardPlayed lets two of these bounce off each other forever.
    /// </summary>
    public abstract class MpStatusEffectTemplate<TPayload> : StatusEffectTemplate, IMpEffect
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
        /// What this does on the receiving player's client. Return the actions and let the
        /// framework queue them; never touch player state directly, because the receiver may be
        /// mid-action or parked at a gate when this runs.
        /// </summary>
        public abstract IEnumerable<BattleAction> Receive(TPayload payload, BattleController battle, int senderId);
    }
}
