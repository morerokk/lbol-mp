using System;
using System.Collections.Generic;
using System.Linq;
using LBOLMP.Session;
using LBoL.Base;
using LBoL.Core;
using LBoL.Presentation.I10N;
using LBoL.Presentation.InputSystemExtend;
using LBoL.Presentation.UI.Panels;
using LBoL.Presentation.UI.Widgets;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace LBOLMP.UI
{
    /// <summary>
    /// All this code is meant to handle the multiplayer-only filter in the collection in the main menu
    /// Bit long, innit? I hate UI
    /// </summary>
    internal static class MpMuseumFilter
    {
        private const string HeaderName = "MpMultiplayerHeader";
        private const string GridName = "MpMultiplayerFilter";

        private const CardType Template = CardType.Friend;

        private static Toggle _toggle;
        private static TextMeshProUGUI _header;
        private static TextMeshProUGUI _label;
        private static bool _givenUp;

        internal static bool Active => _toggle != null && !_toggle.isOn;

        // Builds the section if this panel doesn't have it already
        internal static void Attach(MuseumPanel panel)
        {
            if (_givenUp || panel == null)
            {
                return;
            }

            // Destroyed along with the old panel when the scene reloads
            if (_toggle == null)
            {
                Build(panel);
            }

            if (_toggle == null)
            {
                return;
            }

            _toggle.SetIsOnWithoutNotify(true);
            Relabel(_header, L10n.Get(MpText.MainMenuMultiplayer));
            Relabel(_label, L10n.Get(MpText.MainMenuMultiplayer));
        }

        /// <summary>Only the multiplayer-exclusive cards out of <paramref name="types"/>.</summary>
        internal static IEnumerable<Type> Narrow(IEnumerable<Type> types)
        {
            // MpCardAvailability is what actually keeps these out of singleplayer, so it decides
            var exclusive = new HashSet<Type>(Library.EnumerateCardTypes()
                .Where(entry => MpCardAvailability.IsMultiplayerOnly(entry.Item2.Id))
                .Select(entry => entry.Item1));

            return types.Where(exclusive.Contains).ToList();
        }

        private static void Build(MuseumPanel panel)
        {
            var types = panel.typeFilterList;
            if (types == null || !types.TryGetValue(Template, out var template) || template == null)
            {
                GiveUp("no Teammate toggle to copy");
                return;
            }

            var grid = template.transform.parent;
            var section = grid == null ? null : grid.parent;
            if (section == null || types.Any(pair => pair.Value != null && pair.Value.transform.parent != grid))
            {
                GiveUp("the card type toggles do not share one parent");
                return;
            }

            int index = grid.GetSiblingIndex();
            var header = index > 0 ? section.GetChild(index - 1) : null;
            if (header == null || header.GetComponentInChildren<Toggle>(true) != null
                || header.GetComponentInChildren<TextMeshProUGUI>(true) == null)
            {
                GiveUp("no \"Card Types\" header above the toggles");
                return;
            }

            if (section.GetComponent<LayoutGroup>() == null)
            {
                MpPlugin.Log.LogWarning($"Museum filter section '{section.name}' has no layout group; the Multiplayer filter may overlap the card types");
            }

            var headerClone = UnityEngine.Object.Instantiate(header, section, false);
            headerClone.name = HeaderName;
            headerClone.SetSiblingIndex(index + 1);
            Strip(headerClone);
            _header = headerClone.GetComponentInChildren<TextMeshProUGUI>(true);

            var gridClone = UnityEngine.Object.Instantiate(grid, section, false);
            gridClone.name = GridName;
            gridClone.SetSiblingIndex(index + 2);

            var keep = gridClone.GetChild(template.transform.GetSiblingIndex());
            for (int i = gridClone.childCount - 1; i >= 0; i--)
            {
                var child = gridClone.GetChild(i);
                if (child != keep)
                {
                    // Inactive right away so the layout ignores it before Destroy lands
                    child.gameObject.SetActive(false);
                    UnityEngine.Object.Destroy(child.gameObject);
                }
            }

            OneRow(gridClone);
            Strip(keep);
            _label = keep.GetComponentInChildren<TextMeshProUGUI>(true);

            _toggle = keep.GetComponent<Toggle>();
            Silence(_toggle.onValueChanged);
            _toggle.onValueChanged.AddListener(_ =>
                MpSafe.Run("MpMuseumFilter.Toggle", panel.RefreshCards));

            MpPlugin.Log.LogInfo($"Multiplayer filter added to the Museum under '{section.name}'");
        }

        private static void OneRow(Transform grid)
        {
            var layout = grid.GetComponent<GridLayoutGroup>();
            var rect = grid as RectTransform;
            if (layout == null || rect == null || !Mathf.Approximately(rect.anchorMin.y, rect.anchorMax.y))
            {
                return;
            }

            rect.sizeDelta = new Vector2(rect.sizeDelta.x, layout.padding.vertical + layout.cellSize.y);
        }

        private static void Strip(Transform clone)
        {
            foreach (var tooltip in clone.GetComponentsInChildren<TooltipSource>(true))
            {
                UnityEngine.Object.Destroy(tooltip);
            }

            foreach (var origin in clone.GetComponentsInChildren<GamepadNavigationOrigin>(true))
            {
                UnityEngine.Object.Destroy(origin);
            }
        }

        private static void Silence(UnityEventBase e)
        {
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

        private static void GiveUp(string why)
        {
            _givenUp = true;
            MpPlugin.Log.LogWarning($"The Museum is not laid out the way LBOL MP expects ({why}). Multiplayer filter was not added.");
        }
    }
}
