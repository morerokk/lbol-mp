using System;
using System.Collections.Generic;
using System.Linq;
using LBOLMP.Net;
using LBOLMP.Session.Messages;
using LBoL.Core;
using LBoL.Core.Stations;
using LBoL.Presentation;
using LBoL.Presentation.UI;
using LBoL.Presentation.UI.Panels;

namespace LBOLMP.Session
{
    /// <summary>
    /// Map navigation is a group decision: clicking a node casts a vote instead of moving, and the
    /// host commits everybody once *every* player has voted for the same node.
    /// </summary>
    public static class MapSync
    {
        /// <summary>playerId and the node they voted for on the current map.</summary>
        private static readonly Dictionary<int, (int X, int Y)> Votes = new Dictionary<int, (int, int)>();

        /// <summary>Barriers that have been released, so late arrivals don't wait forever.</summary>
        private static readonly HashSet<string> ReleasedBarriers = new HashSet<string>();

        /// <summary>Host-side tally of who has reached each barrier.</summary>
        private static readonly Dictionary<string, HashSet<int>> BarrierArrivals =
            new Dictionary<string, HashSet<int>>();

        /// <summary>Node the group agreed on and that we are about to enter locally.</summary>
        private static (int X, int Y)? _committed;

        /// <summary>
        /// How many decisions the party has made.
        /// </summary>
        private static int _decision;

        public static IReadOnlyDictionary<int, (int X, int Y)> CurrentVotes => Votes;

        /// <summary>True once somebody has picked a node and the party is deciding.</summary>
        public static bool VoteInProgress => Votes.Count > 0;

        /// <summary>True when every connected player has voted for the same node.</summary>
        public static bool PartyAgrees => TryGetUnanimousNode(out _);

        public static void RegisterHandlers()
        {
            MpNet.On<MapVoteMessage>(OnVote);
            MpNet.On<MapCommitMessage>(OnCommit);
            MpNet.On<BossChosenMessage>(OnBossChosen);
            MpNet.On<BarrierArriveMessage>(OnBarrierArrive);
            MpNet.On<BarrierReleaseMessage>(OnBarrierRelease);
        }

        public static void Reset()
        {
            Votes.Clear();
            BarrierArrivals.Clear();
            ReleasedBarriers.Clear();
            _committed = null;
            _localVote = null;
            _decision = 0;
            Patches.MapVotingPatch.Reset();
            Patches.SetBossSyncPatch.Reset();
            Patches.SelectStationSyncPatch.Reset();
            Patches.BossMapIconPatch.Reset();
            UI.MapVoteMarkers.Clear();
        }

        public static void Update()
        {
            Patches.MapVotingPatch.Update();
            Patches.BossMapIconPatch.Update();
            UI.MapVoteMarkers.Update();
            ResendLocalVote();
        }

        /// <summary>The node this client last clicked, kept so the vote can be repeated.</summary>
        private static (int X, int Y)? _localVote;

        private static float _nextResend;

        /// <summary>
        /// Repeat our own vote while the party is still undecided.
        /// </summary>
        private static void ResendLocalVote()
        {
            if (_localVote == null || _committed != null || !MpSession.IsInRun)
            {
                return;
            }

            if (PartyAgrees || UnityEngine.Time.unscaledTime < _nextResend)
            {
                return;
            }

            _nextResend = UnityEngine.Time.unscaledTime + ResendInterval;
            SendVote(_localVote.Value.X, _localVote.Value.Y);
        }

        private const float ResendInterval = 2f;

        public static void OnPlayerLeft(int playerId)
        {
            Votes.Remove(playerId);
            foreach (var arrivals in BarrierArrivals.Values)
            {
                arrivals.Remove(playerId);
            }

            // Their absence may have completed a pending decision.
            if (MpNet.IsHost)
            {
                TryCommit();
                foreach (var barrierId in BarrierArrivals.Keys.ToList())
                {
                    TryRelease(barrierId);
                }
            }

            if (VoteInProgress)
            {
                MpSession.StatusLine = DescribeVoteState();
            }
        }

        //--
        // VOTING
        //--

        /// <summary>Called from the map panel patch when the local player clicks a node.</summary>
        public static void CastVote(int x, int y)
        {
            _localVote = (x, y);
            _nextResend = UnityEngine.Time.unscaledTime + ResendInterval;
            SendVote(x, y);
        }

        private static void SendVote(int x, int y)
        {
            var from = CurrentNode();
            MpNet.Send(new MapVoteMessage
            {
                StageIndex = CurrentStageIndex(),
                X = x,
                Y = y,
                FromX = from.X,
                FromY = from.Y,
                Decision = _decision
            });
        }

        /// <summary>Where the party is standing right now. Shared by everyone, since they should only ever be able to move together.</summary>
        private static (int X, int Y) CurrentNode()
        {
            var node = GameMaster.Instance?.CurrentGameRun?.CurrentMap?.VisitingNode;
            return node == null ? (-1, -1) : (node.X, node.Y);
        }

        private static void OnVote(MapVoteMessage message)
        {
            if (message.Decision != _decision)
            {
                return;
            }

            if (message.StageIndex != CurrentStageIndex())
            {
                // A vote from a peer that is still on the previous act. They'll revote later. Prevents Act 4 shenanigans I guess?
                return;
            }

            var here = CurrentNode();
            if (message.FromX != here.X || message.FromY != here.Y)
            {
                return;
            }

            Votes[message.SenderId] = (message.X, message.Y);
            MpSession.StatusLine = DescribeVoteState();

            if (MpNet.IsHost)
            {
                TryCommit();
            }
        }

        /// <summary>
        /// Host-only. Commits the party when, and only when, everybody has voted for one node.
        /// </summary>
        private static void TryCommit()
        {
            if (_committed != null)
            {
                return;
            }

            if (!TryGetUnanimousNode(out var node))
            {
                return;
            }

            var commit = new MapCommitMessage
            {
                StageIndex = CurrentStageIndex(),
                X = node.X,
                Y = node.Y
            };

            DecideStationContents(commit);
            MpNet.Send(commit);
        }

        /// <summary>
        /// True when every connected player has voted and all of those votes name the same node.
        /// Players who have lost connection are not waited on.
        /// </summary>
        private static bool TryGetUnanimousNode(out (int X, int Y) node)
        {
            node = default;
            bool any = false;

            foreach (var player in MpSession.RespondingPlayers)
            {
                if (!Votes.TryGetValue(player.Id, out var vote))
                {
                    return false;
                }

                if (!any)
                {
                    node = vote;
                    any = true;
                }
                else if (vote != node)
                {
                    return false;
                }
            }

            return any;
        }

        /// <summary>
        /// What the party is waiting on, in words (translated).
        /// </summary>
        public static string DescribeVoteState()
        {
            var missing = MpSession.RespondingPlayers
                .Where(p => !Votes.ContainsKey(p.Id))
                .Select(p => p.Name)
                .ToList();

            if (missing.Count > 0)
            {
                return missing.Count == 1
                    ? L10n.Get(MpText.MapWaitingForOne, missing[0])
                    : L10n.Get(MpText.MapWaitingForMany, string.Join(", ", missing));
            }

            if (TryGetUnanimousNode(out _))
            {
                return L10n.Get(MpText.MapMovingToNode);
            }

            var picks = MpSession.RespondingPlayers
                .Where(p => Votes.ContainsKey(p.Id))
                .Select(p => L10n.Get(MpText.MapPick, p.Name, Votes[p.Id].X, Votes[p.Id].Y));

            return L10n.Get(MpText.MapPartySplit, string.Join(", ", picks));
        }

        /// <summary>
        /// Host-only. Settles anything about the target node that players must not roll for
        /// themselves, and attaches it to the message so it's already known before anybody enters.
        /// </summary>
        private static void DecideStationContents(MapCommitMessage commit)
        {
            var gameRun = GameMaster.Instance?.CurrentGameRun;
            var stage = gameRun?.CurrentStage;
            if (stage == null)
            {
                return;
            }

            MapNode node;
            try
            {
                node = gameRun.CurrentMap.Nodes[commit.X, commit.Y];
            }
            catch (IndexOutOfRangeException)
            {
                return;
            }

            switch (node.StationType)
            {
                case StationType.Adventure:
                    var type = Patches.AdventureSyncPatch.RollLocally(stage);
                    if (type != null)
                    {
                        commit.AdventureType = type.Name;
                    }
                    break;
            }
        }

        /// <summary>Host's event choice for the node we are entering, if it made one.</summary>
        public static string PendingAdventureType { get; private set; } = string.Empty;

        /// <summary>
        /// Close the current decision. Everything still in flight for it is now stale.
        /// </summary>
        private static void EndDecision()
        {
            _decision++;
            Votes.Clear();
            _localVote = null;
        }

        private static void OnCommit(MapCommitMessage message)
        {
            EndDecision();
            _committed = (message.X, message.Y);
            PendingAdventureType = message.AdventureType;
            MpSession.StatusLine = L10n.Get(MpText.MapHeadingTo, message.X, message.Y);

            Patches.MapVotingPatch.EnterCommittedNode(message.X, message.Y);
        }

        private static void OnBossChosen(BossChosenMessage message)
        {
            if (MpNet.IsHost)
            {
                return;
            }
            Patches.SetBossSyncPatch.ApplyHostChoice(message.StageIndex, message.BossId);
        }

        /// <summary>
        /// Drop the tally outright.
        /// </summary>
        public static void ClearVotes()
        {
            Votes.Clear();
            _localVote = null;
        }

        /// <summary>Called once the local client has actually started moving, so votes can restart.</summary>
        public static void ClearCommit()
        {
            _committed = null;
            _localVote = null;
            Votes.Clear();
        }

        private static int CurrentStageIndex()
        {
            var gameRun = GameMaster.Instance?.CurrentGameRun;
            return gameRun?.CurrentStage?.Index ?? -1;
        }

        //--
        // barriers/waiting gates
        //--

        /// <summary>
        /// Announce that the local player has finished the current phase. Returns immediately.
        /// </summary>
        public static void Arrive(string barrierId)
        {
            MpNet.Send(new BarrierArriveMessage { BarrierId = barrierId });
        }

        public static bool IsReleased(string barrierId) => ReleasedBarriers.Contains(barrierId);

        public static event Action<string> BarrierReleased;

        private static void OnBarrierArrive(BarrierArriveMessage message)
        {
            if (!BarrierArrivals.TryGetValue(message.BarrierId, out var arrivals))
            {
                arrivals = new HashSet<int>();
                BarrierArrivals[message.BarrierId] = arrivals;
            }
            arrivals.Add(message.SenderId);

            if (MpNet.IsHost)
            {
                TryRelease(message.BarrierId);
            }
        }

        private static void TryRelease(string barrierId)
        {
            if (ReleasedBarriers.Contains(barrierId))
            {
                return;
            }

            if (!BarrierArrivals.TryGetValue(barrierId, out var arrivals))
            {
                return;
            }

            foreach (var player in MpSession.ConnectedPlayers)
            {
                if (!arrivals.Contains(player.Id))
                {
                    return;
                }
            }

            MpNet.Send(new BarrierReleaseMessage { BarrierId = barrierId });
        }

        private static void OnBarrierRelease(BarrierReleaseMessage message)
        {
            ReleasedBarriers.Add(message.BarrierId);
            BarrierArrivals.Remove(message.BarrierId);
            BarrierReleased?.Invoke(message.BarrierId);
        }

        /// <summary>Forget a barrier so the same id can be reused later in the run.</summary>
        public static void Forget(string barrierId)
        {
            ReleasedBarriers.Remove(barrierId);
            BarrierArrivals.Remove(barrierId);
        }
    }
}
