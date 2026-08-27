using System;
using System.Collections.Generic;
using LBoL.Core;
using LBoL.Core.Battle;

namespace LBOLMP.Entities
{
    /// <summary>
    /// An action that works out what to do when it resolves, rather than when it's queued.
    /// </summary>
    /// This is necessary because <c>MpBattleSync.QueueReplicated</c> runs <c>Receive</c> and queues every
    /// action before any of them run, so anything read in the body of a Receive is read early. That
    /// is usually harmless, because the replicated queue is read and emptied between the receiving player's
    /// own actions and is normally reached almost immediately.
    ///
    /// However, it is not harmless when they are in the middle of something. Their own actions keep resolving
    /// in the meantime, cards move between zones, and a card that was in the draw pile when the effect
    /// was queued can be sitting in their hand by the time it runs. Thus, a card can visually enter the hand without actually being in the hand.
    /// And then you get big problems :)
    ///
    /// Anything that has to look at the piles, the hand, or the enemies should read them through
    /// this instead. The actions handed back are reacted, so they resolve one at a time and can
    /// check the state each other left behind, exactly like a card's own Actions.
    public sealed class MpDeferredAction : SimpleEventBattleAction<GameEventArgs>
    {
        private readonly Func<BattleController, IEnumerable<BattleAction>> _build;

        public MpDeferredAction(Func<BattleController, IEnumerable<BattleAction>> build)
        {
            _build = build ?? throw new ArgumentNullException(nameof(build));
            Args = new GameEventArgs { CanCancel = false };
        }

        protected override void MainPhase()
        {
            var actions = _build(Battle);
            if (actions != null)
            {
                React(new Reactor(actions));
            }
        }
    }
}
