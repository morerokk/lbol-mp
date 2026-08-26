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
    /// How many cards the other player draws after the swap.
    /// </summary>
    public sealed class MpRewindPayload : MpEffectPayload
    {
        public int Cards;
    }

    public sealed class MpRewindDefinition : MpCardTemplate<MpRewindPayload>
    {
        private static DirectorySource _source;

        private static DirectorySource Source => _source ?? (_source = new DirectorySource(MpInfo.Guid, ""));

        public override IdContainer GetId() => nameof(MpRewind);

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
            config.Owner = VanillaCharNames.Sakuya;
            config.Colors = new List<ManaColor> { ManaColor.Black, ManaColor.Blue };
            config.Cost = new ManaGroup { Black = 1, Blue = 1 };
            config.UpgradedCost = ManaGroup.Hybrids(1, ManaColor.Black, ManaColor.Blue);

            // How many cards they draw off the reordered pile.
            config.Value1 = 2;
            config.UpgradedValue1 = 3;
            config.Keywords = Keyword.Exile;

            // Set to TargetType.SingleEnemy just so we can borrow the selector logic.
            // PartyTargetPatches points it at a partner instead of enemies.
            config.TargetType = TargetType.SingleEnemy;

            config.RelativeEffects = new List<string> { nameof(MpPartner) };
            config.UpgradedRelativeEffects = new List<string> { nameof(MpPartner) };
            return config;
        }

        public override IEnumerable<BattleAction> Receive(
            MpRewindPayload payload, BattleController battle, int senderId)
        {
            if (battle.BattleShouldEnd)
            {
                yield break;
            }

            var wasDrawn = battle.DrawZone.Reverse<Card>().ToList();
            var wasDiscarded = battle.DiscardZone.ToList();

            foreach (var card in wasDrawn)
            {
                yield return new MoveCardAction(card, CardZone.Discard);
            }

            foreach (var card in wasDiscarded)
            {
                yield return new MoveCardToDrawZoneAction(card, DrawZoneTarget.Top);
            }

            if (payload.Cards > 0)
            {
                yield return new DrawManyCardAction(payload.Cards);
            }
        }
    }

    [EntityLogic(typeof(MpRewindDefinition))]
    public sealed class MpRewind : Card, IMpPartnerTargeted
    {
        // Can't be played if there are no valid partners to rewind for
        public override bool CanUse => MpPartyTargeting.AnyValidPartner;

        protected override IEnumerable<BattleAction> Actions(
            UnitSelector selector, ManaGroup consumingMana, Interaction precondition)
        {
            MpEffects.Send(Id, new MpRewindPayload { Cards = Value1 },
                MpEffectTarget.Partner, MpPartyTargeting.Consume());
            yield break;
        }
    }
}
