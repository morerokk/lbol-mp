using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using LBoL.Core;

namespace LBOLMP
{
    /// <summary>
    /// Turns a <see cref="MpText"/> key into the correctly translated text.
    ///
    /// The mod supports English, Simplified Chinese, Traditional Chinese and Japanese.
    /// Everything else falls back to English.
    /// 
    /// Note: language can change mid-game and defaults to Chinese during the startup phase of the game, so this is necessary to fresh fetch content from each time.
    /// </summary>
    public static class L10n
    {
        /// <summary>
        /// Marks a string as a key rather than words. Anything without it is passed through
        /// untouched, which is what lets an error message from Windows or Steam travel the same
        /// pipe as a phrase this mod wrote. See <see cref="Encode"/>.
        /// </summary>
        private const string Marker = "mp:";

        private const char Separator = '|';
        private const char Escape = '\\';

        /// <summary>
        /// The language to render in: currently only ever <c>En</c>, <c>ZhHans</c> or <c>ZhHant</c>.
        /// </summary>
        public static Locale Current
        {
            get
            {
                Locale locale;
                try
                {
                    locale = Localization.CurrentLocale;
                }
                catch // This is really stupid but it was necessary
                {
                    
                    return Locale.En;
                }

                // Collapse everything into Simplified/Traditional Chinese, and otherwise English.
                switch (locale)
                {
                    // Explicitly supported languages
                    case Locale.ZhHans:
                    case Locale.ZhHant:
                    case Locale.Ja:
                        return locale;
                    // Anything else falls back to English (including English)
                    default:
                        return Locale.En;
                }
            }
        }

        /// <summary>The text for a key, in the player's language.</summary>
        public static string Get(MpText key) => Format(Lookup(key, Current), null);

        /// <summary>The text for a key with its placeholders filled in.</summary>
        public static string Get(MpText key, params object[] args) => Format(Lookup(key, Current), args);

        /// <summary>The text for a key, in one particular language.</summary>
        private static string Get(MpText key, Locale locale, object[] args) => Format(Lookup(key, locale), args);

        /// <summary>
        /// Always returns English. Intended for logging purposes.
        /// </summary>
        public static string En(MpText key) => Format(Lookup(key, Locale.En), null);

        /// <summary>
        /// Always returns English. Intended for logging purposes.
        /// </summary>
        public static string En(MpText key, params object[] args) => Format(Lookup(key, Locale.En), args);



        /// <summary>
        /// Helper for translating text that will be read by the other side over the network. Needlessly convoluted, I think.
        /// </summary>
        public static string Encode(MpText key, params object[] args)
        {
            var builder = new StringBuilder(Marker).Append(key.ToString());
            if (args != null)
            {
                foreach (var arg in args)
                {
                    builder.Append(Separator).Append(EscapeArg(Convert.ToString(arg, CultureInfo.InvariantCulture)));
                }
            }
            return builder.ToString();
        }

        /// <summary>
        /// Helper for translating text that has been received over the network.
        /// </summary>
        public static string Decode(string text) => Decode(text, Current);

        /// <summary>
        /// Same as Decode, but specifically for logging.
        /// </summary>
        public static string DecodeEn(string text) => Decode(text, Locale.En);

        /// <summary>
        /// Helper for translating text that has been received over the network.
        /// </summary>
        private static string Decode(string text, Locale locale)
        {
            if (string.IsNullOrEmpty(text) || !text.StartsWith(Marker, StringComparison.Ordinal))
            {
                return text ?? string.Empty;
            }

            var parts = SplitArgs(text.Substring(Marker.Length));
            if (parts.Count == 0 || !Enum.TryParse(parts[0], out MpText key))
            {
                return text;
            }

            var args = new object[parts.Count - 1];
            for (int i = 1; i < parts.Count; i++)
            {
                args[i - 1] = parts[i];
            }

            return Get(key, locale, args);
        }

        // internals from this point on

        private static string Lookup(MpText key, Locale locale)
        {
            // At worst, return a placeholder
            if (!MpStrings.Table.TryGetValue(key, out var phrase))
            {
                return key.ToString();
            }

            // If Japanese text exists, return it
            if (locale == Locale.Ja && !string.IsNullOrWhiteSpace(phrase.Ja))
            {
                return phrase.Ja;
            }

            // An edge case that should probably never happen, but I put it in anyway:
            // Traditional Chinese is preferred if that is the user's current language, but if that string is missing,
            // then Simplified Chinese is an acceptable fallback (surely)
            if (locale == Locale.ZhHant && !string.IsNullOrWhiteSpace(phrase.ZhHant))
            {
                return phrase.ZhHant;
            }

            if ((locale == Locale.ZhHans || locale == Locale.ZhHant)
                && !string.IsNullOrWhiteSpace(phrase.ZhHans))
            {
                return phrase.ZhHans;
            }

            // Fall back to English in every other case
            return string.IsNullOrEmpty(phrase.En) ? key.ToString() : phrase.En;
        }

        private static string Format(string pattern, object[] args)
        {
            if (args == null || args.Length == 0)
            {
                return pattern;
            }

            try
            {
                return string.Format(CultureInfo.InvariantCulture, pattern, args);
            }
            catch (FormatException)
            {
                MpPlugin.Log?.LogWarning($"Malformed localised text: '{pattern}'");
                return pattern;
            }
        }

        private static string EscapeArg(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            // An argument can be an exception message, and those contain anything at all.
            return value.Replace(Escape.ToString(), "\\\\").Replace(Separator.ToString(), "\\|");
        }

        private static List<string> SplitArgs(string body)
        {
            var parts = new List<string>();
            var current = new StringBuilder();
            bool escaped = false;

            foreach (char c in body)
            {
                if (escaped)
                {
                    current.Append(c);
                    escaped = false;
                }
                else if (c == Escape)
                {
                    escaped = true;
                }
                else if (c == Separator)
                {
                    parts.Add(current.ToString());
                    current.Length = 0;
                }
                else
                {
                    current.Append(c);
                }
            }

            parts.Add(current.ToString());
            return parts;
        }

        /// <summary>
        /// Verifies that all expected translations are there. Happens once on startup.
        /// </summary>
        internal static void Verify()
        {
            var missing = new List<string>();
            int untranslatedJa = 0;
            foreach (MpText key in Enum.GetValues(typeof(MpText)))
            {
                if (!MpStrings.Table.TryGetValue(key, out var phrase))
                {
                    missing.Add(key.ToString());
                }
                else if (string.IsNullOrWhiteSpace(phrase.Ja))
                {
                    untranslatedJa++;
                }
            }

            // TODO: Japanese text is currently still being worked on!
            if (untranslatedJa > 0 && untranslatedJa < MpStrings.Table.Count)
            {
                MpPlugin.Log.LogInfo(
                    $"{untranslatedJa} of {MpStrings.Table.Count} phrases still have no Japanese; "
                    + "those will fall back to English");
            }

            if (missing.Count > 0)
            {
                MpPlugin.Log.LogWarning(
                    $"{missing.Count} text key(s) have no entry in MpStrings: {string.Join(", ", missing)}");
            }
        }
    }
}
