using UnityEngine;

namespace LBOLMP.UI
{
    /// <summary>
    /// Shared measuring for the mod's IMGUI overlays.
    ///
    /// Every panel here is sized around text that nobody can predict the width of — a player's
    /// name, a list of who the party is waiting on, a translated sentence that is a different
    /// length in every language. Guessing at a pixel width is what clipped them.
    /// </summary>
    internal static class MpGui
    {
        /// <summary>
        /// A style that draws on one line and is measured honestly.
        ///
        /// Unity's built-in label style has <c>wordWrap</c> on, and that is the trap: CalcSize
        /// reports the width of a single line, and drawing into a box of exactly that width then
        /// wraps the last word onto a second line that the box is not tall enough to show. The
        /// symptom is a sentence that ends mid-word — "Waiting for albert to finish their" — which
        /// reads like a truncation bug rather than a wrap.
        /// </summary>
        internal static GUIStyle SingleLine(GUIStyle style)
        {
            style.wordWrap = false;
            return style;
        }

        /// <summary>
        /// The size a line will actually occupy, rounded up.
        ///
        /// CalcSize returns fractional pixels, and a box built from the unrounded number is
        /// occasionally a hair too narrow for the text it was measured from. A pixel of slack costs
        /// nothing and removes a whole class of "only sometimes clipped" reports.
        /// </summary>
        internal static Vector2 Measure(GUIStyle style, string text)
        {
            var size = style.CalcSize(new GUIContent(text ?? string.Empty));
            return new Vector2(Mathf.Ceil(size.x) + 2f, Mathf.Ceil(size.y));
        }

        /// <summary>The panel fill: dark, and fully opaque.</summary>
        private static readonly Color PanelFill = new Color(0.09f, 0.10f, 0.12f, 1f);

        /// <summary>A hairline around it, so the panel still has an edge against a dark scene.</summary>
        private static readonly Color PanelEdge = new Color(0.42f, 0.44f, 0.50f, 1f);

        private static GUIStyle _windowStyle;
        private static Texture2D _windowTexture;

        /// <summary>
        /// Window background for the mod's draggable panels, opaque rather than the tinted glass
        /// Unity's built-in window style paints.
        ///
        /// The built-in one is roughly half transparent, which is fine over a battle and unreadable
        /// over the main menu — the menu art is bright and animated, and light grey settings text on
        /// top of it is genuinely hard to read. These panels are opened and closed on a keypress, so
        /// there is nothing to be gained by seeing through them.
        ///
        /// Everything else about the style is inherited, so the title bar, padding and drag
        /// behaviour stay exactly as they were; only the two backgrounds are replaced — <c>normal</c>
        /// for a window that is not focused and <c>onNormal</c> for the one that is, which is why
        /// leaving either alone would give a panel that turns to glass the moment you click on it.
        /// </summary>
        internal static GUIStyle Window
        {
            get
            {
                // The texture is rebuilt as well as the style, because Unity destroys textures that
                // nothing in a scene holds on to. The HideAndDontSave below is what normally stops
                // that; the null check is in case something else gets there first, and costs one
                // comparison a frame.
                if (_windowStyle != null && _windowTexture != null)
                {
                    return _windowStyle;
                }

                _windowTexture = PanelTexture();

                _windowStyle = new GUIStyle(GUI.skin.window)
                {
                    // One pixel of the texture per pixel of edge, and the middle stretched.
                    border = new RectOffset(1, 1, 1, 1),

                    // The built-in style bleeds its background past the window to paint a soft
                    // frame and shadow. A flat fill has neither, so drawing outside the rect would
                    // only put an opaque lip around a panel that is already the right size.
                    overflow = new RectOffset(0, 0, 0, 0)
                };

                _windowStyle.normal.background = _windowTexture;
                _windowStyle.onNormal.background = _windowTexture;

                // The title, which the built-in skin renders in a grey chosen for its own lighter
                // background and which reads as disabled on this one.
                _windowStyle.normal.textColor = new Color(0.88f, 0.89f, 0.92f);
                _windowStyle.onNormal.textColor = Color.white;

                return _windowStyle;
            }
        }

        /// <summary>
        /// An 8x8 fill with a one-pixel border, sliced by the style above so the border stays one
        /// pixel wide however big the panel is.
        /// </summary>
        private static Texture2D PanelTexture()
        {
            const int size = 8;

            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                // Or Unity collects it on the next scene change and the panels turn invisible.
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };

            var pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    bool edge = x == 0 || y == 0 || x == size - 1 || y == size - 1;
                    pixels[y * size + x] = edge ? PanelEdge : PanelFill;
                }
            }

            texture.SetPixels(pixels);
            texture.Apply();
            return texture;
        }
    }
}
