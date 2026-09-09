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
    public sealed class MpCosmicInsightPayload : MpEffectPayload
    {
        public bool Upgraded;
    }

    public sealed class MpCosmicInsightDefinition : LbolMpMultiplayerCardTemplate<MpCosmicInsightPayload>
    {
        public override IdContainer GetId() => nameof(MpCosmicInsight);

        public override CardConfig MakeConfig()
        {
            var config = DefaultConfig();
            config.Type = CardType.Skill;
            config.Rarity = Rarity.Uncommon;
            config.Colors = new List<ManaColor> { ManaColor.Black };
            config.Cost = new ManaGroup { Any = 1, Black = 1 };
            config.TargetType = TargetType.Nobody;

            config.RelativeCards = new List<string> { nameof(Astrology) };
            config.UpgradedRelativeCards = new List<string> { nameof(Astrology) + "+" };
            config.RelativeEffects = new List<string> { nameof(MpPartner) };
            config.UpgradedRelativeEffects = new List<string> { nameof(MpPartner) };

            config.Illustrator = "うおのめうろこ";

            return config;
        }


        internal static BattleAction AddAstrology(bool upgraded) =>
            new AddCardsToHandAction(
                new[] { Library.CreateCard(nameof(Astrology), upgraded) }, AddCardsType.Normal, false);

        public override IEnumerable<BattleAction> Receive(
            MpCosmicInsightPayload payload, BattleController battle, int senderId)
        {
            if (battle.BattleShouldEnd)
            {
                yield break;
            }

            yield return AddAstrology(payload.Upgraded);
        }
    }

    [EntityLogic(typeof(MpCosmicInsightDefinition))]
    public sealed class MpCosmicInsight : Card
    {
        protected override IEnumerable<BattleAction> Actions(
            UnitSelector selector, ManaGroup consumingMana, Interaction precondition)
        {
            yield return MpCosmicInsightDefinition.AddAstrology(IsUpgraded);

            MpEffects.Send(Id, new MpCosmicInsightPayload { Upgraded = IsUpgraded },
                MpEffectTarget.AllPartners);
        }
    }
}
