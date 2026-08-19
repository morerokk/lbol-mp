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
using UnityEngine;

namespace LBOLMP.Session.Battle
{
    /// <summary>
    /// Dying stops being the end of your run and becomes the end of your fight.
    ///
    /// A player at 0 HP is knocked down rather than killed: they keep their seat, keep watching, and
    /// cannot act. The party fights on. Win, and they get back up on a sliver of health carrying a
    /// Regret for their trouble. Lose, and the run ends for everybody at once, which is the only
    /// point at which anyone sees a game-over screen.
    ///
    /// The mechanism is the game's own. <c>DieAction</c> raises <c>Unit.Dying</c> before it commits,
    /// and a handler that cancels the event puts the unit back to <c>Alive</c> — this is exactly how
    /// the Ultramarine Orb Elixir (<c>GanzhuYao</c>) saves you from a lethal hit. The difference is
    /// that the elixir heals you back into the fight and this leaves you on the floor at 0 HP.
    ///
    /// Staying Alive-at-zero rather than Dead is load-bearing, because <c>BattleShouldEnd</c> is
    /// <c>_forceWin || Player.IsDead || every enemy gone</c>. A downed player who was really dead
    /// would tear their own battle down instantly and take the vanilla game-over path. Alive at zero
    /// means their battle keeps running: they sit in the input loop, which is also what drains the
    /// replicated damage queue, so the enemies on their screen keep taking the party's hits and the
    /// fight they are watching is the real one. When the last enemy falls, their own
    /// <c>BattleShouldEnd</c> turns true and they leave the battle a winner along with everyone else.
    /// </summary>
    public static class MpDownedPlayers
    {
        /// <summary>Set while we want a death to go through for real, so the hook stands aside.</summary>
        private static bool _allowRealDeath;

        private static GameEventHandler<DieEventArgs> _dyingHandler;
        private static PlayerUnit _hookedPlayer;

        /// <summary>True when the local player is knocked out and watching.</summary>
        public static bool LocalDown { get; private set; }

        /// <summary>
        /// True when the local player takes no part in this fight, for either reason: knocked out
        /// of it, or watching one they declined at an event. Both mean the same thing to the battle
        /// — no turn, no input, nothing published — and differ only in how they end.
        /// </summary>
        public static bool OutOfFight => LocalDown || MpEventBattle.LocalSpectating;

        /// <summary>Names of everyone currently on the floor, for the banner.</summary>
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
            LocalDown = false;
            _allowRealDeath = false;
            _effectsCleared = false;
        }

        // ---------------------------------------------------------------- hook

        /// <summary>
        /// Listen for our own player's death for the duration of a fight.
        ///
        /// Deliberately battle-scoped. <c>GameRunController.Damage</c> runs the same Dying event for
        /// HP lost outside combat, and being knocked down there would leave a player stranded at 0 HP
        /// with no fight to win and no way back up. Out of combat, death still behaves as it always
        /// did.
        /// </summary>
        public static void Hook(BattleController battle)
        {
            Unhook();

            // Every fight starts on your feet. Both flags are cleared on the way out of the last
            // one, so this is belt and braces — but a stale "down" would mean a player who cannot
            // play a single card all fight and has no way to earn their way back, which is a far
            // worse thing to be wrong about than a stale anything else.
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
        ///
        /// Runs at the *lowest* priority, and that is not a detail. Cancelling is a one-shot:
        /// <c>CancelBy</c> sets <c>CanCancel = false</c>, and the <c>IsCanceled</c> setter throws
        /// for anything that tries afterwards. So a real save — the Ultramarine Orb Elixir, which
        /// heals you out of a lethal hit — would throw from inside the Dying phase if we had got
        /// there first, after it had already spent itself on the heal. Going last means anything
        /// that can genuinely rescue the player does so, we see the event already cancelled, and we
        /// stand aside. Being knocked down is the last resort, not the first.
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

            // Somebody already saved them, and saves heal. Nothing for us to do.
            if (args.IsCanceled)
            {
                return;
            }

            if (!args.CanCancel)
            {
                MpPlugin.Log.LogWarning("Something insists this death cannot be cancelled; the run ends here");
                return;
            }

            // The last one standing going down is the party wipe, and that death is real. Everyone
            // else is already watching, so there is nobody left to win the fight for us.
            if (!AnyOtherSeatCanFight())
            {
                MpPlugin.Log.LogInfo("The whole party is down; this death stands");
                AnnounceDown();
                return;
            }

            args.CancelBy(args.Unit);

            // Already on the floor and hit again. The enemy turn that knocked us out carries on to
            // its remaining attacks, and each one re-runs this whole path — so the cancel above has
            // to happen every time, while everything below it happens once.
            if (LocalDown)
            {
                return;
            }

            LocalDown = true;
            MpPlugin.Log.LogInfo("Knocked out; spectating until the party settles this fight");
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

        // ---------------------------------------------------------------- party wipe

        /// <summary>
        /// A seat that could still turn this fight around: not knocked out, and either still in the
        /// battle or out of it as a winner. Read only from explicit signals — never from the mirrored
        /// HP, which arrives on a timer and would let a stale reading end somebody's run.
        /// </summary>
        private static bool CanFight(MpBattleSeat seat)
        {
            if (seat.Down)
            {
                return false;
            }

            // Finished covers both "their battle ended" and "they disconnected"; only a survivor
            // is still of any use to the party.
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
            // Straight to the ally unit, not through the seat. Reviving happens as the battle is
            // being torn down, and the seats go with it — whichever of the two lands first, the
            // character standing next to you has to get up off the floor.
            UI.MpAllyUnits.Revive(message.SenderId);

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

        // ---------------------------------------------------------------- taking no more turns

        /// <summary>
        /// A downed player's turn never begins.
        ///
        /// This sits at the very top of <c>PlayerTurnFlow</c>, ahead of <c>StartPlayerTurnAction</c>,
        /// and that placement does all the work at once. No turn starts, so nothing is drawn — which
        /// matters because a card's <c>OnDraw</c> reactor runs inside <c>DrawCardAction</c> and can
        /// deal damage, add cards or gain mana. A player who is out of the fight has no business
        /// still influencing it. Nothing else that hangs off the start of a turn fires either:
        /// exhibits, mana, status effect ticks.
        ///
        /// The enemies get no turn either, and that falls out of the same placement rather than
        /// needing its own patch. <c>Flow</c> runs <c>PlayerTurnFlow</c>, then the enemy turn, then
        /// the end of the round, all under <c>while (!BattleShouldEnd)</c>. Waiting here means the
        /// only way past this point is for the fight to already be decided — so when the wait ends,
        /// the original iterator finds <c>BattleShouldEnd</c> true, does nothing, and the loop that
        /// would have run the enemy turn exits instead.
        ///
        /// Replicated damage is drained while waiting, exactly as at the enemy-turn gate. It has to
        /// be: this is the one thing keeping the enemies on a spectator's screen in step with the
        /// fight, and it is what eventually ends this wait.
        ///
        /// There is no time limit, for the same reason there is none at the enemy-turn gate — the
        /// party can take as long as it likes. Both ways out are actively driven from
        /// <see cref="Tick"/>: a wipe queues a real death, and a win that somehow left an enemy
        /// standing here is closed out with the game's own <c>InstantWin</c>.
        /// </summary>
        public static IEnumerator<object> WaitWhileDown(BattleController battle)
        {
            if (!MpSession.IsActive || !MpBattleSync.InBattle || battle == null || !OutOfFight)
            {
                yield break;
            }

            MpPlugin.Log.LogInfo(LocalDown
                ? "Down, so taking no turn; watching the rest of the fight"
                : "Not in this fight, so taking no turn; watching it instead");

            float waited = 0f;
            float reportInterval = GateFirstReportSeconds;
            float nextReport = reportInterval;

            while (!MpSafe.Run("DownedGate", () => ShouldStopWaiting(battle), true))
            {
                if (waited > nextReport)
                {
                    reportInterval = Mathf.Min(reportInterval * 2f, GateMaxReportSeconds);
                    nextReport = waited + reportInterval;
                    MpPlugin.Log.LogInfo("Still down, still watching. " + MpBattleSync.DescribeTurnState());
                }

                bool drain = MpSafe.Run("DownedGateDrain", () => battle._debugActionQueue.Count > 0, false);
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
            battle.BattleShouldEnd || !OutOfFight || !MpBattleSync.InBattle || !MpSession.IsActive;

        // ---------------------------------------------------------------- per-frame

        public static void Tick()
        {
            if (!MpSession.IsActive || !OutOfFight)
            {
                return;
            }

            MpSafe.Run("MpDownedPlayers.Tick", () =>
            {
                // Whatever this player was duelling alone leaves with them. Covers both ways of
                // being out of a fight, and has to happen before anything below waits on the
                // battle ending — nothing else on this board can finish that duel.
                MpPrivateEnemies.Dismiss(GameMaster.Instance?.CurrentGameRun?.Battle);

                // Only a knocked-out player has effects to strip or a run to lose. Somebody
                // watching a fight they declined is in no trouble at all: their run cannot end
                // here, and they keep whatever buffs they walked in with.
                if (LocalDown)
                {
                    ClearStatusEffectsOnce();

                    if (EndRunIfPartyWiped())
                    {
                        return;
                    }
                }

                if (EndFightIfEveryFighterIsDown())
                {
                    return;
                }

                EndBattleIfEveryoneElseHasWon();
            });
        }

        private static bool _effectsCleared;

        /// <summary>
        /// Strip the buffs and debuffs off a player who is out of the fight.
        ///
        /// They would otherwise stay for the rest of the battle, because the phase of
        /// <c>DieAction</c> that clears a dead unit's status effects filters out exactly the args we
        /// cancelled — so the one thing the game does to tidy up after a death is the one thing that
        /// does not happen to a player we saved from it.
        ///
        /// Removed through the battle's own action queue rather than by clearing the list, so each
        /// effect's own removal handling runs and the icons come off the ally's widget on every
        /// screen too. Once only: the queue is drained at the gate above, and re-queueing every
        /// frame until it drains would ask for the same effect to be removed hundreds of times.
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

        /// <summary>
        /// Everyone is on the floor, so the run is over for all of us.
        ///
        /// Checked every frame rather than only when a down message lands, because two players can
        /// be knocked out in the same enemy turn: each one sees the other still standing, each one
        /// cancels their own death, and the party is left with nobody able to fight and nobody who
        /// noticed. The pair of down messages resolves it a moment later, here.
        /// </summary>
        private static bool EndRunIfPartyWiped()
        {
            var battle = GameMaster.Instance?.CurrentGameRun?.Battle;
            if (battle == null || !MpBattleSync.InBattle || _allowRealDeath)
            {
                return false;
            }

            // Our own fight is already decided, so there is no wipe left to declare — and declaring
            // one anyway would be a lie. A downed player whose board is clear stands at the
            // end-of-battle gate with their revival still ahead of them, and if the last partner
            // still fighting goes on to lose their own duel, this would read the party as wiped and
            // end a run that was won here several seconds ago. Only a fight still in progress can
            // be lost. See MpBattleSync.WaitForEveryoneToFinish.
            if (battle.BattleShouldEnd)
            {
                return false;
            }

            if (MpBattleSync.AllSeats.Any(CanFight))
            {
                return false;
            }

            MpPlugin.Log.LogInfo("The whole party is down; ending the run");

            // Go out through the game's own death, rather than by setting a status field: this way
            // the Died event, the status-effect cleanup and GameRunController's Failure bookkeeping
            // all run exactly as they do in a single-player loss. The action goes on the debug queue,
            // which is what the input loop we are parked in is already waiting on, so it resolves on
            // the next frame without anything having to be nudged.
            _allowRealDeath = true;
            battle.RequestDebugAction(
                new DamageAction(null, battle.Player, DamageInfo.HpLose(1f)),
                "MP party wipe");
            return true;
        }

        /// <summary>
        /// Everyone who took an event's fight has been knocked out, and somebody who declined it is
        /// still standing.
        ///
        /// This is not a party wipe — a spectator is alive and well, so the run goes on — but it is
        /// not winnable either. Nobody left in the battle can kill anything, so the enemies stay up
        /// and every client sits at the turn gate for ever. Somebody has to call it.
        ///
        /// Ended as a forced win rather than a death, because the fighters are alive at 0 HP and
        /// getting up again is the whole point: <c>LeaveBattle</c> then sees a living player, and
        /// they are revived on the way out exactly as they would be after a fight the party won.
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

            // The fight is lost, and losing it is what forfeits the rest of the event — not being
            // knocked down along the way. Anyone who took the fight and had it won for them by
            // somebody else keeps everything: they are revived, they carry the Regret for going
            // down, and they collect with the rest of the party.
            //
            // Only for those who were actually in it. A spectator has no event left to lose here —
            // their dialogue ended before the fight began — and marking them would leave the flag
            // set with no dialogue of theirs still running to clear it.
            if (MpEventBattle.IsFighting(MpNet.LocalPlayerId))
            {
                MpEventBattle.AbortLocalEvent();
            }

            battle.InstantWin();
            return true;
        }

        /// <summary>
        /// Our own battle should have ended by now, and has not.
        ///
        /// Normally a downed player leaves the fight the same way everybody else does: the party
        /// kills the last enemy, the replicated damage kills it here too, and
        /// <c>BattleShouldEnd</c> turns true on its own. This is the backstop for when it does not —
        /// a dropped damage message leaving one enemy alive on our screen alone. Ordinarily that
        /// costs a cosmetic desync; for someone who cannot act it would be the rest of the run.
        /// </summary>
        private static void EndBattleIfEveryoneElseHasWon()
        {
            var battle = GameMaster.Instance?.CurrentGameRun?.Battle;
            if (battle == null || !MpBattleSync.InBattle || battle.BattleShouldEnd)
            {
                return;
            }

            var others = MpBattleSync.AllSeats.Where(s => s.PlayerId != MpNet.LocalPlayerId).ToList();
            if (others.Count == 0 || !others.All(s => s.Finished) || !others.Any(s => s.Alive))
            {
                return;
            }

            MpPlugin.Log.LogWarning(
                "The party has already won but enemies are still standing here; closing the fight out");
            battle.InstantWin();
        }

        // ---------------------------------------------------------------- revive

        /// <summary>
        /// Get back up, because the party won. Called just before the game leaves the battle, so the
        /// health is already restored by the time <c>LeaveBattle</c> reads it.
        /// </summary>
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

            var seat = MpBattleSync.GetSeat(MpNet.LocalPlayerId);
            if (seat != null)
            {
                seat.Down = false;
            }

            // Something else already put them back on their feet — an Ultramarine Orb Elixir firing
            // on the same death, most likely. Leave that alone; they were never really out.
            if (player.Hp > 0)
            {
                MpPlugin.Log.LogInfo($"Already back up on {player.Hp} HP; no revival needed");
                MpNet.Send(new PlayerRevivedMessage { Hp = player.Hp });
                return;
            }

            int hp = Mathf.Max(1, Mathf.RoundToInt(player.MaxHp * MpSession.ReviveHpFraction));
            hp = Mathf.Min(hp, player.MaxHp);

            // Set rather than heal: healing runs the HealingReceiving event, and an effect that
            // blocks healing would leave the player on the floor at 0 HP with no way to ever get
            // up again.
            gameRun.SetHpAndMaxHp(hp, player.MaxHp, true);

            MpSafe.Run("MpDownedPlayers.AddRegret", () =>
                gameRun.AddDeckCard(Library.CreateCard<Regret>(), true));

            MpPlugin.Log.LogInfo($"The party won; back up on {hp} HP, and carrying a Regret for it");
            MpNet.Send(new PlayerRevivedMessage { Hp = hp });
        }
    }
}
