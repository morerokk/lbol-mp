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
using LBoL.Core.Battle.Interactions;
using LBoL.Core.Cards;
using LBoLEntitySideloader;
using LBoLEntitySideloader.Attributes;
using LBoLEntitySideloader.Entities;
using LBoLEntitySideloader.Resource;

namespace LBOLMP.Entities.Cards
{
    public sealed class MpIntrusiveThoughtDefinition : MpCardTemplate<MpProxyCardPayload>
    {
        private static DirectorySource _source;

        private static DirectorySource Source => _source ?? (_source = new DirectorySource(MpInfo.Guid, ""));

        public override IdContainer GetId() => nameof(MpIntrusiveThought);

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
            config.Owner = VanillaCharNames.Koishi;
            config.Colors = new List<ManaColor> { ManaColor.Black, ManaColor.Blue };
            config.Cost = new ManaGroup { Any = 1 } + ManaGroup.Hybrids(1, ManaColor.Black, ManaColor.Blue);
            config.UpgradedCost = ManaGroup.Hybrids(1, ManaColor.Black, ManaColor.Blue);

            config.Keywords = Keyword.Exile;
            config.UpgradedKeywords = Keyword.None;

            // Set to TargetType.SingleEnemy just so we can borrow the selector logic.
            // PartyTargetPatches points it at a partner instead of enemies.
            config.TargetType = TargetType.SingleEnemy;

            config.RelativeEffects = new List<string> { nameof(MpPartner) };
            config.UpgradedRelativeEffects = new List<string> { nameof(MpPartner) };
            config.RelativeKeyword = Keyword.Copy | Keyword.Tool;
            config.UpgradedRelativeKeyword = Keyword.Copy | Keyword.Tool;
            return config;
        }

        public override IEnumerable<BattleAction> Receive(
            MpProxyCardPayload payload, BattleController battle, int senderId)
            => payload.Play(senderId);
    }

    [EntityLogic(typeof(MpIntrusiveThoughtDefinition))]
    public sealed class MpIntrusiveThought : Card, IMpPartnerTargeted
    {
        /// <summary>The only other card in the hand, when there is no choice to make.</summary>
        private Card _onlyOther;

        /// <summary>The partner picked with the arrow.</summary>
        private int _partner = MpConstants.InvalidPlayerId;

        // Can't be played if there are no valid partners
        public override bool CanUse => MpPartyTargeting.AnyValidPartner;

        /// <summary>Makes it count for "if a card was discarded this turn" effects.</summary>
        public override bool DiscardCard => true;

        public override Interaction Precondition()
        {
            // Partner is immediately specified here rather than in Actions, so that the hand panel doesn't mess it up.
            _partner = MpPartyTargeting.Consume();

            // Like Lost in Paradise, this cannot target Copies and Tools.
            var others = Battle.HandZone
                .Where(card => card != this && !card.IsCopy && card.CardType != CardType.Tool)
                .ToList();
            if (others.Count == 1)
            {
                _onlyOther = others[0];
            }

            return others.Count <= 1 ? null : new SelectHandInteraction(1, 1, others);
        }

        protected override IEnumerable<BattleAction> Actions(
            UnitSelector selector, ManaGroup consumingMana, Interaction precondition)
        {
            Card chosen = null;
            if (precondition is SelectHandInteraction select)
            {
                chosen = select.SelectedCards[0];
            }
            else if (_onlyOther != null)
            {
                chosen = _onlyOther;
                _onlyOther = null;
            }

            if (chosen == null)
            {
                yield break;
            }

            var payload = new MpProxyCardPayload { CardId = chosen.Id, Upgraded = chosen.IsUpgraded };

            // Like Lost in Paradise, if the card is an Ability or has Exile, it is turned into a Copy to avoid shenanigans.
            if (chosen.IsExile || chosen.CardType == CardType.Ability)
            {
                chosen.IsCopy = true;
            }

            yield return new DiscardAction(chosen);

            MpEffects.Send(Id, payload, MpEffectTarget.Partner, _partner);
            _partner = MpConstants.InvalidPlayerId;
        }
    }
}
