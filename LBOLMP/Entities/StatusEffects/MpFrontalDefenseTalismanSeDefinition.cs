using System;
using System.Collections.Generic;
using LBOLMP.Net;
using LBoL.Base;
using LBoL.Base.Extensions;
using LBoL.ConfigData;
using LBoL.Core;
using LBoL.Core.Battle;
using LBoL.Core.Battle.BattleActions;
using LBoL.Core.StatusEffects;
using LBoL.Core.Units;
using LBoLEntitySideloader;
using LBoLEntitySideloader.Attributes;
using LBoLEntitySideloader.Entities;
using LBoLEntitySideloader.Resource;
using UnityEngine;

namespace LBOLMP.Entities.StatusEffects
{
    /// <summary>How much Block each partner is getting.</summary>
    public sealed class MpFrontalDefenseTalismanPayload : MpEffectPayload
    {
        public int Block;
    }

    /// <summary>
    /// Whenever you gain Barrier from a card, all other players gain half that much Block.
    /// </summary>
    public sealed class MpFrontalDefenseTalismanSeDefinition : MpStatusEffectTemplate<MpFrontalDefenseTalismanPayload>
    {
        private static DirectorySource _source;

        private static DirectorySource Source => _source ?? (_source = new DirectorySource(MpInfo.Guid, ""));

        public override IdContainer GetId() => nameof(MpFrontalDefenseTalismanSe);

        public override LocalizationOption LoadLocalization()
        {
            var files = new LocalizationFiles(Source, Locale.En);
            files.AddLocaleFile(Locale.En, "Resources/StatusEffectsEn.yaml");
            return files;
        }

        /// <summary>Borrowed from Resilient until this gets an icon of its own.</summary>
        public override Sprite LoadSprite() => ResourceLoader.LoadSprite("Resources/StatusEffects/MpResilient.png", Source);

        public override StatusEffectConfig MakeConfig()
        {
            var config = DefaultConfig();
            config.Type = StatusEffectType.Positive;
            config.HasLevel = false;
            config.HasDuration = false;
            config.Keywords = Keyword.Shield | Keyword.Block;
            return config;
        }

        public override IEnumerable<BattleAction> Receive(
            MpFrontalDefenseTalismanPayload payload, BattleController battle, int senderId)
        {
            if (payload.Block <= 0 || battle.BattleShouldEnd)
            {
                yield break;
            }

            // Deliberately causeless to avoid feedback loops (this is why the ability only counts Barrier gained from cards)
            yield return new CastBlockShieldAction(battle.Player, payload.Block, 0);
        }
    }

    /// <summary>
    /// Whenever you gain Barrier from a card, all other players gain half that much Block.
    /// </summary>
    [EntityLogic(typeof(MpFrontalDefenseTalismanSeDefinition))]
    public sealed class MpFrontalDefenseTalismanSe : StatusEffect
    {
        protected override void OnAdded(Unit unit)
        {
            // Gained rather than Gaining, so the number also takes all other potential statuses into account.
            HandleOwnerEvent(unit.BlockShieldGained, OnBlockShieldGained);
        }

        private void OnBlockShieldGained(BlockShieldEventArgs args)
        {
            // Only counts Barrier that a card produced, to avoid loops with The Great Hakurei Barrier.
            if (args.Cause != ActionCause.Card || args.Shield <= 0f || Battle.BattleShouldEnd)
            {
                return;
            }

            int block = (args.Shield * 0.5f).RoundToInt(MidpointRounding.AwayFromZero);
            if (block <= 0)
            {
                return;
            }

            NotifyActivating();
            MpEffects.Send(Id, new MpFrontalDefenseTalismanPayload { Block = block },
                MpEffectTarget.AllPartners);
        }
    }
}
