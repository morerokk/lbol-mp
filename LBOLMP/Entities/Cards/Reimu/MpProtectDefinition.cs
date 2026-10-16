using System.Collections.Generic;
using LBOLMP.Entities.StatusEffects;
using LBOLMP.Net;
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

namespace LBOLMP.Entities.Cards.Reimu
{
    /// <summary>How much Barrier the partner is getting.</summary>
    public sealed class MpProtectPayload : MpEffectPayload
    {
        public int Shield;
    }

    /// <summary>
    /// Barrier for the player and one partner. Both amounts are scaled by the Spirit of the player playing it.
    /// </summary>
    public sealed class MpProtectDefinition : LbolMpMultiplayerCardTemplate<MpProtectPayload>
    {
        public override IdContainer GetId() => nameof(MpProtect);

        public override CardConfig MakeConfig()
        {
            var config = DefaultConfig();
            config.Type = CardType.Defense;
            config.Rarity = Rarity.Uncommon;
            config.Owner = VanillaCharNames.Reimu;
            config.Colors = new List<ManaColor> { ManaColor.White };
            config.Cost = new ManaGroup { Any = 2, White = 1 };
            config.Shield = 12;
            config.UpgradedShield = 16;

            // Borrows the enemy selector.
            // PartyTargetPatches points it at a partner instead.
            config.TargetType = TargetType.SingleEnemy;

            config.RelativeEffects = new List<string> { nameof(MpPartner) };
            config.UpgradedRelativeEffects = new List<string> { nameof(MpPartner) };

            config.Illustrator = "";

            return config;
        }

        public override IEnumerable<BattleAction> Receive(
            MpProtectPayload payload, BattleController battle, int senderId)
        {
            // No cause, so only the sending player's Spirit counts.
            yield return new CastBlockShieldAction(battle.Player, 0, payload.Shield);
        }
    }

    [EntityLogic(typeof(MpProtectDefinition))]
    public sealed class MpProtect : Card, IMpPartnerTargeted
    {
        public override bool CanUse => MpPartyTargeting.AnyValidPartner;

        protected override IEnumerable<BattleAction> Actions(
            UnitSelector selector, ManaGroup consumingMana, Interaction precondition)
        {
            int shield = Battle.CalculateBlockShield(this, 0f, Shield.Shield).Item2;

            MpEffects.Send(Id, new MpProtectPayload { Shield = shield },
                MpEffectTarget.Partner, MpPartyTargeting.Consume());

            yield return DefenseAction();
        }
    }
}
