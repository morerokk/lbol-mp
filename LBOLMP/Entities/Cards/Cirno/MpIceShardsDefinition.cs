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
    /// <summary>How many levels of the status effect to add.</summary>
    public sealed class MpIceShardsPayload : MpEffectPayload
    {
        public int Level;
    }

    public sealed class MpIceShardsDefinition : LbolMpMultiplayerCardTemplate<MpIceShardsPayload>
    {
        public override IdContainer GetId() => nameof(MpIceShards);

        public override CardConfig MakeConfig()
        {
            var config = DefaultConfig();
            config.Type = CardType.Ability;
            config.Rarity = Rarity.Rare;
            config.Owner = VanillaCharNames.Cirno;
            config.Colors = new List<ManaColor> { ManaColor.Blue, ManaColor.Green };
            config.Cost = new ManaGroup { Any = 1 } + ManaGroup.Hybrids(1, ManaColor.Blue, ManaColor.Green);
            config.TargetType = TargetType.Self;

            // How much Frost Armor each hit is worth.
            config.Value1 = 1;
            config.UpgradedValue1 = 2;

            config.RelativeEffects = new List<string>
            {
                nameof(MpPartner), nameof(Cold), nameof(FrostArmor)
            };
            config.UpgradedRelativeEffects = new List<string>
            {
                nameof(MpPartner), nameof(Cold), nameof(FrostArmor)
            };

            config.Illustrator = "国家飯";

            return config;
        }

        public override IEnumerable<BattleAction> Receive(
            MpIceShardsPayload payload, BattleController battle, int senderId)
        {
            if (payload.Level <= 0 || battle.BattleShouldEnd)
            {
                yield break;
            }

            yield return new ApplyStatusEffectAction<MpIceShardsSe>(
                battle.Player, payload.Level, occupationTime: 0.2f);
        }
    }

    [EntityLogic(typeof(MpIceShardsDefinition))]
    public sealed class MpIceShards : Card
    {
        protected override IEnumerable<BattleAction> Actions(
            UnitSelector selector, ManaGroup consumingMana, Interaction precondition)
        {
            MpEffects.Send(Id, new MpIceShardsPayload { Level = Value1 }, MpEffectTarget.AllPartners);

            yield return BuffAction<MpIceShardsSe>(Value1, 0, 0, 0, 0.2f);

            foreach (var action in DebuffAction<Cold>(Battle.AllAliveEnemies, 0, 0, 0, 0, true, 0.03f))
            {
                yield return action;
            }
        }
    }
}
