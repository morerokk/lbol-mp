using LBoL.Base;
using LBoL.ConfigData;
using LBoL.Core;
using LBoL.Core.Cards;
using LBoL.Core.Helpers;
using LBoL.Core.StatusEffects;
using LBoLEntitySideloader;
using LBoLEntitySideloader.Attributes;
using LBoLEntitySideloader.CustomKeywords;
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
    public sealed class MpScaled : StatusEffect, IExtendedKeywordName
    {
        public string ExtendedKeywordName(Card card) =>
            TypeFactory<StatusEffect>.LocalizeProperty(nameof(MpScaled), "Name", true, false)
            + " " + UiUtils.ManaGroupToText(card.Mana, false);
    }

    public sealed class MpScaledKeyword : CardKeyword
    {
        public MpScaledKeyword() : base(nameof(MpScaled))
        {
            descPos = KwDescPos.Last;
            hasExtendedKeywordName = true;
        }

        public override CardKeyword Clone(CloningMethod cloningMethod) => new MpScaledKeyword();
    }
}
