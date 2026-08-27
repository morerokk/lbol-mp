using LBoL.Base;
using LBoL.ConfigData;
using LBoL.Core;
using LBoL.Core.Battle;
using LBoL.Core.Battle.BattleActions;
using LBoL.Core.Cards;
using LBoL.EntityLib.Cards.Character.Cirno;
using LBoL.EntityLib.Cards.Character.Koishi;
using LBoL.EntityLib.StatusEffects.Koishi;
using LBoLEntitySideloader;
using LBoLEntitySideloader.Attributes;
using LBoLEntitySideloader.Entities;
using LBoLEntitySideloader.Resource;
using LBOLMP.Entities.StatusEffects;
using System.Collections.Generic;

namespace LBOLMP.Entities.Cards
{
    /// <summary>
    /// The multiplayer replacement for Koishi's Anatta card.
    /// The vanilla one is hidden in multiplayer runs by <see cref="Session.MpCardAvailability"/>,
    /// so only one of the two is ever findable.
    /// </summary>
    public sealed class MpAnattaDefinition : LbolMpCardTemplate, IMpOnlyCard
    {
        public override IdContainer GetId() => nameof(MpAnatta);

        /// <summary>Borrows the vanilla card's art.</summary>
        public override CardImages LoadCardImages() => BorrowVanillaArt(nameof(Anatta));

        public override CardConfig MakeConfig()
        {
            var config = DefaultConfig();
            config.Type = CardType.Ability;
            config.Rarity = Rarity.Uncommon;
            config.Owner = VanillaCharNames.Koishi;
            config.Colors = new List<ManaColor> { ManaColor.White };
            config.Cost = new ManaGroup { Any = 2, White = 1 };
            config.UpgradedCost = new ManaGroup { Any = 1, White = 1 };
            config.TargetType = TargetType.Self;

            config.Mana = MpAnattaSeDefinition.RewardPerStack;
            config.RelativeEffects = new List<string> { nameof(MoodPeace), nameof(MpPartner) };
            config.UpgradedRelativeEffects = new List<string> { nameof(MoodPeace), nameof(MpPartner) };
            config.ImageId = nameof(Anatta);
            config.Illustrator = "Tuck坦";
            return config;
        }
    }

    [EntityLogic(typeof(MpAnattaDefinition))]
    public sealed class MpAnatta : Card
    {
        protected override IEnumerable<BattleAction> Actions(
            UnitSelector selector, ManaGroup consumingMana, Interaction precondition)
        {
            yield return BuffAction<MpAnattaSe>(1, 0, 0, 0, 0.2f);
            yield return BuffAction<MoodPeace>(0, 0, 0, 0, 0.2f);
        }
    }
}
