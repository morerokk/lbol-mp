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
    public sealed class MpIceShardsSeDefinition : LbolMpStatusEffectTemplate
    {
        public override IdContainer GetId() => nameof(MpIceShardsSe);

        // TODO: Give this its own icon.
        public override Sprite LoadSprite() => null;

        public override StatusEffectConfig MakeConfig()
        {
            var config = DefaultConfig();
            config.Type = StatusEffectType.Positive;

            // How much Frost Armor each hit is worth.
            config.HasLevel = true;
            config.HasDuration = false;

            return config;
        }
    }

    [EntityLogic(typeof(MpIceShardsSeDefinition))]
    public sealed class MpIceShardsSe : StatusEffect
    {
        protected override void OnAdded(Unit unit)
        {
            ReactOwnerEvent(Owner.DamageDealt, new EventSequencedReactor<DamageEventArgs>(OnDamageDealt));
        }

        private IEnumerable<BattleAction> OnDamageDealt(DamageEventArgs args)
        {
            if (Battle.BattleShouldEnd || !(args.Target is EnemyUnit)
                || !args.Target.HasStatusEffect<Cold>())
            {
                yield break;
            }

            var damage = args.DamageInfo;
            if (damage.DamageType != DamageType.Attack || damage.Amount <= 0f)
            {
                yield break;
            }

            NotifyActivating();

            yield return new ApplyStatusEffectAction<FrostArmor>(Owner, Level);
        }
    }
}
