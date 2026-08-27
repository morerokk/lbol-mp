using System.Collections.Generic;
using System.Linq;
using LBOLMP.Entities.StatusEffects;
using LBoL.Base;
using LBoL.ConfigData;
using LBoL.Core;
using LBoL.Core.Battle;
using LBoL.Core.Battle.Interactions;
using LBoL.Core.Cards;
using LBoL.EntityLib.Cards.Character.Koishi;
using LBoL.EntityLib.StatusEffects.Koishi;
using LBoLEntitySideloader;
using LBoLEntitySideloader.Attributes;
using LBoLEntitySideloader.Entities;
using LBoLEntitySideloader.Resource;

namespace LBOLMP.Entities.Cards
{
    /// <summary>
    /// The multiplayer replacement for Koishi's Self-Fulfillment card.
    /// The vanilla one is hidden in multiplayer runs by <see cref="Session.MpCardAvailability"/>.
    /// </summary>
    public sealed class MpSelfControlDefinition : LbolMpCardTemplate, IMpOnlyCard
    {
        public override IdContainer GetId() => nameof(MpSelfControl);

        /// <summary>Borrows the vanilla card's art.</summary>
        public override CardImages LoadCardImages() => BorrowVanillaArt(nameof(SelfControl));

        public override CardConfig MakeConfig()
        {
            var config = DefaultConfig();
            config.Type = CardType.Ability;
            config.Owner = VanillaCharNames.Koishi;
            config.TargetType = TargetType.Self;

            config.Rarity = Rarity.Rare;
            config.Colors = new List<ManaColor> { ManaColor.Green };
            config.Cost = new ManaGroup { Any = 4, Green = 1 };

            // Firepower in Passion, and Block in Serenity. Vanilla passes these to the effect as
            // Level and Count respectively, which is what its two halves read.
            config.Value1 = 3;
            config.Value2 = 9;
            config.UpgradedValue1 = 4;
            config.UpgradedValue2 = 12;

            config.RelativeEffects = new List<string>
            {
                nameof(MoodPassion), nameof(MoodPeace), nameof(MpPartner)
            };
            config.UpgradedRelativeEffects = new List<string>
            {
                nameof(MoodPassion), nameof(MoodPeace), nameof(MpPartner)
            };

            config.ImageId = nameof(SelfControl);
            return config;
        }
    }

    /// <inheritdoc cref="MpSelfControlDefinition"/>
    [EntityLogic(typeof(MpSelfControlDefinition))]
    public sealed class MpSelfControl : Card
    {
        public override Interaction Precondition()
        {
            var options = Library.CreateCards<MpSelfControl>(2, IsUpgraded).ToList();

            options[0].ChoiceCardIndicator = 1;
            options[1].ChoiceCardIndicator = 2;
            options[0].SetBattle(Battle);
            options[1].SetBattle(Battle);

            return new MiniSelectCardInteraction(options, false, false, false);
        }

        protected override IEnumerable<BattleAction> Actions(
            UnitSelector selector, ManaGroup consumingMana, Interaction precondition)
        {
            var chosen = (precondition as MiniSelectCardInteraction)?.SelectedCard;
            if (chosen != null)
            {
                yield return chosen.ChoiceCardIndicator == 1
                    ? BuffAction<MoodPassion>(0, 0, 0, 0, 0.2f)
                    : BuffAction<MoodPeace>(0, 0, 0, 0, 0.2f);
            }

            yield return BuffAction<MpSelfControlSe>(Value1, 0, 0, Value2, 0.2f);
        }
    }
}
