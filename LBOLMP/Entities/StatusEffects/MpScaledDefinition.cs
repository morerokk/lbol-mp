using LBoL.Base;
using LBoL.ConfigData;
using LBoL.Core.StatusEffects;
using LBoLEntitySideloader;
using LBoLEntitySideloader.Attributes;
using UnityEngine;

namespace LBOLMP.Entities.StatusEffects
{
    /// <summary>
    /// The "Scaled" keyword, for cards that cost more for each partner in the party.
    /// Never applied to anything, this is just for the card tooltip.
    /// </summary>
    public sealed class MpScaledDefinition : LbolMpStatusEffectTemplate
    {
        public override IdContainer GetId() => nameof(MpScaled);

        public override Sprite LoadSprite() => null;

        public override StatusEffectConfig MakeConfig()
        {
            var config = DefaultConfig();
            config.Type = StatusEffectType.Special;
            config.HasLevel = false;
            config.HasDuration = false;
            return config;
        }
    }

    // Never applied to anything
    [EntityLogic(typeof(MpScaledDefinition))]
    public sealed class MpScaled : StatusEffect
    {
    }
}
