using System;
using System.Collections.Generic;
using System.Linq;
using LBOLMP.Net;
using LBOLMP.Session.Messages;
using LBoL.ConfigData;
using LBoLEntitySideloader;

namespace LBOLMP.Session
{
    /// <summary>
    /// Helper class to find mod mismatches between players.
    /// </summary>
    /// This can help warn players when they don't have the same Sideloader content added.
    /// This only applies to Sideloader content (characters, cards, exhibits, enemies and events).
    /// Even if you agree not to play X character, you might still find their cards from Completely Random, which would probably break things.
    /// Ultimately though, the mod only *warns* about it, it doesn't reject joiners for mismatched mods.
    /// Mods that don't add content (such as gameplay-altering mods, rebalances of existing cards etc) are ignored.
    public static class MpModContent
    {
        private static readonly Dictionary<Type, MpText> Kinds = new Dictionary<Type, MpText>
        {
            [typeof(PlayerUnitConfig)] = MpText.ModKindCharacters,
            [typeof(CardConfig)] = MpText.ModKindCards,
            [typeof(ExhibitConfig)] = MpText.ModKindExhibits,
            [typeof(EnemyUnitConfig)] = MpText.ModKindEnemies,
            [typeof(AdventureConfig)] = MpText.ModKindAdventures
        };

        /// <summary>
        /// What one mod adds, counted per kind.
        /// </summary>
        public sealed class ModEntry
        {
            public string Guid = string.Empty;

            /// <summary>How many entities of each kind.</summary>
            public readonly Dictionary<Type, int> Counts = new Dictionary<Type, int>();

            public int Total => Counts.Values.Sum();

            /// <summary>True when two players' copies of this mod added different things.</summary>
            public bool Matches(ModEntry other) =>
                other != null && Kinds.Keys.All(kind => Count(kind) == other.Count(kind));

            public int Count(Type kind) => Counts.TryGetValue(kind, out int n) ? n : 0;

            /// <summary>"3 cards, 1 character", for the warning.</summary>
            public string Describe() =>
                string.Join(", ", Kinds
                    .Where(kind => Count(kind.Key) > 0)
                    .Select(kind => L10n.Get(kind.Value, Count(kind.Key)))
                    .ToArray());

            public string Encode() =>
                Guid + "|" + string.Join("|", Kinds.Keys.Select(kind => Count(kind).ToString()).ToArray());

            public static ModEntry Decode(string line)
            {
                var parts = (line ?? string.Empty).Split('|');
                if (parts.Length < 1 + Kinds.Count || string.IsNullOrEmpty(parts[0]))
                {
                    return null;
                }

                var entry = new ModEntry { Guid = parts[0] };
                int at = 1;
                foreach (var kind in Kinds.Keys)
                {
                    if (int.TryParse(parts[at++], out int n) && n > 0)
                    {
                        entry.Counts[kind] = n;
                    }
                }

                return entry.Total > 0 ? entry : null;
            }
        }

        /// <summary>Everyone's content, by player id, including our own.</summary>
        private static readonly Dictionary<int, List<ModEntry>> ByPlayer =
            new Dictionary<int, List<ModEntry>>();

        private static string _alreadySentMessageTo = string.Empty;

        private static List<ModEntry> _local;
        private static bool _warned;

        public static void RegisterHandlers() => MpNet.On<ModContentMessage>(OnRemoteContent);

        public static void Reset()
        {
            ByPlayer.Clear();
            _alreadySentMessageTo = string.Empty;
            _warned = false;
        }

        /// <summary>
        /// What this game has loaded, read out of the sideloader's own registry of who added what.
        /// </summary>
        /// This is read once and then cached because it's expensive on performance.
        /// This relies on the assumption that mods cannot add content after starting the game.
        public static List<ModEntry> Local
        {
            get
            {
                if (_local != null)
                {
                    return _local;
                }

                _local = MpSafe.Run("MpModContent.Local", Scan, new List<ModEntry>());
                MpPlugin.Log.LogInfo(_local.Count == 0
                    ? "No sideloaded content beyond this mod's own"
                    : "Sideloaded content: "
                      + string.Join("; ", _local.Select(m => $"{m.Guid} ({m.Describe()})").ToArray()));

                return _local;
            }
        }

        private static List<ModEntry> Scan()
        {
            var byGuid = new Dictionary<string, ModEntry>();

            foreach (var pair in EntityManager.Instance.AllUsers)
            {
                var user = pair.userInfo;
                if (user == null)
                {
                    continue;
                }

                string guid = string.IsNullOrEmpty(user.GUID)
                    ? pair.ass?.GetName()?.Name
                    : user.GUID;

                if (string.IsNullOrEmpty(guid))
                {
                    continue;
                }

                foreach (var definition in user.definitionInstances.Values)
                {
                    // An overwrite changes a vanilla entity rather than adding one, making the issues smaller.
                    if (definition == null || user.IsForOverwriting(definition.GetType()))
                    {
                        continue;
                    }

                    var kind = MpSafe.Run("MpModContent.ConfigType", definition.ConfigType, null);
                    if (kind == null || !Kinds.ContainsKey(kind))
                    {
                        continue;
                    }

                    if (!byGuid.TryGetValue(guid, out var entry))
                    {
                        entry = new ModEntry { Guid = guid };
                        byGuid[guid] = entry;
                    }

                    entry.Counts[kind] = entry.Count(kind) + 1;
                }
            }

            return byGuid.Values.OrderBy(m => m.Guid).ToList();
        }

        public static void Tick()
        {
            if (!MpNet.IsOnline)
            {
                return;
            }

            string here = string.Join(",",
                MpSession.ConnectedPlayers.Select(p => p.Id.ToString()).ToArray());

            if (here == _alreadySentMessageTo)
            {
                return;
            }

            _alreadySentMessageTo = here;

            _warned = false;

            ByPlayer[MpNet.LocalPlayerId] = Local;
            MpNet.Send(new ModContentMessage
            {
                Mods = Local.Select(m => m.Encode()).ToList()
            });
        }

        private static void OnRemoteContent(ModContentMessage message)
        {
            if (message.SenderId == MpNet.LocalPlayerId)
            {
                return;
            }

            ByPlayer[message.SenderId] = message.Mods
                .Select(ModEntry.Decode)
                .Where(entry => entry != null)
                .ToList();

            MpSafe.Run("MpModContent.Announce", Announce);
        }

        /// <summary>Put the mismatch message onscreen as a "toast" warning outside of the F2 menu</summary>
        private static void Announce()
        {
            if (_warned || !Mismatched)
            {
                return;
            }

            _warned = true;
            UI.MpNotice.Show(L10n.Get(MpText.ModMismatchNotice));
            MpPlugin.Log.LogWarning(
                "Players do not have the same sideloaded content: "
                + string.Join("; ", Differences().Select(d => d.Plain).ToArray()));
        }

        /// <summary>True when anybody in the session is missing content somebody else has.</summary>
        public static bool Mismatched => Differences().Count > 0;

        public sealed class Difference
        {
            public string Mod;
            public string Detail;
            public string Who;

            internal string Plain => $"{Mod} ({Detail}): {Who}";
        }

        public static List<Difference> Differences()
        {
            var found = new List<Difference>();
            if (!MpNet.IsOnline)
            {
                return found;
            }

            ByPlayer[MpNet.LocalPlayerId] = Local;

            var known = MpSession.ConnectedPlayers
                .Where(p => ByPlayer.ContainsKey(p.Id))
                .ToList();

            if (known.Count < 2)
            {
                return found;
            }

            foreach (string guid in known.SelectMany(p => ByPlayer[p.Id].Select(m => m.Guid)).Distinct().OrderBy(g => g))
            {
                var haves = new List<MpPlayer>();
                var missing = new List<MpPlayer>();
                var differs = new List<MpPlayer>();
                ModEntry first = null;

                foreach (var player in known)
                {
                    var entry = ByPlayer[player.Id].FirstOrDefault(m => m.Guid == guid);
                    if (entry == null)
                    {
                        missing.Add(player);
                        continue;
                    }

                    haves.Add(player);
                    if (first == null)
                    {
                        first = entry;
                    }
                    else if (!first.Matches(entry))
                    {
                        differs.Add(player);
                    }
                }

                if (missing.Count == 0 && differs.Count == 0)
                {
                    continue;
                }

                found.Add(new Difference
                {
                    Mod = guid,
                    Detail = first == null ? string.Empty : first.Describe(),
                    Who = missing.Count > 0
                        ? L10n.Get(MpText.ModMissingFor, Names(missing))
                        : L10n.Get(MpText.ModDiffersFor, Names(differs))
                });
            }

            return found;
        }

        private static string Names(IEnumerable<MpPlayer> players) =>
            string.Join(", ", players
                .Select(p => p.IsLocal ? L10n.Get(MpText.ModYou) : p.Name)
                .ToArray());
    }
}
