using System.Collections.Generic;
using System.Linq;
using LBOLMP.Net;
using LBOLMP.Session.Messages;
using LBoL.Core;
using LBoL.Presentation;

namespace LBOLMP.Session
{
    /// <summary>
    /// Ensures everyone plays the host's version of fights that may or may not be scripted.
    /// </summary>
    internal static class MpRunFlags
    {
        private static GameRunController _settled;

        private static RunFlagsMessage _fromHost;

        internal static void RegisterHandlers()
        {
            MpNet.On<RunFlagsMessage>(OnRunFlags);
        }

        internal static void Reset()
        {
            _settled = null;
            _fromHost = null;
        }

        private static void OnRunFlags(RunFlagsMessage message)
        {
            if (message.SenderId == MpNet.LocalPlayerId)
            {
                return;
            }

            _fromHost = message;
        }

        internal static void Tick()
        {
            if (!MpNet.IsOnline)
            {
                return;
            }

            var run = GameMaster.Instance?.CurrentGameRun;
            if (run == null)
            {
                _settled = null;
                return;
            }

            if (ReferenceEquals(run, _settled))
            {
                return;
            }

            if (MpNet.IsHost)
            {
                _settled = run;

                var ours = Snapshot(run);
                MpPlugin.Log.LogInfo("Sharing this run's scripted flags with the party: "
                                     + Summarise(ours));
                MpNet.Send(ours);
                return;
            }

            if (_fromHost == null || _fromHost.Seed != run.RootSeed)
            {
                return;
            }

            _settled = run;
            Adopt(run, _fromHost);
        }

        /// <summary>The flags a run is carrying right now, in the shape they travel in.</summary>
        private static RunFlagsMessage Snapshot(GameRunController run)
        {
            var message = new RunFlagsMessage
            {
                Seed = run.RootSeed,
                RunFlags = run.ExtraFlags?.ToList() ?? new List<string>()
            };

            foreach (var stage in Stages(run))
            {
                message.StageFlags.Add(stage.ExtraFlags?.ToList() ?? new List<string>());
            }

            return message;
        }

        private static void Adopt(GameRunController run, RunFlagsMessage message)
        {
            var stages = Stages(run);

            if (stages.Count != message.StageFlags.Count)
            {
                MpPlugin.Log.LogWarning(
                    $"The host's run has {message.StageFlags.Count} stages and this one has "
                    + $"{stages.Count}; leaving the scripted flags alone");
                return;
            }

            string before = Summarise(Snapshot(run));

            run.ExtraFlags = new HashSet<string>(message.RunFlags);
            for (int i = 0; i < stages.Count; i++)
            {
                stages[i].ExtraFlags = new HashSet<string>(message.StageFlags[i]);
            }

            string after = Summarise(message);
            if (before == after)
            {
                return;
            }

            MpPlugin.Log.LogInfo($"Took the host's scripted flags: {before} -> {after}");
        }

        private static List<Stage> Stages(GameRunController run) =>
            run.Stages?.ToList() ?? new List<Stage>();


        private static string Summarise(RunFlagsMessage message)
        {
            var parts = new List<string>
            {
                "run[" + string.Join(" ", message.RunFlags.OrderBy(f => f)) + "]"
            };

            for (int i = 0; i < message.StageFlags.Count; i++)
            {
                parts.Add($"act{i + 1}[" + string.Join(" ", message.StageFlags[i].OrderBy(f => f)) + "]");
            }

            return string.Join(" ", parts);
        }
    }
}
