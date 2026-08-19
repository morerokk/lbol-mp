using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using BepInEx.Configuration;
using LBOLMP.Session;
using UnityEngine;

namespace LBOLMP.UI
{
    /// <summary>
    /// The Balance section of the config file, on screen.
    ///
    /// These settings decide how much harder the game gets with more players.
    /// </summary>
    internal sealed class BalanceSettingsWindow
    {
        private const int WindowId = 0x1B02;
        private const string Section = "Balance";

        internal bool Visible;

        private Rect _window = new Rect(120f, 90f, 560f, 520f);
        private Vector2 _scroll;

        private readonly Dictionary<string, string> _typing = new Dictionary<string, string>();

        private GUIStyle _nameStyle;
        private GUIStyle _helpStyle;
        private GUIStyle _noteStyle;

        internal void Draw()
        {
            if (!Visible)
            {
                return;
            }

            EnsureStyles();
            _window = GUI.Window(WindowId, _window, DrawWindow,
                L10n.Get(MpText.SettingsWindowTitle), MpGui.Window);
        }

        private void EnsureStyles()
        {
            if (_nameStyle != null)
            {
                return;
            }

            _nameStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 14,
                fontStyle = FontStyle.Bold,
                wordWrap = true
            };

            _helpStyle = new GUIStyle(GUI.skin.label) { fontSize = 12, wordWrap = true };

            _noteStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                fontStyle = FontStyle.Italic,
                wordWrap = true
            };
        }

        private void DrawWindow(int id)
        {
            GUILayout.Space(4f);
            GUILayout.Label(L10n.Get(MpText.SettingsIntro), _helpStyle);
            GUILayout.Space(2f);
            GUILayout.Label(L10n.Get(MpText.SettingsHostNote), _noteStyle);

            if (MpSession.IsInRun)
            {
                GUILayout.Label(L10n.Get(MpText.SettingsLockedForThisRun), _noteStyle);
            }

            GUILayout.Space(6f);

            var entries = BalanceEntries();
            if (entries.Count == 0)
            {
                GUILayout.Label(L10n.Get(MpText.SettingsNothingToShow), _helpStyle);
            }
            else
            {
                _scroll = GUILayout.BeginScrollView(_scroll);
                foreach (var entry in entries)
                {
                    DrawEntry(entry);
                }
                GUILayout.EndScrollView();
            }

            GUILayout.Space(4f);
            GUILayout.Label(L10n.Get(MpText.SettingsNextRunNote), _noteStyle);
            GUILayout.Space(4f);

            GUILayout.BeginHorizontal();
            if (GUILayout.Button(L10n.Get(MpText.SettingsResetAll), GUILayout.Width(190f)))
            {
                foreach (var entry in entries)
                {
                    Reset(entry);
                }
            }

            GUILayout.FlexibleSpace();

            if (GUILayout.Button(L10n.Get(MpText.SettingsClose), GUILayout.Width(100f)))
            {
                Visible = false;
            }
            GUILayout.EndHorizontal();

            GUI.DragWindow(new Rect(0f, 0f, 10000f, 20f));
        }

        /// <summary>
        /// Every Balance setting, ordered by config key.
        /// </summary>
        private static List<ConfigEntryBase> BalanceEntries()
        {
            var config = MpPlugin.Instance?.Config;
            if (config == null)
            {
                return new List<ConfigEntryBase>();
            }

            return config
                .Where(pair => pair.Key.Section == Section)
                .OrderBy(pair => pair.Key.Key, StringComparer.Ordinal)
                .Select(pair => pair.Value)
                .ToList();
        }

        private void DrawEntry(ConfigEntryBase entry)
        {
            Describe(entry, out string name, out string help);

            GUILayout.Space(6f);

            GUILayout.BeginHorizontal();
            GUILayout.Label(name, _nameStyle);
            GUILayout.FlexibleSpace();
            DrawEditor(entry);

            if (GUILayout.Button(L10n.Get(MpText.SettingsDefault), GUILayout.Width(76f)))
            {
                Reset(entry);
            }
            GUILayout.EndHorizontal();

            if (!string.IsNullOrEmpty(help))
            {
                GUILayout.Label(help, _helpStyle);
            }
        }

        /// <summary>
        /// What to call a setting on screen.
        /// </summary>
        private static void Describe(ConfigEntryBase entry, out string name, out string help)
        {
            if (ReferenceEquals(entry, MpPlugin.EnemyHpScalePerExtraPlayer))
            {
                name = L10n.Get(MpText.SettingEnemyHpScaleName);
                help = L10n.Get(MpText.SettingEnemyHpScaleHelp);
                return;
            }

            if (ReferenceEquals(entry, MpPlugin.ReviveHpFraction))
            {
                name = L10n.Get(MpText.SettingReviveHpName);
                help = L10n.Get(MpText.SettingReviveHpHelp);
                return;
            }

            if (ReferenceEquals(entry, MpPlugin.EnableEnemyResilience))
            {
                name = L10n.Get(MpText.SettingResilienceName);
                help = L10n.Get(MpText.SettingResilienceHelp);
                return;
            }

            var acts = MpPlugin.EnemyHpEscalationByAct;
            for (int i = 0; acts != null && i < acts.Length; i++)
            {
                if (ReferenceEquals(entry, acts[i]))
                {
                    name = L10n.Get(MpText.SettingEscalationName, i + 1);
                    help = L10n.Get(MpText.SettingEscalationHelp);
                    return;
                }
            }

            name = entry.Definition.Key;
            help = entry.Description?.Description;
        }

        private void DrawEditor(ConfigEntryBase entry)
        {
            if (entry.SettingType == typeof(bool))
            {
                bool current = (bool)entry.BoxedValue;
                bool toggled = GUILayout.Toggle(current, GUIContent.none, GUILayout.Width(90f));
                if (toggled != current)
                {
                    entry.BoxedValue = toggled;
                }

                return;
            }

            string key = entry.Definition.Key;
            if (!_typing.TryGetValue(key, out string text))
            {
                text = Serialize(entry);
            }

            string typed = GUILayout.TextField(text, 12, GUILayout.Width(90f));
            if (typed == text)
            {
                return;
            }

            _typing[key] = typed;
            Commit(entry, typed);
        }

        private static void Commit(ConfigEntryBase entry, string text)
        {
            MpSafe.Run("BalanceSettings.Commit", () =>
            {
                if (entry.SettingType == typeof(float))
                {
                    if (float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float value))
                    {
                        entry.BoxedValue = value;
                    }

                    return;
                }

                if (entry.SettingType == typeof(int))
                {
                    if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value))
                    {
                        entry.BoxedValue = value;
                    }

                    return;
                }

                try
                {
                    entry.SetSerializedValue(text);
                }
                catch (Exception)
                {
                    // This avoids a really nasty softlock if the player enters a wrong thing
                }
            });
        }

        private void Reset(ConfigEntryBase entry)
        {
            MpSafe.Run("BalanceSettings.Reset", () =>
            {
                entry.BoxedValue = entry.DefaultValue;
                _typing.Remove(entry.Definition.Key);
            });
        }

        private static string Serialize(ConfigEntryBase entry)
        {
            if (entry.BoxedValue is float f)
            {
                return f.ToString("0.####", CultureInfo.InvariantCulture);
            }

            return Convert.ToString(entry.BoxedValue, CultureInfo.InvariantCulture) ?? string.Empty;
        }
    }
}
