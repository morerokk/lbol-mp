using HarmonyLib;
using LBOLMP.Net;
using LBOLMP.Session;
using LBoL.Core.SaveData;
using LBoL.Presentation;

namespace LBOLMP.Patches
{
    /// <summary>
    /// Similar to starting a new game, pressing "Continue" in the main menu while in a lobby waits for everyone else to press it too,
    /// so that everyone loads the run at the same time.
    /// </summary>
    [HarmonyPatch(typeof(GameMaster), nameof(GameMaster.RestoreGameRun))]
    public static class RestoreGameInterceptPatch
    {
        private static GameRunSaveData _pending;
        private static bool _allowThrough;

        [HarmonyPrefix]
        private static bool Prefix(GameRunSaveData saveData)
        {
            if (_allowThrough || !MpNet.IsOnline || saveData == null)
            {
                return true;
            }

            // If the run is corrupt, let the game handle that
            if (GameMaster.Instance?.CurrentGameRun != null || !IsLoadable(saveData))
            {
                return true;
            }

            if (MpSession.State != MpSessionState.Lobby)
            {
                MpPlugin.Log.LogWarning(
                    "Continue pressed again while the party was still being waited on! Ignoring it... (stop stimming the button)");
                return false;
            }

            var where = PositionIn(saveData);
            _pending = saveData;

            MpSession.SubmitLocalResume(saveData.RootSeed, where.Stage, where.X, where.Y,
                (int)saveData.Difficulty, saveData.Player?.Name);

            MpPlugin.Log.LogInfo(
                $"Continue held: run {saveData.RootSeed}, act {where.Stage + 1}, "
                + $"node ({where.X}, {where.Y}); waiting for the rest of the party");
            return false;
        }

        /// <summary>
        /// Called once the host has confirmed the whole party is on the same run.
        /// </summary>
        public static void BeginPendingResume(ulong seed)
        {
            if (_pending == null)
            {
                MpPlugin.Log.LogWarning("Resume arrived but this client had nothing held");
                return;
            }

            var pending = _pending;
            _pending = null;

            if (pending.RootSeed != seed)
            {
                // Prevent players from accidentally bringing the wrong run into the session by checking the seed.
                // The chances of seed collision are beyond astronomical, anyway.
                MpPlugin.Log.LogError(
                    $"Refusing to continue: the party agreed on run {seed} but this save is {pending.RootSeed}");
                return;
            }

            MpPlugin.Log.LogInfo($"Continuing multiplayer run {seed}");

            Session.Battle.MpLoadGate.Arm("a saved run is being continued");

            _allowThrough = true;
            try
            {
                GameMaster.RestoreGameRun(pending);
            }
            finally
            {
                _allowThrough = false;
            }
        }

        public static void Cancel()
        {
            _pending = null;
        }

        public static bool HasPending => _pending != null;

        private static bool IsLoadable(GameRunSaveData saveData)
        {
            switch (saveData.Timing)
            {
                case SaveTiming.RunEnd: return false;
                case SaveTiming.EnterMapNode: return saveData.EnteringNode != null;
                case SaveTiming.BattleFinish: return saveData.BattleStationEnemyGroup != null;
                case SaveTiming.Adventure: return saveData.AdventureState != null;
                default: return true;
            }
        }

        /// <summary>
        /// Where a save will actually put the other person when they load in.
        /// This can vary for many different reasons (Act 2 boss finished, everyone quits, but some people have already seen the Act 3 map for instance)
        /// </summary>
        internal static (int Stage, int X, int Y) PositionIn(GameRunSaveData saveData)
        {
            int stage = saveData.StageIndex ?? -1;

            if (saveData.Timing == SaveTiming.EnterMapNode && saveData.EnteringNode != null)
            {
                return (stage, saveData.EnteringNode.X, saveData.EnteringNode.Y);
            }

            var path = saveData.Path;
            if (path != null && path.Count > 0)
            {
                var last = path[path.Count - 1];
                return (stage, last.X, last.Y);
            }

            return (stage, -1, -1);
        }
    }

    /// <summary>
    /// Puts the session back in the "lobby" state when the local player goes back to the main menu.
    /// This lets players start new runs after winning/losing the last one, and lets players immediately reload a saved run,
    /// without having to remake the lobby.
    /// </summary>
    [HarmonyPatch(typeof(GameMaster), nameof(GameMaster.LeaveGameRun))]
    internal static class LeaveGameRunPatch
    {
        [HarmonyPostfix]
        private static void Postfix()
        {
            MpSafe.Run("LeaveGameRunPatch", MpSession.BackToLobby);
        }
    }
}
