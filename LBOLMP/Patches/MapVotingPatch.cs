using System;
using HarmonyLib;
using LBOLMP.Net;
using LBOLMP.Session;
using LBoL.Core;
using LBoL.Core.Stations;
using LBoL.Presentation;
using LBoL.Presentation.UI;
using LBoL.Presentation.UI.Panels;
using LBoL.Presentation.UI.Widgets;

namespace LBOLMP.Patches
{
    /// <summary>
    /// Turns a map click into a map vote instead. Nobody moves until the whole party agrees on a node.
    /// </summary>
    [HarmonyPatch(typeof(MapPanel), nameof(MapPanel.RequestEnterNode))]
    public static class MapVotingPatch
    {
        private static bool _bypass;
        private static bool _hasPending;
        private static int _pendingX;
        private static int _pendingY;

        /// <summary>Set after a vote click, so the widgets it disturbed are put back next frame.</summary>
        private static bool _restoreWidgets;

        [HarmonyPrefix]
        private static bool Prefix(MapNodeWidget enteringWidget)
        {
            if (_bypass || !MpSession.IsActive || !MpSession.IsInRun)
            {
                return true;
            }

            if (!ReadyToLeaveStation())
            {
                MpPlugin.Log.LogWarning("Ignored a map click: this client has not finished its station");
                _restoreWidgets = true;
                return false;
            }

            MapSync.CastVote(enteringWidget.X, enteringWidget.Y);
            _restoreWidgets = true;
            MpPlugin.Log.LogInfo($"Voted for map node ({enteringWidget.X}, {enteringWidget.Y})");
            return false;
        }

        /// <summary>Called when the host commits the party to a node.</summary>
        public static void EnterCommittedNode(int x, int y)
        {
            _hasPending = true;
            _pendingX = x;
            _pendingY = y;
            TryEnterPending();
        }

        /// <summary>Polled each frame so a commit that arrived early (before you opened the map) still works.</summary>
        public static void Update()
        {
            if (_restoreWidgets)
            {
                _restoreWidgets = false;
                MpSafe.Run("MapVotingPatch.RestoreWidgets", RestoreNodeWidgets);
            }

            if (_hasPending)
            {
                TryEnterPending();
                return;
            }

            ConfirmMoveTookEffect();
        }

        private static float _movingSince;

        /// <summary>
        /// Hacky value that I hate. This is long enough to cover the travel animation, short enough not to desync anyone.
        /// Can probably be made tighter but the last time I tried, people got stuck and there was big sadness.
        /// I'm not sure if that was related to this or a Steam networking issue that's now fixed, but this value works so whatever.
        /// </summary>
        private const float MoveConfirmSeconds = 6f;

        /// <summary>
        /// True while the party has agreed on a map node, but the current player is not at that node/station yet.
        /// </summary>
        internal static bool MoveInFlight =>
            _hasPending || (_movingSince > 0f && !StandingOn(_pendingX, _pendingY));

        /// <summary>Where the party is headed. Only meaningful while <see cref="MoveInFlight"/>.</summary>
        internal static (int X, int Y) PendingNode => (_pendingX, _pendingY);

        /// <summary>Whether this client's map is currently visiting the given node.</summary>
        internal static bool StandingOn(int x, int y)
        {
            var visiting = GameMaster.Instance?.CurrentGameRun?.CurrentMap?.VisitingNode;
            return visiting != null && visiting.X == x && visiting.Y == y;
        }

        /// <summary>
        /// If the move fails for whatever reason, try again after 6 seconds.
        /// </summary>
        private static void ConfirmMoveTookEffect()
        {
            if (_movingSince <= 0f || UnityEngine.Time.unscaledTime - _movingSince < MoveConfirmSeconds)
            {
                return;
            }

            if (StandingOn(_pendingX, _pendingY))
            {
                _movingSince = 0f;
                return;
            }

            MpPlugin.Log.LogWarning(
                $"Move to ({_pendingX}, {_pendingY}) never took effect; trying again");
            _movingSince = 0f;
            _hasPending = true;
        }

        /// <summary>
        /// When the player clicks on a node, don't immediately mark it as being visited just yet.
        /// </summary>
        private static void RestoreNodeWidgets()
        {
            var map = GameMaster.Instance?.CurrentGameRun?.CurrentMap;
            var panel = UiManager.GetPanel<MapPanel>();
            if (map?.Nodes == null || panel == null)
            {
                return;
            }

            foreach (var node in map.Nodes)
            {
                if (node == null)
                {
                    continue;
                }

                var widget = panel.GetMapNodeWidget(node.X, node.Y);
                if (widget != null)
                {
                    widget.SetStatus(node);
                }
            }
        }

        /// <summary>
        /// True when this client is actually free to leave for another node.
        /// This is false when you arrive at a shop but haven't opened the map yet.
        /// </summary>
        private static bool ReadyToLeaveStation()
        {
            var gameRun = GameMaster.Instance?.CurrentGameRun;
            if (gameRun == null || gameRun.Status != GameRunStatus.Running)
            {
                return false;
            }

            var station = gameRun.CurrentStation;
            return station == null || station.Status == StationStatus.Finished;
        }

        private static void TryEnterPending()
        {
            // If due to connection being wonky (or restarts) we're already at the requested node, confirm that we're already there and clean up any pending votes or moves.
            // This prevents messages like "waiting for X to vote on a node" being constantly displayed in-battle, even when they're literally right there next to you.
            if (StandingOn(_pendingX, _pendingY))
            {
                _hasPending = false;
                _movingSince = 0f;
                return;
            }

            var gameRun = GameMaster.Instance?.CurrentGameRun;
            if (gameRun?.CurrentMap == null || !ReadyToLeaveStation())
            {
                return;
            }

            MapNode node;
            try
            {
                node = gameRun.CurrentMap.Nodes[_pendingX, _pendingY];
            }
            catch (IndexOutOfRangeException)
            {
                MpPlugin.Log.LogError($"Committed node ({_pendingX}, {_pendingY}) is off this client's map");
                _hasPending = false;
                return;
            }

            if (node.Status != MapNodeStatus.Active && node.Status != MapNodeStatus.CrossActive)
            {
                return;
            }

            var panel = UiManager.GetPanel<MapPanel>();
            if (panel == null)
            {
                return;
            }

            // We can't actually move until the map panel is showed and enabled.
            // If someone votes for a node and then goes to check something else, wait for them to get back.
            if (!panel.isActiveAndEnabled)
            {
                return;
            }

            var widget = panel.GetMapNodeWidget(_pendingX, _pendingY);
            if (widget == null)
            {
                MpPlugin.Log.LogError($"Committed node ({_pendingX}, {_pendingY}) has no widget");
                _hasPending = false;
                return;
            }

            _hasPending = false;
            MapSync.ClearCommit();

            // Party is leaving, time to actually go to the node.
            _restoreWidgets = false;

            _bypass = true;
            try
            {
                panel.RequestEnterNode(widget);
                _movingSince = UnityEngine.Time.unscaledTime;
                MpPlugin.Log.LogInfo($"Party moving to map node ({_pendingX}, {_pendingY})");
            }
            catch (Exception e)
            {
                MpPlugin.Log.LogError("Failed to enter the committed node: " + e);
            }
            finally
            {
                _bypass = false;
            }
        }

        public static void Reset()
        {
            _hasPending = false;
            _restoreWidgets = false;
            _movingSince = 0f;
        }
    }
}
