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
        /// <summary>Where the line sits, as a fraction of the screen from the bottom left.</summary>
        private readonly Vector2 _anchor;

        private readonly string _name;

        private readonly float _fontScale;

        private readonly float _widthScale;

        private TextMeshProUGUI _text;

        /// <summary>This is set once the template has already been looked for but was missing, to stop retrying indefinitely.</summary>
        private bool _givenUp;

        internal MpBanner(string name, Vector2 anchor, float fontScale = 1f, float widthScale = 1f)
        {
            _name = name;
            _anchor = anchor;
            _fontScale = fontScale;
            _widthScale = widthScale;
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
            BattleNotifier notifier;
            try
            {
                notifier = UiManager.GetPanel<BattleNotifier>();
            }
            catch (InvalidOperationException)
            {
                // Not loaded yet. We're not giving up here because it WILL be present later.
                return null;
            }

            var template = notifier == null ? null : notifier.roundCounter;
            var parent = notifier == null ? null : notifier.transform.parent as RectTransform;

            if (template == null || parent == null)
            {
                MpPlugin.Log.LogWarning(
                    "BattleNotifier has no round counter to copy, so '" + _name + "' stays off");
                _givenUp = true;
                return null;
            }

            var clone = UnityEngine.Object.Instantiate(template, parent, false);
            clone.gameObject.name = _name;
            clone.gameObject.SetActive(false);

            float size = template.fontSize * _fontScale;
            clone.fontSize = size;

            // Re-set size when enabled
            var localized = clone.GetComponent<LocalizedText>();
            if (localized != null)
            {
                localized._originSize = size;
            }

            clone.gameObject.SetActive(true);

            var rect = clone.rectTransform;
            rect.anchorMin = _anchor;
            rect.anchorMax = _anchor;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;

            var box = template.rectTransform.sizeDelta;
            rect.sizeDelta = new Vector2(box.x * _widthScale, box.y * _fontScale);

            clone.alignment = TextAlignmentOptions.Center;
            clone.text = string.Empty;

            rect.SetAsLastSibling();

            _text = clone;
            return _text;
        }
    }
}
