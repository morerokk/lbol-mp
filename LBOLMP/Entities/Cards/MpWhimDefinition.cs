using System.Collections.Generic;
using System.Linq;
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
using LBoLEntitySideloader.Resource;

namespace LBOLMP.Entities.Cards
{
    /// <summary>
    /// How many cards off the top of their draw pile get played.
    /// </summary>
    public sealed class MpWhimPayload : MpEffectPayload
    {
        public int Cards;
    }

    /// <summary>
    /// Make another player play the top 1-2 cards of their draw pile.
    /// </summary>
    public sealed class MpWhimDefinition : MpCardTemplate<MpWhimPayload>
    {
        private static DirectorySource _source;

        private static DirectorySource Source => _source ?? (_source = new DirectorySource(MpInfo.Guid, ""));

        public override IdContainer GetId() => nameof(MpWhim);

        public override LocalizationOption LoadLocalization()
        {
            var files = new LocalizationFiles(Source, Locale.En);
            files.AddLocaleFile(Locale.En, "Resources/CardsEn.yaml");
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
            config.Type = CardType.Skill;
            config.Rarity = Rarity.Uncommon;
            config.Owner = VanillaCharNames.Koishi;
            config.Colors = new List<ManaColor> { ManaColor.Green };
            config.Cost = new ManaGroup { Any = 1, Green = 1 };

            // How many cards off the top of their deck get played.
            config.Value1 = 1;
            config.UpgradedValue1 = 2;

            // Set to TargetType.SingleEnemy just so we can borrow the selector logic.
            // PartyTargetPatches points it at a partner instead of enemies.
            config.TargetType = TargetType.SingleEnemy;

            config.RelativeEffects = new List<string> { nameof(MpPartner) };
            config.UpgradedRelativeEffects = new List<string> { nameof(MpPartner) };
            config.Illustrator = "あまにわ";
            return config;
        }

        public override IEnumerable<BattleAction> Receive(
            MpWhimPayload payload, BattleController battle, int senderId)
        {
            if (payload.Cards <= 0 || battle.BattleShouldEnd)
            {
                yield break;
            }

            // One deferred action per card, rather than a list of cards taken now. Reading the top
            // of the pile here reads it before any of this runs, which both hands us the same card
            // twice and risks playing one that has since been drawn into their hand.
            for (int i = 0; i < payload.Cards; i++)
            {
                yield return new MpDeferredAction(PlayTopCard);
            }
        }

        /// <summary>
        /// Play whatever is on top of the draw pile at the moment this runs.
        /// </summary>
        private static IEnumerable<BattleAction> PlayTopCard(BattleController battle)
        {
            if (battle.BattleShouldEnd)
            {
                yield break;
            }

            var card = battle.DrawZone.FirstOrDefault();
            if (card != null)
            {
                // No IsPlayTwiceToken, since it's their own card.
                yield return new PlayCardAction(card);
            }
        }
    }

    [EntityLogic(typeof(MpWhimDefinition))]
    public sealed class MpWhim : Card, IMpPartnerTargeted
    {
        // Can't be played if there are no valid partners to do this to
        public override bool CanUse => MpPartyTargeting.AnyValidPartner;

        protected override IEnumerable<BattleAction> Actions(
            UnitSelector selector, ManaGroup consumingMana, Interaction precondition)
        {
            MpEffects.Send(Id, new MpWhimPayload { Cards = Value1 }, MpEffectTarget.Partner,
                MpPartyTargeting.Consume());
            yield break;
        }
    }
}
