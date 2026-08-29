using System.Collections.Generic;
using System.Linq;
using LBOLMP.Entities.StatusEffects;
using LBOLMP.Net;
using LBOLMP.Session;
using LBOLMP.Session.Battle;
using LBoL.Base;
using LBoL.ConfigData;
using LBoL.Core;
using LBoL.Core.Battle;
using LBoL.Core.Battle.BattleActions;
using LBoL.Core.Battle.Interactions;
using LBoL.Core.Cards;
using LBoL.Presentation;
using LBoLEntitySideloader;
using LBoLEntitySideloader.Attributes;
using LBoLEntitySideloader.Entities;

namespace LBOLMP.Entities.Cards
{
    /// <summary>
    /// Take a copy of something a partner has exiled.
    /// </summary>
    /// <remarks>
    /// Their pile is fetched over the network rather than mirrored continuously, so the chooser
    /// opens a moment after the card resolves rather than as a Precondition.
    /// (Sorry!)
    /// </remarks>
    public sealed class MpDivergingTimePeekDefinition : LbolMpCardTemplate, IMpOnlyCard
    {
        public override IdContainer GetId() => nameof(MpDivergingTimePeek);

        public override CardConfig MakeConfig()
        {
            var config = DefaultConfig();
            config.Type = CardType.Skill;
            config.Owner = VanillaCharNames.Sakuya;

            config.Rarity = Rarity.Uncommon;
            config.Colors = new List<ManaColor> { ManaColor.White, ManaColor.Green };
            config.Cost = new ManaGroup { Any = 1, White = 1, Green = 1 };
            config.UpgradedCost = new ManaGroup { Any = 1 } + ManaGroup.Hybrids(1, ManaColor.White, ManaColor.Green);

            config.Keywords = Keyword.Exile;
            config.UpgradedKeywords = Keyword.Exile;

            // Set to TargetType.SingleEnemy just so we can borrow the selector logic.
            // PartyTargetPatches points it at a partner instead of enemies.
            config.TargetType = TargetType.SingleEnemy;

            config.RelativeEffects = new List<string> { nameof(MpPartner) };
            config.UpgradedRelativeEffects = new List<string> { nameof(MpPartner) };
            // The cost the taken card is given, and what the description shows.
            config.Mana = ManaGroup.Empty;

            config.RelativeKeyword = Keyword.Copy | Keyword.Ethereal | Keyword.TempMorph;
            config.UpgradedRelativeKeyword = Keyword.Copy | Keyword.Ethereal | Keyword.TempMorph;

            config.Illustrator = "幻騒アぽろ";

            return config;
        }
    }

    /// <inheritdoc cref="MpDivergingTimePeekDefinition"/>
    [EntityLogic(typeof(MpDivergingTimePeekDefinition))]
    public sealed class MpDivergingTimePeek : Card, IMpPartnerTargeted
    {
        /// <summary>Kept for the answer, which arrives long after the arrow is gone.</summary>
        private int _partner = MpConstants.InvalidPlayerId;

        public override bool CanUse => MpPartyTargeting.AnyValidPartner;

        protected override IEnumerable<BattleAction> Actions(
            UnitSelector selector, ManaGroup consumingMana, Interaction precondition)
        {
            _partner = MpPartyTargeting.Consume();
            MpExilePeek.Request(_partner, Offer);
            yield break;
        }

        /// <summary>
        /// Runs when the partner sends us back their exile pile, which is some time after this card finished resolving.
        /// </summary>
        private void Offer(List<Card> exile)
        {
            var battle = GameMaster.Instance?.CurrentGameRun?.Battle;

            // Like Lost in Paradise and Intrusive Thought, Copies and Tools cannot be taken.
            var takeable = exile.Where(card => !card.IsCopy && card.CardType != CardType.Tool).ToList();
            if (battle == null || takeable.Count == 0)
            {
                return;
            }

            MpBattleSync.QueueReplicated(battle, new MpDeferredAction(b => Take(b, takeable)),
                nameof(MpDivergingTimePeek));
        }

        private IEnumerable<BattleAction> Take(BattleController battle, List<Card> exile)
        {
            if (battle.BattleShouldEnd)
            {
                yield break;
            }

            var interaction = new SelectCardInteraction(1, 1, exile, SelectedCardHandling.DoNothing)
            {
                Source = this
            };
            yield return new InteractionAction(interaction, false);

            var chosen = interaction.SelectedCards.Count > 0 ? interaction.SelectedCards[0] : null;
            if (chosen == null)
            {
                yield break;
            }

            if (chosen.IsExile || chosen.CardType == CardType.Ability)
            {
                MpExilePeek.MarkCopy(_partner, chosen.Id, chosen.IsUpgraded);
            }

            _partner = MpConstants.InvalidPlayerId;

            // Ours is always a Copy, so it can't be fed back into another one of these.
            chosen.IsCopy = true;
            
            // Make it free so you can actually play it, my bad
            chosen.SetTurnCost(Mana);
            chosen.IsExile = true;
            chosen.IsEthereal = true;

            // Relax dude, it's no longer in Exile, don't throw an exception over it
            chosen.Battle = null;
            chosen.Zone = CardZone.None;

            // It's ours now, so its text should read with our name on it rather than theirs.
            MpCardOwner.Set(chosen, MpNet.LocalPlayerId);

            yield return new AddCardsToHandAction(chosen);
        }
    }
}
