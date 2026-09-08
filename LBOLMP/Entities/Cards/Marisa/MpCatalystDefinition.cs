using System.Collections.Generic;
using LBOLMP.Entities.StatusEffects;
using LBoL.Base;
using LBoL.ConfigData;
using LBoL.Core;
using LBoL.Core.Battle;
using LBoL.Core.Battle.BattleActions;
using LBoL.Core.Cards;
using LBoL.EntityLib.Cards.Character.Marisa;
using LBoLEntitySideloader;
using LBoLEntitySideloader.Attributes;
using LBoLEntitySideloader.Entities;

namespace LBOLMP.Entities.Cards.Marisa
{
    public sealed class MpCatalystDefinition : LbolMpCardTemplate, IMpOnlyCard
    {
        public override IdContainer GetId() => nameof(MpCatalyst);

        public override CardConfig MakeConfig()
        {
            var config = DefaultConfig();
            config.Type = CardType.Ability;
            config.Rarity = Rarity.Rare;
            config.Owner = VanillaCharNames.Marisa;
            config.Colors = new List<ManaColor> { ManaColor.Black };
            config.Cost = new ManaGroup { Any = 2, Black = 3 };
            config.TargetType = TargetType.Self;

            config.Keywords = Keyword.Ethereal;
            config.UpgradedKeywords = Keyword.Ethereal;

            config.RelativeCards = new List<string> { nameof(Potion) };
            config.UpgradedRelativeCards = new List<string> { nameof(Potion) };
            config.RelativeEffects = new List<string> { nameof(MpPartner) };
            config.UpgradedRelativeEffects = new List<string> { nameof(MpPartner) };

            config.Illustrator = "えめらね";

            return config;
        }
    }

    [EntityLogic(typeof(MpCatalystDefinition))]
    public sealed class MpCatalyst : Card
    {
        protected override IEnumerable<BattleAction> Actions(
            UnitSelector selector, ManaGroup consumingMana, Interaction precondition)
        {
            yield return BuffAction<MpCatalystSe>(1, 0, 0, 0, 0.2f);

            if (IsUpgraded)
            {
                yield return new AddCardsToDrawZoneAction(
                    Library.CreateCards<Potion>(2, false),
                    DrawZoneTarget.Random,
                    AddCardsType.Normal);
            }
        }
    }
}
