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
using LBoLEntitySideloader.Resource;
using UnityEngine;

namespace LBOLMP.Entities.StatusEffects
{
    /// <summary>How much rainbow mana everybody gets.</summary>
    public sealed class MpAnattaPayload : MpEffectPayload
    {
        public int Rainbow;
    }

    /// <summary>
    /// Whenever you exit Serenity, all players gain N rainbow mana.
    /// </summary>
    public sealed class MpAnattaSeDefinition : MpStatusEffectTemplate<MpAnattaPayload>
    {
        internal static ManaGroup RewardPerStack => ManaGroup.Philosophies(1);

        private static DirectorySource _source;

        private static DirectorySource Source => _source ?? (_source = new DirectorySource(MpInfo.Guid, ""));

        public override IdContainer GetId() => nameof(MpAnattaSe);

        public override LocalizationOption LoadLocalization()
        {
            var files = new LocalizationFiles(Source, Locale.En);
            files.AddLocaleFile(Locale.En, "Resources/StatusEffectsEn.yaml");
            return files;
        }

        /// <summary>
        /// Falls back to the Resilient icon until Resources/StatusEffects/MpAnattaSe.png exists,
        /// so the status HUD always has something to draw. Drop the fallback once the art lands.
        /// </summary>
        public override Sprite LoadSprite() =>
            ResourceLoader.LoadSprite("Resources/StatusEffects/MpAnattaSe.png", Source)
            ?? ResourceLoader.LoadSprite("Resources/StatusEffects/MpResilient.png", Source);

        public override StatusEffectConfig MakeConfig()
        {
            var config = DefaultConfig();
            config.Type = StatusEffectType.Positive;

            // One rainbow per copy played. The vanilla card this replaces cannot stack at all,
            // which is most of why nobody takes it. LevelStackType is StackType.Add by default.
            config.HasLevel = true;
            config.HasDuration = false;
            return config;
        }

        public override IEnumerable<BattleAction> Receive(
            MpAnattaPayload payload, BattleController battle, int senderId)
        {
            if (payload.Rainbow <= 0)
            {
                yield break;
            }

            yield return new GainManaAction(ManaGroup.Philosophies(payload.Rainbow));
        }
    }

    /// <summary>
    /// Whenever you exit Serenity, all players gain 1 rainbow mana.
    /// </summary>
    /// <remarks>
    /// The vanilla card this replaces works differently: UpgradePeace is an empty marker class and
    /// MoodPeace checks for it to decide whether exiting Serenity gains 3 or 4 P mana.
    /// Vanilla is also not stackable, but this is.
    /// </remarks>
    [EntityLogic(typeof(MpAnattaSeDefinition))]
    public sealed class MpAnattaSe : StatusEffect
    {
        /// <summary>What {Mana} renders as in this effect's description, the way Serenity does it.</summary>
        public ManaGroup Mana => ManaGroup.Philosophies(HasLevel ? Level : 1);

        protected override void OnAdded(Unit unit)
        {
            ReactOwnerEvent(unit.StatusEffectRemoved,
                new EventSequencedReactor<StatusEffectEventArgs>(OnStatusEffectRemoved));
        }

        private IEnumerable<BattleAction> OnStatusEffectRemoved(StatusEffectEventArgs args)
        {
            if (!(args.Effect is MoodPeace) || Level <= 0 || Battle == null || Battle.BattleShouldEnd)
            {
                yield break;
            }

            NotifyActivating();

            // P mana for the local player right now
            yield return new GainManaAction(Mana);
            // And then P mana for everyone else once this arrives
            MpEffects.Send(Id, new MpAnattaPayload { Rainbow = Level }, MpEffectTarget.AllPartners);
        }
    }
}
