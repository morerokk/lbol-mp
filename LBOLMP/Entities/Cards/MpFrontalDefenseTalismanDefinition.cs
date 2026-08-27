using System.Collections.Generic;
using LBOLMP.Entities.StatusEffects;
using LBoL.Base;
using LBoL.ConfigData;
using LBoL.Core;
using LBoL.Core.Battle;
using LBoL.Core.Cards;
using LBoLEntitySideloader;
using LBoLEntitySideloader.Attributes;
using LBoLEntitySideloader.Entities;

namespace LBOLMP.Entities.Cards
{
    public sealed class MpFrontalDefenseTalismanDefinition : LbolMpCardTemplate, IMpOnlyCard
    {
        public override IdContainer GetId() => nameof(MpFrontalDefenseTalisman);

        public override CardConfig MakeConfig()
        {
            var config = DefaultConfig();
            config.Type = CardType.Ability;
            config.Rarity = Rarity.Rare;
            config.Owner = VanillaCharNames.Reimu;
            config.Colors = new List<ManaColor> { ManaColor.White, ManaColor.Blue };
            config.Cost = new ManaGroup { Any = 2, White = 1, Blue = 1 };
            config.UpgradedCost = new ManaGroup { Any = 1 } + ManaGroup.Hybrids(1, ManaColor.White, ManaColor.Blue);
            config.TargetType = TargetType.Self;

            config.RelativeKeyword = Keyword.Shield | Keyword.Block;
            config.UpgradedRelativeKeyword = Keyword.Shield | Keyword.Block;

            config.RelativeEffects = new List<string> { nameof(MpPartner) };
            config.UpgradedRelativeEffects = new List<string> { nameof(MpPartner) };
            config.Illustrator = "他们都叫我凉子";
            return config;
        }
    }

    [EntityLogic(typeof(MpFrontalDefenseTalismanDefinition))]
    public sealed class MpFrontalDefenseTalisman : Card
    {
        protected override IEnumerable<BattleAction> Actions(
            UnitSelector selector, ManaGroup consumingMana, Interaction precondition)
        {
            yield return BuffAction<MpFrontalDefenseTalismanSe>();
        }
    }
}
