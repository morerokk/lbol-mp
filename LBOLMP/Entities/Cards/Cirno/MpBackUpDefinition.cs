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

namespace LBOLMP.Entities.Cards.Cirno
{
    /// <summary>Which teammate is being copied over.</summary>
    public sealed class MpBackUpPayload : MpEffectPayload
    {
        public string CardId;
        public bool Upgraded;
    }

    public sealed class MpBackUpDefinition : LbolMpMultiplayerCardTemplate<MpBackUpPayload>
    {
        /// <summary>What the teammate costs once it's put in a partner's hand.</summary>
        internal static readonly ManaGroup PassedCost = ManaGroup.Empty;

        public override IdContainer GetId() => nameof(MpBackUp);

        public override CardConfig MakeConfig()
        {
            var config = DefaultConfig();
            config.Type = CardType.Skill;
            config.Rarity = Rarity.Uncommon;
            config.Owner = VanillaCharNames.Cirno;
            config.Colors = new List<ManaColor> { ManaColor.Green };
            config.Cost = new ManaGroup { Any = 1, Green = 1 };
            config.UpgradedCost = new ManaGroup { Any = 1 };
            config.Keywords = Keyword.Exile;
            config.UpgradedKeywords = Keyword.Exile;

            // The mana tooltip
            config.Mana = PassedCost;

            // Set to TargetType.SingleEnemy just so we can borrow the selector logic.
            // PartyTargetPatches points it at a partner instead of enemies.
            config.TargetType = TargetType.SingleEnemy;

            config.RelativeEffects = new List<string> { nameof(MpPartner) };
            config.UpgradedRelativeEffects = new List<string> { nameof(MpPartner) };
            config.RelativeKeyword = Keyword.FriendCard | Keyword.TempMorph | Keyword.Ethereal;
            config.UpgradedRelativeKeyword = config.RelativeKeyword;

            config.Illustrator = "さるかな";

            return config;
        }

        public override IEnumerable<BattleAction> Receive(
            MpBackUpPayload payload, BattleController battle, int senderId)
        {
            if (battle.BattleShouldEnd || string.IsNullOrEmpty(payload.CardId))
            {
                yield break;
            }

            var receivedCard = Library.TryCreateCard(payload.CardId, payload.Upgraded);
            if (receivedCard == null)
            {
                MpPlugin.Log.LogWarning($"Player {senderId} sent over '{payload.CardId}', which is an unknown card");
                yield break;
            }

            receivedCard.SetTurnCost(PassedCost);
            receivedCard.IsEthereal = true;

            yield return new AddCardsToHandAction(new[] { receivedCard });
        }
    }

    [EntityLogic(typeof(MpBackUpDefinition))]
    public sealed class MpBackUp : Card, IMpPartnerTargeted
    {
        /// <summary>The only teammate in the hand, if there's only 1 choice.</summary>
        private Card _onlyTeammate;

        /// <summary>The partner picked with the arrow.</summary>
        private int _partner = MpConstants.InvalidPlayerId;

        public override bool CanUse => MpPartyTargeting.AnyValidPartner && AnyTeammateInHand;

        private bool AnyTeammateInHand =>
            Battle != null && Battle.HandZone.Any(card => card.CardType == CardType.Friend);

        public override Interaction Precondition()
        {
            // Partner is immediately specified here rather than in Actions, so that the hand panel doesn't mess it up.
            _partner = MpPartyTargeting.Consume();

            var teammates = Battle.HandZone.Where(card => card.CardType == CardType.Friend).ToList();
            _onlyTeammate = teammates.Count == 1 ? teammates[0] : null;

            return teammates.Count <= 1 ? null : new SelectHandInteraction(1, 1, teammates);
        }

        protected override IEnumerable<BattleAction> Actions(
            UnitSelector selector, ManaGroup consumingMana, Interaction precondition)
        {
            Card chosen = null;
            if (precondition is SelectHandInteraction select)
            {
                chosen = select.SelectedCards.FirstOrDefault();
            }
            else if (_onlyTeammate != null)
            {
                chosen = _onlyTeammate;
                _onlyTeammate = null;
            }

            if (chosen == null || _partner == MpConstants.InvalidPlayerId)
            {
                yield break;
            }

            chosen.NotifyActivating();

            MpEffects.Send(Id, new MpBackUpPayload { CardId = chosen.Id, Upgraded = chosen.IsUpgraded },
                MpEffectTarget.Partner, _partner);
            _partner = MpConstants.InvalidPlayerId;
            yield break;
        }
    }
}
