using System.Collections.Generic;
using LBOLMP.Net;
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

namespace LBOLMP.Entities.Cards
{
    /// <summary>How much Block the partner is getting, and how many times.</summary>
    public sealed class MpDonateBlockPayload : MpEffectPayload
    {
        public int Block;
        public int Times;
    }

    /// <summary>
    /// Give one partner Block immediately. Scaled by the Spirit of the player playing it.
    /// </summary>
    public sealed class MpDonateBlockDefinition : LbolMpMultiplayerCardTemplate<MpDonateBlockPayload>
    {
        public override IdContainer GetId() => nameof(MpDonateBlock);

        public override CardConfig MakeConfig()
        {
            var config = DefaultConfig();
            config.Type = CardType.Defense;
            config.Rarity = Rarity.Common;
            config.Colors = new List<ManaColor> { ManaColor.White };
            config.Cost = new ManaGroup { White = 1 };
            config.UpgradedCost = new ManaGroup { Any = 1 };
            config.Block = 8;
            config.UpgradedBlock = 6;

            // How many separate instances of Block.
            // An upgrade gives 6x2 instead of 8, which lets it benefit from Spirit more.
            config.Value1 = 1;
            config.UpgradedValue1 = 2;

            config.Illustrator = "Tuck坦";

            // Set to TargetType.SingleEnemy just so we can borrow the selector logic.
            // PartyTargetPatches points it at a partner instead of enemies.
            config.TargetType = TargetType.SingleEnemy;

            config.RelativeEffects = new List<string> { nameof(MpPartner) };
            config.UpgradedRelativeEffects = new List<string> { nameof(MpPartner) };
            return config;
        }

        public override IEnumerable<BattleAction> Receive(
            MpDonateBlockPayload payload, BattleController battle, int senderId)
        {
            // Intentionally doesn't have a cause, because only the caster's Spirit/Divine Favor is taken into account for this.
            // This is applied N times to trigger "on block gained" effects
            for (int i = 0; i < payload.Times; i++)
            {
                yield return new CastBlockShieldAction(battle.Player, payload.Block, 0);
            }
        }
    }

    [EntityLogic(typeof(MpDonateBlockDefinition))]
    public sealed class MpDonateBlock : Card, IMpPartnerTargeted
    {
        // Can't be played if there are no valid partners to use it on
        public override bool CanUse => MpPartyTargeting.AnyValidPartner;

        protected override IEnumerable<BattleAction> Actions(
            UnitSelector selector, ManaGroup consumingMana, Interaction precondition)
        {
            // Calculate the block *we* would have gotten from this, but then send it over the network instead.
            // This makes Spirit etc, work.
            int block = Battle.CalculateBlockShield(this, Block.Block, 0f).Item1;

            MpEffects.Send(Id, new MpDonateBlockPayload { Block = block, Times = Value1 },
                MpEffectTarget.Partner, MpPartyTargeting.Consume());
            yield break;
        }
    }
}
