using System.Collections.Generic;
using LBOLMP.Net;
using LBoL.Core.Battle;
using LBoL.Core.Battle.BattleActions;
using LBoLEntitySideloader.Entities;

namespace LBOLMP.Entities
{
    /// <summary>
    /// Base for a status effect that does something to other players when it triggers.
    /// To send something to other players, call <c>MpEffects.Send</c> in your status effect when it should trigger.
    /// </summary>
    /// <remarks>
    /// If you are hooking a card play, use <c>Battle.CardUsed</c> and NOT <c>Battle.CardPlayed</c>!
    /// CardPlayed can also fire when a card is played on another person's behalf (like Offering to the Ownerless).
    /// This can result in infinite loops if you're not careful.
    /// The limitation that I have accepted is therefore, we only ever listen to CardUsed.
    /// Hook into CardPlayed at your own risk. If you do, stay far away from "proxy cards" (like playing a card for all your partners)
    /// </remarks>
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
        /// What this card does on the receiving player's client.
        /// </summary>
        /// <remarks>
        /// Return the actions and let the LBOL MP framework queue them.
        /// Never touch player state directly, because the receiver may be mid-action when this runs.
        /// </remarks>
        public abstract IEnumerable<BattleAction> Receive(TPayload payload, BattleController battle, int senderId);
    }
}
