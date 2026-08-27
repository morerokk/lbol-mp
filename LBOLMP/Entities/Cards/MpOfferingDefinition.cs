using System.Collections.Generic;
using LBoL.Base;
using LBoL.ConfigData;
using LBoL.Core;
using LBoL.Core.Battle;
using LBoL.Core.Battle.BattleActions;
using LBoL.Core.Cards;
using LBoLEntitySideloader;
using LBoLEntitySideloader.Attributes;
using LBoLEntitySideloader.Entities;
using LBOLMP.Entities.StatusEffects;

namespace LBOLMP.Entities.Cards
{
    /// <summary>
    /// Offering to the Ownerless. Adds <see cref="MpOfferingSe"/>, which does the actual work.
    /// </summary>
    /// Note: this is not extended from LbolMpMultiplayerCardTemplate because it does not actually immediately send a network message when played.
    /// It just adds a status effect, and that status effect will actually do something over the network.
    public sealed class MpOfferingDefinition : LbolMpCardTemplate, IMpOnlyCard
    {
        public override IdContainer GetId() => nameof(MpOffering);

        public override CardConfig MakeConfig()
        {
            var config = DefaultConfig();
            config.Type = CardType.Skill;
            config.Rarity = Rarity.Rare;
            config.Colors = new List<ManaColor> { ManaColor.Black, ManaColor.Blue, ManaColor.White };
            config.Cost = new ManaGroup { Any = 1, Black = 1, Blue = 1, White = 1 };
            config.TargetType = TargetType.Self;
            config.Keywords = Keyword.Exile | Keyword.Ethereal;
            config.Value1 = 1;
            config.UpgradedValue1 = 2;
            config.RelativeEffects = new List<string> { nameof(MpPartner) };
            config.UpgradedRelativeEffects = new List<string> { nameof(MpPartner) };
            config.Illustrator = "Sya烙";
            return config;
        }
    }

    [EntityLogic(typeof(MpOfferingDefinition))]
    public sealed class MpOffering : Card
    {
        protected override IEnumerable<BattleAction> Actions(
            UnitSelector selector, ManaGroup consumingMana, Interaction precondition)
        {
            yield return new ApplyStatusEffectAction(typeof(MpOfferingSe), Battle.Player, Value1);
        }
    }
}
