using System;
using System.Collections.Generic;
using LBoL.Core;
using LBoLEntitySideloader.Resource;
using YamlDotNet.RepresentationModel;

namespace LBOLMP
{
    /// <summary>
    /// The mod's interface text, loaded from Resources/Ui*.yaml.
    ///
    /// One file per language, keyed by <see cref="MpText"/>. Actual localization contents in UiEn.yaml files.
    ///
    /// Everything is read once at startup rather than on demand, because the game can switch
    /// language mid-session and <see cref="L10n"/> asks for a specific locale each time it renders.
    /// </summary>
    internal static class MpStrings
    {
        /// <summary>Which file holds which language. They all fall back to English if not found.</summary>
        private static readonly Dictionary<Locale, string> Files = new Dictionary<Locale, string>
        {
            [Locale.En] = "Resources/UiEn.yaml",
            [Locale.ZhHans] = "Resources/UiZhHans.yaml",
            [Locale.ZhHant] = "Resources/UiZhHant.yaml",
            [Locale.Ja] = "Resources/UiJa.yaml"
        };

        private static readonly Dictionary<Locale, Dictionary<string, string>> Tables =
            new Dictionary<Locale, Dictionary<string, string>>();

        private static DirectorySource _source;

        private static DirectorySource Source => _source ?? (_source = new DirectorySource(MpInfo.Guid, ""));

        /// <summary>How many phrases English defines, for the debug startup report.</summary>
        internal static int Count =>
            Tables.TryGetValue(Locale.En, out var en) ? en.Count : 0;

        internal static void Load()
        {
            Tables.Clear();

            foreach (var pair in Files)
            {
                Tables[pair.Key] = ReadFile(pair.Value);
            }
        }

        /// <summary>
        /// The text for a key in one language, or null if this language has nothing usable for it.
        /// Blank entries count as nothing, so an untranslated key falls through to English.
        /// </summary>
        internal static string Get(MpText key, Locale locale)
        {
            if (!Tables.TryGetValue(locale, out var table))
            {
                return null;
            }

            return table.TryGetValue(key.ToString(), out var value) && !string.IsNullOrWhiteSpace(value)
                ? value
                : null;
        }

        private static Dictionary<string, string> ReadFile(string path)
        {
            var table = new Dictionary<string, string>(StringComparer.Ordinal);

            // Sideloader's loader, the same one entity localization uses. Returns null and logs
            // rather than throwing when a file is missing or malformed.
            var yaml = MpSafe.Run("MpStrings.LoadYaml:" + path,
                () => ResourceLoader.LoadYaml(path, Source), null);

            if (yaml == null)
            {
                MpPlugin.Log.LogWarning($"No interface text loaded from '{path}'");
                return table;
            }

            foreach (var entry in yaml.Children)
            {
                if (!(entry.Key is YamlScalarNode key) || !(entry.Value is YamlScalarNode value))
                {
                    MpPlugin.Log.LogWarning($"Skipping a non-text entry in '{path}'");
                    continue;
                }

                if (string.IsNullOrEmpty(key.Value))
                {
                    continue;
                }

                table[key.Value] = value.Value ?? string.Empty;
            }

            return table;
        }
    }
}
