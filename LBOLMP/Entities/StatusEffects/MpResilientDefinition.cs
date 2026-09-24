using System.Collections.Generic;
using LBoL.Base;
using LBoL.ConfigData;
using LBoL.Core.StatusEffects;
using LBoLEntitySideloader;
using LBoLEntitySideloader.Attributes;
using LBoLEntitySideloader.Entities;

namespace LBOLMP.Entities.StatusEffects
{
    /// <summary>
    /// The Resilient status makes enemies lose 1 more stack of Weak, Vulnerable, and Lock On at the end of their turn.
    /// It also makes them gain 1 less firepower down, but each instance always applies *at least* 1 FP down.
    /// The level of this status is equal to playercount - 1.
    /// </summary>
    public sealed class MpResilientDefinition : LbolMpStatusEffectTemplate
    {
        /// <summary>
        /// Where the yaml and the icon are read from
        /// </summary>
        public override IdContainer GetId() => nameof(MpResilient);

        public override StatusEffectConfig MakeConfig()
        {
            var config = DefaultConfig();
            config.Type = StatusEffectType.Positive;
            config.HasLevel = true;
            config.LevelStackType = StackType.Max;
            config.HasDuration = false;
            // Extra effects to show on the tooltip
            config.RelativeEffects = new List<string>
            {
                nameof(Weak),
                nameof(Vulnerable),
                nameof(LockedOn),
                nameof(FirepowerNegative)
            };

            return config;
        }
    }

    /// <summary>
    /// The Resilient status makes enemies lose 1 more stack of Weak, Vulnerable, and Lock On at the end of their turn.
    /// It also makes them gain 1 less firepower down, but each instance always applies *at least* 1 FP down.
    /// The level of this status is equal to playercount - 1.
    /// </summary>
    /// The effects themselves live in <c>EnemyResiliencePatches</c>.
    [EntityLogic(typeof(MpResilientDefinition))]
    public sealed class MpResilient : StatusEffect
    {
    }
}
