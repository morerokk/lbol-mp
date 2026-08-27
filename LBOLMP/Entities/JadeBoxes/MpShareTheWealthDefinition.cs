using LBoL.ConfigData;
using LBoL.Core;
using LBoLEntitySideloader;
using LBoLEntitySideloader.Attributes;
using LBoLEntitySideloader.Entities;
using LBoLEntitySideloader.Resource;

namespace LBOLMP.Entities.JadeBoxes
{
    /// <summary>
    /// Share the Wealth. Money any player gains or loses is gained or lost by everybody.
    /// </summary>
    public sealed class MpShareTheWealthDefinition : JadeBoxTemplate
    {
        public override IdContainer GetId() => nameof(MpShareTheWealth);

        public override LocalizationOption LoadLocalization() => MpLocalization.JadeBoxes.AddEntity(this);

        public override JadeBoxConfig MakeConfig() => DefaultConfig();
    }

    /// <summary>
    /// Deliberately empty.
    /// </summary>
    /// <remarks>
    /// Money is not a battle event and does not move through anything a jade box can react to, so
    /// the work is done by the patches on GameRunController's three money methods and by <c>MpSharedMoney</c>.
    /// </remarks>
    [EntityLogic(typeof(MpShareTheWealthDefinition))]
    public sealed class MpShareTheWealth : JadeBox
    {
    }
}
