using LBOLMP.Session;
using LBoL.Core;
using LBoL.Presentation.UI;
using LBoL.Presentation.UI.Panels;

namespace LBOLMP.UI
{
    /// <summary>
    /// The watched player's draw, discard, exile and deck, shown through the game's own pile
    /// viewer.
    /// </summary>
    public static class MpInspectedPiles
    {
        /// <summary>
        /// Whose character the panel is being opened for, while it is being opened, or empty.
        /// </summary>
        internal static string ShowingFor { get; private set; } = string.Empty;

        public static void ShowDraw()
        {
            // Show the draw pile, but only show it in order if the draw pile's owner has the right Kosuzu book.
            // This avoids 1 player being able to call out everyone's draw pile in order (which is tedious but also optimal, so this really really had to go)
            Show(MpHandInspect.Draw, "Game.DrawZoneOutOfOrder".Localize(),
                ShowCardZone.Draw, MpHandInspect.HideDrawOrder);
        }

        public static void ShowDiscard() =>
            Show(MpHandInspect.Discard, "Game.DiscardZone".Localize(), ShowCardZone.Discard, false);

        public static void ShowExile() =>
            Show(MpHandInspect.Exile, "Game.ExileZone".Localize(), ShowCardZone.Exile, false);

        public static void ShowDeck() =>
            Show(MpHandInspect.Deck, "Game.Deck".Localize(), ShowCardZone.Library, false);

        private static void Show(System.Collections.Generic.IReadOnlyList<LBoL.Core.Cards.Card> cards,
            string zoneName, ShowCardZone zone, bool hideActualOrder)
        {
            MpSafe.Run("MpInspectedPiles", () =>
            {
                ShowingFor = MpSession.Get(MpHandInspect.Target)?.CharacterId ?? string.Empty;
                try
                {
                    UiManager.GetPanel<ShowCardsPanel>().Show(new ShowCardsPayload
                    {
                        Name = L10n.Get(MpText.InspectZoneTitle, MpHandInspect.TargetName, zoneName),
                        Description = "Cards.Show".Localize(),
                        Cards = new System.Collections.Generic.List<LBoL.Core.Cards.Card>(cards),
                        InteractionType = InteractionType.None,
                        CardZone = zone,
                        HideActualOrder = hideActualOrder
                    });
                }
                finally
                {
                    ShowingFor = string.Empty;
                }
            });
        }
    }
}
