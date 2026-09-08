using System.Collections.Generic;
using LBOLMP.Entities.StatusEffects;
using LBOLMP.Net;
using LBoL.Base;
using LBoL.ConfigData;
using LBoL.Core;
using LBoL.Core.Battle;
using LBoL.Core.Battle.BattleActions;
using LBoL.Core.Cards;
using LBoL.Core.StatusEffects;
using LBoLEntitySideloader;
using LBoLEntitySideloader.Attributes;

namespace LBOLMP.Entities.Cards.Marisa
{
    public sealed class MpOverchargePayload : MpEffectPayload
    {
        public int TempFirepower;
    }

    public sealed class MpOverchargeDefinition : LbolMpMultiplayerCardTemplate<MpOverchargePayload>
    {
        public override IdContainer GetId() => nameof(MpOvercharge);

        public override CardConfig MakeConfig()
        {
            var config = DefaultConfig();
            config.Type = CardType.Skill;
            config.Rarity = Rarity.Uncommon;
            config.Owner = VanillaCharNames.Marisa;
            config.Colors = new List<ManaColor> { ManaColor.Red };
            config.Cost = new ManaGroup { Any = 1, Red = 1 };
            config.TargetType = TargetType.Self;

            config.Keywords = Keyword.Overdrive;
            config.UpgradedKeywords = Keyword.Overdrive;

            // How much Temporary Firepower everyone gets.
            config.Value1 = 3;
            config.UpgradedValue1 = 4;
            // Value2 is the Overdrive cost
            config.Value2 = 2;
            config.UpgradedValue2 = 2;

            config.RelativeEffects = new List<string> { nameof(MpPartner), nameof(TempFirepower) };
            config.UpgradedRelativeEffects = new List<string> { nameof(MpPartner), nameof(TempFirepower) };

            config.Illustrator = "Rina里奈";

            return config;
        }

        public override IEnumerable<BattleAction> Receive(
            MpOverchargePayload payload, BattleController battle, int senderId)
        {
            if (payload.TempFirepower <= 0 || battle.BattleShouldEnd)
            {
                yield break;
            }

            yield return new ApplyStatusEffectAction<TempFirepower>(
                battle.Player, payload.TempFirepower, occupationTime: 0.2f);
        }
    }

    [EntityLogic(typeof(MpOverchargeDefinition))]
    public sealed class MpOvercharge : Card
    {
        protected override IEnumerable<BattleAction> Actions(
            UnitSelector selector, ManaGroup consumingMana, Interaction precondition)
        {
            yield return BuffAction<TempFirepower>(Value1, 0, 0, 0, 0.2f);

            // Also give firepower to all partners with Overdrive
            if (!Overdrive(Value2))
            {
                yield break;
            }

            yield return OverdriveAction(Value2);

            MpEffects.Send(Id, new MpOverchargePayload { TempFirepower = Value1 }, MpEffectTarget.AllPartners);
        }
    }
}
