using LBoL.ConfigData;
using LBoL.Core;
using LBoL.Core.Adventures;
using LBoL.Core.Cards;
using LBoL.Core.Randoms;
using LBoL.EntityLib.Adventures.Stage1;
using LBoLEntitySideloader;
using LBoLEntitySideloader.Attributes;
using LBoLEntitySideloader.Entities;
using LBoLEntitySideloader.Resource;
using LBOLMP.Session;
using System;
using System.Collections.Generic;
using System.Linq;
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

        /// <summary>
        /// Which Adventure track to play. This borrows the TewiThreat music.
        /// </summary>
        private static int BorrowedMusic()
        {
            var borrowed = AdventureConfig.FromId(nameof(TewiThreat));
            if (borrowed == null || borrowed.Music == 0)
            {
                return 1;
            }

            return borrowed.Music;
        }

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
            config.Music = BorrowedMusic();

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
                // Trailing empty ids just mean a shorter pick. All three empty, and the yarn
                // hides the option rather than opening an empty picker.
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

            return Ids(GameRun.RollCards(
                GameRun.AdventureRng, weights, Offers, false, false, IsMultiplayerCard));
        }

        private static bool IsMultiplayerCard(CardConfig config) =>
            config != null && MpCardAvailability.IsMultiplayerOnly(config.Id);

        private static List<string> Ids(IEnumerable<Card> cards) =>
            cards == null ? new List<string>() : cards.Select(card => card.Id).ToList();
    }
}
