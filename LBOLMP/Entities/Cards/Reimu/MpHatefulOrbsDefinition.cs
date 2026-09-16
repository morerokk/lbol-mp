using LBoL.Base;
using LBoL.ConfigData;
using LBoL.Core;
using LBoL.Core.Battle;
using LBoL.Core.Battle.BattleActions;
using LBoL.Core.Cards;
using LBoL.Core.StatusEffects;
using LBoL.EntityLib.Cards.Character.Reimu;
using LBoL.EntityLib.Cards.Neutral.TwoColor;
using LBoLEntitySideloader;
using LBoLEntitySideloader.Attributes;
using LBoLEntitySideloader.Entities;
using LBOLMP.Entities.StatusEffects;
using System.Collections.Generic;

namespace LBOLMP.Entities.Cards.Reimu
{
    public sealed class MpHatefulOrbsDefinition : LbolMpCardTemplate, IMpOnlyCard
    {
        public override IdContainer GetId() => nameof(MpHatefulOrbs);

        public override CardConfig MakeConfig()
        {
            var config = DefaultConfig();
            config.Type = CardType.Ability;
            config.Rarity = Rarity.Uncommon;
            config.Owner = VanillaCharNames.Reimu;
            config.Colors = new List<ManaColor> { ManaColor.Red, ManaColor.White };
            config.Cost = new ManaGroup { Any = 1, Red = 1, White = 1 };
            config.UpgradedCost = new ManaGroup { Any = 1 } + ManaGroup.Hybrids(1, ManaColor.Red, ManaColor.White);
            config.TargetType = TargetType.Self;

            // How much Weak and Vulnerable each orb hit is worth.
            config.Value1 = 1;

            config.RelativeEffects = new List<string> { nameof(Weak), nameof(Vulnerable) };
            config.UpgradedRelativeEffects = new List<string> { nameof(Weak), nameof(Vulnerable) };
            config.RelativeCards = new List<string> { nameof(YinyangCard), nameof(ShuihuoCard), nameof(FengleiCard) };
            config.UpgradedRelativeCards = new List<string> { nameof(YinyangCard) + "+", nameof(ShuihuoCard), nameof(FengleiCard) };

            config.Illustrator = "";

            return config;
        }
    }

    [EntityLogic(typeof(MpHatefulOrbsDefinition))]
    public sealed class MpHatefulOrbs : Card
    {
        protected override IEnumerable<BattleAction> Actions(
            UnitSelector selector, ManaGroup consumingMana, Interaction precondition)
        {
            yield return BuffAction<MpHatefulOrbsSe>(Value1, 0, 0, 0, 0.2f);
            yield return new AddCardsToHandAction(new Card[] { Library.CreateCard<YinyangCard>(IsUpgraded) });
        }
    }
}
