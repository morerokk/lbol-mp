using System.Collections.Generic;
using LBOLMP.Net;
using LBoL.Core.Battle;
using LBoL.Core.Battle.BattleActions;

namespace LBOLMP.Entities
{
    /// <summary>
    /// Base for an LBOL MP status effect that does something to other players when it triggers.
    /// Other mods implement <see cref="IMpEffect{TPayload}"/> on their own status effect definitions instead.
    /// To send something to other players, call <c>MpEffects.Send</c> in your status effect when it should trigger.
    /// </summary>
    /// <remarks>
    /// If you are hooking a card play, use <c>Battle.CardUsed</c> and NOT <c>Battle.CardPlayed</c>!
    /// CardPlayed can also fire when a card is played on another person's behalf (like Offering to the Ownerless).
    /// This can result in infinite loops if you're not careful.
    /// The limitation that I have accepted is therefore, we only ever listen to CardUsed.
    /// Hook into CardPlayed at your own risk. If you do, stay far away from "proxy cards" (like playing a card for all your partners)
    /// </remarks>
    public abstract class LbolMpMultiplayerStatusEffectTemplate<TPayload> : LbolMpStatusEffectTemplate, IMpEffect<TPayload>
        where TPayload : MpEffectPayload, new()
    {
        /// <inheritdoc />
        public abstract IEnumerable<BattleAction> Receive(TPayload payload, BattleController battle, int senderId);
    }
}
