using System.Collections.Generic;
using LBOLMP.Net;
using LBoL.Base;
using LBoL.ConfigData;
using LBoL.Core;
using LBoL.Core.Battle;
using LBoL.Core.Battle.BattleActions;
using LBoL.Core.StatusEffects;
using LBoL.Core.Units;
using LBoLEntitySideloader;
using LBoLEntitySideloader.Attributes;
using LBoLEntitySideloader.Entities;
using LBoLEntitySideloader.Resource;
using UnityEngine;

namespace LBOLMP.Entities.StatusEffects
{
    /// <summary>
    /// The next Ability card you play is also played for all your Partners.
    /// </summary>
    public sealed class MpOfferingSeDefinition : MpStatusEffectTemplate<MpProxyCardPayload>
    {
        private static DirectorySource _source;

        private static DirectorySource Source => _source ?? (_source = new DirectorySource(MpInfo.Guid, ""));

        public override IdContainer GetId() => nameof(MpOfferingSe);

        public override LocalizationOption LoadLocalization() => MpLocalization.StatusEffects.AddEntity(this);

        public override Sprite LoadSprite() => ResourceLoader.LoadSprite("Resources/StatusEffects/MpOfferingSe.png", Source);

        public override StatusEffectConfig MakeConfig()
        {
            var config = DefaultConfig();
            config.Type = StatusEffectType.Positive;
            config.HasLevel = true;
            config.HasDuration = false;
            return config;
        }

        public override IEnumerable<BattleAction> Receive(
            MpProxyCardPayload payload, BattleController battle, int senderId)
            => payload.Play(senderId);
    }

    /// <summary>
    /// The next Ability card you play is also played for all your Partners.
    /// </summary>
    [EntityLogic(typeof(MpOfferingSeDefinition))]
    public sealed class MpOfferingSe : StatusEffect
    {
        protected override void OnAdded(Unit unit)
        {
            // Should ONLY be on CardUsed, to avoid potential infinite back-and-forth loops!
            // CardPlayed also fires for free plays and the copies that a Partner is asking the local player to play.
            ReactOwnerEvent(Battle.CardUsed, new EventSequencedReactor<CardUsingEventArgs>(OnCardUsed));
        }

        private IEnumerable<BattleAction> OnCardUsed(CardUsingEventArgs args)
        {
            if (args.Card == null || args.Card.CardType != CardType.Ability
                || Battle.BattleShouldEnd || Level <= 0)
            {
                yield break;
            }

            NotifyActivating();
            args.AddModifier(this);

            MpEffects.Send(Id,
                new MpProxyCardPayload { CardId = args.Card.Id, Upgraded = args.Card.IsUpgraded },
                MpEffectTarget.AllPartners);

            Level -= 1;
            if (Level <= 0)
            {
                yield return new RemoveStatusEffectAction(this, true, 0.1f);
            }
        }
    }
}
