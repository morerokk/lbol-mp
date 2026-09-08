using LBoL.Base;
using LBoL.ConfigData;
using LBoL.Core;
using LBoL.Core.Battle;
using LBoL.Core.Battle.BattleActions;
using LBoL.Core.Battle.Interactions;
using LBoL.Core.Cards;
using LBoLEntitySideloader;
using LBoLEntitySideloader.Attributes;
using LBOLMP.Entities.StatusEffects;
using LBOLMP.Net;
using LBOLMP.Session;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace LBOLMP.Entities.Cards.Reimu
{
    public sealed class MpCleansePayload : MpEffectPayload
    {

    }

    public sealed class MpCleanseDefinition : LbolMpMultiplayerCardTemplate<MpCleansePayload>
    {
        public override IdContainer GetId() => nameof(MpCleanse);

        public override CardConfig MakeConfig()
        {
            var config = DefaultConfig();
            config.Type = CardType.Skill;
            config.Rarity = Rarity.Rare;
            config.Owner = VanillaCharNames.Reimu;
            config.Colors = new List<ManaColor> { ManaColor.Green };
            config.Cost = new ManaGroup { Green = 1 };
            config.TargetType = TargetType.Nobody;
            config.Keywords = Keyword.Exile;
            config.UpgradedKeywords = Keyword.Exile;

            config.RelativeEffects = new List<string> { nameof(MpPartner), nameof(MpScaled) };
            config.UpgradedRelativeEffects = new List<string> { nameof(MpPartner), nameof(MpScaled) };

            // Mana pip for Scaled X
            config.Mana = new ManaGroup { Any = 1 };
            config.UpgradedMana = new ManaGroup { Any = 1 };

            config.Illustrator = "Toro";

            return config;
        }

        public override IEnumerable<BattleAction> Receive(MpCleansePayload payload, BattleController battle, int senderId)
        {
            yield return new RemoveAllNegativeStatusEffectAction(battle.Player);
        }
    }

    [EntityLogic(typeof(MpCleanseDefinition))]
    public sealed class MpCleanse : Card
    {
        private static int PartySize => Mathf.Clamp(MpSession.IsActive ? MpSession.ConnectedCount : 2, 2, 4);

        public override ManaGroup AdditionalCost => new ManaGroup { Any = PartySize - 1 };

        protected override IEnumerable<BattleAction> Actions(UnitSelector selector, ManaGroup consumingMana, Interaction precondition)
        {
            MpEffects.Send(Id, new MpCleansePayload(), MpEffectTarget.AllPartners);

            // When upgraded, also removes them from yourself
            if (IsUpgraded)
            {
                yield return new RemoveAllNegativeStatusEffectAction(Battle.Player);
            }
        }
    }
}
