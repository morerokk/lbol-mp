using System.Collections.Generic;
using System.Linq;
using LBOLMP.Entities.StatusEffects;
using LBOLMP.Net;
using LBoL.Base;
using LBoL.ConfigData;
using LBoL.Core;
using LBoL.Core.Battle;
using LBoL.Core.Battle.BattleActions;
using LBoL.Core.Battle.Interactions;
using LBoL.Core.Cards;
using LBoL.EntityLib.Cards.Character.Sakuya;
using LBoLEntitySideloader;
using LBoLEntitySideloader.Attributes;
using LBoLEntitySideloader.Resource;

namespace LBOLMP.Entities.Cards
{
    /// <summary>How many knives each partner is getting.</summary>
    public sealed class MpBladeSymphonyPayload : MpEffectPayload
    {
        public int Knives;
    }

    public sealed class MpBladeSymphonyDefinition : MpCardTemplate<MpBladeSymphonyPayload>
    {
        private static DirectorySource _source;

        private static DirectorySource Source => _source ?? (_source = new DirectorySource(MpInfo.Guid, ""));

        public override IdContainer GetId() => nameof(MpBladeSymphony);

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
            config.Rarity = Rarity.Uncommon;
            config.Owner = VanillaCharNames.Sakuya;
            config.Colors = new List<ManaColor> { ManaColor.White };
            config.Cost = new ManaGroup { White = 1, Any = 1 };
            config.UpgradedCost = new ManaGroup { Any = 2 };
            config.TargetType = TargetType.Nobody;

            // How many knives you can exile and give to other players.
            config.Value1 = 2;
            config.UpgradedValue1 = 3;

            config.RelativeCards = new List<string> { nameof(Knife) };
            config.UpgradedRelativeCards = new List<string> { nameof(Knife) };
            config.RelativeEffects = new List<string> { nameof(MpPartner) };
            config.UpgradedRelativeEffects = new List<string> { nameof(MpPartner) };

            config.Illustrator = "orientalzenzai";

            return config;
        }

        public override IEnumerable<BattleAction> Receive(
            MpBladeSymphonyPayload payload, BattleController battle, int senderId)
        {
            if (payload.Knives <= 0)
            {
                yield break;
            }

            yield return new AddCardsToHandAction(
                Library.CreateCards<Knife>(payload.Knives, false), AddCardsType.Normal, false);
        }
    }

    [EntityLogic(typeof(MpBladeSymphonyDefinition))]
    public sealed class MpBladeSymphony : Card
    {
        /// <summary>Pick the knives before the card is committed, the same way Ice Wing does.</summary>
        public override Interaction Precondition()
        {
            var knives = KnivesInHand().ToList();
            return knives.Count > 0 ? new SelectHandInteraction(0, Value1, knives) : null;
        }

        protected override IEnumerable<BattleAction> Actions(
            UnitSelector selector, ManaGroup consumingMana, Interaction precondition)
        {
            var chosen = (precondition as SelectHandInteraction)?.SelectedCards;
            if (chosen == null || chosen.Count == 0)
            {
                yield break;
            }

            yield return new ExileManyCardAction(chosen);

            MpEffects.Send(Id, new MpBladeSymphonyPayload { Knives = chosen.Count },
                MpEffectTarget.AllPartners);
        }

        private IEnumerable<Card> KnivesInHand() =>
            Battle == null
                ? Enumerable.Empty<Card>()
                : Battle.HandZone.Where(card => card is Knife);
    }
}
