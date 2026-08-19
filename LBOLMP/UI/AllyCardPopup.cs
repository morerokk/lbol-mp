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

        private static readonly Dictionary<int, GameObject> Active = new Dictionary<int, GameObject>();

        public static void Show(int playerId, string cardId, bool upgraded)
        {
            MpSafe.Run("AllyCardPopup", () =>
            {
                Dismiss(playerId);

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

                var widget = Object.Instantiate(prefab, parent);
                widget.gameObject.name = "MpAllyCard: " + cardId;
                widget.Card = card;
                widget.TooltipEnabled = false;

                var rect = widget.RectTransform;
                rect.localScale = Vector3.one * Scale;
                rect.SetAsLastSibling();

                Active[playerId] = widget.gameObject;
                MpPlugin.Instance.StartCoroutine(Run(playerId, widget, parent));
            });
        }

        private static IEnumerator Run(int playerId, CardWidget widget, RectTransform parent)
        {
            var group = widget.CanvasGroup;
            float elapsed = 0f;

            while (widget != null && elapsed < RiseSeconds + HoldSeconds + FadeSeconds)
            {
                elapsed += Time.unscaledDeltaTime;

                float rise = elapsed < RiseSeconds
                    ? RisePixels * (1f - Mathf.Pow(1f - elapsed / RiseSeconds, 3f))
                    : RisePixels;

                if (MpAllyUnits.TryGetHeadScreenPoint(playerId, out var screenPoint))
                {
                    var camera = ResolveCamera(parent);
                    if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
                            parent, screenPoint, camera, out var local))
                    {
                        widget.RectTransform.anchoredPosition = local + new Vector2(0f, rise);
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

            Dismiss(playerId);
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
            if (!Active.TryGetValue(playerId, out var existing))
            {
                return;
            }

            Active.Remove(playerId);
            if (existing != null)
            {
                Object.Destroy(existing);
            }
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
