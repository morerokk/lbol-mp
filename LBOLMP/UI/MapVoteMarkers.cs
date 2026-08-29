using System;
using System.Collections.Generic;
using LBOLMP.Session;
using LBoL.Presentation;
using LBoL.Presentation.UI;
using LBoL.Presentation.UI.Panels;
using LBoL.Presentation.UI.Widgets;
using UnityEngine;
using UnityEngine.UI;

namespace LBOLMP.UI
{
    /// <summary>
    /// Puts everyone's map vote on the map.
    /// </summary>
    public static class MapVoteMarkers
    {
        private const string MarkerName = "MpVoteMarker";

        private static readonly Dictionary<int, GameObject> Markers = new Dictionary<int, GameObject>();

        private static readonly List<int> Voted = new List<int>();

        public static void Update() => MpSafe.Run("MapVoteMarkers", Refresh);

        public static void Clear()
        {
            MpSafe.Run("MapVoteMarkers.Clear", () =>
            {
                foreach (var marker in Markers.Values)
                {
                    if (marker != null)
                    {
                        UnityEngine.Object.Destroy(marker);
                    }
                }

                Markers.Clear();
            });
        }

        private static void Refresh()
        {
            if (!MpSession.IsActive || !MpSession.IsInRun)
            {
                HideAll();
                return;
            }

            var panel = TryGetPanel<MapPanel>();
            if (panel == null || MapSync.CurrentVotes.Count == 0)
            {
                HideAll();
                return;
            }

            var votes = MapSync.CurrentVotes;
            Voted.Clear();

            var byNode = new Dictionary<(int X, int Y), List<int>>();
            foreach (var player in MpSession.Players)
            {
                if (!votes.TryGetValue(player.Id, out var node))
                {
                    continue;
                }

                if (!byNode.TryGetValue(node, out var group))
                {
                    group = new List<int>();
                    byNode[node] = group;
                }

                group.Add(player.Id);
                Voted.Add(player.Id);
            }

            foreach (var entry in byNode)
            {
                var widget = WidgetAt(panel, entry.Key.X, entry.Key.Y);
                if (widget == null)
                {
                    continue;
                }

                for (int i = 0; i < entry.Value.Count; i++)
                {
                    Place(entry.Value[i], widget, i, entry.Value.Count);
                }
            }

            // Anyone who has not voted yet, or whose node is off-screen this act.
            foreach (var pair in Markers)
            {
                if (!Voted.Contains(pair.Key) && pair.Value != null)
                {
                    pair.Value.SetActive(false);
                }
            }
        }

        private static void Place(int playerId, MapNodeWidget widget, int index, int count)
        {
            var marker = Ensure(playerId);
            if (marker == null)
            {
                return;
            }

            var characterId = MpSession.Get(playerId)?.CharacterId;
            var head = MpPortraits.For(characterId);
            if (head == null)
            {
                marker.SetActive(false);
                return;
            }

            // Null for a character whose portrait already draws its own ring.
            var ring = marker.GetComponent<Image>();
            ring.sprite = MpPortraits.FrameFor(characterId);
            ring.enabled = ring.sprite != null;

            // Drawn as a raw quad with the sprite's own atlas coordinates rather than as an Image.
            // This works slightly better for modded characters and avoids guesswork.
            var face = marker.transform.GetChild(0).GetComponent<RawImage>();
            // Zoomed by sampling less of the sprite, so the head grows without leaving the ring.
            var region = MpPortraits.Middle(head.textureRect, MpPortraits.ZoomFor(characterId));
            face.texture = head.texture;
            face.uvRect = new Rect(
                region.x / head.texture.width,
                region.y / head.texture.height,
                region.width / head.texture.width,
                region.height / head.texture.height);

            var rect = (RectTransform)marker.transform;
            if (rect.parent != widget.transform)
            {
                rect.SetParent(widget.transform, false);
            }

            rect.SetAsLastSibling();

            var host = widget.transform as RectTransform;
            float height = host != null && host.rect.height > 1f ? host.rect.height : 60f;
            float width = host != null && host.rect.width > 1f ? host.rect.width : height;
            float size = Mathf.Clamp(height * 0.85f, 38f, 84f);

            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(size, size);
            rect.localScale = Vector3.one;

            rect.anchoredPosition = new Vector2(
                -(width * 0.5f + size * 0.45f),

                ((count - 1) * 0.5f - index) * (size * (count > 2 ? 0.62f : 0.8f)));

            float inset = size * MpPortraits.HeadScale(characterId);
            float aspect = region.height > 0f ? region.width / region.height : 1f;
            var faceSize = aspect > 1f
                ? new Vector2(inset, inset / aspect)
                : new Vector2(inset * aspect, inset);

            var faceRect = (RectTransform)face.transform;
            faceRect.anchorMin = faceRect.anchorMax = faceRect.pivot = new Vector2(0.5f, 0.5f);
            faceRect.sizeDelta = faceSize;
            faceRect.anchoredPosition = Vector2.zero;
            faceRect.localScale = Vector3.one;

            marker.SetActive(true);
        }

        private static GameObject Ensure(int playerId)
        {
            if (Markers.TryGetValue(playerId, out var existing) && existing != null
                && existing.transform.childCount > 0
                && existing.transform.GetChild(0).GetComponent<RawImage>() != null)
            {
                return existing;
            }

            if (existing != null)
            {
                UnityEngine.Object.Destroy(existing);
            }

            var marker = new GameObject($"{MarkerName}_{playerId}", typeof(RectTransform), typeof(Image));
            var ring = marker.GetComponent<Image>();
            ring.preserveAspect = true;
            Configure(ring);

            var face = new GameObject("Head", typeof(RectTransform), typeof(RawImage));
            face.transform.SetParent(marker.transform, false);
            Configure(face.GetComponent<RawImage>());

            Markers[playerId] = marker;
            return marker;
        }

        private static void Configure(Graphic graphic)
        {
            graphic.raycastTarget = false;
        }

        private static void HideAll()
        {
            foreach (var marker in Markers.Values)
            {
                if (marker != null && marker.activeSelf)
                {
                    marker.SetActive(false);
                }
            }
        }

        /// <summary>
        /// The node widget at a map position, or null if there's nothing to place.
        /// </summary>
        private static MapNodeWidget WidgetAt(MapPanel panel, int x, int y)
        {
            var map = GameMaster.Instance?.CurrentGameRun?.CurrentMap;
            if (map == null || x < 0 || y < 0 || x >= map.Levels || y >= map.Width)
            {
                return null;
            }

            var widgets = panel._mapNodeWidgets;
            if (widgets == null || x >= widgets.GetLength(0) || y >= widgets.GetLength(1))
            {
                return null;
            }

            return widgets[x, y];
        }

        /// <summary>
        /// <c>UiManager.GetPanel</c> throws rather than returning null for a panel that has not been
        /// loaded, and this runs every frame including on screens where it has not been initialized yet.
        /// </summary>
        private static TPanel TryGetPanel<TPanel>() where TPanel : UiPanelBase
        {
            try
            {
                return UiManager.GetPanel<TPanel>();
            }
            catch (InvalidOperationException)
            {
                return null;
            }
        }
    }
}
