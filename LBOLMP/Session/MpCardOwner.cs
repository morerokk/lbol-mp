using System.Collections.Generic;
using System.Runtime.CompilerServices;
using LBOLMP.Net;
using LBoL.ConfigData;
using LBoL.Core;
using LBoL.Core.Cards;
using LBoL.Core.StatusEffects;

namespace LBOLMP.Session
{
    /// <summary>
    /// Remembers which player a card actually belongs to, so that the player name is actually the owning player's name.
    /// </summary>
    internal static class MpCardOwner
    {
        private static readonly ConditionalWeakTable<Card, object> Owners =
            new ConditionalWeakTable<Card, object>();

        // Built names, which would otherwise be rebuilt every time a description is read.
        private static readonly Dictionary<string, UnitName> CharacterNames = new Dictionary<string, UnitName>();
        private static readonly Dictionary<string, UnitName> PlayerNames = new Dictionary<string, UnitName>();

        /// <summary>Say that this card is a mirror of one <paramref name="playerId"/> is holding.</summary>
        internal static void Set(Card card, int playerId)
        {
            if (card == null)
            {
                return;
            }

            Owners.Remove(card);
            Owners.Add(card, playerId);
        }

        internal static void SetAll(IEnumerable<Card> cards, int playerId)
        {
            if (cards == null)
            {
                return;
            }

            foreach (var card in cards)
            {
                Set(card, playerId);
            }
        }

        private static int OwnerOf(GameEntity entity)
        {
            if (entity is Card card)
            {
                return Owners.TryGetValue(card, out var owner) && owner is int playerId
                    ? playerId
                    : MpNet.LocalPlayerId;
            }

            // A status effect standing on a mirrored ally is theirs, and so is its source card.
            if (entity is StatusEffect effect)
            {
                int ally = UI.MpAllyUnits.PlayerFor(effect.Owner);
                if (ally != MpConstants.InvalidPlayerId)
                {
                    return ally;
                }
            }

            return MpNet.LocalPlayerId;
        }

        internal static UnitName NameFor(GameEntity entity)
        {
            if (!MpSession.IsActive)
            {
                return null;
            }

            int owner = OwnerOf(entity);
            bool ours = owner == MpNet.LocalPlayerId;

            if (MpPlugin.ShowPlayerNamesOnCards.Value)
            {
                string name = ours ? MpSession.LocalName : MpSession.Get(owner)?.Name;
                return string.IsNullOrWhiteSpace(name)
                    ? null
                    : Build(name, MpSession.Get(owner)?.CharacterId);
            }

            // Our own cards already read correctly.
            return ours ? null : CharacterName(MpSession.Get(owner)?.CharacterId);
        }

        /// <summary>The character's own name.</summary>
        private static UnitName CharacterName(string characterId)
        {
            if (string.IsNullOrEmpty(characterId))
            {
                return null;
            }

            if (CharacterNames.TryGetValue(characterId, out var cached))
            {
                return cached;
            }

            var name = UnitNameTable.GetName(characterId, NarrativeColor(characterId));
            CharacterNames[characterId] = name;
            return name;
        }

        private static string NarrativeColor(string characterId)
        {
            if (string.IsNullOrEmpty(characterId))
            {
                return null;
            }

            return MpSafe.Run("MpCardOwner.NarrativeColor",
                () => PlayerUnitConfig.FromId(characterId)?.NarrativeColor, null);
        }

        private static UnitName Build(string name, string characterId)
        {
            // Keyed by both, since the same person on a different character is a different colour.
            string key = name + "\n" + characterId;

            if (PlayerNames.TryGetValue(key, out var cached))
            {
                return cached;
            }

            var built = new UnitName(name) { Color = NarrativeColor(characterId) };
            PlayerNames[key] = built;
            return built;
        }

        /// <summary>Names can change between sessions, and characters between runs. Call this to reset them</summary>
        internal static void Reset()
        {
            CharacterNames.Clear();
            PlayerNames.Clear();
        }
    }
}
