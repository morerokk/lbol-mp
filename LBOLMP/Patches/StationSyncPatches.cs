using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using LBOLMP.Net;
using LBOLMP.Session;
using LBoL.Base;
using LBoL.ConfigData;
using LBoL.Core;
using LBoL.Core.Adventures;
using LBoL.Core.Dialogs;
using LBoL.Core.Stations;
using LBoL.Core.Units;
using LBoL.Presentation;
using LBoL.Presentation.UI;
using LBoL.Presentation.UI.Panels;
using LBoL.Presentation.UI.Widgets;

namespace LBOLMP.Patches
{
    /// <summary>
    /// Patches event selection. The host's event is now forced upon everyone.
    /// This is necessary because even with the same RNG streams, what events you get depend entirely on your current player state (gold, P, life, etc).
    /// In the future, I plan to change these events a bit so that they're not so host-dependent, but that was out of scope at the time.
    /// </summary>
    [HarmonyPatch]
    public static class AdventureSyncPatch
    {
        [HarmonyTargetMethods]
        private static IEnumerable<MethodBase> TargetMethods()
        {
            var found = new List<MethodBase>();

            var mustInclude = typeof(LBoL.EntityLib.Stages.NormalStages.NormalStageBase)
                .GetMethod(nameof(Stage.GetAdventure),
                    BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly,
                    null, Type.EmptyTypes, null);

            // This is really sad and may potentially break, but it also guarantees that events added by sideloader also work.
            // Some events are also split across assemblies anyway which is kind of lbol, lbao even
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types;
                try
                {
                    types = assembly.GetTypes();
                }
                catch (ReflectionTypeLoadException e)
                {
                    // I'm looking at your types anyway, kthx
                    types = e.Types.Where(t => t != null).ToArray();
                }
                catch (Exception)
                {
                    continue;
                }

                foreach (var type in types)
                {
                    if (type == null || !typeof(Stage).IsAssignableFrom(type))
                    {
                        continue;
                    }

                    var method = type.GetMethod(nameof(Stage.GetAdventure),
                        BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly,
                        null, Type.EmptyTypes, null);

                    if (method != null)
                    {
                        found.Add(method);
                    }
                }
            }

            if (mustInclude != null && !found.Contains(mustInclude))
            {
                MpPlugin.Log.LogWarning("NormalStageBase.GetAdventure was not found by the scan, which is necessary. Adding it directly, please notify the mod author");
                found.Add(mustInclude);
            }

            MpPlugin.Log.LogInfo($"Watching {found.Count} GetAdventure implementations for the host's event choice");
            return found;
        }

        /// <summary>
        /// True while the host is still deciding on an event on its own. Prevents laggy clients from prematurely entering an event.
        /// </summary>
        private static bool _hostIsDeciding;

        public static Type RollLocally(Stage stage)
        {
            _hostIsDeciding = true;
            try
            {
                return stage.GetAdventure();
            }
            finally
            {
                _hostIsDeciding = false;
            }
        }

        [HarmonyPrefix]
        private static bool Prefix(ref Type __result, out bool __state)
        {
            var chosen = MpSafe.Run("AdventureSyncPatch", HostChoice, null);
            __state = chosen != null;

            if (chosen == null)
            {
                return true;
            }

            __result = chosen;
            return false;
        }

        /// <summary>
        /// DEBUG ONLY: depending on config settings, forces the Yachie, Miyoi and Doremy events so I don't have to play an hour-long run to "maybe" test them.
        /// </summary>
        [HarmonyPostfix]
        private static void Postfix(ref Type __result, bool __state)
        {
            if (__state || !(MpPlugin.ForceCombatEvents.Value || MpPlugin.ForceDoremyEvent.Value))
            {
                return;
            }

            var forced = MpSafe.Run("ForcedEvents", () =>
            {
                var wanted = ForcedEventNames();
                if (wanted.Count == 0)
                {
                    return null;
                }

                string name = wanted[_forcedIndex % wanted.Count];
                var type = TypeFactory<Adventure>.TryGetType(name);
                if (type == null)
                {
                    MpPlugin.Log.LogWarning($"A Force* debug setting wanted '{name}' but this game does not have it");
                    return null;
                }

                _forcedIndex++;
                MpPlugin.Log.LogWarning($"A Force* debug setting is on: this event node is '{name}'");
                return type;
            }, null);

            if (forced != null)
            {
                __result = forced;
            }
        }

        /// <summary>
        /// Orders the debug events correctly so that Yachie, Miyoi and Doremy are always offered in that order
        /// </summary>
        private static List<string> ForcedEventNames()
        {
            var names = new List<string>();

            if (MpPlugin.ForceCombatEvents.Value)
            {
                names.AddRange(CombatEventNames);
            }

            if (MpPlugin.ForceDoremyEvent.Value)
            {
                names.Add("DoremyPortal");
            }

            return names;
        }

        private static readonly string[] CombatEventNames = { "MiyoiBartender", "YachieOppression" };

        private static int _forcedIndex;

        private static Type HostChoice()
        {
            if (_hostIsDeciding || !MpSession.IsActive || !MpSession.IsInRun)
            {
                return null;
            }

            string typeName = MapSync.PendingAdventureType;
            if (string.IsNullOrEmpty(typeName))
            {
                MpPlugin.Log.LogWarning("No host adventure for this node; falling back to a local roll");
                return null;
            }

            var type = TypeFactory<Adventure>.TryGetType(typeName);
            if (type == null)
            {
                MpPlugin.Log.LogWarning($"Host chose adventure '{typeName}' which this client does not have");
                return null;
            }

            MpPlugin.Log.LogInfo($"Running the host's event: {typeName}");
            return type;
        }
    }

    /// <summary>
    /// Everyone gets their own three Act 1 boss candidates at the boss-select node (and a random option).
    /// Slot 1 is always a character that no player is using.
    /// Slots 2 and 3 are always a character that you are not using.
    /// 
    /// The host decides the Act 1 boss that will actually be fought, and whose shining exhibit reward they get.
    /// The clients instead decide whose shining exhibit reward they want to get.
    /// This eliminates "samey" boss exhibits, because otherwise everyone in a 4-man group would always get the same 2 exhibits.
    /// 
    /// Everyone can potentially see different options.
    /// </summary>
    [HarmonyPatch(typeof(SelectStation), nameof(SelectStation.OnEnter))]
    public static class SelectStationSyncPatch
    {
        /// <summary>How many candidates the dialog actually puts on screen.</summary>
        private const int VisibleSlots = 3;

        /// <summary>
        /// The array we installed on the station, or null when we left the game's own in place.
        /// </summary>
        internal static EnemyUnit[] Installed { get; private set; }

        public static void Reset() => Installed = null;

        [HarmonyPostfix]
        private static void Postfix(SelectStation __instance)
        {
            MpSafe.Run("SelectStationSyncPatch", () =>
            {
                Installed = null;

                if (!MpSession.IsActive || !MpSession.IsInRun)
                {
                    return;
                }

                var gameRun = __instance.GameRun;
                var stage = __instance.Stage;
                if (gameRun?.Player == null || stage == null)
                {
                    return;
                }

                var ids = BuildShortlist(gameRun, stage.Index);
                if (ids == null)
                {
                    // Should never happen, but it can maybe happen if we ever decide that more than 4 players should be possible.
                    MpPlugin.Log.LogWarning(
                        "Not enough opponents to build a personal boss list! Using the game's own...");
                    return;
                }

                var opponents = new List<EnemyUnit>();
                foreach (var id in ids)
                {
                    var unit = Library.TryCreateEnemyUnit(id);
                    if (unit != null)
                    {
                        opponents.Add(unit);
                    }
                }

                if (opponents.Count != ids.Count)
                {
                    MpPlugin.Log.LogWarning("Some of the boss list could not be created! Using the game's own");
                    return;
                }

                Installed = opponents.ToArray();
                __instance.Opponents = Installed;
                MpPlugin.Log.LogInfo(
                    $"Your boss list: {string.Join(", ", ids.Take(VisibleSlots))} (random: {ids[VisibleSlots]})");
            });
        }

        /// <summary>
        /// The three candidates for this player plus the hidden random one, or null if the roster cannot fill all three visible slots.
        /// </summary>
        private static List<string> BuildShortlist(GameRunController gameRun, int stageIndex)
        {
            string mine = gameRun.Player.Id;

            var party = new HashSet<string>(
                MpSession.ConnectedPlayers
                    .Select(p => p.CharacterId)
                    .Where(id => !string.IsNullOrEmpty(id)));
            party.Add(mine);

            var rng = new RandomGen(
                MpSession.RunSeed
                ^ (0x9E3779B97F4A7C15UL * (uint)(stageIndex + 1))
                ^ (0xC2B2AE3D27D4EB4FUL * (uint)(MpNet.LocalPlayerId + 1)));

            var roster = Library.EnumerateOpponentIds().ToList();
            var remaining = new List<string>(roster);

            string first = Take(remaining, id => !party.Contains(id), rng)
                           ?? Take(remaining, id => id != mine, rng);
            string second = Take(remaining, id => id != mine, rng);
            string third = Take(remaining, id => id != mine, rng);

            if (first == null || second == null || third == null)
            {
                return null;
            }

            // The random option, rolled off the full roster rather than off what is left of it.
            // It is allowed to land on one of the three above. After all, the option's whole premise is that you
            // give up the choice for free Power points, not that you can choose a fourth candidate freely.
            string random = Take(new List<string>(roster), id => id != mine, rng) ?? first;

            return new List<string> { first, second, third, random };
        }

        /// <summary>Samples one matching id out of the pool and removes it, or null if none match.</summary>
        private static string Take(List<string> pool, Func<string, bool> allowed, RandomGen rng)
        {
            var candidates = pool.Where(allowed).ToList();
            if (candidates.Count == 0)
            {
                return null;
            }

            string chosen = candidates[rng.NextInt(0, candidates.Count - 1)];
            pool.Remove(chosen);
            return chosen;
        }
    }

    /// <summary>
    /// The dialog's "random opponent" option. Picks from the whole roster, not from the three on screen.
    /// </summary>
    [HarmonyPatch(typeof(DialogStorage))]
    public static class RandomOpponentPatch
    {
        private const string IndexVariable = "$randomIndex";
        private const string NameVariable = "$randomName";

        [HarmonyPrefix]
        [HarmonyPatch(nameof(DialogStorage.SetValue), typeof(string), typeof(float))]
        private static void PrefixIndex(string variableName, ref float floatValue)
        {
            if (variableName != IndexVariable)
            {
                return;
            }

            var hidden = MpSafe.Run("RandomOpponentPatch.Index", HiddenCandidate, null);
            if (hidden != null)
            {
                floatValue = SelectStationSyncPatch.Installed.Length;
            }
        }

        [HarmonyPrefix]
        [HarmonyPatch(nameof(DialogStorage.SetValue), typeof(string), typeof(string))]
        private static void PrefixName(string variableName, ref string stringValue)
        {
            if (variableName != NameVariable)
            {
                return;
            }

            var replacement = MpSafe.Run("RandomOpponentPatch.Name",
                () => HiddenCandidate()?.GetName().ToString(true, NounCase.Nominative, UnitNameStyle.Default),
                null);

            if (!string.IsNullOrEmpty(replacement))
            {
                stringValue = replacement;
            }
        }

        /// <summary>
        /// The appended candidate, or null whenever this is not our boss-select dialog.
        /// </summary>
        private static EnemyUnit HiddenCandidate()
        {
            var installed = SelectStationSyncPatch.Installed;
            if (!MpSession.IsActive || !MpSession.IsInRun || installed == null || installed.Length < 2)
            {
                return null;
            }

            if (!(GameMaster.Instance?.CurrentGameRun?.CurrentStation is SelectStation))
            {
                return null;
            }

            return installed[installed.Length - 1];
        }
    }

    /// <summary>
    /// Only the host's choice at the Act 1 boss selection node actually counts for the fight. Apply the host's choice to the actual boss.
    /// </summary>
    [HarmonyPatch(typeof(Stage), nameof(Stage.SetBoss))]
    public static class SetBossSyncPatch
    {
        internal static bool ApplyingRemote;

        /// <summary>The character this player chose at the select node, whether or not it won.</summary>
        internal static string LocalPick = string.Empty;

        /// <summary>Which stage that pick was made on, so it cannot leak into another act or run.</summary>
        internal static int LocalPickStage = -1;

        /// <summary>
        /// The boss the party settled on, by stage index.
        /// </summary>
        private static readonly Dictionary<int, string> PartyBoss = new Dictionary<int, string>();

        public static void Reset()
        {
            LocalPick = string.Empty;
            LocalPickStage = -1;
            PartyBoss.Clear();
        }

        [HarmonyPrefix]
        private static bool Prefix(Stage __instance, ref string enemyGroupName)
        {
            string requested = enemyGroupName;
            string allowed = MpSafe.Run("SetBossSyncPatch", () => Decide(__instance, requested), requested);

            if (allowed == null)
            {
                return false;
            }

            enemyGroupName = allowed;
            return true;
        }

        /// <summary>
        /// Which boss this call should actually set, or null to refuse it.
        /// </summary>
        private static string Decide(Stage stage, string requested)
        {
            if (ApplyingRemote || !MpSession.IsActive || !MpSession.IsInRun || !stage.IsSelectingBoss)
            {
                return requested;
            }

            if (!IsLiveStage(stage))
            {
                if (stage.Boss != null)
                {
                    return null;
                }

                return PartyBoss.TryGetValue(stage.Index, out string saved) ? saved : requested;
            }

            LocalPick = requested;
            LocalPickStage = stage.Index;

            if (!MpNet.IsHost)
            {
                MpPlugin.Log.LogInfo($"Ignoring local boss pick '{requested}'; waiting for the host");
                return null;
            }

            // Setting a boss twice throws, so the host does not get to decide again either.
            if (stage.Boss != null)
            {
                return null;
            }

            PartyBoss[stage.Index] = requested;
            MpNet.Send(new Session.Messages.BossChosenMessage
            {
                StageIndex = stage.Index,
                BossId = requested
            });
            return requested;
        }

        private static bool IsLiveStage(Stage stage)
        {
            var stages = GameMaster.Instance?.CurrentGameRun?.Stages;
            if (stages == null)
            {
                return false;
            }

            for (int i = 0; i < stages.Count; i++)
            {
                if (ReferenceEquals(stages[i], stage))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Applies the host's pick on a client.</summary>
        public static void ApplyHostChoice(int stageIndex, string bossId)
        {
            if (string.IsNullOrEmpty(bossId))
            {
                return;
            }

            PartyBoss[stageIndex] = bossId;
            MpSafe.Run("SetBossSyncPatch.Apply", () => ApplyKnownBosses());
        }

        /// <summary>
        /// Put the party's boss onto any stage that is missing it.
        /// </summary>
        public static void Tick()
        {
            if (!MpSession.IsActive || !MpSession.IsInRun || PartyBoss.Count == 0)
            {
                return;
            }

            MpSafe.Run("SetBossSyncPatch.Tick", ApplyKnownBosses);
        }

        private static void ApplyKnownBosses()
        {
            var stages = GameMaster.Instance?.CurrentGameRun?.Stages;
            if (stages == null)
            {
                return;
            }

            for (int i = 0; i < stages.Count; i++)
            {
                var stage = stages[i];

                // Act 1 is the only act the party picks a boss for.
                if (stage == null || !stage.IsSelectingBoss || stage.Boss != null
                    || !PartyBoss.TryGetValue(stage.Index, out string bossId))
                {
                    continue;
                }

                ApplyingRemote = true;
                try
                {
                    stage.SetBoss(bossId);
                }
                finally
                {
                    ApplyingRemote = false;
                }

                // The map icon is not touched here on purpose.
                // The panel is usually not built yet at this point. BossMapIconPatch waits for one instead.
                MpPlugin.Log.LogInfo($"Act boss set by the host: {bossId}");
            }
        }
    }

    /// <summary>
    /// Only the host's choice at the Act 1 boss selection node actually counts for the fight. Apply the host's choice to just the map here.
    /// </summary>
    [HarmonyPatch(typeof(MapNodeWidget))]
    public static class BossMapIconPatch
    {
        /// <summary>The boss whose portrait is currently on the map, so the check below is idle.</summary>
        private static string _applied = string.Empty;

        public static void Reset() => _applied = string.Empty;

        /// <summary>The boss everyone is going to fight, or empty while nobody has decided.</summary>
        private static string SettledBoss()
        {
            var stage = GameMaster.Instance?.CurrentGameRun?.CurrentStage;
            return stage != null && stage.IsSelectingBoss ? stage.SelectedBoss ?? string.Empty : string.Empty;
        }

        /// <summary>
        /// Refuses to draw anything but the settled boss.
        /// Prevents really fast-clicking clients from picking a boss and then looking at the map before the host has chosen.
        /// If the client does this now, they'll see the "unknown boss" icon as per usual.
        /// </summary>
        [HarmonyPrefix]
        [HarmonyPatch(nameof(MapNodeWidget.SetBoss))]
        private static bool PrefixSetBoss(ref string bossId)
        {
            string settled = MpSafe.Run("BossMapIconPatch.Prefix", () =>
            {
                if (!MpSession.IsActive || !MpSession.IsInRun)
                {
                    return null;
                }

                var stage = GameMaster.Instance?.CurrentGameRun?.CurrentStage;
                return stage != null && stage.IsSelectingBoss ? stage.SelectedBoss ?? string.Empty : null;
            }, null);

            if (settled == null)
            {
                return true;
            }

            if (settled.Length == 0)
            {
                return false;
            }

            bossId = settled;
            return true;
        }

        [HarmonyPostfix]
        [HarmonyPatch(nameof(MapNodeWidget.Initialize))]
        private static void PostfixInitialize(MapNodeWidget __instance, MapNode mapNode)
        {
            MpSafe.Run("BossMapIconPatch.Initialize", () =>
            {
                if (!MpSession.IsActive || !MpSession.IsInRun
                    || mapNode == null || mapNode.StationType != StationType.Boss)
                {
                    return;
                }

                string settled = SettledBoss();
                if (mapNode.Map?.BossId == null && settled.Length > 0)
                {
                    __instance.SetBoss(settled);
                }
            });
        }

        /// <summary>
        /// Draws the settled boss as soon as there is a panel to draw it on, once.
        /// This is polled pretty aggressively rather than immediately set upon the network message arriving, because the map may be null or inactive.
        /// Once it's been set, this polling stops.
        /// </summary>
        public static void Update()
        {
            MpSafe.Run("BossMapIconPatch.Update", () =>
            {
                if (!MpSession.IsActive || !MpSession.IsInRun)
                {
                    return;
                }

                string settled = SettledBoss();
                if (settled.Length == 0 || settled == _applied)
                {
                    return;
                }

                var widget = FinalMapWidget();
                if (widget == null)
                {
                    return;
                }

                widget.SetBoss(settled);
                _applied = settled;
            });
        }

        /// <summary>
        /// The boss node's widget, or null while there is no map to find it on.
        /// </summary>
        private static MapNodeWidget FinalMapWidget()
        {
            var manager = UiManager.Instance;
            if (manager == null
                || !manager._panelTable.TryGetValue(typeof(MapPanel), out var panel))
            {
                return null;
            }

            var map = panel as MapPanel;
            if (map == null || map._map == null || map._mapNodeWidgets == null)
            {
                return null;
            }

            return map.FinalWidget;
        }
    }

    /// <summary>
    /// Patches the Act 1 boss shining exhibit reward to offer you an exhibit of the character you chose, rather than the character you fought.
    /// </summary>
    [HarmonyPatch(typeof(GameRunController), nameof(GameRunController.RollBossExhibits))]
    public static class BossExhibitChoicePatch
    {
        [HarmonyPrefix]
        private static void Prefix(GameRunController __instance, ref string bossId)
        {
            // A ref parameter cannot be captured by a lambda, so decide first and assign after.
            string current = bossId;
            var replacement = MpSafe.Run("BossExhibitChoicePatch", () => Substitute(__instance, current), null);

            if (!string.IsNullOrEmpty(replacement))
            {
                MpPlugin.Log.LogInfo($"Boss reward drawn from your pick ({replacement}) rather than {current}");
                bossId = replacement;
            }
        }

        private static string Substitute(GameRunController gameRun, string bossId)
        {
            if (!MpSession.IsActive || !MpSession.IsInRun
                || string.IsNullOrEmpty(SetBossSyncPatch.LocalPick)
                || SetBossSyncPatch.LocalPick == bossId)
            {
                return null;
            }

            // Only run if this is Act 1 and the boss was chosen
            var stage = gameRun?.CurrentStage;
            if (stage == null || !stage.IsSelectingBoss
                || stage.Index != SetBossSyncPatch.LocalPickStage
                || stage.Boss == null || stage.Boss.Id != bossId)
            {
                return null;
            }

            // If you add an Act 1 boss to the game with Sideloader, please make sure its exhibits are properly configured so that the following code path doesn't happen.
            // To my knowledge, every character mod that adds a boss does this correctly already. Just sayin'.
            if (EnemyUnitConfig.FromId(SetBossSyncPatch.LocalPick) == null)
            {
                MpPlugin.Log.LogWarning($"No unit config for '{SetBossSyncPatch.LocalPick}'! Keeping the boss's own reward");
                return null;
            }

            return SetBossSyncPatch.LocalPick;
        }
    }
}
