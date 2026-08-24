using System.Collections.Generic;
using System.Linq;
using LBOLMP.Entities.StatusEffects;
using LBOLMP.Net;
using LBOLMP.Session.Battle;
using LBoL.Base;
using LBoL.ConfigData;
using LBoL.Core;
using LBoL.Core.Battle;
using LBoL.Core.Battle.BattleActions;
using LBoL.Core.Battle.Interactions;
using LBoL.Core.Cards;
using LBoL.Core.Randoms;
using LBoLEntitySideloader;
using LBoLEntitySideloader.Attributes;
using LBoLEntitySideloader.Entities;
using LBoLEntitySideloader.Resource;

namespace LBOLMP.Entities.Cards
{
    /// <summary>
    /// Steal a page out of a partner's book (literally but not literally).
    /// This is like Remote Support, but you pick a Partner whose card pool you want to use, and it can't generate neutrals.
    /// </summary>
    public sealed class MpBorrowTechniquesDefinition : CardTemplate, IMpOnlyCard
    {
        private static DirectorySource _source;

        private static DirectorySource Source => _source ?? (_source = new DirectorySource(MpInfo.Guid, ""));

        public override IdContainer GetId() => nameof(MpBorrowTechniques);

        public override LocalizationOption LoadLocalization()
        {
            var files = new LocalizationFiles(Source, Locale.En);
            files.AddLocaleFile(Locale.En, "Resources/CardsEn.yaml");
            return files;
        }

        public override CardImages LoadCardImages()
        {
            var images = new CardImages(Source);
            images.AutoLoad(this, extension: ".png", relativePath: "Resources/Cards/");
            return images;
        }

        public override CardConfig MakeConfig()
        {
            var config = DefaultConfig();
            config.Type = CardType.Skill;
            config.Rarity = Rarity.Uncommon;
            config.Owner = VanillaCharNames.Marisa;
            config.Colors = new List<ManaColor> { ManaColor.Green, ManaColor.Black };
            config.Cost = new ManaGroup { Any = 1, Green = 1, Black = 1 };
            config.UpgradedCost = new ManaGroup { Any = 1 }
                                  + ManaGroup.Hybrids(1, ManaColor.Green, ManaColor.Black);

            // How many cards you get to choose between.
            config.Value1 = 3;
            config.UpgradedValue1 = 5;

            // What the borrowed card temporarily costs, and what {Mana} renders as in the
            // description. Empty rather than the literal "0" so it draws as a mana pip.
            config.Mana = ManaGroup.Empty;

            config.Keywords = Keyword.Exile;
            config.UpgradedKeywords = Keyword.Exile;

            // Explains what the borrowed card arrives with. Exile is already covered by Keywords.
            // TempMorph is "Temporary Cost Change", and has to be spelled out because it is marked
            // AutoAppend = false, so nothing infers it from the SetTurnCost call.
            config.RelativeKeyword = Keyword.Ethereal | Keyword.TempMorph;
            config.UpgradedRelativeKeyword = Keyword.Ethereal | Keyword.TempMorph;

            // Set to TargetType.SingleEnemy just so we can borrow the selector logic.
            // PartyTargetPatches points it at a partner instead of enemies.
            config.TargetType = TargetType.SingleEnemy;

            config.RelativeEffects = new List<string> { nameof(MpPartner) };
            config.UpgradedRelativeEffects = new List<string> { nameof(MpPartner) };
            config.Illustrator = "ebuna";
            return config;
        }
    }

    [EntityLogic(typeof(MpBorrowTechniquesDefinition))]
    public sealed class MpBorrowTechniques : Card, IMpPartnerTargeted
    {
        // Can't be played if there are no valid partners to borrow from
        public override bool CanUse => MpPartyTargeting.AnyValidPartner;

        protected override IEnumerable<BattleAction> Actions(
            UnitSelector selector, ManaGroup consumingMana, Interaction precondition)
        {
            var seat = MpBattleSync.GetSeat(MpPartyTargeting.Consume());
            if (seat == null || string.IsNullOrEmpty(seat.CharacterId))
            {
                yield break;
            }

            // RollCardsWithoutManaLimit rather than RollCards: their cards should show up whatever
            // colors we happen to be running, and we are not paying their cost anyway.
            var offers = Battle.RollCardsWithoutManaLimit(
                new CardWeightTable(RarityWeightTable.BattleCard, OwnerWeightTable.AllOnes,
                    CardTypeWeightTable.CanBeLoot, false),
                Value1,
                config => config.Owner == seat.CharacterId);

            if (offers.Length == 0)
            {
                yield break;
            }

            var interaction = new MiniSelectCardInteraction(offers, false, false, false) { Source = this };
            yield return new InteractionAction(interaction, false);

            var borrowed = interaction.SelectedCard;
            if (borrowed == null)
            {
                yield break;
            }

            borrowed.SetTurnCost(Mana);
            borrowed.IsEthereal = true;
            borrowed.IsExile = true;

            yield return new AddCardsToHandAction(new[] { borrowed });
        }
    }
}
