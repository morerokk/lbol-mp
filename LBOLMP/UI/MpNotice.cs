using UnityEngine;

namespace LBOLMP.UI
{
    /// <summary>
    /// One line of text, on screen, for a few seconds.
    /// </summary>
    internal static class MpNotice
    {
        private const float Seconds = 7f;

        private static string _text = string.Empty;
        private static float _until;

        internal static void Show(string text)
        {
            _text = text ?? string.Empty;
            _until = Time.unscaledTime + Seconds;
        }

        internal static void Clear()
        {
            _text = string.Empty;
            _until = 0f;
        }

        /// <summary>What to draw, or empty once it has had its time.</summary>
        internal static string Current =>
            Time.unscaledTime < _until ? _text : string.Empty;
    }
}
