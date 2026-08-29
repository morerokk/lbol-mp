using System.Collections.Generic;
using LBOLMP.Entities.StatusEffects;
using LBOLMP.Net;
using LBOLMP.Session.Battle;
using LBoL.Base;
using LBoL.ConfigData;
using LBoL.Core;
using LBoL.Core.Battle;
using LBoL.Core.Battle.BattleActions;
using LBoL.Core.Cards;
using LBoL.Core.StatusEffects;
using LBoLEntitySideloader;
using LBoLEntitySideloader.Attributes;
using LBoLEntitySideloader.Entities;

namespace LBOLMP.Entities.Cards
{
    /// <summary>
    /// How many extra turns the partner is getting.
    /// </summary>
    /// Is this ever really higher than 1? Maybe.
    public sealed class MpTimeDilationPayload : MpEffectPayload
    {
        public int Turns;
    }

    public sealed class MpTimeDilationDefinition : LbolMpMultiplayerCardTemplate<MpTimeDilationPayload>
    {
        public override IdContainer GetId() => nameof(MpTimeDilation);

        public override CardConfig MakeConfig()
        {
            var config = DefaultConfig();
            config.Type = CardType.Skill;
            config.Owner = VanillaCharNames.Sakuya;

            config.Rarity = Rarity.Uncommon;
            config.Colors = new List<ManaColor> { ManaColor.Blue };
            config.Cost = new ManaGroup { Any = 3, Blue = 2 };
            config.UpgradedCost = new ManaGroup { Any = 2, Blue = 2 };

            config.Keywords = Keyword.Exile;
            config.UpgradedKeywords = Keyword.Exile;

            // How many extra turns the partner gets.
            config.Value1 = 1;
            config.UpgradedValue1 = 1;

            // Set to TargetType.SingleEnemy just so we can borrow the selector logic.
            // PartyTargetPatches points it at a partner instead of enemies.
            config.TargetType = TargetType.SingleEnemy;

            config.RelativeEffects = new List<string> { nameof(MpPartner), nameof(ExtraTurn) };
            config.UpgradedRelativeEffects = new List<string> { nameof(MpPartner), nameof(ExtraTurn) };

            config.Illustrator = "DEINLOJR";

            return config;
        }

        public override IEnumerable<BattleAction> Receive(
            MpTimeDilationPayload payload, BattleController battle, int senderId)
        {
            if (payload.Turns <= 0)
            {
                yield break;
            }

            int turns = payload.Turns;
            yield return new MpDeferredAction(b => GrantExtraTurn(b, turns));
        }

        private static IEnumerable<BattleAction> GrantExtraTurn(BattleController battle, int turns)
        {
            if (battle.BattleShouldEnd)
            {
                yield break;
            }

            // Play the effect
            yield return PerformAction.Effect(battle.Player, "ExtraTime");
            yield return PerformAction.Sfx("ExtraTurnLaunch");

            yield return new ApplyStatusEffectAction<ExtraTurn>(
                battle.Player, turns, occupationTime: 0.2f);
        }
    }

    [EntityLogic(typeof(MpTimeDilationDefinition))]
    public sealed class MpTimeDilation : Card, IMpPartnerTargeted
    {
        public override bool CanUse => MpPartyTargeting.AnyValidPartner;

        protected override IEnumerable<BattleAction> Actions(
            UnitSelector selector, ManaGroup consumingMana, Interaction precondition)
        {
            MpEffects.Send(Id, new MpTimeDilationPayload { Turns = Value1 },
                MpEffectTarget.Partner, MpPartyTargeting.Consume());

            // Same closer as Luna Dial, minus the extra turn for ourselves.
            yield return new RequestEndPlayerTurnAction();
        }
    }
}
