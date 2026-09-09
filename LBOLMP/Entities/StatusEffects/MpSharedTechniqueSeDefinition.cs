using System.Collections.Generic;
using LBOLMP.Net;
using LBoL.Base;
using LBoL.ConfigData;
using LBoL.Core;
using LBoL.Core.Battle;
using LBoL.Core.Battle.BattleActions;
using LBoL.Core.Cards;
using LBoL.Core.StatusEffects;
using LBoL.Core.Units;
using LBoLEntitySideloader;
using LBoLEntitySideloader.Attributes;
using UnityEngine;

namespace LBOLMP.Entities.StatusEffects
{
    /// <summary>
    /// The next X attack cards you play this turn are also played for all your Partners.
    /// </summary>
    public sealed class MpSharedTechniqueSeDefinition : LbolMpMultiplayerStatusEffectTemplate<MpProxyCardPayload>
    {
        public override IdContainer GetId() => nameof(MpSharedTechniqueSe);

        // TODO: Give this its own icon.
        public override Sprite LoadSprite() => null;

        public override StatusEffectConfig MakeConfig()
        {
            var config = DefaultConfig();
            config.Type = StatusEffectType.Positive;

            config.HasLevel = true;
            config.HasDuration = true;
            config.DurationDecreaseTiming = DurationDecreaseTiming.TurnEnd;

            return config;
        }

        public override IEnumerable<BattleAction> Receive(
            MpProxyCardPayload payload, BattleController battle, int senderId)
            => payload.Play(senderId);
    }

    /// <summary>
    /// The next X attack cards you play this turn are also played for all your Partners.
    /// </summary>
    [EntityLogic(typeof(MpSharedTechniqueSeDefinition))]
    public sealed class MpSharedTechniqueSe : StatusEffect
    {
        protected override void OnAdded(Unit unit)
        {
            // Should ONLY be on CardUsed, to avoid potential infinite back-and-forth loops!
            ReactOwnerEvent(Battle.CardUsed, new EventSequencedReactor<CardUsingEventArgs>(OnCardUsed));
        }

        private IEnumerable<BattleAction> OnCardUsed(CardUsingEventArgs args)
        {
            if (args.Card == null || args.Card.CardType != CardType.Attack
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
