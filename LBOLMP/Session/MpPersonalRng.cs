using LBOLMP.Net;
using LBoL.Base;
using LBoL.Core;
using LBoL.Presentation;

namespace LBOLMP.Session
{
    /// <summary>
    /// Keeps a player's event rewards their own for the whole run, not just the first act.
    /// </summary>
    internal static class MpPersonalRng
    {
        internal static ulong Salt =>
            MpNet.LocalPlayerId <= 0 ? 0UL : 0x9E3779B97F4A7C15UL * (ulong)(MpNet.LocalPlayerId + 1);

        /// <summary>The generator we last installed, to recognise somebody else replacing it.</summary>
        private static RandomGen _installed;

        private static RandomGen _supply;

        /// <summary>The run the two above belong to, so a new one starts clean.</summary>
        private static GameRunController _run;

        /// <summary>
        /// What Reisen's supply offers are rolled from.
        /// </summary>
        internal static RandomGen Supply => _supply;

        internal static void Reset()
        {
            _installed = null;
            _supply = null;
            _run = null;
        }

        /// <summary>
        /// Called every frame for reasons that are beyond me now.
        /// I think it was related to people being slow loaders, but this will return early after the 1st time anyway.
        /// </summary>
        internal static void Tick()
        {
            if (!MpNet.IsOnline)
            {
                return;
            }

            ulong salt = Salt;
            if (salt == 0)
            {
                return;
            }

            var run = GameMaster.Instance?.CurrentGameRun;
            if (run == null)
            {
                Reset();
                return;
            }

            var current = run.AdventureRng;
            if (ReferenceEquals(current, _installed) && ReferenceEquals(run, _run))
            {
                return;
            }

            ulong from = current?.State ?? run.RootSeed;

            var personal = new RandomGen(Seed(from, salt, "adventure"));
            run.AdventureRng = personal;

            _installed = personal;
            _supply = new RandomGen(Seed(from, salt, "supply"));

            if (!ReferenceEquals(run, _run))
            {
                _run = run;
                MpPlugin.Log.LogInfo(
                    $"Event rewards personalised for player {MpNet.LocalPlayerId}, and kept that way "
                    + "for the rest of the run");
            }
        }

        private static ulong Seed(ulong from, ulong salt, string streamName)
        {
            ulong hash = 14695981039346656037UL;
            foreach (char c in streamName)
            {
                hash ^= c;
                hash *= 1099511628211UL;
            }

            ulong seed = from ^ salt ^ hash;
            return seed == 0 ? 1UL : seed;
        }
    }
}
