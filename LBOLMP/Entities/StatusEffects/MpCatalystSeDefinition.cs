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
using LBoL.EntityLib.Cards.Character.Marisa;
using LBoL.EntityLib.StatusEffects.Marisa;
using LBoLEntitySideloader;
using LBoLEntitySideloader.Attributes;
using LBoLEntitySideloader.Entities;
using UnityEngine;

namespace LBOLMP.Entities.StatusEffects
{
    public sealed class MpCatalystPayload : MpEffectPayload
    {
        public int Potions;
    }

    public sealed class MpCatalystSeDefinition : LbolMpMultiplayerStatusEffectTemplate<MpCatalystPayload>
    {
        public override IdContainer GetId() => nameof(MpCatalystSe);

        public override StatusEffectConfig MakeConfig()
        {
            var config = DefaultConfig();
            config.Type = StatusEffectType.Positive;
            config.HasLevel = true;
            config.HasDuration = false;
            return config;
        }

        public override IEnumerable<BattleAction> Receive(
            MpCatalystPayload payload, BattleController battle, int senderId)
        {
            if (payload.Potions <= 0 || battle.BattleShouldEnd)
            {
                yield break;
            }

            yield return new AddCardsToDrawZoneAction(
                Library.CreateCards<Potion>(payload.Potions, false),
                DrawZoneTarget.Random,
                AddCardsType.Normal);
        }
    }

    [EntityLogic(typeof(MpCatalystSeDefinition))]
    public sealed class MpCatalystSe : StatusEffect
    {
        protected override void OnAdded(Unit unit)
        {
            HandleOwnerEvent(Battle.CardsAddedToHand, OnCardsAdded);
            HandleOwnerEvent(Battle.CardsAddedToDiscard, OnCardsAdded);
            HandleOwnerEvent(Battle.CardsAddedToDrawZone, OnCardsAddedToDrawZone);
        }

        private void OnCardsAdded(CardsEventArgs args) => Deliver(args.Cards, args.ActionSource);

        private void OnCardsAddedToDrawZone(CardsAddingToDrawZoneEventArgs args)
            => Deliver(args.Cards, args.ActionSource);

        private void Deliver(IReadOnlyList<Card> cards, GameEntity source)
        {
            // Networked potions have no source. To avoid infinite loops, any potions we get over the network don't count.
            // Potions created by cards or status effects work.
            if (source == null || Battle.BattleShouldEnd || cards == null || !MpEffects.CanSend)
            {
                return;
            }

            int sending = cards.Count(card => card is Potion) * Level;
            var partners = MpEffects.ValidPartners.ToList();
            if (sending <= 0 || partners.Count == 0)
            {
                return;
            }

            NotifyActivating();

            var shares = new Dictionary<int, int>();
            for (int i = 0; i < sending; i++)
            {
                int playerId = partners.Sample(GameRun.BattleRng).PlayerId;
                shares.TryGetValue(playerId, out int share);
                shares[playerId] = share + 1;
            }

            foreach (var share in shares)
            {
                MpEffects.Send(Id, new MpCatalystPayload { Potions = share.Value },
                    MpEffectTarget.Partner, share.Key);
            }
        }
    }
}
