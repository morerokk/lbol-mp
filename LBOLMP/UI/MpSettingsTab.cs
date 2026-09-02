using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using LBoL.Presentation.I10N;
using LBoL.Presentation.UI.ExtraWidgets;
using LBoL.Presentation.UI.Panels;
using LBoL.Presentation.UI.Widgets;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace LBOLMP.UI
{
    /// <summary>
    /// A fourth tab in the game's own options panel, holding the multiplayer settings.
    /// </summary>
    internal static class MpSettingsTab
    {
        /// <summary>Our page's position in <see cref="SettingPanel.tabs"/>, or -1 before it exists.</summary>
        internal static int Index { get; private set; } = -1;

        private static Toggle _tab;
        private static CanvasGroup _page;

        private static bool _givenUp;

        private static readonly List<Action> Refreshers = new List<Action>();

        internal static bool Owns(Toggle toggle) => _tab != null && toggle == _tab;

        /// <summary>Builds the tab the first time the options are opened, then refreshes it.</summary>
        internal static void Attach(SettingPanel panel)
        {
            if (_givenUp || panel == null)
            {
                return;
            }

            if (_page == null)
            {
                MpSafe.Run("MpSettingsTab.Build", () => Build(panel));
            }

            foreach (var refresh in Refreshers)
            {
                MpSafe.Run("MpSettingsTab.Refresh", refresh);
            }
        }

        private static void Build(SettingPanel panel)
        {
            var tabs = panel.tabGroup == null ? null : panel.tabGroup.transform;
            var pages = panel.tabs;

            // The three pages we take our parts from.
            var preference = Find(panel.transform, "Root/Preference");
            var main = Find(panel.transform, "Root/Main");
            var keyMapping = Find(panel.transform, "Root/KeyMapping");

            if (tabs == null || pages == null || preference == null || main == null || keyMapping == null)
            {
                MpPlugin.Log.LogWarning("The options panel is not laid out the way this mod expects! Will not add the multiplayer tab to settings.");
                _givenUp = true;
                return;
            }

            var sliderTemplate = Find(main, "RightPanel/Master");
            var switchTemplate = Find(preference, "LeftPanel/TurboMode");
            var buttonTemplate = Find(keyMapping, "ResetDefault");

            if (sliderTemplate == null || switchTemplate == null || buttonTemplate == null)
            {
                MpPlugin.Log.LogWarning("Could not find a row to copy in the options panel! Will not add the multiplayer tab to settings.");
                _givenUp = true;
                return;
            }

            BuildTabButton(tabs);
            BuildPage(preference, pages);

            var left = Find(_page.transform, "LeftPanel");
            var right = Find(_page.transform, "RightPanel");

            BuildRows(left, right, sliderTemplate, switchTemplate, buttonTemplate);

            MpPlugin.Log.LogInfo($"Multiplayer settings added to the options panel as tab {Index}");
        }

        //--
        // the tab button
        //--

        private static void BuildTabButton(Transform tabs)
        {
            // The last one, so ours inherits whatever a mod before us may have added.
            var template = tabs.GetChild(tabs.childCount - 1);

            var clone = UnityEngine.Object.Instantiate(template, tabs, false);
            clone.gameObject.name = TabName;

            _tab = clone.GetComponent<Toggle>();
            if (_tab != null)
            {
                _tab.isOn = false;
            }

            var label = Find(clone, "Text (TMP)");
            if (label != null)
            {
                var text = label.GetComponent<TextMeshProUGUI>();
                Relabel(text, L10n.Get(MpText.SettingsTabTitle));

                // The left margin exists to clear the icon, which we are hiding: without resetting
                // it the label sits off to the right of its own button.
                if (text != null)
                {
                    var margin = text.margin;
                    text.margin = new Vector4(margin.z, margin.y, margin.z, margin.w);
                }
            }

            // No icon of our own, and a copy of the Controls one would just read as a mistake.
            var icon = Find(clone, "Icon");
            if (icon != null)
            {
                icon.gameObject.SetActive(false);
            }
        }

        /// <summary>The toggle's own name, which the game's tab handler switches on.</summary>
        private const string TabName = "MpMultiplayer";

        //--
        // the page
        //--

        private static void BuildPage(Transform template, List<CanvasGroup> pages)
        {
            var clone = UnityEngine.Object.Instantiate(template, template.parent, false);
            clone.gameObject.name = "MpMultiplayerPage";

            _page = clone.GetComponent<CanvasGroup>();
            if (_page != null)
            {
                _page.alpha = 0f;
            }

            // Emptied rather than rebuilt: the two grid columns keep their metrics this way.
            foreach (var column in new[] { Find(clone, "LeftPanel"), Find(clone, "RightPanel") })
            {
                if (column == null)
                {
                    continue;
                }

                for (int i = column.childCount - 1; i >= 0; i--)
                {
                    UnityEngine.Object.Destroy(column.GetChild(i).gameObject);
                }
            }

            clone.gameObject.SetActive(false);

            pages.Add(_page);
            Index = pages.Count - 1;
        }

        //--
        // the rows
        //--

        private static void BuildRows(Transform left, Transform right,
            Transform sliderTemplate, Transform switchTemplate, Transform buttonTemplate)
        {
            Slider(left, sliderTemplate, MpPlugin.EnemyHpScalePerExtraPlayer,
                () => L10n.Get(MpText.SettingEnemyHpScaleName),
                () => L10n.Get(MpText.SettingEnemyHpScaleHelp),
                0f, 4f, Percent);

            var acts = MpPlugin.EnemyHpEscalationByAct;
            for (int act = 0; acts != null && act < acts.Length; act++)
            {
                int number = act + 1;

                Slider(left, sliderTemplate, acts[act],
                    () => L10n.Get(MpText.SettingEscalationName, number),
                    () => L10n.Get(MpText.SettingEscalationHelp),
                    0f, 1f, Percent);
            }

            Slider(left, sliderTemplate, MpPlugin.ReviveHpFraction,
                () => L10n.Get(MpText.SettingReviveHpName),
                () => L10n.Get(MpText.SettingReviveHpHelp),
                0.01f, 1f, Fraction);

            // Switches and the reset on the right.
            Switch(right, switchTemplate, MpPlugin.EnableEnemyResilience,
                () => L10n.Get(MpText.SettingResilienceName),
                () => L10n.Get(MpText.SettingResilienceHelp));
            Switch(right, switchTemplate, MpPlugin.MultiplayerCardsEnabled,
                () => L10n.Get(MpText.SettingMultiplayerCardsName),
                () => Config(MpPlugin.MultiplayerCardsEnabled));
            Switch(right, switchTemplate, MpPlugin.SharedPartyPositions,
                () => L10n.Get(MpText.SettingSharedPartyPositionsName),
                () => Config(MpPlugin.SharedPartyPositions));
            Switch(right, switchTemplate, MpPlugin.ShowPlayerNamesOnCards,
                () => L10n.Get(MpText.SettingPlayerNamesOnCardsName),
                () => Config(MpPlugin.ShowPlayerNamesOnCards));

            ResetButton(buttonTemplate);
        }

        private static string Config(ConfigEntryBase entry) =>
            entry == null || entry.Description == null ? null : entry.Description.Description;

        private static void Tooltip(Transform row, Func<string> title, Func<string> help)
        {
            if (row == null || title == null)
            {
                return;
            }

            SimpleTooltipSource.CreateWithGetter(row.gameObject, title, help);
        }

        private static string Percent(float value) => "+" + Mathf.RoundToInt(value * 100f) + "%";

        private static string Fraction(float value) => Mathf.RoundToInt(value * 100f) + "%";

        private static void Slider(Transform column, Transform template, ConfigEntry<float> entry,
            Func<string> label, Func<string> help, float min, float max, Func<float, string> format)
        {
            if (column == null || entry == null)
            {
                return;
            }

            var row = UnityEngine.Object.Instantiate(template, column, false);
            row.gameObject.name = "Mp" + entry.Definition.Key;

            var caption = Component<TextMeshProUGUI>(row, "KeyTmp");
            Relabel(caption, label());
            Tooltip(row, label, help);

            var value = Component<TextMeshProUGUI>(row, "ValueTmp");
            var slider = Component<Slider>(row, "Slider");
            if (slider == null)
            {
                return;
            }

            Silence(slider.onValueChanged);
            slider.onValueChanged.RemoveAllListeners();

            slider.minValue = min;
            slider.maxValue = max;
            slider.wholeNumbers = false;

            slider.onValueChanged.AddListener(v => MpSafe.Run("MpSettingsTab.Slider", () =>
            {
                entry.Value = v;
                if (value != null)
                {
                    value.text = format(v);
                }
            }));

            Refreshers.Add(() =>
            {
                float current = Mathf.Clamp(entry.Value, min, max);
                slider.SetValueWithoutNotify(current);
                if (value != null)
                {
                    value.text = format(current);
                }

                Relabel(caption, label());
            });
        }

        private static void Switch(Transform column, Transform template, ConfigEntry<bool> entry,
            Func<string> label, Func<string> help)
        {
            if (column == null || entry == null)
            {
                return;
            }

            var row = UnityEngine.Object.Instantiate(template, column, false);
            row.gameObject.name = "Mp" + entry.Definition.Key;

            var caption = Component<TextMeshProUGUI>(row, "KeyTmp");
            Relabel(caption, label());

            Tooltip(row, label, help);

            var toggle = Component<SwitchWidget>(row, "Switch");
            if (toggle == null)
            {
                return;
            }

            Silence(toggle.onToggleChanged);
            toggle.onToggleChanged.RemoveAllListeners();
            toggle.onToggleChanged.AddListener(on =>
                MpSafe.Run("MpSettingsTab.Switch", () => entry.Value = on));

            Refreshers.Add(() =>
            {
                toggle.SetValueWithoutNotifier(entry.Value);
                Relabel(caption, label());
            });
        }

        private static void ResetButton(Transform template)
        {
            var clone = UnityEngine.Object.Instantiate(template, _page.transform, false);
            clone.gameObject.name = "MpResetDefaults";

            Relabel(clone.GetComponentInChildren<TextMeshProUGUI>(true), L10n.Get(MpText.SettingsResetAll));

            var button = clone.GetComponent<Button>();
            if (button == null)
            {
                return;
            }

            Silence(button.onClick);
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(() => MpSafe.Run("MpSettingsTab.Reset", ResetAll));
        }

        private static void ResetAll()
        {
            foreach (var entry in Entries())
            {
                entry.BoxedValue = entry.DefaultValue;
            }

            foreach (var refresh in Refreshers)
            {
                MpSafe.Run("MpSettingsTab.Refresh", refresh);
            }

            MpPlugin.Log.LogInfo("Multiplayer settings put back to their defaults");
        }

        private static IEnumerable<ConfigEntryBase> Entries()
        {
            yield return MpPlugin.EnemyHpScalePerExtraPlayer;
            yield return MpPlugin.ReviveHpFraction;
            yield return MpPlugin.EnableEnemyResilience;
            yield return MpPlugin.MultiplayerCardsEnabled;
            yield return MpPlugin.SharedPartyPositions;
            yield return MpPlugin.ShowPlayerNamesOnCards;

            var acts = MpPlugin.EnemyHpEscalationByAct;
            for (int act = 0; acts != null && act < acts.Length; act++)
            {
                yield return acts[act];
            }
        }

        //--
        // shared cloning chores
        //--

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

        private static Transform Find(Transform root, string path) =>
            root == null ? null : root.Find(path);

        private static T Component<T>(Transform row, string path) where T : Component
        {
            var child = Find(row, path);
            return child == null ? null : child.GetComponent<T>();
        }
    }
}
