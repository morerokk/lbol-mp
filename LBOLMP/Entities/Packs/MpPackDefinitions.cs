using System.IO;
using LBoL.Presentation;
using LBoLEntitySideloader;
using LBoLEntitySideloader.Entities;
using LBoLEntitySideloader.Resource;
using UnityEngine;

namespace LBOLMP.Entities.Packs
{
    /// <summary>
    /// Shared plumbing for the mod's booster packs.
    /// </summary>
    internal static class MpPackArt
    {
        private const string Folder = "Resources/Packs/";

        private static DirectorySource _source;

        private static DirectorySource Source =>
            _source ?? (_source = new DirectorySource(MpInfo.Guid, ""));

        internal static PackIcons Load(string id)
        {
            var icons = new PackIcons
            {
                mainIcon = Sprite(id + ".png"),
                disabledIcon = Sprite(id + "Off.png")
            };

            return icons;
        }

        private static Sprite Sprite(string file)
        {
            return Exists(Folder + file)
                ? MpSafe.Run("MpPackArt.Sprite", () => ResourceLoader.LoadSprite(Folder + file, Source), null)
                : null;
        }

        private static bool Exists(string relativePath)
        {
            string folder = Path.GetDirectoryName(typeof(MpPackArt).Assembly.Location);
            return !string.IsNullOrEmpty(folder)
                   && File.Exists(Path.Combine(folder,
                       relativePath.Replace('/', Path.DirectorySeparatorChar)));
        }
    }

    /// <summary>The pack holding Whim.</summary>
    public sealed class MpWhimPackDefinition : PackTemplate
    {
        internal const string Id = "MpWhimPack";

        public override IdContainer GetId() => Id;

        public override PackIcons LoadPackIcon() => MpPackArt.Load(Id);

        public override LocalizationOption LoadLocalization() => MpLocalization.Packs.AddEntity(this);
    }

    /// <summary>The pack holding Intrusive Thought.</summary>
    public sealed class MpIntrusiveThoughtPackDefinition : PackTemplate
    {
        internal const string Id = "MpIntrusiveThoughtPack";

        public override IdContainer GetId() => Id;

        public override PackIcons LoadPackIcon() => MpPackArt.Load(Id);

        public override LocalizationOption LoadLocalization() => MpLocalization.Packs.AddEntity(this);
    }
}
