using System.Collections.Generic;
using System.Linq;
using LBOLMP.Net;
using LBOLMP.Session.Battle;
using LBOLMP.Session.Messages;
using LBoL.Base;
using LBoL.Core.Cards;
using LBoL.Presentation;
using UnityEngine;

namespace LBOLMP.Session
{
    /// <summary>
    /// Who is looking at whose hand.
    /// </summary>
    public static class MpHandInspect
    {
        /// <summary>
        /// How often a watched player may re-send. Fast enough that a card leaving their hand shows
        /// up as a change rather than as a delay, slow enough that a whole bunch of card draws is one message.
        /// </summary>
        private const float PublishInterval = 0.2f;

        /// <summary>The cards and mana one other player has, as last heard from them.</summary>
        private sealed class Mirror
        {
            public readonly List<Card> Hand = new List<Card>();
            public readonly List<Card> Draw = new List<Card>();
            public readonly List<Card> Discard = new List<Card>();
            public readonly List<Card> Exile = new List<Card>();
            public readonly List<Card> Deck = new List<Card>();

            public ManaGroup Mana;
            public bool HideDrawOrder;

            /// <summary>Bumped whenever anything above changes, so the view knows to redraw.</summary>
            public int Revision;
        }

        private static readonly Dictionary<int, Mirror> Mirrors = new Dictionary<int, Mirror>();

        /// <summary>Everyone who has told us they are watching this client.</summary>
        private static readonly HashSet<int> Watchers = new HashSet<int>();

        private static byte[] _lastCards;
        private static byte[] _lastDeck;
        private static float _nextPublish;

        /// <summary>Whose hand this client is looking at, or <c>InvalidPlayerId</c> if we're not watching.</summary>
        public static int Target { get; private set; } = MpConstants.InvalidPlayerId;

        public static bool IsInspecting => Target != MpConstants.InvalidPlayerId;

        /// <summary>The name to put on the banner, and on the pile viewer's heading.</summary>
        public static string TargetName => MpSession.Get(Target)?.Name ?? string.Empty;

        public static int Revision => Find(Target)?.Revision ?? 0;

        public static IReadOnlyList<Card> Hand => Find(Target)?.Hand ?? Empty;
        public static IReadOnlyList<Card> Draw => Find(Target)?.Draw ?? Empty;
        public static IReadOnlyList<Card> Discard => Find(Target)?.Discard ?? Empty;
        public static IReadOnlyList<Card> Exile => Find(Target)?.Exile ?? Empty;
        public static IReadOnlyList<Card> Deck => Find(Target)?.Deck ?? Empty;

        public static ManaGroup Mana => Find(Target)?.Mana ?? ManaGroup.Empty;
        public static bool HideDrawOrder => Find(Target)?.HideDrawOrder ?? true;

        private static readonly List<Card> Empty = new List<Card>();

        /// <summary>
        /// Whether a player is still in the session.
        /// </summary>
        private static bool Present(int playerId)
        {
            var player = MpSession.Get(playerId);
            return player != null && player.State != MpPlayerState.Disconnected;
        }

        private static Mirror Find(int playerId) =>
            Mirrors.TryGetValue(playerId, out var mirror) ? mirror : null;

        public static void RegisterHandlers()
        {
            MpNet.On<HandInspectMessage>(OnInspect);
            MpNet.On<PlayerCardsMessage>(OnCards);
            MpNet.On<PlayerDeckMessage>(OnDeck);
        }

        /// <summary>Start looking at somebody else's hand.</summary>
        public static void Begin(int playerId)
        {
            if (playerId == MpNet.LocalPlayerId || !Present(playerId))
            {
                return;
            }

            if (Target == playerId)
            {
                return;
            }

            Target = playerId;
            if (!Mirrors.ContainsKey(playerId))
            {
                Mirrors[playerId] = new Mirror();
            }

            MpPlugin.Log.LogInfo($"Watching player {playerId}'s hand");
            MpNet.Send(new HandInspectMessage { TargetPlayerId = playerId });
        }

        public static void End()
        {
            if (!IsInspecting)
            {
                return;
            }

            MpPlugin.Log.LogInfo($"Stopped watching player {Target}'s hand");
            Target = MpConstants.InvalidPlayerId;
            MpNet.Send(new HandInspectMessage { TargetPlayerId = MpConstants.InvalidPlayerId });
        }

        public static void Reset()
        {
            Target = MpConstants.InvalidPlayerId;
            Mirrors.Clear();
            Watchers.Clear();
            _lastCards = null;
            _lastDeck = null;
        }

        public static void Update()
        {
            MpSafe.Run("MpHandInspect.Update", () =>
            {
                if (!MpSession.IsActive || !MpSession.IsInRun)
                {
                    Reset();
                    return;
                }

                if (IsInspecting && (!MpBattleSync.InBattle || !Present(Target)))
                {
                    End();
                }

                if (Watchers.Count > 0)
                {
                    foreach (int id in Watchers.Where(id => !Present(id)).ToList())
                    {
                        Watchers.Remove(id);
                    }
                }

                Publish();
            });
        }

        private static void Publish()
        {
            if (Watchers.Count == 0)
            {
                _lastCards = null;
                _lastDeck = null;
                return;
            }

            if (Time.unscaledTime < _nextPublish)
            {
                return;
            }
            _nextPublish = Time.unscaledTime + PublishInterval;

            var gameRun = GameMaster.Instance?.CurrentGameRun;
            if (gameRun == null)
            {
                return;
            }

            var battle = gameRun.Battle;
            var cards = new PlayerCardsMessage
            {
                Mana = battle?.BattleMana ?? ManaGroup.Empty,
                HideDrawOrder = gameRun.CanViewDrawZoneActualOrder <= 0,
                Cards = new List<MpCardState>()
            };

            if (battle != null)
            {
                cards.Cards.AddRange(MpCardMirror.Capture(battle.HandZone));
                cards.Cards.AddRange(MpCardMirror.Capture(battle.DrawZone));
                cards.Cards.AddRange(MpCardMirror.Capture(battle.DiscardZone));
                cards.Cards.AddRange(MpCardMirror.Capture(battle.ExileZone));
            }

            SendIfChanged(cards, ref _lastCards);
            SendIfChanged(new PlayerDeckMessage { Cards = MpCardMirror.Capture(gameRun.BaseDeck) },
                ref _lastDeck);
        }

        /// <summary>
        /// Send a snapshot only when it differs from the last one that went out.
        /// </summary>
        private static void SendIfChanged(NetMessage message, ref byte[] previous)
        {
            var payload = MpNet.BodyOf(message);
            if (MpNet.SameBytes(previous, payload))
            {
                return;
            }

            previous = payload;
            MpNet.Send(message);
        }

        private static void OnInspect(HandInspectMessage message)
        {
            if (message.SenderId == MpNet.LocalPlayerId)
            {
                return;
            }

            bool watchingUs = message.TargetPlayerId == MpNet.LocalPlayerId;
            bool changed = watchingUs ? Watchers.Add(message.SenderId) : Watchers.Remove(message.SenderId);

            if (changed && watchingUs)
            {
                _lastCards = null;
                _lastDeck = null;
                _nextPublish = 0f;
            }
        }

        private static void OnCards(PlayerCardsMessage message)
        {
            if (message.SenderId == MpNet.LocalPlayerId)
            {
                return;
            }

            MpSafe.Run("MpHandInspect.OnCards", () =>
            {
                var mirror = MirrorFor(message.SenderId);
                mirror.Mana = message.Mana;
                mirror.HideDrawOrder = message.HideDrawOrder;

                mirror.Hand.Clear();
                mirror.Draw.Clear();
                mirror.Discard.Clear();
                mirror.Exile.Clear();

                foreach (var card in MpCardMirror.Rebuild(message.Cards, message.SenderId))
                {
                    switch (card.Zone)
                    {
                        case CardZone.Hand: mirror.Hand.Add(card); break;
                        case CardZone.Draw: mirror.Draw.Add(card); break;
                        case CardZone.Discard: mirror.Discard.Add(card); break;
                        case CardZone.Exile: mirror.Exile.Add(card); break;
                    }
                }

                mirror.Revision++;
            });
        }

        private static void OnDeck(PlayerDeckMessage message)
        {
            if (message.SenderId == MpNet.LocalPlayerId)
            {
                return;
            }

            MpSafe.Run("MpHandInspect.OnDeck", () =>
            {
                var mirror = MirrorFor(message.SenderId);
                mirror.Deck.Clear();
                mirror.Deck.AddRange(MpCardMirror.Rebuild(message.Cards, message.SenderId));
                mirror.Revision++;
            });
        }

        private static Mirror MirrorFor(int playerId)
        {
            if (!Mirrors.TryGetValue(playerId, out var mirror))
            {
                mirror = new Mirror();
                Mirrors[playerId] = mirror;
            }
            return mirror;
        }
    }
}
