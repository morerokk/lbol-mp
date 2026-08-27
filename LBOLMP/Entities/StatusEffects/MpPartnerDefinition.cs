using LBOLMP.Net;
using LBoL.Base;
using LBoL.ConfigData;
using LBoL.Core;
using LBoL.Core.StatusEffects;
using LBoLEntitySideloader;
using LBoLEntitySideloader.Attributes;
using LBoLEntitySideloader.Entities;
using LBoLEntitySideloader.Resource;
using UnityEngine;

namespace LBOLMP.Entities.StatusEffects
{
    /// <summary>
    /// The "Partner" keyword, so cards can explain what a Partner is.
    /// To use it: put <c>nameof(MpPartner)</c> in the card's RelativeEffects.
    /// Is otherwise unused as an actual status.
    /// </summary>
    public sealed class MpPartnerDefinition : StatusEffectTemplate
    {
        private static DirectorySource _source;

        private static DirectorySource Source => _source ?? (_source = new DirectorySource(MpInfo.Guid, ""));

        public override IdContainer GetId() => nameof(MpPartner);

        public override LocalizationOption LoadLocalization() => MpLocalization.StatusEffects.AddEntity(this);

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

    /// <summary>Never applied to anything. See <see cref="MpPartnerDefinition"/>.</summary>
    [EntityLogic(typeof(MpPartnerDefinition))]
    public sealed class MpPartner : StatusEffect
    {
    }
}
