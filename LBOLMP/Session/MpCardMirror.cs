using System;
using System.Collections.Generic;
using LBOLMP.Net;
using LBoL.Base;
using LBoL.Core;
using LBoL.Core.Cards;
using LBoL.Presentation;

namespace LBOLMP.Session
{
    /// <summary>Which of a card's fields are worth networking.</summary>
    [Flags]
    internal enum MpCardFields : ushort
    {
        None = 0,
        Upgraded = 1,
        Keywords = 2,
        BaseCost = 4,
        TurnCostDelta = 8,
        AuraCost = 16,
        FreeCost = 32,
        Summoned = 64,
        Loyalty = 128,
        UpgradeCounter = 256
    }

    /// <summary>One card exactly as it reads on its owner's screen.</summary>
    public sealed class MpCardState
    {
        public string Id = string.Empty;

        /// <summary>A <c>CardZone</c> ordinal.</summary>
        public int Zone;

        internal MpCardFields Fields;

        public ulong Keywords;
        public ManaGroup BaseCost;
        public ManaGroup TurnCostDelta;
        public ManaGroup AuraCost;
        public int Loyalty;
        public int UpgradeCounter;
    }

    /// <summary>
    /// Serializes/deserializes cards for other players to see or do something with.
    /// </summary>
    public static class MpCardMirror
    {
        private static readonly Dictionary<string, Card> References = new Dictionary<string, Card>();

        /// <summary>Card ids the local install has never heard of, so each is only logged once.</summary>
        /// Please don't play the game with different modlists, it makes the programmers unhappy
        private static readonly HashSet<string> Unknown = new HashSet<string>();

        /// <summary>Snapshot a collection of cards, in order.</summary>
        public static List<MpCardState> Capture(IEnumerable<Card> cards)
        {
            var result = new List<MpCardState>();
            if (cards == null)
            {
                return result;
            }

            foreach (var card in cards)
            {
                if (card == null || string.IsNullOrEmpty(card.Id))
                {
                    continue;
                }

                result.Add(Capture(card));
            }

            return result;
        }

        private static MpCardState Capture(Card card)
        {
            var state = new MpCardState { Id = card.Id, Zone = (int)card.Zone };
            var fields = MpCardFields.None;

            if (card.IsUpgraded)
            {
                fields |= MpCardFields.Upgraded;
            }

            if (card.UpgradeCounter.GetValueOrDefault() > 0)
            {
                fields |= MpCardFields.UpgradeCounter;
                state.UpgradeCounter = card.UpgradeCounter.Value;
            }

            var reference = Reference(card.Id, card.IsUpgraded);

            // Copy keywords worth copying.
            if (reference == null || card.Keywords != reference.Keywords)
            {
                fields |= MpCardFields.Keywords;
                state.Keywords = (ulong)card.Keywords;
            }

            // Sync cost for non X-cost cards (since they can't be cost-reduced)
            if (!card.IsXCost)
            {
                if (card.BaseCost != card.ConfigCost)
                {
                    fields |= MpCardFields.BaseCost;
                    state.BaseCost = card.BaseCost;
                }

                if (card.TurnCostDelta != ManaGroup.Empty)
                {
                    fields |= MpCardFields.TurnCostDelta;
                    state.TurnCostDelta = card.TurnCostDelta;
                }
            }

            if (card.AuraCost != ManaGroup.Empty)
            {
                fields |= MpCardFields.AuraCost;
                state.AuraCost = card.AuraCost;
            }

            if (card.FreeCost)
            {
                fields |= MpCardFields.FreeCost;
            }

            if (card.Summoned)
            {
                fields |= MpCardFields.Summoned;
            }

            // Sync "Unity" (teammate card resource) too
            if (card.CardType == CardType.Friend
                && (reference == null || card.Loyalty != reference.Loyalty))
            {
                fields |= MpCardFields.Loyalty;
                state.Loyalty = card.Loyalty;
            }

            state.Fields = fields;
            return state;
        }

        private static Card Reference(string id, bool upgraded)
        {
            string key = upgraded ? id + "+" : id;
            if (References.TryGetValue(key, out var existing))
            {
                return existing;
            }

            var card = MpSafe.Run("MpCardMirror.Reference",
                () => Library.TryCreateCard(id, upgraded), null);
            References[key] = card;
            return card;
        }

        /// <summary>
        /// Deserializes cards into readable cards.
        /// </summary>
        /// <param name="ownerPlayerId">
        /// Who is holding these, so that text about "you" names them and not the local player.
        /// </param>
        public static List<Card> Rebuild(IReadOnlyList<MpCardState> states, int ownerPlayerId)
        {
            var result = new List<Card>();
            if (states == null)
            {
                return result;
            }

            var gameRun = GameMaster.Instance?.CurrentGameRun;
            var battle = gameRun?.Battle;

            foreach (var state in states)
            {
                var card = MpSafe.Run("MpCardMirror.Rebuild", () => Rebuild(state, gameRun, battle), null);
                if (card != null)
                {
                    MpCardOwner.Set(card, ownerPlayerId);
                    result.Add(card);
                }
            }

            return result;
        }

        private static Card Rebuild(MpCardState state, GameRunController gameRun, LBoL.Core.Battle.BattleController battle)
        {
            bool upgraded = (state.Fields & MpCardFields.Upgraded) != 0;
            int? counter = (state.Fields & MpCardFields.UpgradeCounter) != 0
                ? state.UpgradeCounter
                : (int?)null;

            var card = Library.TryCreateCard(state.Id, upgraded, counter);
            if (card == null)
            {
                if (Unknown.Add(state.Id))
                {
                    MpPlugin.Log.LogWarning(
                        $"A player is holding '{state.Id}', which this install does not have; "
                        + "it will be missing from their hand here");
                }
                return null;
            }

            if (battle != null)
            {
                card.SetBattle(battle);
            }
            else if (gameRun != null)
            {
                card.GameRun = gameRun;
            }

            card.Zone = (CardZone)state.Zone;

            if ((state.Fields & MpCardFields.Keywords) != 0)
            {
                card.Keywords = (Keyword)state.Keywords;
            }

            if (!card.IsXCost)
            {
                if ((state.Fields & MpCardFields.BaseCost) != 0)
                {
                    card.BaseCost = state.BaseCost;
                }
                if ((state.Fields & MpCardFields.TurnCostDelta) != 0)
                {
                    card.TurnCostDelta = state.TurnCostDelta;
                }
            }

            if ((state.Fields & MpCardFields.AuraCost) != 0)
            {
                card.AuraCost = state.AuraCost;
            }

            card.FreeCost = (state.Fields & MpCardFields.FreeCost) != 0;
            card.Summoned = (state.Fields & MpCardFields.Summoned) != 0;

            if ((state.Fields & MpCardFields.Loyalty) != 0)
            {
                card.Loyalty = state.Loyalty;
            }

            return card;
        }

        /// <summary>
        /// Ids are written once and then referred to by index.
        /// A deck can have a lot of repeats, we can kinda sorta compress that maybe? This code is half-finished for that reason
        /// </summary>
        public static void Write(NetWriter w, IReadOnlyList<MpCardState> states)
        {
            var ids = new List<string>();
            var indices = new Dictionary<string, int>();

            for (int i = 0; i < states.Count; i++)
            {
                string id = states[i].Id ?? string.Empty;
                if (!indices.ContainsKey(id))
                {
                    indices[id] = ids.Count;
                    ids.Add(id);
                }
            }

            w.StringList(ids);
            w.Int(states.Count);

            for (int i = 0; i < states.Count; i++)
            {
                var state = states[i];
                w.UShort((ushort)indices[state.Id ?? string.Empty]);
                w.Byte((byte)state.Zone);
                w.UShort((ushort)state.Fields);

                if ((state.Fields & MpCardFields.Keywords) != 0)
                {
                    w.ULong(state.Keywords);
                }
                if ((state.Fields & MpCardFields.BaseCost) != 0)
                {
                    WriteMana(w, state.BaseCost);
                }
                if ((state.Fields & MpCardFields.TurnCostDelta) != 0)
                {
                    WriteMana(w, state.TurnCostDelta);
                }
                if ((state.Fields & MpCardFields.AuraCost) != 0)
                {
                    WriteMana(w, state.AuraCost);
                }
                if ((state.Fields & MpCardFields.Loyalty) != 0)
                {
                    w.Short((short)state.Loyalty);
                }
                if ((state.Fields & MpCardFields.UpgradeCounter) != 0)
                {
                    w.Short((short)state.UpgradeCounter);
                }
            }
        }

        public static List<MpCardState> Read(NetReader r)
        {
            var ids = r.StringArray();
            int count = r.Int();

            var result = new List<MpCardState>(count);
            for (int i = 0; i < count; i++)
            {
                int index = r.UShort();
                var state = new MpCardState
                {
                    Id = index >= 0 && index < ids.Length ? ids[index] : string.Empty,
                    Zone = r.Byte(),
                    Fields = (MpCardFields)r.UShort()
                };

                if ((state.Fields & MpCardFields.Keywords) != 0)
                {
                    state.Keywords = r.ULong();
                }
                if ((state.Fields & MpCardFields.BaseCost) != 0)
                {
                    state.BaseCost = ReadMana(r);
                }
                if ((state.Fields & MpCardFields.TurnCostDelta) != 0)
                {
                    state.TurnCostDelta = ReadMana(r);
                }
                if ((state.Fields & MpCardFields.AuraCost) != 0)
                {
                    state.AuraCost = ReadMana(r);
                }
                if ((state.Fields & MpCardFields.Loyalty) != 0)
                {
                    state.Loyalty = r.Short();
                }
                if ((state.Fields & MpCardFields.UpgradeCounter) != 0)
                {
                    state.UpgradeCounter = r.Short();
                }

                result.Add(state);
            }

            return result;
        }

        // Only write the mana cost if it's been messed with.
        // We can assume the vast majority of serialized cards did not have their mana messed with, so this cheaply lets us send the same card in fewer bytes.
        public static void WriteMana(NetWriter w, ManaGroup mana)
        {
            w.Short((short)mana.Any);
            w.Short((short)mana.White);
            w.Short((short)mana.Blue);
            w.Short((short)mana.Black);
            w.Short((short)mana.Red);
            w.Short((short)mana.Green);
            w.Short((short)mana.Colorless);
            w.Short((short)mana.Philosophy);
            w.Short((short)mana.Hybrid);
            w.Short((short)mana.HybridColor);
        }

        public static ManaGroup ReadMana(NetReader r)
        {
            return new ManaGroup
            {
                Any = r.Short(),
                White = r.Short(),
                Blue = r.Short(),
                Black = r.Short(),
                Red = r.Short(),
                Green = r.Short(),
                Colorless = r.Short(),
                Philosophy = r.Short(),
                Hybrid = r.Short(),
                HybridColor = r.Short()
            };
        }
    }
}
