using System.Collections.Generic;
using System.Linq;
using LBOLMP.Net;
using LBOLMP.Session.Messages;
using LBoL.Core;
using LBoL.Core.Battle;
using LBoL.Core.Battle.BattleActions;
using LBoL.Core.Units;
using LBoL.EntityLib.Cards.Misfortune;
using LBoL.Presentation;
using LBoL.Presentation.Units;
using UnityEngine;

namespace LBOLMP.Session.Battle
{
    /// <summary>
    /// When players die, they start spectating the combat.
    /// They only lose the run if everyone else also does.
    /// If the party wins the combat, the player is revived at 20% HP.
    /// </summary>
    public static class MpDownedPlayers
    {
        private static bool _allowRealDeath;

        private static GameEventHandler<DieEventArgs> _dyingHandler;
        private static PlayerUnit _hookedPlayer;

        /// <summary>True when the local player is knocked out and watching.</summary>
        public static bool LocalDown { get; private set; }

        /// <summary>
        /// True when the local player takes no part in this fight.
        /// </summary>
        public static bool OutOfFight => LocalDown || MpEventBattle.LocalSpectating;

        public static string[] DownedNames =>
            MpBattleSync.AllSeats.Where(s => s.Down).Select(s => s.Name).ToArray();

        public static void RegisterHandlers()
        {
            MpNet.On<PlayerDownMessage>(OnPlayerDown);
            MpNet.On<PlayerRevivedMessage>(OnPlayerRevived);
        }

        public static void Reset()
        {
            Unhook();
            MpSafe.Run("MpDownedPlayers.Reset", RestoreLocalView);
            LocalDown = false;
            _allowRealDeath = false;
            _effectsCleared = false;
        }

        //--
        // hooks
        //--

        /// <summary>
        /// Listen for our own player's death for the duration of a fight.
        /// did.
        /// </summary>
        public static void Hook(BattleController battle)
        {
            Unhook();
            MpSafe.Run("MpDownedPlayers.Hook", RestoreLocalView);

            LocalDown = false;
            _allowRealDeath = false;
            _effectsCleared = false;

            var player = battle?.Player;
            if (!MpSession.IsActive || player == null)
            {
                return;
            }

            _dyingHandler = args => MpSafe.Run("MpDownedPlayers.OnDying", () => OnDying(args));
            player.Dying.AddHandler(_dyingHandler, GameEventPriority.Lowest);
            _hookedPlayer = player;
        }

        public static void Unhook()
        {
            if (_hookedPlayer != null && _dyingHandler != null)
            {
                MpSafe.Run("MpDownedPlayers.Unhook", () =>
                    _hookedPlayer.Dying.RemoveHandler(_dyingHandler, GameEventPriority.Lowest));
            }

            _hookedPlayer = null;
            _dyingHandler = null;
        }

        /// <summary>
        /// Decide whether this death is the end of a fight or the end of the run.
        /// </summary>
        private static void OnDying(DieEventArgs args)
        {
            if (_allowRealDeath || !MpSession.IsActive || !MpBattleSync.InBattle)
            {
                return;
            }

            if (args.Unit == null || !(args.Unit is PlayerUnit) || args.Unit != _hookedPlayer)
            {
                return;
            }

            if (args.IsCanceled)
            {
                return;
            }

            if (!args.CanCancel)
            {
                MpPlugin.Log.LogWarning("Something insists this death cannot be cancelled; the run ends here");
                return;
            }

            if (!AnyOtherSeatCanFight())
            {
                MpPlugin.Log.LogInfo("The whole party is down; this death stands");
                AnnounceDown();
                return;
            }

            args.CancelBy(args.Unit);

            if (LocalDown)
            {
                return;
            }

            LocalDown = true;
            MpPlugin.Log.LogInfo("Knocked out, spectating until the party settles this combat");
            AnnounceDown();
        }

        private static void AnnounceDown()
        {
            var seat = MpBattleSync.GetSeat(MpNet.LocalPlayerId);
            if (seat != null)
            {
                if (seat.Down)
                {
                    return;
                }
                seat.Down = true;
            }

            MpNet.Send(new PlayerDownMessage());
        }

        //--
        // party wipe
        //--

        private static bool CanFight(MpBattleSeat seat)
        {
            if (seat.Down)
            {
                return false;
            }

            return !seat.Finished || seat.Alive;
        }

        private static bool AnyOtherSeatCanFight() =>
            MpBattleSync.AllSeats.Any(s => s.PlayerId != MpNet.LocalPlayerId && CanFight(s));

        private static void OnPlayerDown(PlayerDownMessage message)
        {
            var seat = MpBattleSync.GetSeat(message.SenderId);
            if (seat == null)
            {
                return;
            }

            seat.Down = true;
            MpPlugin.Log.LogInfo($"{seat.Name} was knocked out");
        }

        private static void OnPlayerRevived(PlayerRevivedMessage message)
        {
            UI.MpAllyUnits.Revive(message.SenderId, message.Defibrillated);

            var seat = MpBattleSync.GetSeat(message.SenderId);
            if (seat == null)
            {
                return;
            }

            seat.Down = false;
            seat.Hp = message.Hp;
            seat.Alive = message.Hp > 0;
            MpPlugin.Log.LogInfo($"{seat.Name} got back up on {message.Hp} HP");
        }

        //--
        // taking no more turns
        //--

        /// <summary>
        /// Spectate an event combat.
        /// </summary>
        public static IEnumerator<object> WaitWhileSpectating(BattleController battle)
        {
            if (!MpSession.IsActive || !MpBattleSync.InBattle || battle == null
                || !MpEventBattle.LocalSpectating)
            {
                yield break;
            }

            MpPlugin.Log.LogInfo("Not in this fight, so taking no turn; watching it instead");

            float waited = 0f;
            float reportInterval = GateFirstReportSeconds;
            float nextReport = reportInterval;

            while (!MpSafe.Run("SpectatorGate", () => ShouldStopWaiting(battle), true))
            {
                if (waited > nextReport)
                {
                    reportInterval = Mathf.Min(reportInterval * 2f, GateMaxReportSeconds);
                    nextReport = waited + reportInterval;
                    MpPlugin.Log.LogInfo("Still watching. " + MpBattleSync.DescribeTurnState());
                }

                bool drain = MpSafe.Run("SpectatorGateDrain", () => battle._debugActionQueue.Count > 0, false);
                if (drain)
                {
                    yield return battle.ResolveDebugActions();
                }

                waited += Time.unscaledDeltaTime;
                yield return null;
            }
        }

        private const float GateFirstReportSeconds = 5f;
        private const float GateMaxReportSeconds = 30f;

        private static bool ShouldStopWaiting(BattleController battle) =>
            battle.BattleShouldEnd || !MpEventBattle.LocalSpectating
            || !MpBattleSync.InBattle || !MpSession.IsActive;

        //--
        // per-frame
        //

        public static void Tick()
        {
            if (!MpSession.IsActive || !OutOfFight)
            {
                return;
            }

            MpSafe.Run("MpDownedPlayers.Tick", () =>
            {
                // Make any private enemies run away (Eiki Shiki mirror) so that they don't hold up the combat
                MpPrivateEnemies.Dismiss(GameMaster.Instance?.CurrentGameRun?.Battle);

                if (LocalDown)
                {
                    ClearStatusEffectsOnce();
                    PlayLocalDeathOnce();

                    if (EndRunIfPartyWiped())
                    {
                        return;
                    }
                }

                EndTurnWhileOut();

                if (EndFightIfEveryFighterIsDown())
                {
                    return;
                }

                EndBattleIfEveryoneElseHasWon();
            });
        }

        /// <summary>
        /// Helper to immediately end the player's turn when they are downed.
        /// This keeps the battle running properly (particularly at Seija, but this is better for visuals everywhere else too).
        /// </summary>
        private static void EndTurnWhileOut()
        {
            var battle = GameMaster.Instance?.CurrentGameRun?.Battle;
            if (battle == null || !MpBattleSync.InBattle || battle.BattleShouldEnd
                || !battle.IsWaitingPlayerInput)
            {
                return;
            }

            battle.RequestEndPlayerTurn();
        }

        private static bool _deathPlayed;

        /// <summary>
        /// Since we cancel our own death, the game never plays the explosion for it.
        /// We do it by hand here.
        /// </summary>
        private static void PlayLocalDeathOnce()
        {
            if (_deathPlayed)
            {
                return;
            }

            var view = GameDirector.Instance?.PlayerUnitView;
            if (view == null)
            {
                return;
            }

            _deathPlayed = true;
            MpPlugin.Instance.StartCoroutine(view.DieViewer());
        }

        /// <summary>
        /// Bring the character back on screen.
        /// </summary>
        private static void RestoreLocalView()
        {
            if (!_deathPlayed)
            {
                return;
            }

            _deathPlayed = false;

            var view = GameDirector.Instance?.PlayerUnitView;
            if (view != null)
            {
                UI.MpAllyUnits.Undie(view);
            }
        }

        private static bool _effectsCleared;

        /// <summary>
        /// Strip the buffs and debuffs off a player who is out of the fight
        /// </summary>
        private static void ClearStatusEffectsOnce()
        {
            if (_effectsCleared)
            {
                return;
            }

            var battle = GameMaster.Instance?.CurrentGameRun?.Battle;
            var player = battle?.Player;
            if (player == null)
            {
                return;
            }

            _effectsCleared = true;

            var effects = player.StatusEffects.ToList();
            foreach (var effect in effects)
            {
                battle.RequestDebugAction(new RemoveStatusEffectAction(effect), "MP downed cleanup");
            }

            if (effects.Count > 0)
            {
                MpPlugin.Log.LogInfo($"Clearing {effects.Count} status effect(s) off a downed player");
            }
        }

        private static bool EndRunIfPartyWiped()
        {
            var battle = GameMaster.Instance?.CurrentGameRun?.Battle;
            if (battle == null || !MpBattleSync.InBattle || _allowRealDeath)
            {
                return false;
            }

            if (battle.BattleShouldEnd)
            {
                return false;
            }

            if (MpBattleSync.AllSeats.Any(CanFight))
            {
                return false;
            }

            MpPlugin.Log.LogInfo("The whole party is down; ending the run");


            _allowRealDeath = true;
            battle.RequestDebugAction(
                new DamageAction(null, battle.Player, DamageInfo.HpLose(1f)),
                "MP party wipe");
            return true;
        }

        /// <summary>
        /// Handle cases where 3 people take an event combat but a spectator doesn't... and then everyone in the event combat dies.
        /// This is not a run loss, as the last guy is alive.
        /// </summary>
        private static bool EndFightIfEveryFighterIsDown()
        {
            var battle = GameMaster.Instance?.CurrentGameRun?.Battle;
            if (battle == null || !MpBattleSync.InBattle || battle.BattleShouldEnd)
            {
                return false;
            }

            if (!MpEventBattle.Active)
            {
                return false;
            }

            var seats = MpBattleSync.AllSeats.ToList();
            bool anyWatching = seats.Any(s => s.Spectating);
            bool anyFighterLeft = seats.Any(s => !s.Spectating && CanFight(s));

            if (!anyWatching || anyFighterLeft)
            {
                return false;
            }

            MpPlugin.Log.LogInfo("Everyone who took this fight is down; ending it without ending the run");

            if (MpEventBattle.IsFighting(MpNet.LocalPlayerId))
            {
                MpEventBattle.AbortLocalEvent();
            }

            battle.InstantWin();
            return true;
        }

        /// <summary>
        /// Our own battle should have ended by now, and has not.
        /// </summary>
        private static void EndBattleIfEveryoneElseHasWon()
        {
            var battle = GameMaster.Instance?.CurrentGameRun?.Battle;
            if (battle == null || !MpBattleSync.InBattle || battle.BattleShouldEnd)
            {
                return;
            }

            var others = MpBattleSync.AllSeats.Where(s => s.PlayerId != MpNet.LocalPlayerId).ToList();
            if (others.Count == 0)
            {
                return;
            }

            // Fixes the following:
            // Two players defeated at the same time would each hold the other here forever, and the party's end-of-battle gate waits on both of them.
            bool everyFighterDone = others.All(s => s.Finished || s.Down || s.Spectating);
            bool somebodyWon = others.Any(s => s.Finished && s.Alive);

            if (!everyFighterDone || !somebodyWon)
            {
                return;
            }

            MpPlugin.Log.LogWarning(
                "The party has already won but enemies are still standing here, closing the fight out");
            battle.InstantWin();
        }

        //--
        // revive logic
        //--

        ///<summary>
        /// Handles downed player reviving.
        /// </summary>
        /// <returns>True if they were downed and are now alive again.</returns>
        public static bool ReviveInBattle(float lifeFraction)
        {
            if (!LocalDown)
            {
                return false;
            }

            var gameRun = GameMaster.Instance?.CurrentGameRun;
            var player = gameRun?.Player;
            if (player == null || !player.IsAlive || !MpBattleSync.InBattle)
            {
                MpPlugin.Log.LogWarning("Cannot revive here: player="
                    + (player == null ? "none" : player.IsAlive ? "alive" : "dead")
                    + $", inBattle={MpBattleSync.InBattle}");
                return false;
            }

            LocalDown = false;

            // So a second down in the same combat strips the effects and plays out again.
            _effectsCleared = false;

            int hp = Mathf.Max(1, Mathf.RoundToInt(player.MaxHp * lifeFraction));
            hp = Mathf.Min(hp, player.MaxHp);

            if (player.Hp < hp)
            {
                gameRun.SetHpAndMaxHp(hp, player.MaxHp, true);
            }

            var seat = MpBattleSync.GetSeat(MpNet.LocalPlayerId);
            if (seat != null)
            {
                seat.Down = false;
            }

            MpPlugin.Log.LogInfo($"Revived player: now at {player.Hp} HP");
            MpNet.Send(new PlayerRevivedMessage { Hp = player.Hp, Defibrillated = true });

            UnplayDeath();
            return true;
        }

        /// <summary>
        /// Run the explosion we played for the defeat backwards, and unhide the character.
        /// </summary>
        private static void UnplayDeath()
        {
            if (!_deathPlayed)
            {
                // Never blew up in the first place, so there is nothing to undo but the hiding.
                RestoreLocalView();
                return;
            }

            _deathPlayed = false;

            var view = GameDirector.Instance?.PlayerUnitView;
            if (view != null)
            {
                UI.MpRevivalFx.Play(view);
            }
        }

        public static void ReviveIfWon(GameRunController gameRun)
        {
            if (!LocalDown)
            {
                return;
            }

            var player = gameRun?.Player;
            if (player == null)
            {
                return;
            }

            // Dead here means the party wiped. There is nothing to come back to.
            if (!player.IsAlive)
            {
                LocalDown = false;
                return;
            }

            LocalDown = false;
            RestoreLocalView();

            var seat = MpBattleSync.GetSeat(MpNet.LocalPlayerId);
            if (seat != null)
            {
                seat.Down = false;
            }

            // Something else already revived them, like an Ultramarine Orb Elixir firing on the same death. If so, nevermind, we skip whatever we're going to do next.
            if (player.Hp > 0)
            {
                MpPlugin.Log.LogInfo($"Already back up on {player.Hp} HP; no revival needed");
                MpNet.Send(new PlayerRevivedMessage { Hp = player.Hp });
                return;
            }

            int hp = Mathf.Max(1, Mathf.RoundToInt(player.MaxHp * MpSession.ReviveHpFraction));
            hp = Mathf.Min(hp, player.MaxHp);

            gameRun.SetHpAndMaxHp(hp, player.MaxHp, true);

            MpSafe.Run("MpDownedPlayers.AddRegret", () =>
                gameRun.AddDeckCard(Library.CreateCard<Regret>(), true));

            MpPlugin.Log.LogInfo($"The party won; back up on {hp} HP, and carrying a Regret for it");
            MpNet.Send(new PlayerRevivedMessage { Hp = hp });
        }
    }
}
