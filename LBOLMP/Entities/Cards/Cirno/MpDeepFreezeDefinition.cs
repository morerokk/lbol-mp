using System.Collections.Generic;
using LBOLMP.Entities.StatusEffects;
using LBoL.Base;
using LBoL.ConfigData;
using LBoL.Core;
using LBoL.Core.Battle;
using LBoL.Core.Battle.BattleActions;
using LBoL.Core.Cards;
using LBoL.Core.StatusEffects;
using LBoL.EntityLib.StatusEffects.Cirno;
using LBoLEntitySideloader;
using LBoLEntitySideloader.Attributes;
using LBoLEntitySideloader.Entities;

namespace LBOLMP.Entities.Cards.Cirno
{
    public sealed class MpDeepFreezeDefinition : LbolMpCardTemplate, IMpOnlyCard
    {
        public override IdContainer GetId() => nameof(MpDeepFreeze);

        public override CardConfig MakeConfig()
        {
            var config = DefaultConfig();
            config.Type = CardType.Ability;
            config.Rarity = Rarity.Uncommon;
            config.Owner = VanillaCharNames.Cirno;
            config.Colors = new List<ManaColor> { ManaColor.Blue };
            config.Cost = new ManaGroup { Blue = 2 };
            config.UpgradedCost = new ManaGroup { Any = 1, Blue = 1 };
            config.TargetType = TargetType.Self;

            // How much Vulnerable should be applied when stacking cold.
            config.Value1 = 1;

            config.RelativeEffects = new List<string> { nameof(Cold), nameof(Vulnerable) };
            config.UpgradedRelativeEffects = new List<string> { nameof(Cold), nameof(Vulnerable) };

            config.Illustrator = "クシャビリア☆";

            return config;
        }
    }

    [EntityLogic(typeof(MpDeepFreezeDefinition))]
    public sealed class MpDeepFreeze : Card
    {
        protected override IEnumerable<BattleAction> Actions(
            UnitSelector selector, ManaGroup consumingMana, Interaction precondition)
        {
            yield return BuffAction<MpDeepFreezeSe>(Value1, 0, 0, 0, 0.2f);

            if (!IsUpgraded || Battle.BattleShouldEnd)
            {
                yield break;
            }

            foreach (var action in DebuffAction<Cold>(Battle.AllAliveEnemies, 0, 0, 0, 0, true, 0.03f))
            {
                yield return action;
            }
        }
    }
}
