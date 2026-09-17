using System.Runtime.CompilerServices;
using LBOLMP.Session.Battle;
using LBoL.Core;
using LBoL.Core.Battle;
using LBoL.Core.Battle.BattleActions;
using LBoL.Core.Cards;
using LBoL.Core.Units;

namespace LBOLMP.Api
{
    /// <summary>
    /// Handles cases where another player plays a card, letting you react to it.
    /// </summary>
    public sealed class MpPartnerCardEventArgs : GameEventArgs
    {
        /// <summary>
        /// The player who played the card.
        /// </summary>
        public int PlayerId { get; internal set; }

        /// <summary>
        /// The player who played the card.
        /// </summary>
        public string PlayerName { get; internal set; } = string.Empty;

        public string CardId { get; internal set; } = string.Empty;

        public bool IsUpgraded { get; internal set; }

        /// <summary>
        /// True if the card was a free extra play, like a double-play or a follow-up.
        /// </summary>
        /// <remarks>
        /// Be very careful reacting to these. Two statuses that copy each other's copies may loop infinitely.
        /// </remarks>
        public bool IsToken { get; internal set; }

        /// <summary>
        /// The enemy they aimed it at, if it's still alive here. Null otherwise.
        /// </summary>
        public EnemyUnit TargetEnemy { get; internal set; }

        /// <summary>
        /// The player they aimed it at, for a partner-targeted card. -1 otherwise.
        /// </summary>
        public int TargetPlayerId { get; internal set; } = Net.MpConstants.InvalidPlayerId;

        /// <summary>
        /// Create a fresh copy of the card.
        /// </summary>
        /// <remarks>
        /// Returns null if the client does not recognize this card (this should never happen unless modlists are mismatched)
        /// </remarks>
        public Card CreateCopy() => Library.TryCreateCard(CardId, IsUpgraded);

        /// <summary>
        /// Play a temporary copy of the card for the local player, at the same target where possible.
        /// </summary>
        public PlayCardAction PlayCopy()
        {
            var copy = CreateCopy();
            if (copy == null)
            {
                MpPlugin.Log.LogWarning($"Cannot copy '{CardId}' from player {PlayerId}! Unknown card, are the modlists the same?");
                return null;
            }

            copy.IsPlayTwiceToken = true;

            if (MpPartyTargeting.WantsPartner(copy))
            {
                // Their target, unless that's us. Otherwise, the player being copied.
                MpPartyTargeting.Prefer(copy, TargetPlayerId, PlayerId);
            }

            return TargetEnemy != null
                ? new PlayCardAction(copy, new UnitSelector(TargetEnemy))
                : new PlayCardAction(copy);
        }
    }

    /// <summary>
    /// Battle events for things that happen on other players' clients.
    /// </summary>
    /// <remarks>
    /// Subscribe from a status effect's <c>OnAdded</c> like so:
    /// <code>
    /// ReactOwnerEvent(MpBattleEvents.PartnerCardPlayed(Battle), new EventSequencedReactor&lt;MpPartnerCardEventArgs&gt;(OnPartnerCardPlayed));
    /// </code>
    /// The handler is removed along with the status effect. The events are raised inside a battle action, so a reactor can return actions as usual.
    /// Note: they are NOT raised while the local player is downed or only watching the fight.
    /// </remarks>
    public static class MpBattleEvents
    {
        private sealed class Events
        {
            public readonly GameEvent<MpPartnerCardEventArgs> PartnerCardPlayed =
                new GameEvent<MpPartnerCardEventArgs>();
        }

        private static readonly ConditionalWeakTable<BattleController, Events> ByBattle = new ConditionalWeakTable<BattleController, Events>();

        private static Events For(BattleController battle) => ByBattle.GetOrCreateValue(battle);

        /// <summary>
        /// A partner played a card. Raised once per card, on every other client.
        /// </summary>
        public static GameEvent<MpPartnerCardEventArgs> PartnerCardPlayed(BattleController battle) => For(battle).PartnerCardPlayed;

        internal static void RaisePartnerCardPlayed(BattleController battle, MpPartnerCardEventArgs args)
        {
            if (battle == null || battle.BattleShouldEnd || MpBattleSync.SpectatingOnly)
            {
                return;
            }

            MpBattleSync.QueueReplicated(battle, new MpBattleEventAction<MpPartnerCardEventArgs>(args, For(battle).PartnerCardPlayed), "MP partner card played");
        }
    }

    /// <summary>
    /// Raises an event from inside the battle's action queue, so its reactors can return actions.
    /// </summary>
    internal sealed class MpBattleEventAction<TArgs> : SimpleEventBattleAction<TArgs> where TArgs : GameEventArgs
    {
        private readonly GameEvent<TArgs> _event;

        public MpBattleEventAction(TArgs args, GameEvent<TArgs> e)
        {
            args.CanCancel = false;
            Args = args;
            _event = e;
        }

        protected override void MainPhase() => Trigger(_event);
    }
}
