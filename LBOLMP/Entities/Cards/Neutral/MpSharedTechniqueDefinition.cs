using System.Collections.Generic;
using LBOLMP.Entities.StatusEffects;
using LBoL.Base;
using LBoL.ConfigData;
using LBoL.Core;
using LBoL.Core.Battle;
using LBoL.Core.Battle.BattleActions;
using LBoL.Core.Cards;
using LBoLEntitySideloader;
using LBoLEntitySideloader.Attributes;

namespace LBOLMP.Entities.Cards.Neutral
{
    public sealed class MpSharedTechniqueDefinition : LbolMpCardTemplate, IMpOnlyCard
    {
        public override IdContainer GetId() => nameof(MpSharedTechnique);

        public override CardConfig MakeConfig()
        {
            var config = DefaultConfig();
            config.Type = CardType.Skill;
            config.Rarity = Rarity.Rare;
            config.Colors = new List<ManaColor> { ManaColor.Red };
            config.Cost = new ManaGroup { Any = 1, Red = 1 };
            config.TargetType = TargetType.Self;

            // How many attack cards are shared.
            config.Value1 = 1;
            config.UpgradedValue1 = 2;

            config.RelativeEffects = new List<string> { nameof(MpPartner) };
            config.UpgradedRelativeEffects = new List<string> { nameof(MpPartner) };

            config.Illustrator = "犬野ラクガキ";

            return config;
        }
    }

    [EntityLogic(typeof(MpSharedTechniqueDefinition))]
    public sealed class MpSharedTechnique : Card
    {
        protected override IEnumerable<BattleAction> Actions(
            UnitSelector selector, ManaGroup consumingMana, Interaction precondition)
        {
            yield return BuffAction<MpSharedTechniqueSe>(Value1, 1, 0, 0, 0.2f);
        }
    }
}
