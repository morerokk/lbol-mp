using System.Collections.Generic;
using LBOLMP.Entities.StatusEffects;
using LBOLMP.Net;
using LBoL.Base;
using LBoL.ConfigData;
using LBoL.Core;
using LBoL.Core.Battle;
using LBoL.Core.Battle.BattleActions;
using LBoL.Core.Cards;
using LBoL.EntityLib.Cards.Neutral.NoColor;
using LBoLEntitySideloader;
using LBoLEntitySideloader.Attributes;

namespace LBOLMP.Entities.Cards.Neutral
{
    /// <summary>How many Pures each partner is getting, and whether they are upgraded.</summary>
    public sealed class MpAuraOfTranquilityPayload : MpEffectPayload
    {
        public int Pures;
        public bool Upgraded;
    }

    public sealed class MpAuraOfTranquilityDefinition : LbolMpMultiplayerCardTemplate<MpAuraOfTranquilityPayload>
    {
        public override IdContainer GetId() => nameof(MpAuraOfTranquility);

        public override CardConfig MakeConfig()
        {
            var config = DefaultConfig();
            config.Type = CardType.Skill;
            config.Rarity = Rarity.Uncommon;
            config.Colors = new List<ManaColor> { ManaColor.Colorless };
            config.TargetType = TargetType.Nobody;

            config.IsXCost = true;
            config.Cost = new ManaGroup { Colorless = 1 };

            config.RelativeCards = new List<string> { nameof(CManaCard) };
            // The trailing + makes the preview copy the upgraded one.
            config.UpgradedRelativeCards = new List<string> { nameof(CManaCard) + "+" };
            config.RelativeEffects = new List<string> { nameof(MpPartner) };
            config.UpgradedRelativeEffects = new List<string> { nameof(MpPartner) };

            config.Illustrator = "降旗原";

            return config;
        }

        public override IEnumerable<BattleAction> Receive(
            MpAuraOfTranquilityPayload payload, BattleController battle, int senderId)
        {
            if (payload.Pures <= 0 || battle.BattleShouldEnd)
            {
                yield break;
            }

            var pures = new List<Card>();
            for (int i = 0; i < payload.Pures; i++)
            {
                pures.Add(Library.CreateCard(nameof(CManaCard), payload.Upgraded));
            }

            yield return new AddCardsToHandAction(pures, AddCardsType.Normal, false);
        }
    }

    [EntityLogic(typeof(MpAuraOfTranquilityDefinition))]
    public sealed class MpAuraOfTranquility : Card
    {
        private const int ManaPerPure = 2;

        public override ManaGroup GetXCostFromPooled(ManaGroup pooledMana)
        {
            if ((pooledMana.Amount + XCostRequiredMana.Amount) % ManaPerPure == 0)
            {
                return pooledMana;
            }

            // Give back the most useful one in case of oops plays
            if (pooledMana.Philosophy > 0)
            {
                return pooledMana - ManaGroup.Single(ManaColor.Philosophy);
            }

            foreach (var color in ManaColors.WUBRGC)
            {
                if (pooledMana.GetValue(color) > 0)
                {
                    return pooledMana - ManaGroup.Single(color);
                }
            }

            return pooledMana;
        }

        protected override IEnumerable<BattleAction> Actions(
            UnitSelector selector, ManaGroup consumingMana, Interaction precondition)
        {
            int pures = SynergyAmount(consumingMana, ManaColor.Any, ManaPerPure);
            if (pures <= 0)
            {
                yield break;
            }

            MpEffects.Send(
                Id,
                new MpAuraOfTranquilityPayload { Pures = pures, Upgraded = IsUpgraded },
                MpEffectTarget.AllPartners);
        }
    }
}
