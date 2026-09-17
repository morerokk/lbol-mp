using System.Collections.Generic;
using System.Linq;
using LBOLMP.Api;
using LBOLMP.Net;
using LBOLMP.Session;
using LBoL.Base;
using LBoL.ConfigData;
using LBoL.Core;
using LBoL.Core.Battle;
using LBoL.Core.Battle.BattleActions;
using LBoL.Core.StatusEffects;
using LBoL.Core.Units;
using LBoLEntitySideloader;
using LBoLEntitySideloader.Attributes;
using UnityEngine;

namespace LBOLMP.Entities.StatusEffects
{
    public sealed class MpMimicSeDefinition : LbolMpStatusEffectTemplate
    {
        public override IdContainer GetId() => nameof(MpMimicSe);

        // TODO: Give this its own icon.
        public override Sprite LoadSprite() => null;

        public override StatusEffectConfig MakeConfig()
        {
            var config = DefaultConfig();
            config.Type = StatusEffectType.Positive;
            config.HasLevel = true;
            config.HasDuration = false;

            return config;
        }
    }

    /// <summary>
    /// Plays a copy of the next X cards one partner plays.
    /// </summary>
    /// <remarks>
    /// This has one instance per partner. Status effects can only stack if they refer to the same partner.
    /// </remarks>
    [EntityLogic(typeof(MpMimicSeDefinition))]
    public sealed class MpMimicSe : StatusEffect
    {
        /// <summary>
        /// The player being copied. You should set this *before* the status is applied.
        /// </summary>
        public int PartnerId { get; set; } = MpConstants.InvalidPlayerId;

        public string PartnerName => MpSession.Get(PartnerId)?.Name ?? "?";

        protected override void OnAdded(Unit unit)
        {
            ReactOwnerEvent(MpBattleEvents.PartnerCardPlayed(Battle),
                new EventSequencedReactor<MpPartnerCardEventArgs>(OnPartnerCardPlayed));
        }

        /// <summary>
        /// This can only stack with status effects matching the same partner.
        /// </summary>
        public override bool Stack(StatusEffect other)
        {
            if (!(other is MpMimicSe incoming) || incoming.PartnerId == PartnerId)
            {
                return base.Stack(other);
            }

            var sibling = Owner?.StatusEffects
                .OfType<MpMimicSe>()
                .FirstOrDefault(effect => effect != this && effect.PartnerId == incoming.PartnerId);

            return sibling != null && sibling.Stack(other);
        }

        private IEnumerable<BattleAction> OnPartnerCardPlayed(MpPartnerCardEventArgs args)
        {
            // Tokens are skipped! Otherwise, two players copying each other would loop infinitely back and forth.
            if (args.PlayerId != PartnerId || args.IsToken || Level <= 0 || Battle.BattleShouldEnd)
            {
                yield break;
            }

            var play = args.PlayCopy();
            if (play == null)
            {
                yield break;
            }

            NotifyActivating();
            Level -= 1;

            yield return play;

            if (Level <= 0)
            {
                yield return new RemoveStatusEffectAction(this, true, 0.1f);
            }
        }
    }
}
