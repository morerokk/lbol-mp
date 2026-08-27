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

        public override LocalizationOption LoadLocalization() => MpLocalization.Cards.AddEntity(this);

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

            // Deferred, because the piles have to be read at the moment the swap happens rather
            // than now, when the receiver may still be several actions away from getting to it.
            yield return new MpDeferredAction(SwapPiles);

            // Needs no deferring: this one already reads the draw pile as it resolves, by which
            // point the swap above has happened.
            if (payload.Cards > 0)
            {
                yield return new DrawManyCardAction(payload.Cards);
            }
        }

        /// <summary>
        /// Turn the two piles inside out, the way Back to the Future does.
        /// </summary>
        private static IEnumerable<BattleAction> SwapPiles(BattleController battle)
        {
            var wasDrawn = battle.DrawZone.Reverse<Card>().ToList();
            var wasDiscarded = battle.DiscardZone.ToList();

            // Back to the Future's own guards, working here for the same reason they work there:
            // these resolve one at a time, so a card something else has moved in between is left
            // where it went instead of being dragged out of it.
            foreach (var card in wasDrawn)
            {
                if (card.Zone == CardZone.Draw)
                {
                    yield return new MoveCardAction(card, CardZone.Discard);
                }
            }

            foreach (var card in wasDiscarded)
            {
                if (card.Zone == CardZone.Discard)
                {
                    yield return new MoveCardToDrawZoneAction(card, DrawZoneTarget.Top);
                }
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
