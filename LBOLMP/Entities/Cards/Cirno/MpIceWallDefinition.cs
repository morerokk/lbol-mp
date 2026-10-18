using System.Collections.Generic;
using LBOLMP.Entities.StatusEffects;
using LBOLMP.Net;
using LBOLMP.Session;
using LBoL.Base;
using LBoL.ConfigData;
using LBoL.Core;
using LBoL.Core.Battle;
using LBoL.Core.Battle.BattleActions;
using LBoL.Core.Cards;
using LBoL.EntityLib.StatusEffects.Cirno;
using LBoLEntitySideloader;
using LBoLEntitySideloader.Attributes;
using LBoLEntitySideloader.CustomKeywords;
using UnityEngine;

namespace LBOLMP.Entities.Cards.Cirno
{
    /// <summary>
    /// How much Block the other player is getting
    /// </summary>
    public sealed class MpIceWallPayload : MpEffectPayload
    {
        public int Block;
    }

    /// <summary>
    /// Gives every partner Block equal to your own Frost Armor
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
            config.Cost = new ManaGroup { Blue = 1 };
            config.TargetType = TargetType.Self;

            config.RelativeEffects = new List<string> { nameof(MpPartner), nameof(FrostArmor) };
            config.UpgradedRelativeEffects = new List<string> { nameof(MpPartner), nameof(FrostArmor) };
            config.RelativeKeyword = Keyword.Block;
            config.UpgradedRelativeKeyword = Keyword.Block;

            // Mana pip for Scaled X
            config.Mana = new ManaGroup { Any = 1 };
            config.UpgradedMana = new ManaGroup { Any = 1 };

            config.Illustrator = "たれみなみ";

            return config;
        }

        public override IEnumerable<BattleAction> Receive(
            MpIceWallPayload payload, BattleController battle, int senderId)
        {
            if (payload.Block <= 0 || battle.BattleShouldEnd)
            {
                yield break;
            }

            yield return new CastBlockShieldAction(battle.Player, payload.Block, 0);
        }
    }

    [EntityLogic(typeof(MpIceWallDefinition))]
    public sealed class MpIceWall : Card
    {
        private static int PartySize => Mathf.Clamp(MpSession.IsActive ? MpSession.ConnectedCount : 2, 2, 4);

        public override ManaGroup AdditionalCost => new ManaGroup { Any = PartySize - 1 };

        public override void Initialize()
        {
            base.Initialize();
            this.AddCustomKeyword(new MpScaledKeyword());
        }

        protected override IEnumerable<BattleAction> Actions(
            UnitSelector selector, ManaGroup consumingMana, Interaction precondition)
        {
            int frost = Battle.Player.GetStatusEffect<FrostArmor>()?.Level ?? 0;
            if (frost <= 0)
            {
                yield break;
            }

            MpEffects.Send(Id, new MpIceWallPayload { Block = frost }, MpEffectTarget.AllPartners);

            if (IsUpgraded)
            {
                yield return DefenseAction(frost, 0);
            }
        }
    }
}
