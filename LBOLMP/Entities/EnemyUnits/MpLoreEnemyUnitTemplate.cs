using System.Collections.Generic;
using LBoL.Base;
using LBoL.ConfigData;
using LBoLEntitySideloader.Entities;
using LBoLEntitySideloader.Resource;

namespace LBOLMP.Entities.EnemyUnits
{
    /// <summary>
    /// Base for a character who only ever shows up to talk. They are enemy units because that is
    /// what the event system speaks: a host has to resolve to a unit id, even when there is no fight.
    /// </summary>
    /// <remarks>
    /// Everything below the OnlyLore flag is filler. Nothing reads a stat off a unit that never
    /// enters a battle, but the config has no shorter constructor.
    /// </remarks>
    public abstract class MpLoreEnemyUnitTemplate : EnemyUnitTemplate
    {
        public override LocalizationOption LoadLocalization() => MpLocalization.EnemyUnits.AddEntity(this);

        public override EnemyUnitConfig MakeConfig() => new EnemyUnitConfig(
            Id: GetId(),
            RealName: true,
            OnlyLore: true,
            BaseManaColor: new ManaColor[] { },
            Order: 10,
            ModleName: "",
            NarrativeColor: null,
            Type: EnemyType.Normal,
            IsPreludeOpponent: false,
            HpLength: null,
            MaxHpAdd: null,
            MaxHp: 20,
            Damage1: null,
            Damage2: null,
            Damage3: null,
            Damage4: null,
            Power: null,
            Defend: null,
            Count1: null,
            Count2: null,
            MaxHpHard: null,
            Damage1Hard: null,
            Damage2Hard: null,
            Damage3Hard: null,
            Damage4Hard: null,
            PowerHard: null,
            DefendHard: null,
            Count1Hard: null,
            Count2Hard: null,
            MaxHpLunatic: null,
            Damage1Lunatic: null,
            Damage2Lunatic: null,
            Damage3Lunatic: null,
            Damage4Lunatic: null,
            PowerLunatic: null,
            DefendLunatic: null,
            Count1Lunatic: null,
            Count2Lunatic: null,
            PowerLoot: new MinMax(0, 0),
            BluePointLoot: new MinMax(0, 0),
            Gun1: new List<string> { "Simple1" },
            Gun2: new List<string> { "Simple1" },
            Gun3: new List<string> { "Simple1" },
            Gun4: new List<string> { "Simple1" });
    }
}
