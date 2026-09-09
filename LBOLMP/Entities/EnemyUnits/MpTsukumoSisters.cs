using LBoL.Core.Units;
using LBoLEntitySideloader;
using LBoLEntitySideloader.Attributes;

namespace LBOLMP.Entities.EnemyUnits
{
    /// <summary>
    /// Placeholder unit to use as event host.
    /// </summary>
    /// TODO: Is this really how we should do this?
    public sealed class MpBenbenDefinition : MpLoreEnemyUnitTemplate
    {
        public override IdContainer GetId() => nameof(MpBenben);
    }

    [EntityLogic(typeof(MpBenbenDefinition))]
    public sealed class MpBenben : EnemyUnit
    {
    }

    /// <summary>
    /// Placeholder unit to use as event host.
    /// </summary>
    /// TODO: Is this really how we should do this?
    public sealed class MpYatsuhashiDefinition : MpLoreEnemyUnitTemplate
    {
        public override IdContainer GetId() => nameof(MpYatsuhashi);
    }

    [EntityLogic(typeof(MpYatsuhashiDefinition))]
    public sealed class MpYatsuhashi : EnemyUnit
    {
    }
}
