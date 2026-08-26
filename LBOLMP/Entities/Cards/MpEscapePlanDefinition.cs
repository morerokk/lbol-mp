using System.Collections.Generic;
using LBOLMP.Entities.StatusEffects;
using LBOLMP.Net;
using LBOLMP.Session.Battle;
using LBoL.Base;
using LBoL.ConfigData;
using LBoL.Core;
using LBoL.Core.Battle;
using LBoL.Core.Battle.BattleActions;
using LBoL.Core.Cards;
using LBoL.Core.StatusEffects;
using LBoLEntitySideloader;
using LBoLEntitySideloader.Attributes;
using LBoLEntitySideloader.Entities;
using LBoLEntitySideloader.Resource;

namespace LBOLMP.Entities.Cards
{
    /// <summary>How much Graze the partner is getting.</summary>
    public sealed class MpEscapePlanPayload : MpEffectPayload
    {
        public int Graze;
    }

    /// <summary>
    /// Give yourself and one partner Graze.
    /// </summary>
    public sealed class MpEscapePlanDefinition : MpCardTemplate<MpEscapePlanPayload>
    {
        private static DirectorySource _source;

        private static DirectorySource Source => _source ?? (_source = new DirectorySource(MpInfo.Guid, ""));

        public override IdContainer GetId() => nameof(MpEscapePlan);

        public override LocalizationOption LoadLocalization()
        {
            var files = new LocalizationFiles(Source, Locale.En);
            files.AddLocaleFile(Locale.En, "Resources/CardsEn.yaml");
            return files;
        }

        public override CardImages LoadCardImages()
        {
            var images = new CardImages(Source);
            images.AutoLoad(this, extension: ".png", relativePath: "Resources/Cards/");
            return images;
        }

        public override CardConfig MakeConfig()
        {
            var config = DefaultConfig();
            config.Type = CardType.Skill;
            config.Rarity = Rarity.Common;
            config.Colors = new List<ManaColor> { ManaColor.Green };
            config.Cost = new ManaGroup { Any = 2, Green = 1 };
            config.UpgradedCost = new ManaGroup { Any = 3 };

            // How much Graze each of the two players gets.
            config.Value1 = 1;
            config.UpgradedValue1 = 2;

            config.Illustrator = "min_k_2";

            // Set to TargetType.SingleEnemy just so we can borrow the selector logic.
            // PartyTargetPatches points it at a partner instead of enemies.
            config.TargetType = TargetType.SingleEnemy;

            config.RelativeEffects = new List<string> { nameof(MpPartner), nameof(Graze) };
            config.UpgradedRelativeEffects = new List<string> { nameof(MpPartner), nameof(Graze) };
            return config;
        }

        public override IEnumerable<BattleAction> Receive(
            MpEscapePlanPayload payload, BattleController battle, int senderId)
        {
            if (payload.Graze <= 0 || battle.BattleShouldEnd)
            {
                yield break;
            }

            yield return new ApplyStatusEffectAction<Graze>(battle.Player, payload.Graze, occupationTime: 0.2f);
        }
    }

    [EntityLogic(typeof(MpEscapePlanDefinition))]
    public sealed class MpEscapePlan : Card, IMpPartnerTargeted
    {
        public override bool CanUse => MpPartyTargeting.AnyValidPartner;

        protected override IEnumerable<BattleAction> Actions(
            UnitSelector selector, ManaGroup consumingMana, Interaction precondition)
        {
            int partner = MpPartyTargeting.Consume();

            // Graze for us right now
            yield return BuffAction<Graze>(Value1);
            // And then Graze for them once this arrives
            MpEffects.Send(Id, new MpEscapePlanPayload { Graze = Value1 }, MpEffectTarget.Partner, partner);
        }
    }
}
