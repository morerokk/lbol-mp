using System.Collections.Generic;
using LBOLMP.Entities.StatusEffects;
using LBOLMP.Net;
using LBoL.Base;
using LBoL.ConfigData;
using LBoL.Core;
using LBoL.Core.Battle;
using LBoL.Core.Battle.BattleActions;
using LBoL.Core.Cards;
using LBoL.EntityLib.Cards.Character.Reimu;
using LBoLEntitySideloader;
using LBoLEntitySideloader.Attributes;

namespace LBOLMP.Entities.Cards
{
    /// <summary>Whether the orb the partner is getting is upgraded.</summary>
    public sealed class MpYinYangDistributionPayload : MpEffectPayload
    {
        public bool Upgraded;
    }

    /// <summary>
    /// Give a yin-yang orb to all other players.
    /// </summary>
    public sealed class MpYinYangDistributionDefinition : LbolMpMultiplayerCardTemplate<MpYinYangDistributionPayload>
    {
        public override IdContainer GetId() => nameof(MpYinYangDistribution);

        public override CardConfig MakeConfig()
        {
            var config = DefaultConfig();
            config.Type = CardType.Skill;
            config.Owner = VanillaCharNames.Reimu;
            config.Colors = new List<ManaColor> { ManaColor.White, ManaColor.Red };
            config.TargetType = TargetType.Nobody;

            config.Rarity = Rarity.Common;
            config.Cost = new ManaGroup { White = 1, Red = 1 };
            config.UpgradedCost = new ManaGroup { Any = 1 } + ManaGroup.Hybrids(1, ManaColor.White, ManaColor.Red);
            config.Illustrator = "あきゅー";

            config.RelativeCards = new List<string> { nameof(YinyangCard) };
            // The trailing + makes EnumerateRelativeCards upgrade the preview copy.
            config.UpgradedRelativeCards = new List<string> { nameof(YinyangCard) + "+" };
            config.RelativeEffects = new List<string> { nameof(MpPartner) };
            config.UpgradedRelativeEffects = new List<string> { nameof(MpPartner) };
            return config;
        }

        public override IEnumerable<BattleAction> Receive(
            MpYinYangDistributionPayload payload, BattleController battle, int senderId)
        {
            yield return new AddCardsToHandAction(
                Library.CreateCards<YinyangCard>(1, payload.Upgraded), AddCardsType.Normal, false);
        }
    }

    [EntityLogic(typeof(MpYinYangDistributionDefinition))]
    public sealed class MpYinYangDistribution : Card
    {
        protected override IEnumerable<BattleAction> Actions(
            UnitSelector selector, ManaGroup consumingMana, Interaction precondition)
        {
            MpEffects.Send(Id, new MpYinYangDistributionPayload { Upgraded = IsUpgraded },
                MpEffectTarget.AllPartners);
            yield break;
        }
    }
}
