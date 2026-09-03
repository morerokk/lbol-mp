using System;
using System.Collections.Generic;
using System.IO;
using LBoL.Core;
using LBoLEntitySideloader.Entities;
using LBoLEntitySideloader.Resource;

namespace LBOLMP.Entities
{
    /// <summary>
    /// The yaml every card and status effect reads its name and description out of.
    /// </summary>
    internal static class MpLocalization
    {
        /// <summary>The locale whose file is used when the player's own language is not implemented.</summary>
        private const Locale Fallback = Locale.En;

        private static DirectorySource _source;

        private static DirectorySource Source => _source ?? (_source = new DirectorySource(MpInfo.Guid, ""));

        private static BatchLocalization _cards;
        private static BatchLocalization _statusEffects;
        private static BatchLocalization _jadeBoxes;
        private static BatchLocalization _packs;

        /// <summary>Resources/Cards&lt;Locale&gt;.yaml</summary>
        internal static BatchLocalization Cards =>
            _cards ?? (_cards = Build(typeof(CardTemplate), "Resources/Cards"));

        /// <summary>Resources/StatusEffects&lt;Locale&gt;.yaml</summary>
        internal static BatchLocalization StatusEffects =>
            _statusEffects ?? (_statusEffects = Build(typeof(StatusEffectTemplate), "Resources/StatusEffects"));

        /// <summary>Resources/JadeBoxes&lt;Locale&gt;.yaml</summary>
        internal static BatchLocalization JadeBoxes =>
            _jadeBoxes ?? (_jadeBoxes = Build(typeof(JadeBoxTemplate), "Resources/JadeBoxes"));

        /// <summary>Resources/Packs&lt;Locale&gt;.yaml</summary>
        internal static BatchLocalization Packs =>
            _packs ?? (_packs = Build(typeof(PackTemplate), "Resources/Packs"));

        private static BatchLocalization Build(Type templateType, string prefix)
        {
            var batch = new BatchLocalization(Source, templateType, Fallback, FileFor(prefix, Fallback));
            var found = new List<string> { Fallback.ToString() };

            MpSafe.Run("MpLocalization.Build", () => AddTranslations(batch, prefix, found));

            MpPlugin.Log.LogInfo($"{prefix}: loaded {string.Join(", ", found)}");
            return batch;
        }

        private static void AddTranslations(BatchLocalization batch, string prefix, List<string> found)
        {
            foreach (Locale locale in Enum.GetValues(typeof(Locale)))
            {
                string file = FileFor(prefix, locale);
                if (locale == Fallback || !Exists(file))
                {
                    continue;
                }

                batch.localizationFiles.AddLocaleFile(locale, file);
                found.Add(locale.ToString());
            }
        }

        private static string FileFor(string prefix, Locale locale) => $"{prefix}{locale}.yaml";

        private static bool Exists(string relativePath)
        {
            string folder = Folder;
            return !string.IsNullOrEmpty(folder)
                   && File.Exists(Path.Combine(folder,
                       relativePath.Replace('/', Path.DirectorySeparatorChar)));
        }

        private static string _folder;

        private static string Folder =>
            _folder ?? (_folder = Path.GetDirectoryName(typeof(MpLocalization).Assembly.Location) ?? string.Empty);
    }
}
