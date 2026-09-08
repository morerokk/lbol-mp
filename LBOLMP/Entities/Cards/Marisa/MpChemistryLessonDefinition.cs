using System.Collections.Generic;
using System.Linq;
using LBOLMP.Entities.StatusEffects;
using LBOLMP.Net;
using LBoL.Base;
using LBoL.Base.Extensions;
using LBoL.ConfigData;
using LBoL.Core;
using LBoL.Core.Battle;
using LBoL.Core.Battle.BattleActions;
using LBoL.Core.Cards;
using LBoL.EntityLib.Cards.Character.Marisa;
using LBoLEntitySideloader;
using LBoLEntitySideloader.Attributes;

namespace LBOLMP.Entities.Cards.Marisa
{
    /// <summary>How many potions a partner is getting.</summary>
    public sealed class MpChemistryLessonPayload : MpEffectPayload
    {
        public int Potions;
    }

    public sealed class MpChemistryLessonDefinition : LbolMpMultiplayerCardTemplate<MpChemistryLessonPayload>
    {
        public override IdContainer GetId() => nameof(MpChemistryLesson);

        public override CardConfig MakeConfig()
        {
            var config = DefaultConfig();
            config.Type = CardType.Skill;
            config.Rarity = Rarity.Rare;
            config.Owner = VanillaCharNames.Marisa;
            config.Colors = new List<ManaColor> { ManaColor.Black };
            config.Cost = new ManaGroup { Any = 1, Black = 2 };
            config.UpgradedCost = new ManaGroup { Black = 2 };
            config.TargetType = TargetType.Nobody;
            config.Keywords = Keyword.Exile | Keyword.Retain;
            config.UpgradedKeywords = Keyword.Exile | Keyword.Retain;

            config.RelativeCards = new List<string> { nameof(Potion) };
            config.UpgradedRelativeCards = new List<string> { nameof(Potion) };
            config.RelativeEffects = new List<string> { nameof(MpPartner) };
            config.UpgradedRelativeEffects = new List<string> { nameof(MpPartner) };

            config.Illustrator = "しらたま";

            return config;
        }

        public override IEnumerable<BattleAction> Receive(MpChemistryLessonPayload payload, BattleController battle, int senderId)
        {
            if (payload.Potions <= 0 || battle.BattleShouldEnd)
            {
                yield break;
            }

            yield return new AddCardsToDrawZoneAction(
                Library.CreateCards<Potion>(payload.Potions, false),
                DrawZoneTarget.Random,
                AddCardsType.Normal);
        }
    }

    [EntityLogic(typeof(MpChemistryLessonDefinition))]
    public sealed class MpChemistryLesson : Card
    {
        protected override IEnumerable<BattleAction> Actions(
            UnitSelector selector, ManaGroup consumingMana, Interaction precondition)
        {
            if (IsUpgraded)
            {
                yield return new AddCardsToDrawZoneAction(
                    Library.CreateCards<Potion>(1, false),
                    DrawZoneTarget.Random,
                    AddCardsType.Normal);
            }

            int potions = Battle.DrawZone.Count(card => card is Potion)
                          + Battle.DiscardZone.Count(card => card is Potion);
            if (potions <= 0)
            {
                yield break;
            }

            var partners = MpEffects.ValidPartners.ToList();
            if (partners.Count == 0)
            {
                yield break;
            }

            // Randomly distribute potions among alive partners, then send 1 network message
            var potionsPerPlayer = new Dictionary<int, int>();
            for (int i = 0; i < potions; i++)
            {
                int playerId = partners.Sample(BattleRng).PlayerId;
                potionsPerPlayer.TryGetValue(playerId, out int potionsForThisPlayer);
                potionsPerPlayer[playerId] = potionsForThisPlayer + 1;
            }

            foreach (var potionEntry in potionsPerPlayer)
            {
                MpEffects.Send(Id, new MpChemistryLessonPayload { Potions = potionEntry.Value }, MpEffectTarget.Partner, potionEntry.Key);
            }
        }
    }
}
