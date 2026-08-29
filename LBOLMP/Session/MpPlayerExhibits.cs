using System;
using System.Collections.Generic;
using System.Linq;
using LBOLMP.Net;
using LBOLMP.Session.Messages;
using LBoL.Presentation;

namespace LBOLMP.Session
{
    /// <summary>
    /// Helper class to determine what exhibits everyone has.
    /// </summary>
    public static class MpPlayerExhibits
    {
        private sealed class Carried
        {
            internal IReadOnlyList<string> Exhibits = Array.Empty<string>();

            /// <summary>
            /// Extra percent this player's Vulnerable adds on enemies, over the base 50%.
            /// </summary>
            internal int EnemyVulnerableExtra;
        }

        private static readonly Dictionary<int, Carried> ByPlayer = new Dictionary<int, Carried>();

        /// <summary>
        /// What we last sent, so this message is only sent when it changes.
        /// </summary>
        private static string _sent;

        private const float CheckInterval = 2f;
        private static float _nextCheck;

        public static void RegisterHandlers() => MpNet.On<PlayerExhibitsMessage>(OnRemoteExhibits);

        public static void Reset()
        {
            ByPlayer.Clear();
            _sent = null;
            _nextCheck = 0f;
        }

        /// <summary>The exhibits a player has, empty for anyone we have not gotten a sync message from yet.</summary>
        public static IReadOnlyList<string> Of(int playerId) =>
            ByPlayer.TryGetValue(playerId, out var carried) ? carried.Exhibits : Array.Empty<string>();

        /// <summary>
        /// How much extra a player's own Vulnerable adds when they hit an enemy, over the base 50%.
        /// </summary>
        /// This is a dirty hack for Laevateinn.
        public static int EnemyVulnerableExtra(int playerId) =>
            ByPlayer.TryGetValue(playerId, out var carried) ? carried.EnemyVulnerableExtra : 0;

        /// <summary>Publish our exhibits when they change, which is fortunately a bit rare.</summary>
        public static void Tick()
        {
            // Exhibits change a handful of times a run, so this is polled slowly rather than
            // rebuilding the list every frame to find out it is the same as last time.
            if (!MpNet.IsOnline || UnityEngine.Time.unscaledTime < _nextCheck)
            {
                return;
            }

            _nextCheck = UnityEngine.Time.unscaledTime + CheckInterval;

            var run = GameMaster.Instance?.CurrentGameRun;
            var player = run?.Player;
            if (player == null)
            {
                return;
            }

            var message = new PlayerExhibitsMessage
            {
                Exhibits = player.Exhibits.Select(e => e.Id).ToList(),
                EnemyVulnerableExtra = run.EnemyVulnerableExtraPercentage
            };

            string state = message.EnemyVulnerableExtra + ":" + string.Join(",", message.Exhibits.ToArray());
            if (state == _sent)
            {
                return;
            }

            _sent = state;
            Remember(MpNet.LocalPlayerId, message);
            MpNet.Send(message);
        }

        private static void OnRemoteExhibits(PlayerExhibitsMessage message)
        {
            if (message.SenderId == MpNet.LocalPlayerId)
            {
                return;
            }

            Remember(message.SenderId, message);
        }

        private static void Remember(int playerId, PlayerExhibitsMessage message)
        {
            ByPlayer[playerId] = new Carried
            {
                Exhibits = message.Exhibits,
                EnemyVulnerableExtra = message.EnemyVulnerableExtra
            };
        }
    }
}
