using System.Collections.Generic;
using LBOLMP.Entities.StatusEffects;
using LBOLMP.Net;
using LBoL.Base;
using LBoL.ConfigData;
using LBoL.Core;
using LBoL.Core.Battle;
using LBoL.Core.Battle.BattleActions;
using LBoL.Core.Cards;
using LBoL.EntityLib.StatusEffects.Cirno;
using LBoLEntitySideloader;
using LBoLEntitySideloader.Attributes;
using LBoLEntitySideloader.Entities;

namespace LBOLMP.Entities.Cards.Cirno
{
    /// <summary>How much Frost Armor everybody is getting.</summary>
    public sealed class MpIceWallPayload : MpEffectPayload
    {
        public int FrostArmor;
    }

    /// <summary>
    /// Copies your own Frost Armor onto every partner.
    /// </summary>
    public sealed class MpIceWallDefinition : LbolMpMultiplayerCardTemplate<MpIceWallPayload>
    {
        public override IdContainer GetId() => nameof(MpIceWall);

        public override CardConfig MakeConfig()
        {
            var config = DefaultConfig();
            config.Type = CardType.Skill;
            config.Rarity = Rarity.Uncommon;
            config.Owner = VanillaCharNames.Cirno;
            config.Colors = new List<ManaColor> { ManaColor.Blue };
            config.Cost = new ManaGroup { Any = 1, Blue = 1 };
            config.TargetType = TargetType.Self;
            config.Keywords = Keyword.Exile;
            config.UpgradedKeywords = Keyword.Exile;

            config.RelativeEffects = new List<string> { nameof(MpPartner), nameof(FrostArmor) };
            config.UpgradedRelativeEffects = new List<string> { nameof(MpPartner), nameof(FrostArmor) };
            config.UpgradedRelativeKeyword = Keyword.Block;

            config.Illustrator = "";

            return config;
        }

        public override IEnumerable<BattleAction> Receive(
            MpIceWallPayload payload, BattleController battle, int senderId)
        {
            if (payload.FrostArmor <= 0 || battle.BattleShouldEnd)
            {
                yield break;
            }

            yield return new ApplyStatusEffectAction<FrostArmor>(
                battle.Player, payload.FrostArmor, occupationTime: 0.2f);
        }
    }

    [EntityLogic(typeof(MpIceWallDefinition))]
    public sealed class MpIceWall : Card
    {
        protected override IEnumerable<BattleAction> Actions(
            UnitSelector selector, ManaGroup consumingMana, Interaction precondition)
        {
            int frost = Battle.Player.GetStatusEffect<FrostArmor>()?.Level ?? 0;
            if (frost <= 0)
            {
                yield break;
            }

            MpEffects.Send(Id, new MpIceWallPayload { FrostArmor = frost }, MpEffectTarget.AllPartners);

            if (IsUpgraded)
            {
                yield return DefenseAction(frost, 0);
            }
        }
    }
}
