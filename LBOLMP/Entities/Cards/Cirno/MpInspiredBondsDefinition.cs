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
using LBoLEntitySideloader.Entities;

namespace LBOLMP.Entities.Cards.Cirno
{
    public sealed class MpInspiredBondsDefinition : LbolMpCardTemplate, IMpOnlyCard
    {
        public override IdContainer GetId() => nameof(MpInspiredBonds);

        public override CardConfig MakeConfig()
        {
            var config = DefaultConfig();
            config.Type = CardType.Ability;
            config.Rarity = Rarity.Rare;
            config.Owner = VanillaCharNames.Cirno;
            config.Colors = new List<ManaColor> { ManaColor.Green };
            config.Cost = new ManaGroup { Green = 2 };
            config.UpgradedCost = ManaGroup.Empty;
            config.TargetType = TargetType.Self;

            // How much Unity a partner's teammate gets each time.
            config.Value1 = 1;

            config.RelativeEffects = new List<string> { nameof(MpPartner) };
            config.UpgradedRelativeEffects = new List<string> { nameof(MpPartner) };
            config.RelativeKeyword = Keyword.FriendCard | Keyword.Loyalty;
            config.UpgradedRelativeKeyword = Keyword.FriendCard | Keyword.Loyalty;

            config.Illustrator = "";

            return config;
        }
    }

    [EntityLogic(typeof(MpInspiredBondsDefinition))]
    public sealed class MpInspiredBonds : Card
    {
        protected override IEnumerable<BattleAction> Actions(
            UnitSelector selector, ManaGroup consumingMana, Interaction precondition)
        {
            yield return BuffAction<MpInspiredBondsSe>(Value1, 0, 0, 0, 0.2f);
        }
    }
}
