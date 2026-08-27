using System.Collections;
using System.Collections.Generic;
using LBoL.Core;
using LBoL.Presentation;
using LBoL.Presentation.UI;
using LBoL.Presentation.UI.Panels;
using LBoL.Presentation.UI.Widgets;
using UnityEngine;

namespace LBOLMP.UI
{
    /// <summary>
    /// Shows the card another player just played, using the game's own <see cref="CardWidget"/>.
    /// </summary>
    public static class AllyCardPopup
    {
        private const float HoldSeconds = 1.15f;
        private const float FadeSeconds = 0.35f;
        private const float RiseSeconds = 0.18f;
        private const float RisePixels = 40f;

        private const float Scale = 0.25f;
        private const float Alpha = 0.85f;

        /// <summary>How far apart concurrent popups sit, as a fraction of a card's own width.</summary>
        private const float FanFraction = 0.3f;

        /// <summary>
        /// How many of one player's cards can be on screen together.
        /// </summary>
        private const int MaxStack = 4;

        /// <summary>
        /// Every popup currently on screen, oldest first, per player.
        /// </summary>
        private static readonly Dictionary<int, List<GameObject>> Active =
            new Dictionary<int, List<GameObject>>();

        public static void Show(int playerId, string cardId, bool upgraded)
        {
            MpSafe.Run("AllyCardPopup", () =>
            {
                var playBoard = UiManager.GetPanel<PlayBoard>();
                var prefab = playBoard?.CardUi?.cardPrefab;
                if (prefab == null)
                {
                    return;
                }

                // Deliberately *not* anywhere in the play board's hierarchy.
                var parent = GetOwnLayer();
                if (parent == null)
                {
                    return;
                }

                var card = Library.TryCreateCard(cardId, upgraded);
                if (card == null)
                {
                    MpPlugin.Log.LogWarning("Unknown card over the wire: " + cardId);
                    return;
                }

                // The widget reads the card's description, which often requires current run parameters to display correctly.
                card.GameRun = GameMaster.Instance?.CurrentGameRun;
                Session.MpCardOwner.Set(card, playerId);

                var widget = Object.Instantiate(prefab, parent);
                widget.gameObject.name = "MpAllyCard: " + cardId;
                widget.Card = card;
                widget.TooltipEnabled = false;

                var rect = widget.RectTransform;
                rect.localScale = Vector3.one * Scale;
                rect.SetAsLastSibling();

                var live = LiveFor(playerId);

                // Somebody is chaining cards faster than these can fade. Drop the oldest rather
                // than letting the fan grow across the screen.
                while (live.Count >= MaxStack)
                {
                    Retire(playerId, live[0]);
                }

                live.Add(widget.gameObject);
                MpPlugin.Instance.StartCoroutine(Run(playerId, widget, parent));
            });
        }

        private static IEnumerator Run(int playerId, CardWidget widget, RectTransform parent)
        {
            // Kept separately from the widget to avoid certain card popups not showing up at all
            var own = widget.gameObject;

            var group = widget.CanvasGroup;
            float elapsed = 0f;

            float fan = FanOffset(playerId, own, widget);

            while (widget != null && elapsed < RiseSeconds + HoldSeconds + FadeSeconds)
            {
                elapsed += Time.unscaledDeltaTime;

                float rise = elapsed < RiseSeconds
                    ? RisePixels * (1f - Mathf.Pow(1f - elapsed / RiseSeconds, 3f))
                    : RisePixels;

                fan = Mathf.Lerp(fan, FanOffset(playerId, own, widget),
                    1f - Mathf.Exp(-14f * Time.unscaledDeltaTime));

                if (MpAllyUnits.TryGetHeadScreenPoint(playerId, out var screenPoint))
                {
                    var camera = ResolveCamera(parent);
                    if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
                            parent, screenPoint, camera, out var local))
                    {
                        widget.RectTransform.anchoredPosition = local + new Vector2(fan, rise);
                    }
                }

                if (group != null)
                {
                    float fadeStart = RiseSeconds + HoldSeconds;
                    group.alpha = elapsed <= fadeStart
                        ? Alpha
                        : Alpha * Mathf.Clamp01(1f - (elapsed - fadeStart) / FadeSeconds);
                }

                yield return null;
            }

            Retire(playerId, own);
        }

        private static List<GameObject> LiveFor(int playerId)
        {
            if (!Active.TryGetValue(playerId, out var live))
            {
                live = new List<GameObject>();
                Active[playerId] = live;
            }

            return live;
        }

        /// <summary>
        /// How far to one side this popup sits, so that a few of them stay readable.
        /// </summary>
        private static float FanOffset(int playerId, GameObject own, CardWidget widget)
        {
            var live = LiveFor(playerId);
            int index = IndexOfOwn(live, own);
            if (index < 0 || live.Count < 2)
            {
                return 0f;
            }

            float width = widget.RectTransform.rect.width;
            float step = (width > 1f ? width : 300f) * Scale * FanFraction;

            return (index - (live.Count - 1) * 0.5f) * step;
        }

        /// <summary>
        /// Take one popup off screen. Only ever removes its own entry.
        /// </summary>
        private static void Retire(int playerId, GameObject own)
        {
            if (Active.TryGetValue(playerId, out var live))
            {
                int index = IndexOfOwn(live, own);
                if (index >= 0)
                {
                    live.RemoveAt(index);
                }
            }

            if (own != null)
            {
                Object.Destroy(own);
            }
        }

        private static int IndexOfOwn(List<GameObject> live, GameObject own)
        {
            for (int i = 0; i < live.Count; i++)
            {
                // ReferenceEquals on purpose, because of Unity's weird null checks.
                if (ReferenceEquals(live[i], own))
                {
                    return i;
                }
            }

            return -1;
        }

        private static RectTransform _layer;

        /// <summary>
        /// Our own screen-space canvas, created once. Sorting order sits above the board right now.
        /// TODO: This should probably be below the "select a card" dialog though.
        /// </summary>
        private static RectTransform GetOwnLayer()
        {
            if (_layer != null)
            {
                return _layer;
            }

            var host = new GameObject("MpAllyCardLayer");
            Object.DontDestroyOnLoad(host);

            var canvas = host.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 500;

            var scaler = host.AddComponent<UnityEngine.UI.CanvasScaler>();
            scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            _layer = host.GetComponent<RectTransform>();
            return _layer;
        }

        private static Camera ResolveCamera(RectTransform parent)
        {
            var canvas = parent.GetComponentInParent<Canvas>();
            if (canvas == null)
            {
                return null;
            }

            return canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
        }

        public static void Dismiss(int playerId)
        {
            if (!Active.TryGetValue(playerId, out var live))
            {
                return;
            }

            // Emptied rather than deleted
            foreach (var popup in live.ToArray())
            {
                if (popup != null)
                {
                    Object.Destroy(popup);
                }
            }

            live.Clear();
        }

        public static void DismissAll()
        {
            foreach (var id in new List<int>(Active.Keys))
            {
                Dismiss(id);
            }
        }
    }
}
