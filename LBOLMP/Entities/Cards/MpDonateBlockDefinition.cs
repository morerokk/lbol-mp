using System.Collections.Generic;
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
using LBoLEntitySideloader.Resource;

namespace LBOLMP.Entities.Cards
{
    /// <summary>How much Block the partner is getting.</summary>
    public sealed class MpDonateBlockPayload : MpEffectPayload
    {
        public int Block;
    }

    /// <summary>
    /// Give one partner Block.
    ///
    /// This is worth playing after somebody has already ended their turn: the enemy cannot move
    /// until the whole party has finished its player phase, and the enemy-turn gate keeps draining
    /// replicated work while it waits, so the Block always lands before anything swings at them.
    /// </summary>
    public sealed class MpDonateBlockDefinition : MpCardTemplate<MpDonateBlockPayload>
    {
        private static DirectorySource _source;

        private static DirectorySource Source => _source ?? (_source = new DirectorySource(MpInfo.Guid, ""));

        public override IdContainer GetId() => nameof(MpDonateBlock);

        public override LocalizationOption LoadLocalization()
        {
            var files = new LocalizationFiles(Source, Locale.En);
            files.AddLocaleFile(Locale.En, "Resources/CardsEn.yaml");
            files.AddLocaleFile(Locale.ZhHans, "Resources/CardsZhHans.yaml");
            files.AddLocaleFile(Locale.ZhHant, "Resources/CardsZhHant.yaml");
            files.AddLocaleFile(Locale.Ja, "Resources/CardsJa.yaml");
            return files;
        }

        // No art yet. Swap for the usual AutoLoad once Resources/MpDonateBlock.png exists:
        //     var images = new CardImages(Source);
        //     images.AutoLoad(this, extension: ".png");
        //     return images;
        public override CardImages LoadCardImages() => null;

        public override CardConfig MakeConfig()
        {
            var config = DefaultConfig();
            config.Type = CardType.Defense;
            config.Rarity = Rarity.Common;

            // Not in the reward pool. These are useless outside a lobby, so they need an
            // MP-only gate before they can be handed out normally.
            config.IsPooled = false;
            config.Colors = new List<ManaColor> { ManaColor.White };
            config.Cost = new ManaGroup { White = 1 };
            config.Block = 10;
            config.UpgradedBlock = 14;

            // Borrowed for the pick-a-target arrow, which no other target type gives us.
            // PartyTargetPatches points it at the party instead of the enemies.
            config.TargetType = TargetType.SingleEnemy;
            return config;
        }

        public override IEnumerable<BattleAction> Receive(
            MpDonateBlockPayload payload, BattleController battle, int senderId)
        {
            // Left without a cause on purpose, like every other replicated action. Spirit only
            // reacts to Card, Us and OnlyCalculate, so the number the sender already worked out
            // does not get boosted a second time by the receiver's own buffs.
            yield return new CastBlockShieldAction(battle.Player, payload.Block, 0);
        }
    }

    [EntityLogic(typeof(MpDonateBlockDefinition))]
    public sealed class MpDonateBlock : Card, IMpPartnerTargeted
    {
        /// <summary>Unplayable with nobody to give it to, rather than fizzling and wasting the mana.</summary>
        public override bool CanUse => MpPartyTargeting.AnyValidPartner;

        protected override IEnumerable<BattleAction> Actions(
            UnitSelector selector, ManaGroup consumingMana, Interaction precondition)
        {
            // What we would have gained ourselves, so our Spirit, Grace and Fragil all count.
            // Card.Block is only the printed number; the modifiers live on the gaining unit's
            // BlockShieldGaining event, and we never gain anything here to trigger them.
            int block = Battle.CalculateBlockShield(this, Block.Block, 0f).Item1;

            MpEffects.Send(Id, new MpDonateBlockPayload { Block = block }, MpEffectTarget.Partner,
                MpPartyTargeting.Consume());
            yield break;
        }
    }
}
