using LBOLMP.Session.Battle;
using LBoL.Core;
using LBoL.Core.Stations;
using LBoL.Core.Units;
using LBoL.EntityLib.Cards.Misfortune;
using LBoL.Presentation;
using UnityEngine;

namespace LBOLMP.Session
{
    /// <summary>
    /// Handles players dying inside events.
    /// </summary>
    public static class MpEventDeaths
    {
        private static GameEventHandler<DieEventArgs> _dyingHandler;
        private static PlayerUnit _hookedPlayer;

        public static void Reset() => Unhook();

        private static void Unhook()
        {
            if (_hookedPlayer != null && _dyingHandler != null)
            {
                MpSafe.Run("MpEventDeaths.Unhook", () =>
                    _hookedPlayer.Dying.RemoveHandler(_dyingHandler, GameEventPriority.Lowest));
            }

            _hookedPlayer = null;
            _dyingHandler = null;
        }

        public static void Tick()
        {
            if (!MpSession.IsActive || !MpSession.IsInRun)
            {
                if (_hookedPlayer != null)
                {
                    Unhook();
                }
                return;
            }

            MpSafe.Run("MpEventDeaths.Tick", () =>
            {
                var gameRun = GameMaster.Instance?.CurrentGameRun;
                Hook(gameRun);
                ReviveIfDowned(gameRun);
            });
        }

        private static void Hook(GameRunController gameRun)
        {
            var player = gameRun?.Player;
            if (player == _hookedPlayer)
            {
                return;
            }

            Unhook();

            if (player == null)
            {
                return;
            }

            _dyingHandler = args => MpSafe.Run("MpEventDeaths.OnDying", () => OnDying(args));
            player.Dying.AddHandler(_dyingHandler, GameEventPriority.Lowest);
            _hookedPlayer = player;
        }

        private static void OnDying(DieEventArgs args)
        {
            if (!OutsideBattle() || args.Unit == null || args.Unit != _hookedPlayer)
            {
                return;
            }

            if (args.IsCanceled)
            {
                return;
            }

            if (!args.CanCancel)
            {
                return;
            }

            args.CancelBy(args.Unit);
            MpPlugin.Log.LogInfo("Player death in combat");

            if (GameMaster.Instance?.CurrentGameRun?.CurrentStation is AdventureStation)
            {
                MpEventBattle.AbortLocalEvent("Player death in event, canceling event");
            }
        }

        private static void ReviveIfDowned(GameRunController gameRun)
        {
            var player = gameRun?.Player;
            if (player == null || player.Hp > 0 || !OutsideBattle())
            {
                return;
            }

            if (gameRun.Status != GameRunStatus.Running)
            {
                return;
            }

            player.Status = UnitStatus.Alive;

            int hp = Mathf.Clamp(
                Mathf.RoundToInt(player.MaxHp * MpSession.ReviveHpFraction), 1, player.MaxHp);
            gameRun.SetHpAndMaxHp(hp, player.MaxHp, true);

            MpSafe.Run("MpEventDeaths.AddRegret",
                () => gameRun.AddDeckCard(Library.CreateCard<Regret>(), true));

            MpPlugin.Log.LogInfo($"Revived player with {hp} HP outside combat");
        }

        private static bool OutsideBattle() =>
            !MpBattleSync.InBattle && GameMaster.Instance?.CurrentGameRun?.Battle == null;
    }
}
