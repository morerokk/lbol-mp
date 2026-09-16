using System;
using LBoL.Presentation.I10N;
using LBoL.Presentation.UI;
using LBoL.Presentation.UI.Panels;
using TMPro;
using UnityEngine;

namespace LBOLMP.UI
{
    /// <summary>
    /// One line of text in the game's own font/rendering, can be displayed on screen for as long as the mod wants to display a message.
    /// </summary>
    /// <remarks>
    /// Cloned from <see cref="BattleNotifier"/>'s round counter.
    /// </remarks>
    internal sealed class MpBanner
    {
        /// <summary>
        /// Which of the game's own bits of text a banner copies itself from.
        /// </summary>
        internal enum Source
        {
            /// <summary>The round counter, which only exists inside a fight.</summary>
            Battle,

            /// <summary>A main menu entry's label, which is only before a run starts.</summary>
            Menu
        }

        /// <summary>Where the line sits, as a fraction of the screen from the bottom left.</summary>
        private readonly Vector2 _anchor;

        private readonly string _name;

        private readonly float _fontScale;

        private readonly float _widthScale;

        private readonly Source _source;

        private TextMeshProUGUI _text;

        /// <summary>This is set once the template has already been looked for but was missing, to stop retrying indefinitely.</summary>
        private bool _givenUp;

        internal MpBanner(string name, Vector2 anchor, float fontScale = 1f, float widthScale = 1f,
                          Source source = Source.Battle)
        {
            _name = name;
            _anchor = anchor;
            _fontScale = fontScale;
            _widthScale = widthScale;
            _source = source;
        }

        internal void Show(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                Hide();
                return;
            }

            var line = Ensure();
            if (line == null)
            {
                return;
            }

            if (!line.gameObject.activeSelf)
            {
                line.gameObject.SetActive(true);
            }

            // Assigning text rebuilds the mesh, so only when it has actually changed.
            if (line.text != text)
            {
                line.text = text;
            }
        }

        internal void Hide()
        {
            if (_text != null && _text.gameObject.activeSelf)
            {
                _text.gameObject.SetActive(false);
            }
        }

        private TextMeshProUGUI Ensure()
        {
            if (_text != null)
            {
                return _text;
            }

            return _givenUp ? null : MpSafe.Run("MpBanner.Build", Build, null);
        }

        private TextMeshProUGUI Build()
        {
            TextMeshProUGUI template;
            RectTransform parent;

            bool loaded = _source == Source.Menu
                ? MenuTemplate(out template, out parent)
                : BattleTemplate(out template, out parent);

            // The panel we copy is not up yet. It will be, so this is not a reason to give up.
            if (!loaded)
            {
                return null;
            }

            if (template == null || parent == null)
            {
                MpPlugin.Log.LogWarning(
                    "Found no text of the game's own to copy, so '" + _name + "' stays off");
                _givenUp = true;
                return null;
            }

            var clone = UnityEngine.Object.Instantiate(template, parent, false);
            clone.gameObject.name = _name;
            clone.gameObject.SetActive(false);

            float size;
            Vector2 box;
            if (_source == Source.Menu)
            {
                var screen = parent.rect;
                size = screen.height * MenuFontShare * _fontScale;
                box = new Vector2(screen.width * _widthScale, size * 2f);

                clone.enableAutoSizing = false;
                clone.textWrappingMode = TextWrappingModes.NoWrap;
                clone.overflowMode = TextOverflowModes.Overflow;
            }
            else
            {
                size = template.fontSize * _fontScale;
                var templateBox = template.rectTransform.sizeDelta;
                box = new Vector2(templateBox.x * _widthScale, templateBox.y * _fontScale);
            }

            clone.fontSize = size;

            clone.raycastTarget = false;

            var localized = clone.GetComponent<LocalizedText>();
            if (localized != null)
            {
                localized.key = string.Empty;
                localized._originSize = size;
            }

            clone.gameObject.SetActive(true);

            var rect = clone.rectTransform;
            rect.anchorMin = _anchor;
            rect.anchorMax = _anchor;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;

            rect.sizeDelta = box;

            clone.alignment = TextAlignmentOptions.Center;
            clone.text = string.Empty;

            rect.SetAsLastSibling();

            _text = clone;
            return _text;
        }

        /// <summary>The round counter, and the notifier's own place on the board.</summary>
        private static bool BattleTemplate(out TextMeshProUGUI template, out RectTransform parent)
        {
            template = null;
            parent = null;

            BattleNotifier notifier;
            try
            {
                notifier = UiManager.GetPanel<BattleNotifier>();
            }
            catch (InvalidOperationException)
            {
                return false;
            }

            template = notifier == null ? null : notifier.roundCounter;
            parent = notifier == null ? null : notifier.transform.parent as RectTransform;
            return true;
        }

        /// <summary>The label off a main menu entry, on the layer that outlives every panel.</summary>
        private const string MenuLabelPath = "Main/MainMenuLayout/Setting/Text";

        /// <summary>A menu banner's font size, as a share of the screen's height.</summary>
        private const float MenuFontShare = 0.035f;

        private static bool MenuTemplate(out TextMeshProUGUI template, out RectTransform parent)
        {
            template = null;
            parent = null;

            MainMenuPanel menu;
            try
            {
                menu = UiManager.GetPanel<MainMenuPanel>();
            }
            catch (InvalidOperationException)
            {
                return false;
            }

            var label = menu == null ? null : menu.transform.Find(MenuLabelPath);
            template = label == null ? null : label.GetComponent<TextMeshProUGUI>();

            // The menu hides itself behind character select, so the banner cannot live under it.
            parent = UiManager.Instance == null ? null : UiManager.Instance.topmostLayer;
            return true;
        }
    }
}
