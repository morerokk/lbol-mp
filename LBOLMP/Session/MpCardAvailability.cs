using System;
using System.Collections.Generic;
using System.Reflection;
using LBOLMP.Entities;
using LBoL.ConfigData;
using LBoLEntitySideloader.Entities;

namespace LBOLMP.Session
{
    /// <summary>
    /// Decides whether singleplayer-only/multiplayer-only cards are available or not.
    /// </summary>
    /// <remarks>
    /// Some cards are multiplayer-exclusive, others have been replaced with better versions (or removed from multiplayer for other reasons).
    /// This class automagically sets debug levels on cards, to hide them from the card pools when they aren't relevant.
    /// In all cases, the debug level is set low enough in the main menu so that they're still visible in the Museum/Collection.
    /// </remarks>
    public static class MpCardAvailability
    {
        /// <summary>High enough that it can't be found naturally ingame. The Museum still shows level 1 cards.</summary>
        private const int HiddenDebugLevel = 1;

        /// <summary>
        /// Cards that only exist in a multiplayer run.
        /// </summary>
        private static readonly List<string> MultiplayerOnly = new List<string>();

        /// <summary>
        /// Cards that only exist in a singleplayer run.
        /// </summary>
        private static readonly List<string> SingleplayerOnly = new List<string>
        {
            // Koishi's Anatta, replaced by MpAnatta.
            "Anatta",
            // Cirno's Ice Block, replaced by MpIceBlock.
            "IceBlock"
        };

        private static bool _inMultiplayerRun;
        private static bool _applied;

        /// <summary>
        /// Collect the multiplayer-only cards in an assembly. Other mods call this with their own
        /// assembly if they want the same treatment for cards marked <see cref="IMpOnlyCard"/>.
        /// </summary>
        public static void RegisterAll(Assembly assembly)
        {
            foreach (var type in assembly.GetTypes())
            {
                if (type.IsAbstract || type.IsGenericTypeDefinition
                    || !typeof(IMpOnlyCard).IsAssignableFrom(type)
                    || !typeof(CardTemplate).IsAssignableFrom(type))
                {
                    continue;
                }

                // We use UniqueId and not GetId here.
                // Sideloader renames an entity if another mod already claimed the plain id.
                var definition = (EntityDefinition)Activator.CreateInstance(type);
                MultiplayerOnly.Add(definition.UniqueId.ToString());
            }

            MpPlugin.Log.LogInfo($"{MultiplayerOnly.Count} multiplayer-only card(s) registered");
        }

        /// <summary>
        /// Mark a *vanilla* card ID as unfindable in singleplayer.
        /// </summary>
        /// <remarks>
        /// Not guaranteed to work for modded cards in case of ID collisions!
        /// </remarks>
        public static void SetVanillaCardSingleplayerOnly(string vanillaCardId)
        {
            if (!string.IsNullOrEmpty(vanillaCardId) && !SingleplayerOnly.Contains(vanillaCardId))
            {
                SingleplayerOnly.Add(vanillaCardId);
            }
        }

        /// <summary>
        /// Multiplayer cards are only in play when the host has them enabled.
        /// </summary>
        internal static void OnRunSetup() => Apply(MpSession.IsActive && MpSession.MultiplayerCards);
        internal static void OnLeftRun() => Restore();

        private static void Apply(bool multiplayer)
        {
            if (_applied && multiplayer == _inMultiplayerRun)
            {
                return;
            }

            _applied = true;
            _inMultiplayerRun = multiplayer;

            SetLevel(MultiplayerOnly, multiplayer ? 0 : HiddenDebugLevel);
            SetLevel(SingleplayerOnly, multiplayer ? HiddenDebugLevel : 0);

            MpPlugin.Log.LogInfo(multiplayer
                ? "Multiplayer cards available; the cards they replace are hidden"
                : "Multiplayer cards hidden; this is either a single player run or the host turned them off");
        }

        private static void Restore()
        {
            if (!_applied)
            {
                return;
            }

            _applied = false;
            SetLevel(MultiplayerOnly, 0);
            SetLevel(SingleplayerOnly, 0);
            MpPlugin.Log.LogInfo("Every card is on display again");
        }

        private static void SetLevel(List<string> ids, int level)
        {
            foreach (var id in ids)
            {
                var config = CardConfig.FromId(id);
                if (config == null)
                {
                    MpPlugin.Log.LogWarning($"No card config for '{id}'; cannot change its availability");
                    continue;
                }

                config.DebugLevel = level;
            }
        }
    }
}
