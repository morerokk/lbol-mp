using System;
using System.Collections.Generic;
using System.Linq;
using LBOLMP.Entities;
using LBOLMP.Net;
using LBOLMP.Session.Battle;
using LBOLMP.Session.Messages;
using LBoL.Core.Battle;
using LBoL.Core.Battle.BattleActions;
using LBoL.Core.Cards;
using LBoL.Presentation;
using UnityEngine;

namespace LBOLMP.Session
{
    /// <summary>
    /// One-shot lookups of another player's exile pile.
    /// </summary>
    /// <remarks>
    /// Deliberately not part of <see cref="MpHandInspect"/>: that one is a subscription that keeps
    /// republishing for as long as somebody is watching, which is what a viewer wants and is far
    /// more than a card needs. This asks once and is answered once.
    ///
    /// The answer arrives some frames later, from <c>MpNet.Pump</c>. Whatever asked has to be able
    /// to cope with that, and with never being answered at all.
    /// </remarks>
    public static class MpExilePeek
    {
        /// <summary>
        /// How long to keep waiting before giving up on an answer, in seconds. Only ever reached
        /// when the other player has stopped responding, in which case nothing happens at all.
        /// </summary>
        private const float Timeout = 5f;

        private static int _awaiting = MpConstants.InvalidPlayerId;
        private static Action<List<Card>> _onArrived;
        private static float _expires;

        public static void RegisterHandlers()
        {
            MpNet.On<ExilePeekRequestMessage>(OnRequest);
            MpNet.On<ExilePeekMessage>(OnReply);
            MpNet.On<ExileMarkCopyMessage>(OnMarkCopy);
        }

        public static void Reset()
        {
            _awaiting = MpConstants.InvalidPlayerId;
            _onArrived = null;
            _expires = 0f;
        }

        /// <summary>
        /// Ask one player for their exile pile. <paramref name="onArrived"/> runs once the answer
        /// lands, on the main thread, with the pile rebuilt as readable cards.
        /// </summary>
        /// <remarks>
        /// Only one request can be outstanding at a time, because only one is ever needed: a card
        /// asks and then waits for its own answer. A second request replaces the first.
        /// </remarks>
        public static void Request(int playerId, Action<List<Card>> onArrived)
        {
            if (playerId == MpConstants.InvalidPlayerId || playerId == MpNet.LocalPlayerId
                || onArrived == null)
            {
                return;
            }

            _awaiting = playerId;
            _onArrived = onArrived;
            _expires = Time.unscaledTime + Timeout;

            MpNet.Send(new ExilePeekRequestMessage { TargetPlayerId = playerId });
        }

        public static void Update()
        {
            if (_onArrived != null && Time.unscaledTime > _expires)
            {
                MpPlugin.Log.LogWarning($"Player {_awaiting} never answered the exile peek");
                Reset();
            }
        }

        /// <summary>Somebody wants to see what we have exiled.</summary>
        private static void OnRequest(ExilePeekRequestMessage message)
        {
            if (message.TargetPlayerId != MpNet.LocalPlayerId
                || message.SenderId == MpNet.LocalPlayerId)
            {
                return;
            }

            MpSafe.Run("MpExilePeek.OnRequest", () =>
            {
                var battle = GameMaster.Instance?.CurrentGameRun?.Battle;
                MpNet.Send(new ExilePeekMessage
                {
                    TargetPlayerId = message.SenderId,
                    Cards = battle == null
                        ? new List<MpCardState>()
                        : MpCardMirror.Capture(battle.ExileZone)
                });
            });
        }

        private static void OnReply(ExilePeekMessage message)
        {
            if (message.TargetPlayerId != MpNet.LocalPlayerId
                || message.SenderId != _awaiting
                || _onArrived == null)
            {
                return;
            }

            var handler = _onArrived;
            Reset();

            MpSafe.Run("MpExilePeek.OnReply",
                () => handler(MpCardMirror.Rebuild(message.Cards, message.SenderId)));
        }

        /// <summary>
        /// Tell a player that one card in their exile pile has been spent, so it turns into a Copy
        /// over there and cannot be taken again.
        /// </summary>
        public static void MarkCopy(int playerId, string cardId, bool upgraded)
        {
            if (playerId == MpConstants.InvalidPlayerId || playerId == MpNet.LocalPlayerId
                || string.IsNullOrEmpty(cardId))
            {
                return;
            }

            MpNet.Send(new ExileMarkCopyMessage
            {
                TargetPlayerId = playerId,
                CardId = cardId,
                Upgraded = upgraded
            });
        }

        private static void OnMarkCopy(ExileMarkCopyMessage message)
        {
            if (message.TargetPlayerId != MpNet.LocalPlayerId
                || message.SenderId == MpNet.LocalPlayerId)
            {
                return;
            }

            MpSafe.Run("MpExilePeek.OnMarkCopy", () =>
            {
                var battle = GameMaster.Instance?.CurrentGameRun?.Battle;
                if (battle == null)
                {
                    return;
                }

                // Deferred, because which card this lands on has to be worked out against the pile
                // as it is when it resolves, not as it was when the message arrived.
                MpBattleSync.QueueReplicated(battle,
                    new MpDeferredAction(b => MarkOne(b, message.CardId, message.Upgraded)),
                    nameof(ExileMarkCopyMessage));
            });
        }

        /// <summary>
        /// Marks the first card in the exile pile that still matches and is not already a Copy.
        /// Two cards that match are interchangeable, so which one it picks does not matter.
        /// </summary>
        private static IEnumerable<BattleAction> MarkOne(BattleController battle, string cardId, bool upgraded)
        {
            var card = battle.ExileZone.FirstOrDefault(
                c => c.Id == cardId && c.IsUpgraded == upgraded && !c.IsCopy);

            if (card != null)
            {
                card.IsCopy = true;
            }

            yield break;
        }
    }
}
