using System.Collections.Generic;
using LBoL.Base;
using LBoL.ConfigData;
using LBoL.Core;
using LBoL.Core.Cards;
using LBoL.Core.StatusEffects;
using LBoL.Core.Units;
using LBoL.EntityLib.StatusEffects.ExtraTurn;
using LBoLEntitySideloader;
using LBoLEntitySideloader.Attributes;
using LBoLEntitySideloader.Entities;
using LBoLEntitySideloader.Resource;
using UnityEngine;

namespace LBOLMP.Entities.StatusEffects
{
    /// <summary>
    /// Ability cards cost one more per level.
    /// Only added by the Ability Pile jade box.
    /// </summary>
    public sealed class MpAbilityPileSeDefinition : StatusEffectTemplate
    {
        public override IdContainer GetId() => nameof(MpAbilityPileSe);

        public override LocalizationOption LoadLocalization() => MpLocalization.StatusEffects.AddEntity(this);

        /// <summary>Time Limit's icon is borrowed instead. See <see cref="MpAbilityPileSe.OverrideIconName"/>.</summary>
        public override Sprite LoadSprite() => null;

        public override StatusEffectConfig MakeConfig()
        {
            var config = DefaultConfig();

            // Cannot be removed with debuff cleanses or amulet
            config.Type = StatusEffectType.Special;
            config.HasLevel = true;
            config.HasDuration = false;
            return config;
        }
    }

    /// <summary>
    /// Ability cards cost one more per level, for the rest of the combat.
    /// </summary>
    /// Time Limit but filtered to Ability cards only.
    /// Also doesn't lose levels like Time Limit does when the enemy gets a turn.
    [EntityLogic(typeof(MpAbilityPileSeDefinition))]
    public sealed class MpAbilityPileSe : StatusEffect
    {
        public ManaGroup Mana => ManaGroup.Anys(Level);

        public override string OverrideIconName => nameof(TimeIsLimited);

        protected override void OnAdded(Unit unit)
        {
            Charge(Battle.EnumerateAllCards(), Level);

            HandleOwnerEvent(Battle.CardsAddedToHand, new GameEventHandler<CardsEventArgs>(OnCardsAdded));
            HandleOwnerEvent(Battle.CardsAddedToDiscard, new GameEventHandler<CardsEventArgs>(OnCardsAdded));
            HandleOwnerEvent(Battle.CardsAddedToExile, new GameEventHandler<CardsEventArgs>(OnCardsAdded));
            HandleOwnerEvent(Battle.CardsAddedToDrawZone,
                new GameEventHandler<CardsAddingToDrawZoneEventArgs>(OnCardsAddedToDrawZone));
            HandleOwnerEvent(Battle.CardTransformed, new GameEventHandler<CardTransformEventArgs>(OnCardTransformed));
        }

        /// <summary>Charge for the levels being added, on top of what everything already pays.</summary>
        public override bool Stack(StatusEffect other)
        {
            bool stacked = base.Stack(other);
            if (stacked)
            {
                Charge(Battle.EnumerateAllCards(), other.Level);
            }

            return stacked;
        }

        protected override void OnRemoved(Unit unit)
        {
            if (Battle != null)
            {
                Charge(Battle.EnumerateAllCards(), -Level);
            }
        }

        private void OnCardsAdded(CardsEventArgs args) => Charge(args.Cards, Level);

        private void OnCardsAddedToDrawZone(CardsAddingToDrawZoneEventArgs args) => Charge(args.Cards, Level);

        private void OnCardTransformed(CardTransformEventArgs args)
            => Charge(new[] { args.DestinationCard }, Level);

        private static void Charge(IEnumerable<Card> cards, int amount)
        {
            if (amount == 0)
            {
                return;
            }

            var mana = ManaGroup.Anys(amount);
            foreach (var card in cards)
            {
                if (card.CardType == CardType.Ability)
                {
                    card.AuraCost += mana;
                }
            }
        }
    }
}
