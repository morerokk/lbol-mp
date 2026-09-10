using System.Collections.Generic;
using LBoL.Base;
using LBoL.ConfigData;
using LBoL.Core;
using LBoL.Core.Battle;
using LBoL.Core.Battle.BattleActions;
using LBoL.Core.StatusEffects;
using LBoL.Core.Units;
using LBoL.EntityLib.StatusEffects.Cirno;
using LBoLEntitySideloader;
using LBoLEntitySideloader.Attributes;
using UnityEngine;

namespace LBOLMP.Entities.StatusEffects
{
    /// <summary>
    /// Stacking Cold on an enemy also makes them Vulnerable.
    /// </summary>
    public sealed class MpDeepFreezeSeDefinition : LbolMpStatusEffectTemplate
    {
        public override IdContainer GetId() => nameof(MpDeepFreezeSe);

        // TODO: Give this its own icon.
        public override Sprite LoadSprite() => null;

        public override StatusEffectConfig MakeConfig()
        {
            var config = DefaultConfig();
            config.Type = StatusEffectType.Positive;

            // How much Vulnerable each stacked Cold is worth.
            config.HasLevel = true;
            config.HasDuration = false;

            return config;
        }
    }

    /// <summary>
    /// Stacking Cold on an enemy also makes them Vulnerable.
    /// </summary>
    [EntityLogic(typeof(MpDeepFreezeSeDefinition))]
    public sealed class MpDeepFreezeSe : StatusEffect
    {
        protected override void OnAdded(Unit unit)
        {
            foreach (var enemy in Battle.AllAliveEnemies)
            {
                Watch(enemy);
            }

            HandleOwnerEvent(Battle.EnemySpawned, OnEnemySpawned);
        }

        private void OnEnemySpawned(UnitEventArgs args) => Watch(args.Unit);

        private void Watch(Unit enemy) =>
            ReactOwnerEvent(enemy.StatusEffectAdded,
                new EventSequencedReactor<StatusEffectApplyEventArgs>(OnEnemySeAdded));

        private IEnumerable<BattleAction> OnEnemySeAdded(StatusEffectApplyEventArgs args)
        {
            if (!(args.Effect is Cold) || args.AddResult != StatusEffectAddResult.Stacked
                || Battle.BattleShouldEnd || !args.Unit.IsAlive)
            {
                yield break;
            }

            NotifyActivating();

            yield return new ApplyStatusEffectAction<Vulnerable>(
                args.Unit, duration: Level, occupationTime: 0.2f);
        }
    }
}
