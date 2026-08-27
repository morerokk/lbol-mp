using LBoL.Presentation;
using LBoLEntitySideloader.Entities;
using LBoLEntitySideloader.Resource;
using UnityEngine;

namespace LBOLMP.Entities
{
    /// <summary>
    /// Base for every card LBOL MP adds.
    /// </summary>
    public abstract class LbolMpCardTemplate : CardTemplate
    {
        /// <summary>Where a card's art is expected to sit, under a file named after the card's id.</summary>
        protected const string CardArtFolder = "Resources/Cards/";

        private static DirectorySource _source;

        /// <summary>
        /// This mod's own folder. Shared, because every card reads out of the same one.
        /// </summary>
        protected static DirectorySource Source =>
            _source ?? (_source = new DirectorySource(MpInfo.Guid, ""));

        public override LocalizationOption LoadLocalization() => MpLocalization.Cards.AddEntity(this);

        /// <summary>
        /// Loads Resources/Cards/&lt;Id&gt;.png. Cards that stand in for one the game already has
        /// override this and return <see cref="BorrowVanillaArt"/> instead.
        /// </summary>
        public override CardImages LoadCardImages()
        {
            var images = new CardImages(Source);
            images.AutoLoad(this, extension: ".png", relativePath: CardArtFolder);
            return images;
        }

        /// <summary>
        /// Borrow the art from a vanilla card, particularly useful when replacing a vanilla card.
        /// </summary>
        /// <remarks>
        /// The config's <c>ImageId</c> has to name the same card.
        /// </remarks>
        protected static CardImages BorrowVanillaArt(string cardId)
        {
            return new CardImages(Source)
            {
                main = ResourcesHelper.TryGetCardImage(cardId) as Texture2D
            };
        }
    }
}
