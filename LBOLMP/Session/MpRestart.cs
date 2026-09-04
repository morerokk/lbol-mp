using LBOLMP.Net;
using LBOLMP.Session.Messages;
using LBoL.Core.SaveData;
using LBoL.Presentation;
using UnityEngine;

namespace LBOLMP.Session
{
    /// <summary>
    /// Restart Level can only be done by the host, and is applied to everyone if they do restart.
    /// </summary>
    internal static class MpRestart
    {
        /// <summary>
        /// Set while carrying out the party's restart locally, so the patch on the game's own restart lets that one through instead of asking the host about it again.
        /// </summary>
        private static bool _applying;

        public static void RegisterHandlers() => MpNet.On<StationRestartMessage>(OnRemote);

        internal static bool LocalDecides =>
            !MpSession.IsActive || !MpSession.IsInRun || MpNet.IsHost;

        /// <summary>
        /// Called in place of the game's restart. Returns true to let it go ahead right now.
        /// </summary>
        internal static bool OnLocalRequest()
        {
            if (_applying)
            {
                return true;
            }

            // Playing alone, or in a session that has not started a run.
            if (!MpSession.IsActive || !MpSession.IsInRun)
            {
                return true;
            }

            if (!MpNet.IsHost)
            {
                MpPlugin.Log.LogInfo(
                    "Ignoring Restart Level: in multiplayer only the host restarts the level");
                return false;
            }

            // Check if the host is behind the party. Ordering a restart from here would tell everybody to
            // replay the station they are in, which would keep people desynced.
            if (Patches.MapVotingPatch.MoveInFlight)
            {
                var target = Patches.MapVotingPatch.PendingNode;
                MpPlugin.Log.LogWarning(
                    $"Refusing Restart Level: the party has moved on to ({target.X}, {target.Y}) "
                    + "and this client is not standing there yet");
                UI.MpNotice.Show(L10n.Get(MpText.RestartPartyMoving));
                return false;
            }

            // Check if the host is about to order a restart it has not checked it can perform itself.
            if (!CanRestartHere(out var timing))
            {
                MpPlugin.Log.LogWarning(
                    "Restart Level pressed, but this run has nothing to restart from; telling nobody");
                return true;
            }

            var here = Here();
            MpPlugin.Log.LogInfo(
                $"Restarting the level for the whole party (from {timing}, node ({here.X}, {here.Y}))");
            MpNet.Send(new StationRestartMessage
            {
                Timing = (int)timing,
                StageIndex = here.Stage,
                X = here.X,
                Y = here.Y
            });

            Teardown();
            return true;
        }

        private static void OnRemote(StationRestartMessage message)
        {
            if (message.SenderId == MpNet.LocalPlayerId)
            {
                return;
            }

            // Only the host restarts the level. A client saying otherwise is either a bug or somebody being clever, and either way the party does not act on it.
            if (message.SenderId != MpConstants.HostPlayerId)
            {
                MpPlugin.Log.LogWarning(
                    $"Ignoring a restart from player {message.SenderId}: only the host restarts the level");
                return;
            }

            MpSafe.Run("MpRestart.OnRemote", () =>
            {
                (int Stage, int X, int Y) ordered = (message.StageIndex, message.X, message.Y);
                var here = Here();

                if (here == ordered)
                {
                    Restart((SaveTiming)message.Timing);
                    return;
                }

                // If we're behind the party RIGHT as we're moving and then someone tries to restart really quickly
                // (in that small 0.25s timeframe that someone somehow hit), defer the restart until we actually get there
                // How does this even happen
                _parked = ordered;
                _parkedTiming = (SaveTiming)message.Timing;
                _parkedUntil = Time.unscaledTime + ParkSeconds;

                MpPlugin.Log.LogInfo(
                    $"The host restarted at node ({ordered.X}, {ordered.Y}), but this client is still "
                    + $"at ({here.X}, {here.Y}); holding the restart until it gets there");
            });
        }

        /// <summary>The host's order, held until this client is standing where the host was.</summary>
        private static (int Stage, int X, int Y)? _parked;

        private static SaveTiming _parkedTiming;
        private static float _parkedUntil;

        private const float ParkSeconds = 180f;

        /// <summary>Polled each frame, so a held order lands the moment this client catches up.</summary>
        public static void Update()
        {
            if (_parked == null)
            {
                return;
            }

            MpSafe.Run("MpRestart.Update", () =>
            {
                var ordered = _parked.Value;

                if (!MpSession.IsActive || !MpSession.IsInRun)
                {
                    _parked = null;
                    return;
                }

                if (Here() == ordered && CanRestartHere(out _))
                {
                    _parked = null;
                    Restart(_parkedTiming);
                    return;
                }

                if (Time.unscaledTime >= _parkedUntil)
                {
                    _parked = null;
                    var here = Here();
                    MpPlugin.Log.LogError(
                        $"Gave up on the host's restart at node ({ordered.X}, {ordered.Y}): this client "
                        + $"is still at ({here.X}, {here.Y}) and the party is now out of step");
                }
            });
        }

        /// <summary>Drops a held order. Called when a run ends, so it cannot fire into the next one.</summary>
        public static void Reset() => _parked = null;

        /// <summary>
        /// The act and node this client is standing on.
        /// </summary>
        private static (int Stage, int X, int Y) Here()
        {
            var gameRun = GameMaster.Instance?.CurrentGameRun;
            var node = gameRun?.CurrentMap?.VisitingNode;
            return (gameRun?.CurrentStage?.Index ?? -1, node?.X ?? -1, node?.Y ?? -1);
        }

        /// <summary>Carry out the host's order on this client.</summary>
        private static void Restart(SaveTiming ordered)
        {
            if (!CanRestartHere(out var timing))
            {
                MpPlugin.Log.LogWarning(
                    "The host restarted the level, but this client has no save to restart from; staying put");
                return;
            }

            if (timing != ordered)
            {
                MpPlugin.Log.LogWarning(
                    $"The host restarted from {ordered}, but this client is at {timing}; "
                    + "restarting from here instead");
            }

            MpPlugin.Log.LogInfo("The host restarted the level");

            Teardown();

            _applying = true;
            try
            {
                GameMaster.RequestReenterStation();
            }
            finally
            {
                _applying = false;
            }
        }

        /// <summary>
        /// Whether the game would actually rewind properly.
        /// </summary>
        private static bool CanRestartHere(out SaveTiming timing)
        {
            timing = SaveTiming.RunEnd;

            var master = GameMaster.Instance;
            if (master?.CurrentGameRun?.CurrentStation == null)
            {
                return false;
            }

            var save = master.GameRunSaveData;
            if (save == null)
            {
                return false;
            }

            timing = save.Timing;
            return timing == SaveTiming.EnterMapNode
                   || timing == SaveTiming.BattleFinish
                   || timing == SaveTiming.AfterBossReward
                   || timing == SaveTiming.Adventure;
        }

        /// <summary>
        /// The game does not like force restarting under certain conditions,
        /// so over here, we tear down anything that might still be open.
        /// (Particularly those pesky dialog panels that can only ever have 1 open at a time)
        /// </summary>
        private static void Teardown()
        {
            MpSafe.Run("MpRestart.Teardown", () =>
            {
                UI.MpUiTeardown.CloseOpenWindows();

                Patches.EnemyDamageHook.UnhookAll();
                Patches.PlayerDamageHook.Unhook();
                Patches.CardPlayHook.Unhook();
                Battle.MpPrivateEnemies.Reset();

                Battle.MpBattleSync.LeaveBattle();
                Battle.MpBattleSync.Reset();

                MapSync.ClearCommit();
                Patches.MapVotingPatch.Reset();
            });
        }
    }
}
