using System.Collections.Generic;
using System.Linq;
using LBOLMP.Entities.StatusEffects;
using LBOLMP.Net;
using LBOLMP.Session.Battle;
using LBoL.Base;
using LBoL.ConfigData;
using LBoL.Core;
using LBoL.Core.Battle;
using LBoL.Core.Battle.BattleActions;
using LBoL.Core.Cards;
using LBoL.EntityLib.StatusEffects.Cirno;
using LBoLEntitySideloader;
using LBoLEntitySideloader.Attributes;
using LBoLEntitySideloader.Entities;

namespace LBOLMP.Entities.Cards
{
    public sealed class MpDefibrillatorPayload : MpEffectPayload
    {
        public int LifePercent;
        public int ImmuneTurns;
    }

    /// <summary>
    /// Rare Tool card that revives every downed player with 50% HP.
    /// This also gives them the Ice Block effect so they don't immediately die again afterwards.
    /// </summary>
    public sealed class MpDefibrillatorDefinition
        : LbolMpMultiplayerCardTemplate<MpDefibrillatorPayload>, IMpReachesDownedPlayers
    {
        public override IdContainer GetId() => nameof(MpDefibrillator);

        public override CardConfig MakeConfig()
        {
            var config = DefaultConfig();
            config.Type = CardType.Tool;
            config.Rarity = Rarity.Rare;
            config.Cost = ManaGroup.Empty;
            config.TargetType = TargetType.Nobody;
            // This isn't being released yet due to translations and other things
            config.DebugLevel = 3;

            config.Colors = new List<ManaColor>();

            config.ToolPlayableTimes = 1;

            config.Value1 = 50; // Revival HP in percentage
            config.Value2 = 1; // How many turns they're immune for

            config.RelativeEffects = new List<string> { nameof(MpPartner) };
            config.UpgradedRelativeEffects = new List<string> { nameof(MpPartner) };
            return config;
        }

        public override IEnumerable<BattleAction> Receive(
            MpDefibrillatorPayload payload, BattleController battle, int senderId)
        {
            if (battle.BattleShouldEnd)
            {
                yield break;
            }

            yield return new MpDeferredAction(b => Revive(b, payload));
        }

        private static IEnumerable<BattleAction> Revive(
            BattleController battle, MpDefibrillatorPayload payload)
        {
            bool up = MpSafe.Run("MpDefibrillator.Revive",
                () => MpDownedPlayers.ReviveInBattle(payload.LifePercent / 100f), false);

            if (!up || battle.BattleShouldEnd)
            {
                yield break;
            }

            yield return new ApplyStatusEffectAction(
                typeof(Immune), battle.Player, null, payload.ImmuneTurns, null, null, 0.2f, true);
        }
    }

    [EntityLogic(typeof(MpDefibrillatorDefinition))]
    public sealed class MpDefibrillator : Card
    {
        public override bool CanUse =>
            MpEffects.CanSend && MpBattleSync.AllSeats.Any(seat => seat.Down);

        protected override IEnumerable<BattleAction> Actions(
            UnitSelector selector, ManaGroup consumingMana, Interaction precondition)
        {
            MpEffects.Send(Id, new MpDefibrillatorPayload
            {
                LifePercent = Value1,
                ImmuneTurns = Value2
            }, MpEffectTarget.AllPartners);

            yield break;
        }
    }
}
