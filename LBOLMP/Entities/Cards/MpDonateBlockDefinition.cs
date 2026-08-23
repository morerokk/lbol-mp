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
using LBoLEntitySideloader.Resource;

namespace LBOLMP.Entities.Cards
{
    /// <summary>How much Block the partner is getting.</summary>
    public sealed class MpDonateBlockPayload : MpEffectPayload
    {
        public int Block;
    }

    /// <summary>
    /// Give one partner Block immediately. Scaled by the Spirit of the player playing it.
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
            //files.AddLocaleFile(Locale.ZhHans, "Resources/CardsZhHans.yaml");
            //files.AddLocaleFile(Locale.ZhHant, "Resources/CardsZhHant.yaml");
            //files.AddLocaleFile(Locale.Ja, "Resources/CardsJa.yaml");
            return files;
        }

        public override CardImages LoadCardImages()
        {
            var images = new CardImages(Source);
            images.AutoLoad(this, extension: ".png", relativePath: "Resources/Cards/");
            return images;
        }

        public override CardConfig MakeConfig()
        {
            var config = DefaultConfig();
            config.Type = CardType.Defense;
            config.Rarity = Rarity.Common;
            config.Colors = new List<ManaColor> { ManaColor.White };
            config.Cost = new ManaGroup { White = 1 };
            config.UpgradedCost = new ManaGroup { Any = 1 };
            config.Block = 8;
            config.UpgradedBlock = 12;
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
            // Intentionally doesn't have a cause, because only the caster's Spirit/Divine Favor is taken into account for this
            yield return new CastBlockShieldAction(battle.Player, payload.Block, 0);
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

            MpEffects.Send(Id, new MpDonateBlockPayload { Block = block }, MpEffectTarget.Partner,
                MpPartyTargeting.Consume());
            yield break;
        }
    }
}
