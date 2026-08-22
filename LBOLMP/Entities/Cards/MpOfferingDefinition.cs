using System.Collections.Generic;
using LBoL.Base;
using LBoL.ConfigData;
using LBoL.Core;
using LBoL.Core.Battle;
using LBoL.Core.Battle.BattleActions;
using LBoL.Core.Cards;
using LBoLEntitySideloader;
using LBoLEntitySideloader.Attributes;
using LBoLEntitySideloader.Entities;
using LBoLEntitySideloader.Resource;

namespace LBOLMP.Entities.Cards
{
    /// <summary>
    /// Offering to the Ownerless. Sets up <see cref="MpOfferingSe"/>, which does the actual work.
    ///
    /// Plain CardTemplate rather than MpCardTemplate: the card sends nothing itself, it only hands
    /// the player a status that will.
    /// </summary>
    public sealed class MpOfferingDefinition : CardTemplate, IMpOnlyCard
    {
        private static DirectorySource _source;

        private static DirectorySource Source => _source ?? (_source = new DirectorySource(MpInfo.Guid, ""));

        public override IdContainer GetId() => nameof(MpOffering);

        public override LocalizationOption LoadLocalization()
        {
            var files = new LocalizationFiles(Source, Locale.En);
            files.AddLocaleFile(Locale.En, "Resources/CardsEn.yaml");
            files.AddLocaleFile(Locale.ZhHans, "Resources/CardsZhHans.yaml");
            files.AddLocaleFile(Locale.ZhHant, "Resources/CardsZhHant.yaml");
            files.AddLocaleFile(Locale.Ja, "Resources/CardsJa.yaml");
            return files;
        }

        // No art yet. Swap for the usual AutoLoad once Resources/MpOffering.png exists:
        //     var images = new CardImages(Source);
        //     images.AutoLoad(this, extension: ".png");
        //     return images;
        public override CardImages LoadCardImages() => null;

        public override CardConfig MakeConfig()
        {
            var config = DefaultConfig();
            config.Type = CardType.Skill;
            config.Rarity = Rarity.Rare;
            config.Colors = new List<ManaColor> { ManaColor.Black };
            config.Cost = new ManaGroup { Black = 1 };
            config.TargetType = TargetType.Nobody;
            config.Keywords = Keyword.Exile | Keyword.Retain;
            config.UpgradedKeywords = Keyword.Exile | Keyword.Retain;
            config.RelativeEffects = new List<string> { nameof(MpOfferingSe) };
            config.UpgradedRelativeEffects = new List<string> { nameof(MpOfferingSe) };
            return config;
        }
    }

    [EntityLogic(typeof(MpOfferingDefinition))]
    public sealed class MpOffering : Card
    {
        protected override IEnumerable<BattleAction> Actions(
            UnitSelector selector, ManaGroup consumingMana, Interaction precondition)
        {
            yield return new ApplyStatusEffectAction(typeof(MpOfferingSe), Battle.Player, 1);
        }
    }
}
