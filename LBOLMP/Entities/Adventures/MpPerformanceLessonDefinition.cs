using System;
using System.Collections.Generic;
using System.Linq;
using LBOLMP.Session;
using LBoL.ConfigData;
using LBoL.Core;
using LBoL.Core.Adventures;
using LBoL.Core.Cards;
using LBoL.Core.Randoms;
using LBoLEntitySideloader;
using LBoLEntitySideloader.Attributes;
using LBoLEntitySideloader.Entities;
using LBoLEntitySideloader.Resource;
using Yarn;

namespace LBOLMP.Entities.Adventures
{
    /// <summary>
    /// Tsukumo Sisters offer the players a lesson in playing together.
    /// Rewards a multiplayer-exclusive card, or lets you leave.
    /// </summary>
    public sealed class MpPerformanceLessonDefinition : AdventureTemplate
    {
        private static DirectorySource _source;

        private static DirectorySource Source =>
            _source ?? (_source = new DirectorySource(MpInfo.Guid, ""));

        public override IdContainer GetId() => nameof(MpPerformanceLesson);

        public override LocalizationOption LoadLocalization() => MpLocalization.Adventures.AddEntity(this);

        public override AdventureImages LoadAdventureImages()
        {
            var images = new AdventureImages();
            images.AutoLoad(this, Source);
            return images;
        }

        public override YarnData LoadYarnData()
        {
            var yarn = new YarnData();
            yarn.AutoLoad(this, Source, Source);
            return yarn;
        }

        public override AdventureConfig MakeConfig()
        {
            var config = DefaultConfig();

            config.HostId = nameof(EnemyUnits.MpYatsuhashi);
            config.HostId2 = nameof(EnemyUnits.MpBenben);

            return config;
        }
    }

    [AdventureInfo(WeighterType = typeof(MpPerformanceLesson.MpPerformanceLessonWeighter))]
    [EntityLogic(typeof(MpPerformanceLessonDefinition))]
    public sealed class MpPerformanceLesson : Adventure
    {
        /// <summary>How many cards the player can pick between.</summary>
        private const int Offers = 3;

        /// <summary>
        /// The reward is multiplayer cards, which do not exist outside a multiplayer run.
        /// </summary>
        public sealed class MpPerformanceLessonWeighter : IAdventureWeighter
        {
            public float WeightFor(Type type, GameRunController gameRun) =>
                MpSession.IsActive && MpSession.MultiplayerCards ? 1f : 0f;
        }

        public override void InitVariables(IVariableStorage storage)
        {
            var offers = RollMultiplayerCards();

            for (int i = 0; i < Offers; i++)
            {
                // An empty id turns the option off rather than crashing the picker.
                // TODO: Should we make it unavailable instead?
                storage.SetValue("$card" + (i + 1), i < offers.Count ? offers[i] : string.Empty);
            }
        }

        private List<string> RollMultiplayerCards()
        {
            // TODO: This literally does not care about the rarity of cards!
            // That's because this mod only has a few cards to begin with. This may turn out to be broken
            // The obvious solution is add moar cards
            var weights = new CardWeightTable(
                RarityWeightTable.AllOnes, OwnerWeightTable.Valid, CardTypeWeightTable.CanBeLoot, false);

            var rolled = Ids(GameRun.RollCards(
                GameRun.AdventureRng, weights, Offers, false, false, IsMultiplayerCard));

            // The colour limit can come up short against a pool this size. Rather an off-colour
            // card than a missing option.
            if (rolled.Count < Offers)
            {
                rolled = Ids(GameRun.RollCardsWithoutManaLimit(
                    GameRun.AdventureRng, weights, Offers, false, false, IsMultiplayerCard));
            }

            return rolled;
        }

        private static bool IsMultiplayerCard(CardConfig config) =>
            config != null && MpCardAvailability.IsMultiplayerOnly(config.Id);

        private static List<string> Ids(IEnumerable<Card> cards) =>
            cards == null ? new List<string>() : cards.Select(card => card.Id).ToList();
    }
}
