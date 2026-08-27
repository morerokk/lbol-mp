using LBoLEntitySideloader.Entities;
using LBoLEntitySideloader.Resource;
using UnityEngine;

namespace LBOLMP.Entities
{
    /// <summary>
    /// Base for every status effect LBOL MP adds.
    /// </summary>
    /// <remarks>
    /// Effects that also do something over the network derive from
    /// <see cref="LbolMpMultiplayerStatusEffectTemplate{TPayload}"/> instead, which adds the
    /// receiving half on top of this.
    /// </remarks>
    public abstract class LbolMpStatusEffectTemplate : StatusEffectTemplate
    {
        protected const string IconFolder = "Resources/StatusEffects/";

        private static DirectorySource _source;

        protected static DirectorySource Source =>
            _source ?? (_source = new DirectorySource(MpInfo.Guid, ""));

        public override LocalizationOption LoadLocalization() => MpLocalization.StatusEffects.AddEntity(this);

        /// <summary>
        /// Loads Resources/StatusEffects/&lt;Id&gt;.png.
        /// </summary>
        public override Sprite LoadSprite() =>
            ResourceLoader.LoadSprite(IconFolder + GetId() + ".png", Source);
    }
}
