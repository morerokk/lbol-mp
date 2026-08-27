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

        protected override void OnEnterBattle()
        {
            ReactBattleEvent(Battle.BattleStarted, new EventSequencedReactor<GameEventArgs>(OnBattleStarted));
            ReactBattleEvent(Battle.CardUsed, new EventSequencedReactor<CardUsingEventArgs>(OnCardUsed));
        }

        /// <summary>
        /// The sharing is just Offering to the Ownerless, handed out for free at the start of every
        /// fight, so the count is visible on the player and spends itself the same way.
        /// </summary>
        private IEnumerable<BattleAction> OnBattleStarted(GameEventArgs args)
        {
            int abilities = GameRun.CurrentStation.Act >= LateActFrom ? Value2 : Value1;

            NotifyActivating();
            yield return new ApplyStatusEffectAction(typeof(MpOfferingSe), Battle.Player, abilities);
        }

        /// <summary>
        /// Only a card played from the hand counts, for the same reason Offering to the Ownerless
        /// listens here: CardPlayed also fires for the copies a Partner is asking this player to
        /// play, and taxing people for each other's turns is not what the jade box says.
        /// </summary>
        private IEnumerable<BattleAction> OnCardUsed(CardUsingEventArgs args)
        {
            if (args.Card == null || args.Card.CardType != CardType.Ability || Battle.BattleShouldEnd)
            {
                yield break;
            }

            NotifyActivating();
            yield return new ApplyStatusEffectAction(typeof(MpAbilityPileSe), Battle.Player, Mana.Amount);
        }
    }
}
