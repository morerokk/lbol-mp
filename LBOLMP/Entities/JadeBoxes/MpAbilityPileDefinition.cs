using System.Collections.Generic;
using LBOLMP.Entities.StatusEffects;
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

namespace LBOLMP.Entities.JadeBoxes
{
    /// <summary>
    /// Ability Pile. Your first Ability card each combat is played for the whole party, and every
    /// Ability card you play makes the rest of them cost more.
    /// </summary>
    public sealed class MpAbilityPileDefinition : JadeBoxTemplate
    {
        public override IdContainer GetId() => nameof(MpAbilityPile);

        public override LocalizationOption LoadLocalization() => MpLocalization.JadeBoxes.AddEntity(this);

        public override JadeBoxConfig MakeConfig()
        {
            var config = DefaultConfig();

            // Abilities shared per combat, before and after the halfway point of the run.
            config.Value1 = 1;
            config.Value2 = 2;

            // What each Ability card played adds to the cost of the rest.
            config.Mana = ManaGroup.Anys(1);

            config.RelativeEffects = new List<string>
            {
                nameof(MpPartner), nameof(MpOfferingSe), nameof(MpAbilityPileSe)
            };
            return config;
        }
    }

    /// <inheritdoc cref="MpAbilityPileDefinition"/>
    [EntityLogic(typeof(MpAbilityPileDefinition))]
    public sealed class MpAbilityPile : JadeBox
    {
        /// <summary>The act from which the party gets the larger share.</summary>
        private const int LateActFrom = 3;

        /// <summary>How many of this combat's Ability cards are shared, and so go untaxed.</summary>
        private int _shared;

        /// <summary>Ability cards played from hand so far this combat.</summary>
        private int _played;

        protected override void OnEnterBattle()
        {
            // Stage.Level, not Station.Act: the latter counts the segments of one act's map, so it
            // climbs from 1 to 3 during every act and picks which enemy pool a node draws from.
            int act = GameRun.CurrentStage?.Level ?? 1;
            _shared = act >= LateActFrom ? Value2 : Value1;
            _played = 0;

            ReactBattleEvent(Battle.BattleStarted, new EventSequencedReactor<GameEventArgs>(OnBattleStarted));
            ReactBattleEvent(Battle.CardUsed, new EventSequencedReactor<CardUsingEventArgs>(OnCardUsed));
        }

        /// <summary>
        /// The sharing is just Offering to the Ownerless, added at the start of every fight.
        /// </summary>
        private IEnumerable<BattleAction> OnBattleStarted(GameEventArgs args)
        {
            NotifyActivating();
            yield return new ApplyStatusEffectAction(typeof(MpOfferingSe), Battle.Player, _shared);
        }

        /// <summary>
        /// Only a card played from the hand counts, to avoid accidental back-and-forths.
        /// </summary>
        private IEnumerable<BattleAction> OnCardUsed(CardUsingEventArgs args)
        {
            if (args.Card == null || args.Card.CardType != CardType.Ability || Battle.BattleShouldEnd)
            {
                yield break;
            }

            _played++;
            if (_played <= _shared)
            {
                yield break;
            }

            // We ran out of free plays, time to tax abilities more
            NotifyActivating();
            yield return new ApplyStatusEffectAction(typeof(MpAbilityPileSe), Battle.Player, Mana.Amount);
        }
    }
}
