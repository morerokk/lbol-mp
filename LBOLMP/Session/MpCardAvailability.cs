using System;
using System.Collections.Generic;
using System.Reflection;
using LBOLMP.Entities;
using LBoL.ConfigData;
using LBoLEntitySideloader.Entities;

namespace LBOLMP.Session
{
    /// <summary>
    /// Keeps multiplayer-only cards out of single player runs.
    ///
    /// Card pools are fixed sets that cannot be extended without adding one per character, so
    /// rather than inventing pools we leave each card in whichever pool it belongs to and hide it
    /// from the roller instead. <c>GameRunController.RollCards</c> only considers a card when
    /// <c>config.DebugLevel &lt;= gameRun.CardValidDebugLevel</c>, and that ceiling is 0 in a
    /// normal run, so a debug level of 1 makes a card invisible to every roll without touching
    /// its pool, colour, owner or rarity.
    ///
    /// The level goes back to 0 on the way out to the main menu, so the Museum still lists these
    /// cards normally rather than tagging them as debug cards.
    /// </summary>
    public static class MpCardAvailability
    {
        /// <summary>High enough that no normal run will roll it. The Museum still shows level 1.</summary>
        private const int HiddenDebugLevel = 1;

        private static readonly List<string> CardIds = new List<string>();

        private static bool _hidden;

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

                // UniqueId, not GetId: Sideloader renames an entity if another mod already claimed
                // the plain id, and the config table is keyed by the name it ended up with.
                var definition = (EntityDefinition)Activator.CreateInstance(type);
                CardIds.Add(definition.UniqueId.ToString());
            }

            MpPlugin.Log.LogInfo($"{CardIds.Count} multiplayer-only card(s) registered");
        }

        /// <summary>A run is starting or being restored. Offer these cards only if it is a lobby run.</summary>
        internal static void OnRunSetup() => SetHidden(!MpSession.IsActive);

        /// <summary>Back at the menu, where the Museum should show everything.</summary>
        internal static void OnLeftRun() => SetHidden(false);

        private static void SetHidden(bool hidden)
        {
            if (hidden == _hidden || CardIds.Count == 0)
            {
                return;
            }

            _hidden = hidden;
            int level = hidden ? HiddenDebugLevel : 0;

            foreach (var id in CardIds)
            {
                var config = CardConfig.FromId(id);
                if (config == null)
                {
                    MpPlugin.Log.LogWarning($"No card config for '{id}'; cannot change its availability");
                    continue;
                }

                config.DebugLevel = level;
            }

            MpPlugin.Log.LogInfo(hidden
                ? "Multiplayer-only cards hidden for this run"
                : "Multiplayer-only cards available again");
        }
    }
}
