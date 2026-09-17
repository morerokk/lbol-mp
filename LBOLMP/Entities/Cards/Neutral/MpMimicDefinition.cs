using System.Collections.Generic;
using LBOLMP.Entities.StatusEffects;
using LBOLMP.Session.Battle;
using LBoL.Base;
using LBoL.ConfigData;
using LBoL.Core;
using LBoL.Core.Battle;
using LBoL.Core.Battle.BattleActions;
using LBoL.Core.Cards;
using LBoLEntitySideloader;
using LBoLEntitySideloader.Attributes;
using LBoLEntitySideloader.Entities;

namespace LBOLMP.Entities.Cards.Neutral
{
    public sealed class MpMimicDefinition : LbolMpCardTemplate, IMpOnlyCard
    {
        public override IdContainer GetId() => nameof(MpMimic);

        public override CardConfig MakeConfig()
        {
            var config = DefaultConfig();
            config.Type = CardType.Skill;
            config.Rarity = Rarity.Uncommon;
            config.Colors = new List<ManaColor> { ManaColor.White, ManaColor.Black };
            config.Cost = new ManaGroup { White = 1, Black = 1 };
            config.UpgradedCost = ManaGroup.Hybrids(1, ManaColor.White, ManaColor.Black);
            config.Keywords = Keyword.Exile;
            config.UpgradedKeywords = Keyword.Exile;

            // How many of their cards get copied.
            config.Value1 = 1;

            // Borrows the enemy selector.
            // PartyTargetPatches points it at a partner instead.
            config.TargetType = TargetType.SingleEnemy;

            config.RelativeEffects = new List<string> { nameof(MpPartner) };
            config.UpgradedRelativeEffects = new List<string> { nameof(MpPartner) };

            // TODO: Temp disabled
            config.DebugLevel = 3;
            config.Illustrator = "";

            return config;
        }
    }

    [EntityLogic(typeof(MpMimicDefinition))]
    public sealed class MpMimic : Card, IMpPartnerTargeted
    {
        public override bool CanUse => MpPartyTargeting.AnyValidPartner;

        protected override IEnumerable<BattleAction> Actions(UnitSelector selector, ManaGroup consumingMana, Interaction precondition)
        {
            int partner = MpPartyTargeting.Consume();
            if (partner == Net.MpConstants.InvalidPlayerId)
            {
                yield break;
            }

            var apply = new ApplyStatusEffectAction(typeof(MpMimicSe), Battle.Player, Value1, occupationTime: 0.2f);
            ((MpMimicSe)apply.Args.Effect).PartnerId = partner;
            yield return apply;
        }
    }
}
