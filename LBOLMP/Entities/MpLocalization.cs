using LBoL.Core;
using LBoLEntitySideloader.Entities;
using LBoLEntitySideloader.Resource;

namespace LBOLMP.Entities
{
    internal static class MpLocalization
    {
        private static DirectorySource _source;

        private static DirectorySource Source => _source ?? (_source = new DirectorySource(MpInfo.Guid, ""));

        private static BatchLocalization _cards;
        private static BatchLocalization _statusEffects;

        /// <summary>Resources/Cards&lt;Locale&gt;.yaml</summary>
        internal static BatchLocalization Cards =>
            _cards ?? (_cards = new BatchLocalization(Source, typeof(CardTemplate), "Resources/Cards", Locale.En));

        /// <summary>Resources/StatusEffects&lt;Locale&gt;.yaml</summary>
        internal static BatchLocalization StatusEffects =>
            _statusEffects ?? (_statusEffects =
                new BatchLocalization(Source, typeof(StatusEffectTemplate), "Resources/StatusEffects", Locale.En));
    }
}
