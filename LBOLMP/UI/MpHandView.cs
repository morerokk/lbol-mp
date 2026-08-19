using System;
using System.Collections.Generic;
using LBOLMP.Session;
using LBOLMP.Session.Battle;
using LBoL.Base;
using LBoL.Core.Cards;
using LBoL.Presentation;
using LBoL.Presentation.UI;
using LBoL.Presentation.UI.ExtraWidgets;
using LBoL.Presentation.UI.Panels;
using LBoL.Presentation.UI.Widgets;
using UnityEngine;

namespace LBOLMP.UI
{
    /// <summary>
    /// Puts another player's hand on your board in place of your own to preview (right-click).
    /// </summary>
    public static class MpHandView
    {
        /// <summary>One card on screen.</summary>
        private sealed class Slot
        {
            public HandCard Hand;

            /// <summary>Identity: which card this is, so a redraw can match it up again.</summary>
            public string Key;

            /// <summary>State: everything the widget draws, so an unchanged card is left alone.</summary>
            public string Signature;
        }

        private static readonly List<Slot> Slots = new List<Slot>();

        /// <summary>The card under the mouse, to show the hover effect on.</summary>
        private static HandCard _hovered;

        private static bool _active;
        private static int _shownRevision = -1;
        private static ManaGroup _shownMana;
        private static bool _manaSwapped;
        private static readonly CancelHandler Cancel = new CancelHandler();
        private static bool _handlerPushed;

        /// <summary>True while the currently shown board belongs to somebody else.</summary>
        public static bool Active => _active;

        public static void Tick() => MpSafe.Run("MpHandView", Refresh);

        private static void Refresh()
        {
            var playBoard = TryGetPanel<PlayBoard>();
            var cardUi = playBoard != null ? playBoard.CardUi : null;

            HandleMousePointer(cardUi);

            bool wanted = MpHandInspect.IsInspecting && cardUi != null && MpBattleSync.InBattle;
            if (!wanted)
            {
                if (_active)
                {
                    Leave(cardUi);
                }
                return;
            }

            if (!_active)
            {
                Enter();
            }

            HideOwnHand(cardUi);
            ShowCounts(cardUi);
            ShowMana();

            if (_shownRevision != MpHandInspect.Revision)
            {
                _shownRevision = MpHandInspect.Revision;
                Sync(cardUi);
            }

            Layout(cardUi);
        }

        private static void Enter()
        {
            _active = true;
            _shownRevision = -1;
            _hovered = null;

            UiManager.PushActionHandler(Cancel);
            _handlerPushed = true;
        }

        private static void Leave(CardUi cardUi)
        {
            _active = false;
            _shownRevision = -1;

            Clear();
            RestoreMana();
            ShowOwnHand(cardUi);
            RestoreCounts(cardUi);

            if (_handlerPushed)
            {
                _handlerPushed = false;
                MpSafe.Run("MpHandView.PopHandler", RemoveHandler);
            }
        }

        /// <summary>
        /// Take our handler off the input stack from wherever it has ended up.
        /// </summary>
        private static void RemoveHandler()
        {
            var stack = UiManager.Instance?._actionHandlerStack;
            if (stack == null || stack.Count == 0)
            {
                return;
            }

            var above = new List<IInputActionHandler>();
            bool found = false;

            while (stack.Count > 0)
            {
                var handler = stack.Pop();
                if (ReferenceEquals(handler, Cancel))
                {
                    found = true;
                    break;
                }
                above.Add(handler);
            }

            for (int i = above.Count - 1; i >= 0; i--)
            {
                stack.Push(above[i]);
            }

            if (!found)
            {
                MpPlugin.Log.LogWarning("The hand view's input handler was gone before it closed");
            }
        }

        private static void Clear()
        {
            foreach (var slot in Slots)
            {
                Discard(slot);
            }
            Slots.Clear();
            _hovered = null;
        }

        private static void Sync(CardUi cardUi)
        {
            var spare = new List<Slot>(Slots);
            var next = new List<Slot>();

            foreach (var card in MpHandInspect.Hand)
            {
                string key = Key(card);
                int found = spare.FindIndex(slot => slot.Hand != null && slot.Key == key);

                Slot reused;
                if (found >= 0)
                {
                    reused = spare[found];
                    spare.RemoveAt(found);

                    string signature = Signature(card);
                    if (reused.Signature != signature)
                    {
                        reused.Signature = signature;
                        reused.Hand.CardWidget.Card = card;
                        reused.Hand.RefreshStatus();
                    }
                }
                else
                {
                    reused = Create(cardUi, card);
                    if (reused == null)
                    {
                        continue;
                    }
                }

                next.Add(reused);
            }

            foreach (var leftover in spare)
            {
                Discard(leftover);
            }

            Slots.Clear();
            Slots.AddRange(next);
            ReOrder(cardUi);
        }

        /// <summary>
        /// Put the cards back in hand order as siblings, so each draws over the one to its left.
        /// </summary>
        private static void ReOrder(CardUi cardUi)
        {
            var cache = cardUi.cardHandReorderCache;
            var parent = cardUi.cardHandParent;
            if (cache == null || parent == null)
            {
                return;
            }

            foreach (var slot in Slots)
            {
                if (slot.Hand == null)
                {
                    continue;
                }

                var hand = slot.Hand;
                MpSafe.Run("MpHandView.ReOrder", () =>
                {
                    hand.MoveToParentWhenReordering(cache);
                    hand.MoveToParentWhenReordering(parent);
                });
            }
        }

        private static string Key(Card card) =>
            card == null ? string.Empty : card.Id + (card.IsUpgraded ? "+" : string.Empty);

        private static string Signature(Card card)
        {
            if (card == null)
            {
                return string.Empty;
            }

            return Key(card) + "|" + card.Cost + "|" + card.BaseCost + "|" + card.AuraCost
                   + "|" + (ulong)card.Keywords + "|" + card.Loyalty
                   + "|" + card.UpgradeCounter.GetValueOrDefault() + "|" + card.Summoned;
        }

        // Is THIS your card? No? Now it is.
        // tl;dr: this makes a fake card to put on screen.
        private static Slot Create(CardUi cardUi, Card card)
        {
            var widgetPrefab = cardUi.cardPrefab;
            var handPrefab = cardUi.handCardPrefab;
            var parent = cardUi.cardHandParent;
            if (widgetPrefab == null || handPrefab == null || parent == null)
            {
                return null;
            }

            var widget = UnityEngine.Object.Instantiate(widgetPrefab, parent);
            widget.gameObject.name = "MpInspected: " + card.Id;
            widget.Card = card;
            widget.ShowManaHand = true;

            var hand = UnityEngine.Object.Instantiate(handPrefab, parent);
            hand.gameObject.name = "MpInspectedHand: " + card.Id;
            hand.CardWidget = widget;

            var inner = widget.transform;
            inner.SetParent(hand.cardRoot);
            inner.localPosition = Vector3.zero;
            inner.localScale = Vector3.one;
            inner.localRotation = Quaternion.identity;

            hand.NormalParent = parent;
            hand.HoveredParent = cardUi.cardHoveredParent;
            hand.ActiveHandParent = cardUi.cardHoveredParent;
            hand.SpecialReactingPosition = Vector3.zero;
            hand.SpecialReactingRotation = Quaternion.identity;

            hand.ShowShortcut = false;

            hand.transform.localPosition = cardUi.cardDrawPoint.localPosition;
            hand.transform.localScale = cardUi.cardDrawPoint.localScale;

            return new Slot { Hand = hand, Key = Key(card), Signature = Signature(card) };
        }

        private static void Discard(Slot slot)
        {
            if (slot?.Hand == null)
            {
                return;
            }

            if (ReferenceEquals(slot.Hand, _hovered))
            {
                _hovered = null;
            }

            MpSafe.Run("MpHandView.Discard", () => slot.Hand.CardWidget?.HideTooltip());
            UnityEngine.Object.Destroy(slot.Hand.gameObject);
        }

        private static void Layout(CardUi cardUi)
        {
            for (int i = 0; i < Slots.Count; i++)
            {
                var hand = Slots[i].Hand;
                if (hand == null)
                {
                    continue;
                }

                Place(cardUi, i, Slots.Count, out var resting, out float angle);

                hand.HandIndex = i;
                hand.NormalPosition = resting;
                hand.NormalRotation = Quaternion.Euler(0f, 0f, angle);
                hand.HoveredPosition =
                    new Vector3(resting.x, cardUi.hoveredY + cardUi.handOffset.y);
                hand.HoveredRotation = Quaternion.identity;
            }
        }

        private static void Place(CardUi cardUi, int i, int count, out Vector3 position, out float angle)
        {
            float radius = cardUi._rectTransform.rect.width / cardUi.curvatureRatio;
            float middle = count / 2f - 0.5f;

            float deltaX = cardUi.deltaX;
            if (count >= 12)
            {
                deltaX *= 0.84f;
            }
            else if (count == 11)
            {
                deltaX *= 0.91f;
            }

            float offset = (i - middle) * deltaX;
            float lift = float.IsInfinity(radius)
                ? 0f
                : Mathf.Sqrt(Mathf.Max(0f, radius * radius - offset * offset)) - radius;

            position = (Vector3)(new Vector2(offset, lift) + cardUi.handOffset);
            angle = -cardUi.deltaRotate * (i - middle);
        }

        private static void HideOwnHand(CardUi cardUi)
        {
            foreach (var hand in cardUi._handWidgets)
            {
                if (hand != null && hand.gameObject.activeSelf)
                {
                    hand.gameObject.SetActive(false);
                }
            }
        }

        private static void ShowOwnHand(CardUi cardUi)
        {
            if (cardUi == null)
            {
                return;
            }

            foreach (var hand in cardUi._handWidgets)
            {
                if (hand != null && !hand.gameObject.activeSelf)
                {
                    hand.gameObject.SetActive(true);
                }
            }
        }

        private static void ShowCounts(CardUi cardUi)
        {
            cardUi.DrawCount = MpHandInspect.Draw.Count;
            cardUi.DiscardCount = MpHandInspect.Discard.Count;
            cardUi.ExileCount = MpHandInspect.Exile.Count;
        }

        private static void RestoreCounts(CardUi cardUi)
        {
            var battle = GameMaster.Instance?.CurrentGameRun?.Battle;
            if (cardUi == null || battle == null)
            {
                return;
            }

            cardUi.DrawCount = battle.DrawZone.Count;
            cardUi.DiscardCount = battle.DiscardZone.Count;
            cardUi.ExileCount = battle.ExileZone.Count;
        }

        /// <summary>
        /// Put their mana on the left instead of yours.
        /// </summary>
        private static void ShowMana()
        {
            var panel = TryGetPanel<BattleManaPanel>();
            if (panel == null)
            {
                return;
            }

            var mana = MpHandInspect.Mana;
            if (_manaSwapped && mana == _shownMana)
            {
                return;
            }

            _manaSwapped = true;
            _shownMana = mana;
            panel.ResetAllManas(mana, ManaGroup.Empty, false);
        }

        private static void RestoreMana()
        {
            if (!_manaSwapped)
            {
                return;
            }

            _manaSwapped = false;

            var panel = TryGetPanel<BattleManaPanel>();
            var battle = GameMaster.Instance?.CurrentGameRun?.Battle;
            if (panel != null && battle != null)
            {
                panel.ResetAllManas(battle.BattleMana, ManaGroup.Empty, false);
            }
        }

        private static void HandleMousePointer(CardUi cardUi)
        {
            if (_active)
            {
                Hover(cardUi);
            }

            if (!Input.GetMouseButtonDown(1))
            {
                return;
            }

            if (IsVisible<ShowCardsPanel>() || IsVisible<CardDetailPanel>())
            {
                return;
            }

            if (_active)
            {
                var widget = _hovered != null ? _hovered.CardWidget : null;
                if (widget != null && widget.Card != null)
                {
                    MpSafe.Run("MpHandView.Detail", () =>
                        UiManager.GetPanel<CardDetailPanel>()
                            .Show(new CardDetailPayload(widget.RectTransform, widget.Card)));
                    return;
                }

                MpHandInspect.End();
                return;
            }

            if (cardUi == null || !MpBattleSync.InBattle || UiManager.IsBlockingInput)
            {
                return;
            }

            var playBoard = TryGetPanel<PlayBoard>();
            if (playBoard == null || playBoard._status != PlayBoard.InteractionStatus.Normal)
            {
                return;
            }

            int playerId = Patches.MpHoveredUnit.HoveredPlayer;
            if (playerId != Net.MpConstants.InvalidPlayerId)
            {
                MpHandInspect.Begin(playerId);
            }
        }

        private static void Hover(CardUi cardUi)
        {
            HandCard now = null;

            var parent = cardUi.cardHandParent;
            if (parent != null && Slots.Count > 0
                && RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    parent, Input.mousePosition, ResolveCamera(parent), out var pointer))
            {
                for (int i = Slots.Count - 1; i >= 0; i--)
                {
                    var hand = Slots[i].Hand;
                    if (hand == null || hand.CardWidget == null)
                    {
                        continue;
                    }

                    Place(cardUi, i, Slots.Count, out var resting, out float angle);

                    // Into the card's own frame: undo the fan's rotation about its centre, then
                    // measure against the card at the size it sits at when it is not hovered.
                    var offset = Quaternion.Euler(0f, 0f, -angle) * (pointer - (Vector2)resting);
                    var half = hand.CardWidget.RectTransform.rect.size * (HandCard.NormalScale * 0.5f);

                    if (Mathf.Abs(offset.x) <= half.x && Mathf.Abs(offset.y) <= half.y)
                    {
                        now = hand;
                        break;
                    }
                }
            }

            if (ReferenceEquals(now, _hovered))
            {
                return;
            }

            if (_hovered != null)
            {
                var leaving = _hovered;
                MpSafe.Run("MpHandView.EndHover", () => leaving.EndHover());
            }

            _hovered = now;

            if (_hovered != null)
            {
                var entering = _hovered;
                MpSafe.Run("MpHandView.StartHover", () => entering.StartHover());
            }
        }

        private static Camera ResolveCamera(RectTransform rect)
        {
            var canvas = rect != null ? rect.GetComponentInParent<Canvas>() : null;
            if (canvas == null)
            {
                return null;
            }
            return canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
        }

        private sealed class CancelHandler : IInputActionHandler
        {
            public void OnCancel() => MpSafe.Run("MpHandView.Cancel", MpHandInspect.End);

            // Right-click is handled by the poll above, which can tell a card from the background.
            // Left as a no-op so the interface's default does not close the view a second time.
            public void OnRightClickCancel() { }

            public void OnToggleDrawZone() => MpInspectedPiles.ShowDraw();
            public void OnToggleDiscardZone() => MpInspectedPiles.ShowDiscard();
            public void OnToggleExileZone() => MpInspectedPiles.ShowExile();
            public void OnToggleBaseDeck() => MpInspectedPiles.ShowDeck();
        }

        private static bool IsVisible<TPanel>() where TPanel : UiPanelBase
        {
            var panel = TryGetPanel<TPanel>();
            return panel != null && panel.IsVisible;
        }

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
            catch (NullReferenceException)
            {
                return null;
            }
        }
    }
}
