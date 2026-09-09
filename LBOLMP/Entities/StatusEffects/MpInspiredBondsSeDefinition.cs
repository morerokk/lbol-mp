using System.Collections.Generic;
using System.Linq;
using LBOLMP.Net;
using LBoL.Base;
using LBoL.Base.Extensions;
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
    /// <summary>How much Unity each partner is giving.</summary>
    public sealed class MpInspiredBondsPayload : MpEffectPayload
    {
        public int Unity;
    }

    public sealed class MpInspiredBondsSeDefinition : LbolMpMultiplayerStatusEffectTemplate<MpInspiredBondsPayload>
    {
        public override IdContainer GetId() => nameof(MpInspiredBondsSe);

        // TODO: Give this its own icon.
        public override Sprite LoadSprite() => null;

        public override StatusEffectConfig MakeConfig()
        {
            var config = DefaultConfig();
            config.Type = StatusEffectType.Positive;

            config.HasLevel = true;
            config.HasDuration = false;

            return config;
        }

        public override IEnumerable<BattleAction> Receive(
            MpInspiredBondsPayload payload, BattleController battle, int senderId)
        {
            if (payload.Unity <= 0 || battle.BattleShouldEnd)
            {
                yield break;
            }

            int unity = payload.Unity;
            yield return new MpDeferredAction(b => GrantUnity(b, unity));
        }

        private static IEnumerable<BattleAction> GrantUnity(BattleController battle, int unity)
        {
            if (battle.BattleShouldEnd)
            {
                return null;
            }

            // A maxed out teammate would waste the Unity, so they are not preferred.
            var teammates = battle.HandZone
                .Where(card => card.CardType == CardType.Friend && card.Loyalty < GlobalConfig.MaxLoyalty)
                .ToList();
            if (teammates.Count == 0)
            {
                return null;
            }

            var fed = teammates.Sample(battle.GameRun.BattleRng);
            fed.NotifyActivating();
            fed.Loyalty += unity;

            return null;
        }
    }

    [EntityLogic(typeof(MpInspiredBondsSeDefinition))]
    public sealed class MpInspiredBondsSe : StatusEffect
    {
        protected override void OnAdded(Unit unit)
        {
            // Should ONLY be on CardUsed, to avoid potential infinite back-and-forth loops!
            HandleOwnerEvent(Battle.CardUsed, OnCardUsed);
        }

        private void OnCardUsed(CardUsingEventArgs args)
        {
            var card = args.Card;

            // Summoning a teammate is not using one of their skills, so skip that.
            if (card == null || card.CardType != CardType.Friend || card.Summoning
                || Battle.BattleShouldEnd || !MpEffects.CanSend)
            {
                return;
            }

            NotifyActivating();

            MpEffects.Send(Id, new MpInspiredBondsPayload { Unity = Level }, MpEffectTarget.AllPartners);
        }
    }
}
