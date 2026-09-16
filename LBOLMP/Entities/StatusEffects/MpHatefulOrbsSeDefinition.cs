using System.Collections.Generic;
using System.Linq;
using LBoL.Base;
using LBoL.ConfigData;
using LBoL.Core;
using LBoL.Core.Battle;
using LBoL.Core.Battle.BattleActions;
using LBoL.Core.StatusEffects;
using LBoL.Core.Units;
using LBoL.EntityLib.Cards.Character.Reimu;
using LBoLEntitySideloader;
using LBoLEntitySideloader.Attributes;
using UnityEngine;

namespace LBOLMP.Entities.StatusEffects
{
    public sealed class MpHatefulOrbsSeDefinition : LbolMpStatusEffectTemplate
    {
        public override IdContainer GetId() => nameof(MpHatefulOrbsSe);

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

    [EntityLogic(typeof(MpHatefulOrbsSeDefinition))]
    public sealed class MpHatefulOrbsSe : StatusEffect
    {
        /// <summary>Enemies the orb being played has hit so far, in the order it hit them.</summary>
        private readonly List<EnemyUnit> _hit = new List<EnemyUnit>();

        protected override void OnAdded(Unit unit)
        {
            HandleOwnerEvent(Owner.DamageDealt, OnDamageDealt);
            ReactOwnerEvent(Battle.CardUsed, new EventSequencedReactor<CardUsingEventArgs>(OnCardUsed));
        }

        private void OnDamageDealt(DamageEventArgs args)
        {
            if (args.ActionSource is YinyangCardBase
                && args.Target is EnemyUnit enemy
                && args.DamageInfo.DamageType == DamageType.Attack
                && !_hit.Contains(enemy))
            {
                _hit.Add(enemy);
            }
        }

        // After the orb is done, so its own hit is not boosted by the Vulnerable.
        private IEnumerable<BattleAction> OnCardUsed(CardUsingEventArgs args)
        {
            if (!(args.Card is YinyangCardBase) || _hit.Count == 0)
            {
                yield break;
            }

            var targets = _hit.Where(enemy => enemy.IsAlive).ToList();
            _hit.Clear();

            if (Battle.BattleShouldEnd || targets.Count == 0)
            {
                yield break;
            }

            NotifyActivating();

            foreach (var enemy in targets)
            {
                yield return new ApplyStatusEffectAction<Weak>(enemy, duration: Level, occupationTime: 0.1f);
                yield return new ApplyStatusEffectAction<Vulnerable>(enemy, duration: Level, occupationTime: 0.1f);
            }
        }
    }
}
