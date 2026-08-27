using System.Collections.Generic;
using LBOLMP.Entities.StatusEffects;
using LBoL.Base;
using LBoL.ConfigData;
using LBoL.Core;
using LBoL.Core.Battle;
using LBoL.Core.Battle.BattleActions;
using LBoL.Core.Cards;
using LBoL.EntityLib.StatusEffects.Koishi;
using LBoLEntitySideloader;
using LBoLEntitySideloader.Attributes;
using LBoLEntitySideloader.Entities;
using LBoLEntitySideloader.Resource;

namespace LBOLMP.Entities.Cards
{
    /// <summary>
    /// The multiplayer replacement for Koishi's Anatta card.
    /// The vanilla one is hidden in multiplayer runs by <see cref="Session.MpCardAvailability"/>,
    /// so only one of the two is ever findable.
    /// </summary>
    public sealed class MpAnattaDefinition : CardTemplate, IMpOnlyCard
    {
        private static DirectorySource _source;

        private static DirectorySource Source => _source ?? (_source = new DirectorySource(MpInfo.Guid, ""));

        public override IdContainer GetId() => nameof(MpAnatta);

        public override LocalizationOption LoadLocalization() => MpLocalization.Cards.AddEntity(this);

        public override CardImages LoadCardImages()
        {
            var images = new CardImages(Source);
            images.AutoLoad(this, extension: ".png", relativePath: "Resources/Cards/");
            return images;
        }

        public override CardConfig MakeConfig()
        {
            var config = DefaultConfig();
            config.Type = CardType.Ability;
            config.Rarity = Rarity.Uncommon;
            config.Owner = VanillaCharNames.Koishi;
            config.Colors = new List<ManaColor> { ManaColor.White };
            config.Cost = new ManaGroup { Any = 2, White = 1 };
            config.UpgradedCost = new ManaGroup { Any = 1, White = 1 };
            config.TargetType = TargetType.Self;

            config.Mana = MpAnattaSeDefinition.RewardPerStack;
            config.RelativeEffects = new List<string> { nameof(MoodPeace), nameof(MpPartner) };
            config.UpgradedRelativeEffects = new List<string> { nameof(MoodPeace), nameof(MpPartner) };
            config.Illustrator = "Tuck坦";
            return config;
        }
    }

    [EntityLogic(typeof(MpAnattaDefinition))]
    public sealed class MpAnatta : Card
    {
        protected override IEnumerable<BattleAction> Actions(
            UnitSelector selector, ManaGroup consumingMana, Interaction precondition)
        {
            yield return BuffAction<MpAnattaSe>(1, 0, 0, 0, 0.2f);
            yield return BuffAction<MoodPeace>(0, 0, 0, 0, 0.2f);
        }
    }
}
