using System;
using System.Collections.Generic;
using System.IO;
using LBoL.Presentation;
using LBoL.Presentation.UI;
using LBoL.Presentation.UI.Panels;
using LBoL.Presentation.UI.Widgets;
using LBoLEntitySideloader.Resource;
using UnityEngine;
using UnityEngine.UI;

namespace LBOLMP.UI
{
    /// <summary>
    /// Funny map preview portraits to show who voted for what.
    /// </summary>
    public static class MpPortraits
    {
        private const string FallbackKey = "null";

        /// <summary>
        /// How much of the ring the head covers, if the template cannot be measured.
        /// </summary>
        private const float DefaultHeadScale = 0.78f;

        /// <summary>
        /// Zoom in a bit further for "fallback" heads (modded characters)
        /// </summary>
        private const float FallbackZoom = 1.3f;

        private const string IconFolder = "Resources/UI/";
        private const string IconSuffix = "Icon.png";

        private static readonly Dictionary<string, Sprite> Heads = new Dictionary<string, Sprite>();
        private static readonly Dictionary<string, Sprite> Avatars = new Dictionary<string, Sprite>();
        private static readonly Dictionary<string, Sprite> Icons = new Dictionary<string, Sprite>();

        private static DirectorySource _source;
        private static Sprite _frame;
        private static float _headScale;
        private static bool _warmed;
        private static bool _iconsLoaded;

        private static DirectorySource Source =>
            _source ?? (_source = new DirectorySource(MpInfo.Guid, ""));

        /// <summary>
        /// Takes a copy of the collection's art while the collection still exists.
        /// tl;dr: I'm hijacking the Museum's sprite for this, but I can only do this in the main menu, so this is done on game start.
        /// The reference is lost when the scene reloads, so once the run is started, I have to hold on to my own cached copy.
        /// </summary>
        public static void Warm()
        {
            MpSafe.Run("MpPortraits.LoadIcons", LoadIcons);

            if (_warmed)
            {
                return;
            }

            MpSafe.Run("MpPortraits.Warm", () =>
            {
                var museum = Panel<MuseumPanel>();
                var portraits = museum?.portraitList;
                if (portraits == null)
                {
                    return;
                }

                foreach (var pair in portraits)
                {
                    if (!string.IsNullOrEmpty(pair.Key) && pair.Value != null)
                    {
                        Heads[pair.Key] = pair.Value;
                    }
                }

                if (_frame == null)
                {
                    _frame = Square(DiscoverRing()) ?? Square(Template()?.normalSprite);
                }

                if (Heads.Count > 0)
                {
                    _warmed = true;
                    MpPlugin.Log.LogInfo(
                        $"Cached {Heads.Count} collection portraits before the main menu UI is unloaded; " +
                        $"ring={(_frame == null ? "<none, drawing the face bare>" : $"'{_frame.name}' {_frame.rect.width:0}x{_frame.rect.height:0}")}, " +
                        $"headScale={DefaultHeadScale:0.00}");
                }
            });
        }

        private static void LoadIcons()
        {
            if (_iconsLoaded)
            {
                return;
            }

            _iconsLoaded = true;

            var root = Source?.dirInfo;
            var folder = root == null
                ? null
                : new DirectoryInfo(Path.Combine(root.FullName, IconFolder.Replace('/', Path.DirectorySeparatorChar)));

            if (folder == null || !folder.Exists)
            {
                MpPlugin.Log.LogWarning(
                    $"No '{IconFolder}' to read character icons from. Is the mod installed correctly? All characters will get a janky assembled portrait");
                return;
            }

            foreach (var file in folder.GetFiles("*" + IconSuffix))
            {
                string characterId = file.Name.Substring(0, file.Name.Length - IconSuffix.Length);
                if (characterId.Length == 0)
                {
                    continue;
                }

                var sprite = ResourceLoader.LoadSprite(IconFolder + file.Name, Source);
                if (sprite == null)
                {
                    MpPlugin.Log.LogWarning($"Could not read the character icon '{file.Name}'");
                    continue;
                }

                Icons[characterId] = sprite;
            }

            MpPlugin.Log.LogInfo(
                $"Loaded {Icons.Count} finished character icons: {string.Join(", ", Icons.Keys)}");
        }

        /// <summary>
        /// How square a sprite has to be before it will be believed to be a ring.
        /// </summary>
        private const float MinRingAspect = 0.85f;
        private const float MaxRingAspect = 1.18f;

        private static Sprite Frame => _frame;

        /// <summary>
        /// The portrait for a character, or null if no source has one yet.
        /// </summary>
        public static Sprite For(string characterId)
        {
            return Icon(characterId) ?? Head(characterId) ?? ProfileHead(characterId) ?? Avatar(characterId);
        }

        /// <summary>
        /// The ring to draw behind a character's portrait, or null if it already has one.
        /// </summary>
        public static Sprite FrameFor(string characterId) => Icon(characterId) != null ? null : Frame;

        /// <summary>
        /// The portrait's width as a fraction of the ring's.
        /// </summary>
        public static float HeadScale(string characterId)
        {
            return _frame == null || Icon(characterId) != null ? 1f : DefaultHeadScale;
        }

        /// <summary>
        /// How far into a portrait to zoom, used for fallbacks.
        /// </summary>
        public static float ZoomFor(string characterId) =>
            Icon(characterId) != null ? 1f : FallbackZoom;

        public static Rect Middle(Rect region, float zoom)
        {
            if (zoom <= 1f || region.width <= 0f || region.height <= 0f)
            {
                return region;
            }

            float width = region.width / zoom;
            float height = region.height / zoom;

            return new Rect(
                region.x + (region.width - width) * 0.5f,
                region.y + (region.height - height) * 0.5f,
                width,
                height);
        }

        /// <summary>The complete portrait for this character, if there is one.</summary>
        private static Sprite Icon(string characterId)
        {
            LoadIcons();

            return !string.IsNullOrEmpty(characterId) && Icons.TryGetValue(characterId, out var icon)
                ? icon
                : null;
        }

        /// <summary>
        /// The ring this is expected to find: sprite '空', 320x320, a sibling of the main menu's profile head.
        /// </summary>
        private const string ExpectedRingName = "空";

        /// <summary>
        /// Hunts for the circular frame the main menu draws around its profile head.
        /// </summary>
        private static Sprite DiscoverRing()
        {
            var head = Panel<MainMenuPanel>()?.profileHead;
            var parent = head == null ? null : head.transform.parent;
            if (parent == null)
            {
                return null;
            }

            Sprite best = null;
            float bestWidth = 0f;

            foreach (var image in parent.GetComponentsInChildren<Image>(true))
            {
                if (image == head || image.sprite == null)
                {
                    continue;
                }

                var rect = image.sprite.rect;
                if (rect.width < 1f || rect.height < 1f)
                {
                    continue;
                }

                float aspect = rect.width / rect.height;
                if (aspect < MinRingAspect || aspect > MaxRingAspect)
                {
                    continue;
                }

                float width = image.rectTransform.rect.width;
                if (width > bestWidth)
                {
                    best = image.sprite;
                    bestWidth = width;
                }
            }

            if (best != null && best.name != ExpectedRingName)
            {
                MpPlugin.Log.LogWarning(
                    $"Portrait ring is '{best.name}', not the expected '{ExpectedRingName}' — " +
                    "the main menu's profile art may have changed; check how the markers look");
            }

            return best;
        }

        /// <summary>
        /// Draws the framed portrait into an IMGUI rect: ring first, head centred on top of it.
        /// </summary>
        public static void Draw(Rect area, string characterId)
        {
            var head = For(characterId);
            var frame = FrameFor(characterId);

            if (frame != null)
            {
                DrawSprite(area, frame);

                float inset = HeadScale(characterId);
                float size = Mathf.Min(area.width, area.height) * inset;
                area = new Rect(
                    area.x + (area.width - size) * 0.5f,
                    area.y + (area.height - size) * 0.5f,
                    size,
                    size);
            }

            DrawSprite(area, head, ZoomFor(characterId));
        }

        /// <summary>
        /// Draws one sprite into an IMGUI rect, centered and undistorted (unless mods lol).
        /// </summary>
        public static void DrawSprite(Rect area, Sprite sprite) => DrawSprite(area, sprite, 1f);

        /// <summary>
        /// Same, taking only the middle of the sprite when zoomed past 1.
        /// </summary>
        public static void DrawSprite(Rect area, Sprite sprite, float zoom)
        {
            if (sprite == null || sprite.texture == null)
            {
                return;
            }

            var texture = sprite.texture;
            var region = Middle(sprite.textureRect, zoom);
            if (region.width <= 0f || region.height <= 0f)
            {
                return;
            }

            var coords = new Rect(
                region.x / texture.width,
                region.y / texture.height,
                region.width / texture.width,
                region.height / texture.height);

            // DrawTextureWithTexCoords has no ScaleMode, so the letterboxing is done here.
            float aspect = region.width / region.height;
            float width = area.width;
            float height = area.height;
            if (width / height > aspect)
            {
                width = height * aspect;
            }
            else
            {
                height = width / aspect;
            }

            var fitted = new Rect(
                area.x + (area.width - width) * 0.5f,
                area.y + (area.height - height) * 0.5f,
                width,
                height);

            GUI.DrawTextureWithTexCoords(fitted, texture, coords);
        }

        private static Sprite Head(string characterId)
        {
            if (string.IsNullOrEmpty(characterId))
            {
                return null;
            }

            if (Heads.TryGetValue(characterId, out var cached) && cached != null)
            {
                return cached;
            }

            var portraits = Panel<MuseumPanel>()?.portraitList;
            if (portraits == null || !portraits.TryGetValue(characterId, out var sprite) || sprite == null)
            {
                return null;
            }

            Heads[characterId] = sprite;
            return sprite;
        }

        private static Sprite ProfileHead(string characterId)
        {
            var profile = Panel<ProfilePanel>();
            if (profile == null)
            {
                return null;
            }

            try
            {
                // Unknown ids resolve to the game's own placeholder rather than throwing.
                return profile.GetHeadSprite(string.IsNullOrEmpty(characterId) ? FallbackKey : characterId);
            }
            catch (Exception e)
            {
                MpPlugin.Log.LogWarning($"Could not get a profile head for '{characterId}': {e.Message}");
                return null;
            }
        }

        private static Sprite Avatar(string characterId)
        {
            if (string.IsNullOrEmpty(characterId))
            {
                return null;
            }

            if (Avatars.TryGetValue(characterId, out var cached))
            {
                return cached;
            }

            Sprite sprite = null;
            try
            {
                sprite = ResourcesHelper.LoadCharacterAvatarSprite(characterId);
            }
            catch (Exception e)
            {
                MpPlugin.Log.LogWarning($"Could not load an avatar for {characterId}: {e.Message}");
            }

            Avatars[characterId] = sprite;
            return sprite;
        }

        private static Sprite Square(Sprite sprite)
        {
            if (sprite == null || sprite.rect.width < 1f || sprite.rect.height < 1f)
            {
                return null;
            }

            float aspect = sprite.rect.width / sprite.rect.height;
            if (aspect < MinRingAspect || aspect > MaxRingAspect)
            {
                MpPlugin.Log.LogInfo(
                    $"Ignoring '{sprite.name}' as a portrait ring: {sprite.rect.width:0}x{sprite.rect.height:0} is not square");
                return null;
            }

            return sprite;
        }

        private static CharacterToggleWidget Template()
        {
            var toggle = Panel<MuseumPanel>()?.characterToggleTemplate;
            return toggle == null ? null : toggle.GetComponent<CharacterToggleWidget>();
        }

        private static TPanel Panel<TPanel>() where TPanel : UiPanelBase
        {
            try
            {
                return UiManager.GetPanel<TPanel>();
            }
            catch (InvalidOperationException)
            {
                return null;
            }
        }
    }
}
