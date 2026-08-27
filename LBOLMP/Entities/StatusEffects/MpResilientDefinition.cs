using System.Collections.Generic;
using System.Linq;
using LBoL.Base;
using LBoL.ConfigData;
using LBoL.Core;
using LBoL.Core.Battle.BattleActions;
using LBoL.Core.StatusEffects;
using LBoL.Core.Units;
using LBoLEntitySideloader;
using LBoLEntitySideloader.Attributes;
using LBoLEntitySideloader.Entities;
using UnityEngine;

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
    [EntityLogic(typeof(MpResilientDefinition))]
    public sealed class MpResilient : StatusEffect
    {
        /// <summary>
        /// Hooked on TurnEnded rather than TurnEnding deliberately.
        /// This has the same effect, but doesn't require me to use transpilers (for edge cases that I forgot about).
        /// </summary>
        protected override void OnAdded(Unit unit)
        {
            HandleOwnerEvent(unit.TurnEnded, OnTurnEnded);
        }

        private void OnTurnEnded(UnitEventArgs args)
        {
            MpSafe.Run("MpResilient.TurnEnded", () =>
            {
                int extra = Level;
                if (extra <= 0 || Owner == null)
                {
                    return;
                }

                // Check if we have to flash the icon, to help visually explain why the debuffs dropped faster
                bool activated = false;
                foreach (var effect in Owner.StatusEffects.ToList())
                {
                    if (effect is Weak || effect is Vulnerable || effect is LockedOn)
                    {
                        activated |= ReduceStatusEffects(effect, extra);
                    }
                }

                if (activated)
                {
                    NotifyActivating();
                }
            });
        }

        /// <summary>
        /// Removes Weak/Vulnerable/Lock On, and also strips zero-stack effects off (todo: is this necessary?)
        /// </summary>
        private bool ReduceStatusEffects(StatusEffect effect, int extra)
        {
            if (effect.HasDuration)
            {
                if (effect.Duration <= 0)
                {
                    return false;
                }

                effect.Duration = Mathf.Max(0, effect.Duration - extra);
                if (effect.Duration == 0)
                {
                    React(new RemoveStatusEffectAction(effect, true, 0.1f));
                }
                return true;
            }

            if (effect.HasLevel)
            {
                if (effect.Level <= 0)
                {
                    return false;
                }

                effect.Level = Mathf.Max(0, effect.Level - extra);
                if (effect.Level == 0)
                {
                    React(new RemoveStatusEffectAction(effect, true, 0.1f));
                }
                return true;
            }

            return false;
        }
    }
}
