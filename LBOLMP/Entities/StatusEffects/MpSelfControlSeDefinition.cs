using System.Collections.Generic;
using LBOLMP.Net;
using LBoL.Base;
using LBoL.ConfigData;
using LBoL.Core;
using LBoL.Core.Battle;
using LBoL.Core.Battle.BattleActions;
using LBoL.Core.StatusEffects;
using LBoL.Core.Units;
using LBoL.EntityLib.StatusEffects.Koishi;
using LBoLEntitySideloader;
using LBoLEntitySideloader.Attributes;
using LBoLEntitySideloader.Entities;
using UnityEngine;

namespace LBOLMP.Entities.StatusEffects
{
    /// <summary>Whichever of the two the holder's mood earned this turn. Only one is ever set.</summary>
    public sealed class MpSelfControlPayload : MpEffectPayload
    {
        public int Firepower;
        public int Block;
    }

    /// <summary>
    /// At the end of the holder's turn, all players gain Firepower if the holder is in Passion,
    /// or Block if the holder is in Serenity.
    /// </summary>
    public sealed class MpSelfControlSeDefinition : LbolMpMultiplayerStatusEffectTemplate<MpSelfControlPayload>
    {
        public override IdContainer GetId() => nameof(MpSelfControlSe);

        /// <summary>The vanilla effect's icon is borrowed instead. See <see cref="MpSelfControlSe.OverrideIconName"/>.</summary>
        public override Sprite LoadSprite() => null;

        public override StatusEffectConfig MakeConfig()
        {
            var config = DefaultConfig();
            config.Type = StatusEffectType.Positive;
            config.HasLevel = true;
            config.HasCount = true;
            config.HasDuration = false;
            return config;
        }

        public override IEnumerable<BattleAction> Receive(
            MpSelfControlPayload payload, BattleController battle, int senderId)
        {
            if (payload.Block > 0)
            {
                // Direct and uncast, exactly as the vanilla effect gives it to its own holder, so
                // everybody ends up with the number the card promises rather than their own Spirit's
                // version of it.
                yield return new CastBlockShieldAction(
                    battle.Player, payload.Block, 0, BlockShieldType.Direct, false);
            }

            if (payload.Firepower > 0)
            {
                yield return new ApplyStatusEffectAction(
                    typeof(Firepower), battle.Player, payload.Firepower);
            }
        }
    }

    /// <inheritdoc cref="MpSelfControlSeDefinition"/>
    /// <remarks>
    /// The vanilla effect this replaces is the same thing without the sending. The mood that
    /// decides which half fires is the holder's, so the whole party gets whichever one they earned.
    /// </remarks>
    [EntityLogic(typeof(MpSelfControlSeDefinition))]
    public sealed class MpSelfControlSe : StatusEffect
    {
        /// <summary>The vanilla effect's own icon, resolved when it is drawn rather than at load.</summary>
        public override string OverrideIconName => nameof(SelfControlSe);

        protected override void OnAdded(Unit unit)
        {
            ReactOwnerEvent(Battle.Player.TurnEnding,
                new EventSequencedReactor<UnitEventArgs>(OnPlayerTurnEnding));
        }

        private IEnumerable<BattleAction> OnPlayerTurnEnding(UnitEventArgs args)
        {
            if (Battle.BattleShouldEnd)
            {
                yield break;
            }

            if (Battle.Player.HasStatusEffect<MoodPeace>())
            {
                NotifyActivating();
                yield return new CastBlockShieldAction(
                    Battle.Player, Count, 0, BlockShieldType.Direct, false);

                MpEffects.Send(Id, new MpSelfControlPayload { Block = Count },
                    MpEffectTarget.AllPartners);
            }
            else if (Battle.Player.HasStatusEffect<MoodPassion>())
            {
                NotifyActivating();
                yield return BuffAction<Firepower>(Level, 0, 0, 0, 0.2f);

                MpEffects.Send(Id, new MpSelfControlPayload { Firepower = Level },
                    MpEffectTarget.AllPartners);
            }
        }
    }
}
