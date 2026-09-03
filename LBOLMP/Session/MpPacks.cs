using System.Collections.Generic;
using System.Linq;
using LBOLMP.Entities.Packs;
using LBoL.Presentation;

namespace LBOLMP.Session
{
    /// <summary>
    /// Handles the mod's booster packs.
    /// Specifically, this makes LBOL MP's booster packs enabled by default.
    /// They can still be disabled, but they will default-enabled rather than having to be toggled on manually.
    /// </summary>
    internal static class MpPacks
    {
        /// <summary>Everything this mod puts behind a pack toggle.</summary>
        internal static readonly string[] Ours =
        {
            MpWhimPackDefinition.Id,
            MpIntrusiveThoughtPackDefinition.Id
        };

        private static bool _seeded;

        /// <summary>
        /// The packs this player has switched on, which is what the host sends to everyone else.
        /// </summary>
        internal static List<string> Local()
        {
            var packs = Settings();
            return packs == null ? new List<string>() : new List<string>(packs);
        }

        /// <summary>
        /// Runs until the profile exists, since it is not loaded yet on the very first frames.
        /// </summary>
        internal static void Tick()
        {
            if (_seeded)
            {
                return;
            }

            MpSafe.Run("MpPacks.Seed", Seed);
        }

        private static void Seed()
        {
            var master = GameMaster.Instance;
            var packs = Settings();
            if (master == null || packs == null)
            {
                // No profile yet. Try again on the next frame.
                return;
            }

            _seeded = true;

            var offered = new HashSet<string>(Offered());
            bool offeredAny = false;

            foreach (var id in Ours)
            {
                if (offered.Contains(id))
                {
                    // Already offered once. If it is off now, that was the player's choice.
                    continue;
                }

                offered.Add(id);
                offeredAny = true;

                if (packs.Contains(id))
                {
                    continue;
                }

                packs.Add(id);
            }

            if (offeredAny)
            {
                MpPlugin.SeededPacks.Value = string.Join(",", offered.OrderBy(id => id).ToArray());
            }

            master.SaveProfile();
        }

        /// <summary>The packs already offered once, remembered outside the game's own settings.</summary>
        private static IEnumerable<string> Offered()
        {
            string stored = MpPlugin.SeededPacks?.Value ?? string.Empty;
            return stored.Split(',')
                .Select(id => id.Trim())
                .Where(id => id.Length > 0);
        }

        private static List<string> Settings()
        {
            var profile = GameMaster.Instance == null ? null : GameMaster.Instance.CurrentProfile;
            var settings = profile == null ? null : profile.Settings;
            return settings == null ? null : settings.Packs;
        }
    }
}
