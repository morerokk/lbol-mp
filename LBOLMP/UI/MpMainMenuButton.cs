using LBoL.Presentation.I10N;
using LBoL.Presentation.UI.Panels;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace LBOLMP.UI
{
    internal static class MpMainMenuButton
    {
        private const string Name = "MpMultiplayer";

        private const string LayoutPath = "Main/MainMenuLayout";
        private const string Template = "Setting";

        private static void Place(Transform layout, RectTransform clone, RectTransform settings)
        {
            var siblings = Entries(layout, clone);

            float anchor = settings.anchoredPosition.y;
            float step = Step(siblings, anchor);

            if (step <= 0f)
            {
                MpPlugin.Log.LogWarning("Could not measure the main menu's spacing! The multiplayer button is going to look wrong.");
                clone.anchoredPosition = settings.anchoredPosition;
                return;
            }

            float? above = null;
            foreach (var entry in siblings)
            {
                float y = entry.anchoredPosition.y;
                if (y > anchor && (above == null || y < above.Value))
                {
                    above = y;
                }
            }

            bool room = above == null || above.Value - anchor >= step * 2f - 1f;

            if (!room)
            {
                foreach (var entry in siblings)
                {
                    if (entry.anchoredPosition.y <= anchor)
                    {
                        entry.anchoredPosition -= new Vector2(0f, step);
                    }
                }
            }

            clone.anchoredPosition = new Vector2(settings.anchoredPosition.x, anchor + (room ? step : 0f));
        }

        private static float Step(RectTransform[] siblings, float anchor)
        {
            float? below = null;
            foreach (var entry in siblings)
            {
                float y = entry.anchoredPosition.y;
                if (y < anchor && (below == null || y > below.Value))
                {
                    below = y;
                }
            }

            return below == null ? 0f : anchor - below.Value;
        }

        /// <summary>
        /// The menu's entries, our own excluded.
        /// </summary>
        private static RectTransform[] Entries(Transform layout, RectTransform ours)
        {
            var found = new System.Collections.Generic.List<RectTransform>();

            for (int i = 0; i < layout.childCount; i++)
            {
                var child = layout.GetChild(i) as RectTransform;
                if (child != null && child != ours)
                {
                    found.Add(child);
                }
            }

            return found.ToArray();
        }

        internal static void Attach(MainMenuPanel panel)
        {
            if (panel == null)
            {
                return;
            }

            var layout = panel.transform.Find(LayoutPath);
            var template = layout == null ? null : layout.Find(Template);

            if (layout == null || template == null)
            {
                MpPlugin.Log.LogWarning("The main menu is not laid out the way LBOL MP expects! Multiplayer button was not added.");
                return;
            }

            if (layout.Find(Name) != null)
            {
                return;
            }

            Build(layout, (RectTransform)template);
        }

        private static void Build(Transform layout, RectTransform template)
        {
            var clone = Object.Instantiate(template, layout, false);
            clone.gameObject.name = Name;
            clone.gameObject.SetActive(true);

            Place(layout, clone, template);

            // Set the correct sibling index so the button slide-in is correct
            clone.SetSiblingIndex(template.GetSiblingIndex());

            var label = clone.Find("Text");
            Relabel(label == null ? null : label.GetComponent<TextMeshProUGUI>(),
                L10n.Get(MpText.MainMenuMultiplayer));

            var button = clone.GetComponent<Button>();
            if (button == null)
            {
                return;
            }

            Silence(button.onClick);
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(() =>
                MpSafe.Run("MpMainMenuButton.Click", LobbyOverlay.Toggle));
        }

        private static void Silence(UnityEventBase e)
        {
            if (e == null)
            {
                return;
            }

            for (int i = 0; i < e.GetPersistentEventCount(); i++)
            {
                e.SetPersistentListenerState(i, UnityEventCallState.Off);
            }
        }

        private static void Relabel(TextMeshProUGUI text, string label)
        {
            if (text == null)
            {
                return;
            }

            var localized = text.GetComponent<LocalizedText>();
            if (localized != null)
            {
                localized.key = string.Empty;
            }

            text.text = label;
        }
    }
}
