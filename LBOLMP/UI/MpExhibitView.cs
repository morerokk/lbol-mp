using System;
using System.Collections.Generic;
using System.Linq;
using LBOLMP.Net;
using LBOLMP.Session;
using LBoL.Base;
using LBoL.Core;
using LBoL.Presentation.UI;
using LBoL.Presentation.UI.Panels;
using LBoL.Presentation.UI.Widgets;
using UnityEngine;

namespace LBOLMP.UI
{
    /// <summary>
    /// Shows the previewed player's exhibits along the top instead of your own.
    /// </summary>
    internal static class MpExhibitView
    {
        /// <summary>The widgets we made, which we can safely destroy.</summary>
        private static readonly List<ExhibitWidget> Spawned = new List<ExhibitWidget>();

        /// <summary>The board's own widgets, switched off while somebody else's are up.</summary>
        private static readonly List<GameObject> Hidden = new List<GameObject>();

        /// <summary>True while the board is showing somebody else's.</summary>
        private static bool _swapped;

        /// <summary>
        /// Who is currently being previewed.
        /// </summary>
        private static int _shownPlayer = MpConstants.InvalidPlayerId;
        private static IReadOnlyList<string> _shownIds;

        private static Vector2 _contentSize;

        /// <summary>Show another player's exhibits.</summary>
        internal static void Show(int playerId)
        {
            var board = Board();
            if (board == null)
            {
                return;
            }

            var ids = MpPlayerExhibits.Of(playerId);
            if (playerId == _shownPlayer && ReferenceEquals(ids, _shownIds))
            {
                return;
            }

            // Their exhibits changed, or we switched to a different player.
            Despawn();

            if (!_swapped)
            {
                _swapped = true;
                HideOwn(board);
                _contentSize = board.scrollRect.content.sizeDelta;
            }

            _shownPlayer = playerId;
            _shownIds = ids;

            var exhibits = ids
                .Select(Library.TryCreateExhibit)
                .Where(exhibit => exhibit != null)
                .OrderBy(Order)
                .ToList();

            foreach (var exhibit in exhibits)
            {
                var widget = UnityEngine.Object.Instantiate(board.exhibitTemplate, board.scrollRect.content);
                widget.name = "MP exhibit: " + exhibit.Id;
                widget.Exhibit = exhibit;

                // Left off on purpose: it reads the live Active and Blackout flags every frame, and
                // these copies are not attached to anybody's run to have them.
                widget.ShowBattleStatus = false;
                widget.gameObject.SetActive(true);
                Spawned.Add(widget);
            }

            Grid(board);
        }

        /// <summary>Give the board its own exhibits back.</summary>
        internal static void Restore()
        {
            if (!_swapped)
            {
                return;
            }

            _swapped = false;
            _shownPlayer = MpConstants.InvalidPlayerId;
            _shownIds = null;
            Despawn();

            var board = Board();
            if (board != null)
            {
                board.scrollRect.content.sizeDelta = _contentSize;
            }

            foreach (var own in Hidden)
            {
                if (own != null)
                {
                    own.SetActive(true);
                }
            }

            Hidden.Clear();
        }

        /// <summary>Mythic first, then Shining, the way the board sorts its own.</summary>
        private static int Order(Exhibit exhibit)
        {
            switch (exhibit.Config.Rarity)
            {
                case Rarity.Mythic: return 0;
                case Rarity.Shining: return 1;
                default: return 2;
            }
        }

        private static void HideOwn(SystemBoard board)
        {
            Hidden.Clear();

            foreach (var widget in board.sortedExhibitWidgets)
            {
                if (widget == null || widget.gameObject == null)
                {
                    continue;
                }

                Hidden.Add(widget.gameObject);
                widget.gameObject.SetActive(false);
            }
        }

        private static void Despawn()
        {
            foreach (var widget in Spawned)
            {
                if (widget != null)
                {
                    UnityEngine.Object.Destroy(widget.gameObject);
                }
            }

            Spawned.Clear();
        }

        /// <summary>Lays ours out the same way <c>GridExhibits</c> lays out the board's.</summary>
        private static void Grid(SystemBoard board)
        {
            for (int i = 0; i < Spawned.Count; i++)
            {
                var rect = Spawned[i].GetComponent<RectTransform>();
                rect.anchorMin = new Vector2(0f, 0.5f);
                rect.anchorMax = new Vector2(0f, 0.5f);
                rect.pivot = new Vector2(0f, 0.5f);
                rect.sizeDelta = board.cellSize;
                rect.anchoredPosition = new Vector2(
                    board.padding.x + i * (board.cellSize.x + board.spacing.x), 0f);
            }

            board.scrollRect.content.sizeDelta = new Vector2(
                board.padding.x + Spawned.Count * (board.cellSize.x + board.spacing.x), 0f);
        }

        private static SystemBoard Board()
        {
            try
            {
                return UiManager.GetPanel<SystemBoard>();
            }
            catch (InvalidOperationException)
            {
                return null;
            }
        }
    }
}
