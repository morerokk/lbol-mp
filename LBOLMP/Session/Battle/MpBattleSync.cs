using System;
using System.Collections.Generic;
using System.Linq;
using LBOLMP.Net;
using LBOLMP.Session.Messages;
using LBoL.Base;
using LBoL.Core;
using LBoL.Core.Battle;
using LBoL.Core.Battle.BattleActions;
using LBoL.Core.StatusEffects;
using LBoL.Core.Units;
using LBoL.Presentation;
using UnityEngine;

namespace LBOLMP.Session.Battle
{
    /// <summary>Everything one client knows about another player during a fight.</summary>
    public sealed class MpBattleSeat
    {
        public int PlayerId;
        public string Name = string.Empty;
        public string CharacterId = string.Empty;

        public int Hp;
        public int MaxHp;
        public int Block;
        public int Shield;
        public int HandCount;
        public int DrawCount;
        public int DiscardCount;

        /// <summary>
        /// Last round number that this player finished their whole player phase for, or -1 for none yet.
        /// </summary>
        public int CompletedRound = -1;

        public bool Finished;
        public bool Alive = true;

        /// <summary>
        /// Used for diagnostics only, ensures that extremely poor connections can't accidentally send messages for the previous battle
        /// </summary>
        public ulong ReportedSeed;

        /// <summary>
        /// True if the player was defeated mid-combat and is currently spectating the rest of the fight.
        /// </summary>
        public bool Down;

        /// <summary>
        /// True if the player is not participating in the combat at all, and is merely watching (they declined an event combat)
        /// </summary>
        public bool Spectating;

        /// <summary>
        /// True if this seat should be ignored while waiting for the end of turn/end of combat.
        /// </summary>
        public bool IsOutOfPlay => !Alive || Finished || Down || Spectating;

        public bool HasCompleted(int round) => IsOutOfPlay || CompletedRound >= round;

        /// <summary>
        /// Status effects as "Id:level:duration". -1 if the effect has no level/duration.
        /// Why did I even do them like this? I don't get it
        /// </summary>
        public List<string> StatusEffects = new List<string>();

        /// <summary>Most recent card they played, kept briefly so the board can show it.</summary>
        public string LastCardId;
        public bool LastCardUpgraded;
        public float LastCardTime;
        public int LastCardTargetEnemyIndex;
    }

    /// <summary>
    /// Keeps the fight consistent across clients.
    ///
    /// Each client simulates its own player's side of the battle in full, so some bookkeeping has to be done to keep things in sync.
    /// </summary>
    public static class MpBattleSync
    {
        private static readonly Dictionary<int, MpBattleSeat> Seats = new Dictionary<int, MpBattleSeat>();

        /// <summary>
        /// Actions this client injected to replay somebody else's play.
        /// </summary>
        private static readonly HashSet<BattleAction> Injected =
            new HashSet<BattleAction>(ReferenceEqualityComparer.Instance);

        private sealed class ReferenceEqualityComparer : IEqualityComparer<BattleAction>
        {
            public static readonly ReferenceEqualityComparer Instance = new ReferenceEqualityComparer();

            public bool Equals(BattleAction x, BattleAction y) => ReferenceEquals(x, y);

            public int GetHashCode(BattleAction obj) =>
                System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
        }

        /// <summary>
        /// Number of replicated actions handed to the battle that have not finished resolving.
        /// </summary>
        private static int _pendingInjections;

        /// <summary>
        /// True while it is unsafe to commit player input straight to the battle, like if an enemy is dying to someone else's attack.
        /// </summary>
        public static bool ShouldDeferPlayerInput
        {
            get
            {
                if (!InBattle)
                {
                    return false;
                }
                if (_pendingInjections > 0)
                {
                    return true;
                }
                var battle = GameMaster.Instance?.CurrentGameRun?.Battle;
                return battle != null && battle._debugActionQueue.Count > 0;
            }
        }

        /// <summary>
        /// Subscribes to the battle's WaitingPlayerInput event.
        /// </summary>
        private static void SetWaitingHook(BattleController battle, bool subscribe)
        {
            if (battle == null)
            {
                return;
            }

            MpSafe.Run("SetWaitingHook", () =>
            {
                var declared = typeof(BattleController).GetEvent("WaitingPlayerInput");
                if (declared == null)
                {
                    return;
                }

                var handler = (Action)OnBattleWaitingForInput;
                declared.RemoveEventHandler(battle, handler);
                if (subscribe)
                {
                    declared.AddEventHandler(battle, handler);
                }
            });
        }

        /// <summary>
        /// Re-attaches the player's ability to do things again.
        /// </summary>
        private static void OnBattleWaitingForInput()
        {
            MpSafe.Run("OnBattleWaitingForInput", () =>
            {
                var battle = GameMaster.Instance?.CurrentGameRun?.Battle;
                if (battle != null && battle._debugActionQueue.Count == 0)
                {
                    _pendingInjections = 0;
                }
            });
        }

        /// <summary>Marks an action as a replay from someone else, so the patches know not to publish it again.</summary>
        private static T Inject<T>(T action) where T : BattleAction
        {
            Injected.Add(action);
            _pendingInjections++;
            return action;
        }

        /// <summary>
        /// Hands the battle an action that came from another player. Also marks the action as "this is a replay" so that our own client doesn't re-send it again.
        /// </summary>
        internal static void QueueReplicated(BattleController battle, BattleAction action, string reason)
        {
            battle?.RequestDebugAction(Inject(action), reason);
        }

        /// <summary>
        /// Same, for a whole effect's worth of actions. The remote flag is held for the queueing
        /// only: these actions belong to us once they resolve, so anything they do to a shared
        /// enemy should still be published like our own play.
        /// </summary>
        internal static void QueueReplicated(BattleController battle, IEnumerable<BattleAction> actions, string reason)
        {
            if (battle == null || actions == null)
            {
                return;
            }

            ApplyingRemoteEffect = true;
            try
            {
                foreach (var action in actions)
                {
                    if (action != null)
                    {
                        battle.RequestDebugAction(Inject(action), reason);
                    }
                }
            }
            finally
            {
                ApplyingRemoteEffect = false;
            }
        }

        /// <summary>
        /// Returns true if this action was one of ours, and forgets it. Used in patches to avoid double-plays.
        /// </summary>
        public static bool ConsumeInjected(BattleAction action) => Injected.Remove(action);

        /// <summary>
        /// Same as <see cref="ConsumeInjected"/>, but only peeks instead of consuming.
        /// </summary>
        public static bool IsInjected(BattleAction action) => Injected.Contains(action);

        /// <summary>Set while we are queueing a replicated effect.</summary>
        public static bool ApplyingRemoteEffect { get; private set; }

        /// <summary>
        /// True if the local player is out of the fight and only watching it, for any reason.
        /// </summary>
        public static bool SpectatingOnly => MpDownedPlayers.LocalDown || MpEventBattle.LocalSpectating;

        /// <summary>
        /// True while this client is resolving the enemies' moves.
        /// </summary>
        public static bool EnemyTurnRunning { get; internal set; }

        public static bool InBattle { get; private set; }

        public static ulong BattleSeed { get; private set; }

        /// <summary>
        /// The fight this client last announced the end of, or zero if it has not finished one.
        /// </summary>
        private static ulong _finishedSeed;

        public static int PlayerCountAtBattleStart { get; private set; } = 1;

        /// <summary>The round the local battle is on, or -1 when there is no battle.</summary>
        public static int CurrentRound =>
            GameMaster.Instance?.CurrentGameRun?.Battle?.RoundCounter ?? -1;

        /// <summary>True once we have reached the "waiting for other players to end their round" gate (right before the enemy round).</summary>
        public static bool LocalTurnComplete
        {
            get
            {
                var seat = GetSeat(MpNet.LocalPlayerId);
                return seat != null && InBattle && seat.CompletedRound >= CurrentRound;
            }
        }

        private static float _nextStatusBroadcast;

        /// <summary>Every seat including our own, for diagnostics.</summary>
        public static IEnumerable<MpBattleSeat> AllSeats => Seats.Values.OrderBy(s => s.PlayerId);

        public static IEnumerable<MpBattleSeat> RemoteSeats =>
            Seats.Values.Where(s => s.PlayerId != MpNet.LocalPlayerId).OrderBy(s => s.PlayerId);

        public static MpBattleSeat GetSeat(int playerId) =>
            Seats.TryGetValue(playerId, out var seat) ? seat : null;

        public static void RegisterHandlers()
        {
            MpNet.On<BattleStartMessage>(OnBattleStart);
            MpNet.On<TurnCompleteMessage>(OnTurnComplete);
            MpNet.On<EnemyDamageMessage>(OnEnemyDamage);
            MpNet.On<EnemyStatusMessage>(OnEnemyStatus);
            MpNet.On<CuriosityFirepowerMessage>(OnRemoteCuriosity);
            MpNet.On<RemoteCardPlayMessage>(OnRemoteCardPlay);
            MpNet.On<RemoteAnimationMessage>(OnRemoteAnimation);
            MpNet.On<RemoteEffectMessage>(OnRemoteEffect);
            MpNet.On<RemoteHitMessage>(OnRemoteHit);
            MpNet.On<RemoteEmoteMessage>(OnRemoteEmote);
            MpNet.On<BattleStatusMessage>(OnBattleStatus);
            MpNet.On<BattleProgressMessage>(OnBattleProgress);
            MpNet.On<BattleFinishedMessage>(OnBattleFinished);
            MpNet.On<EnemyVitalsMessage>(OnEnemyVitals);
            MpDownedPlayers.RegisterHandlers();
            MpEventBattle.RegisterHandlers();
            MpJunko.RegisterHandlers();
            MpEffects.RegisterHandlers();
        }

        public static void Reset()
        {
            Seats.Clear();
            Injected.Clear();
            MpEffects.Reset();
            MpPartyTargeting.Clear();
            _seenAboveZero.Clear();
            _playerAppliedToEnemies.Clear();
            _reportedSilent.Clear();
            _lastStatus = null;
            _lastProgress = null;
            InBattle = false;
            EnemyTurnRunning = false;
            _atEndOfBattleGate = false;
            _reportedFinished = false;
            BattleSeed = 0;
            _finishedSeed = 0;
            _vitalsSequence = 0;
            _seenVitals.Clear();
            PlayerCountAtBattleStart = 1;
            MpDownedPlayers.Reset();
            MpEventBattle.Reset();
            MpJunko.Reset();
        }

        public static void OnPlayerLeft(int playerId)
        {
            if (Seats.TryGetValue(playerId, out var seat))
            {
                seat.Alive = false;
                seat.Finished = true;
                // If the player has disconnected, prevent them from locking up the rest of the battle (ensures they cannot be waited on).
                seat.CompletedRound = int.MaxValue;
            }
        }

        // (I don't like #regions, I don't use them, pretend this is a region or something idk)
        //--
        // Battle Lifecycle
        //--

        /// <summary>
        /// Deterministically determine a seed for the current battle. Should be the same for every client.
        /// </summary>
        public static ulong StationSeed(GameRunController gameRun, string enemyGroupId)
        {
            var node = gameRun?.CurrentMap?.VisitingNode;
            ulong seed = MpSession.RunSeed;
            seed ^= 0x9E3779B97F4A7C15UL * (ulong)((gameRun?.CurrentStage?.Index ?? 0) + 1);
            seed ^= 0xC2B2AE3D27D4EB4FUL * (ulong)((node?.X ?? 0) + 1);
            seed ^= 0x165667B19E3779F9UL * (ulong)((node?.Y ?? 0) + 1);
            seed ^= (ulong)(enemyGroupId?.GetHashCode() ?? 0);
            return seed == 0 ? 1UL : seed;
        }

        /// <summary>
        /// Opens a fight locally.
        /// </summary>
        public static void BeginBattle(GameRunController gameRun, EnemyGroup enemyGroup)
        {
            if (!MpSession.IsActive)
            {
                return;
            }

            BattleSeed = StationSeed(gameRun, enemyGroup.Id);
            PlayerCountAtBattleStart = Math.Max(1, MpSession.ConnectedCount);
            InBattle = true;
            _atEndOfBattleGate = false;
            _reportedFinished = false;
            _pendingInjections = 0;

            _finishedSeed = 0;
            _seenVitals.Clear();

            _lastStatus = null;
            _lastProgress = null;

            SetWaitingHook(gameRun.Battle, true);

            // Repair broken spellcards caused by people stimming with the "Confirm" button in the main menu
            Patches.StartGameInterceptPatch.RepairUsOwner(gameRun.Player);

            MapSync.ClearVotes();

            // Reset the "how many rainbow/philosopher's mana has everyone acquired" counter for the Junko fight
            MpJunko.Reset();

            Seats.Clear();

            bool eventFight = MpEventBattle.Active;

            foreach (var player in MpSession.ConnectedPlayers)
            {
                Seats[player.Id] = new MpBattleSeat
                {
                    PlayerId = player.Id,
                    Name = player.Name,
                    CharacterId = player.CharacterId,
                    Hp = player.Hp,
                    MaxHp = player.MaxHp,
                    Spectating = eventFight && !MpEventBattle.IsFighting(player.Id)
                };
            }

            MpPlugin.Log.LogInfo($"Battle '{enemyGroup.Id}' starting, seed {BattleSeed}, {PlayerCountAtBattleStart} players");
        }

        private static void OnBattleStart(BattleStartMessage message)
        {
            // TODO: is this even necessary? This was once thought to be necessary for syncing enemy intents, but not required with RNGFix.
        }

        public static void LeaveBattle()
        {
            SetWaitingHook(GameMaster.Instance?.CurrentGameRun?.Battle, false);

            InBattle = false;
            EnemyTurnRunning = false;
            _atEndOfBattleGate = false;
            _reportedFinished = false;
            Seats.Clear();
            Injected.Clear();
            _seenAboveZero.Clear();
            _playerAppliedToEnemies.Clear();
            MpEventBattle.EndFight();
            _pendingInjections = 0;
        }

        //--
        // End of turn barriers
        //--

        /// <summary>
        /// Record and publish that our player phase for this round is over.
        /// </summary>
        public static void SubmitLocalTurnComplete(int round)
        {
            var local = GetSeat(MpNet.LocalPlayerId);
            if (local == null || local.CompletedRound >= round)
            {
                return;
            }

            local.CompletedRound = round;
            MpNet.Send(new TurnCompleteMessage { BattleSeed = BattleSeed, Round = round });
            MpPlugin.Log.LogInfo($"Player phase complete for round {round}; waiting at the enemy-turn gate");
        }

        private static void OnTurnComplete(TurnCompleteMessage message)
        {
            // Ignore our own turn complete messages just in case they arrive really, really, really late (yes this has happened)
            if (message.SenderId == MpNet.LocalPlayerId)
            {
                return;
            }

            if (!IsAboutThisFight(message.BattleSeed))
            {
                return;
            }

            var seat = GetSeat(message.SenderId);
            if (seat != null && message.Round > seat.CompletedRound)
            {
                seat.CompletedRound = message.Round;
            }
        }

        /// <summary>
        /// Whether a message about a fight is about the fight we are actually in.
        /// This could be false for extraordinarily bad Steam connections.
        /// </summary>
        private static bool IsAboutThisFight(ulong seed) => seed != 0 && seed == BattleSeed;

        /// <summary>
        /// On Turn 1, I tried breaking Doremy's barrier, but I drew no attack cards.
        /// On Turn 2, I tried breaking Doremy's barrier, but someone else played Slipping Consciousness and was limited to 2 cards.
        /// On Turn 3, I tried breaking Doremy's barrier, and she woke up.
        /// I'm actually kidding, because I never reached Turn 3. The client had already unplugged their router in a fit of rage.
        /// And thus, the battle was locked up, forever waiting for the client to finish their turn.
        /// (This checks if a client is unresponsive or lost connection)
        /// </summary>
        public static bool IsUnresponsive(MpBattleSeat seat) =>
            seat != null && MpSession.IsUnresponsive(seat.PlayerId);

        /// <summary>
        /// If someone is disconnected, put it up on screen for you to laugh at
        /// </summary>
        public static IEnumerable<string> SilentSeats =>
            Seats.Values.Where(IsUnresponsive).OrderBy(s => s.PlayerId).Select(s => s.Name);

        /// <summary>Seats we have already said have gone quiet</summary>
        private static readonly HashSet<int> _reportedSilent = new HashSet<int>();

        /// <summary>
        /// Write the moment a seat drops out of the combat, and the moment it comes back.
        /// </summary>
        private static void AnnounceSilentSeats()
        {
            foreach (var seat in Seats.Values)
            {
                bool silent = IsUnresponsive(seat);
                if (silent && _reportedSilent.Add(seat.PlayerId))
                {
                    MpPlugin.Log.LogWarning(
                        $"{seat.Name} has not sent anything for {MpSession.UnresponsiveSeconds:F0}s; " +
                        "the party will stop waiting for them. " + DescribeTurnState());
                }
                else if (!silent && _reportedSilent.Remove(seat.PlayerId))
                {
                    MpPlugin.Log.LogInfo($"{seat.Name} is talking to us again");
                }
            }
        }

        /// <summary>
        /// True once every seat still in the fight has finished its player phase for this round.
        /// </summary>
        public static bool AllSeatsCompleted(int round)
        {
            if (!InBattle)
            {
                return true;
            }

            foreach (var seat in Seats.Values)
            {
                if (!seat.HasCompleted(round) && !IsUnresponsive(seat))
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// Who the party is still waiting on, for the banner and the diagnostics overlay.
        /// </summary>
        public static IEnumerable<string> SeatsStillPlaying
        {
            get
            {
                int round = CurrentRound;
                return Seats.Values
                    .Where(s => !s.HasCompleted(round) && !IsUnresponsive(s))
                    .Select(s => s.Name);
            }
        }

        /// <summary>
        /// One-line dump of everything that decides whether input is accepted right now.
        /// This was made in a fit of mild annoyance after Steam Networking failed me for the 20th time.
        /// </summary>
        public static string DescribeTurnState()
        {
            var battle = GameMaster.Instance?.CurrentGameRun?.Battle;
            int round = CurrentRound;
            var parts = new List<string>
            {
                "inBattle=" + InBattle,
                "localComplete=" + LocalTurnComplete,
                "waitingInput=" + (battle?.IsWaitingPlayerInput.ToString() ?? "n/a"),
                "round=" + round,
                "allComplete=" + AllSeatsCompleted(round),
                "fight=" + BattleSeed
            };

            foreach (var seat in Seats.Values.OrderBy(s => s.PlayerId))
            {
                string fight = seat.PlayerId == MpNet.LocalPlayerId || seat.ReportedSeed == BattleSeed
                    ? string.Empty
                    : $" fight={seat.ReportedSeed}";

                parts.Add($"#{seat.PlayerId}({seat.Name}) completed={seat.CompletedRound} " +
                          $"alive={seat.Alive} done={seat.Finished} down={seat.Down} " +
                          $"silent={MpNet.SilenceFor(seat.PlayerId):F0}s{fight}");
            }

            string links = MpSafe.Run("DescribeLinks", MpNet.DescribeLinks, string.Empty);
            if (!string.IsNullOrEmpty(links))
            {
                parts.Add(links);
            }

            return string.Join(", ", parts);
        }

        /// <summary>
        /// The waiting gate every player waits at between their own turn and the enemies'.
        /// Tip: if you want to stim while the Sakuya player takes their 5th extra turn, try right-clicking them
        /// </summary>
        public static IEnumerator<object> WaitForEnemyTurn(BattleController battle)
        {
            if (!MpSession.IsActive || !InBattle || battle == null)
            {
                yield break;
            }

            int round = battle.RoundCounter;
            MpSafe.Run("SubmitTurnComplete", () => SubmitLocalTurnComplete(round));

            float waited = 0f;
            float reportInterval = GateFirstReportSeconds;
            float nextReport = reportInterval;

            while (!MpSafe.Run("TurnGate",
                       () => battle.BattleShouldEnd
                             || (AllSeatsCompleted(round) && battle._debugActionQueue.Count == 0),
                       true))
            {
                if (waited > nextReport)
                {
                    // This can happen legitimately, so we just log it now.
                    reportInterval = Math.Min(reportInterval * 2f, GateMaxReportSeconds);
                    nextReport = waited + reportInterval;
                    MpPlugin.Log.LogInfo("Still at the enemy-turn gate. " + DescribeTurnState());
                }

                // Ensure other player's card plays still apply to the enemy.
                bool drain = MpSafe.Run("TurnGateDrain", () => battle._debugActionQueue.Count > 0, false);
                if (drain)
                {
                    yield return battle.ResolveDebugActions();
                }

                waited += Time.unscaledDeltaTime;
                yield return null;
            }
        }

        /// <summary>First report of who we are still waiting on; the interval doubles from here.</summary>
        private const float GateFirstReportSeconds = 5f;

        /// <summary>Ceiling on the backoff, so a very long turn still leaves a usable trail.</summary>
        private const float GateMaxReportSeconds = 30f;

        //--
        // End of battle waiting gate
        //--

        /// <summary>
        /// True while this client is finished and waiting for the rest.
        /// Generally should only happen for a little bit of time, unless it's Eiki Shiki.
        /// </summary>
        public static bool AtEndOfBattleGate => _atEndOfBattleGate && InBattle;

        private static bool _atEndOfBattleGate;

        /// <summary>True once every seat's battle has ended, whether they won or lost.</summary>
        public static bool AllSeatsFinished => Seats.Values.All(s => s.Finished || IsUnresponsive(s));

        /// <summary>Who is still fighting.</summary>
        public static IEnumerable<string> SeatsStillFighting =>
            Seats.Values.Where(s => !s.Finished && !IsUnresponsive(s)).Select(s => s.Name);

        /// <summary>
        /// True when nothing is holding the end-of-battle waiting gate.
        /// </summary>
        public static bool EndOfBattleGateOpen =>
            AllSeatsFinished || !InBattle || !MpSession.IsActive;

        /// <summary>
        /// Wait for everyone to finish the fight. This avoids the host restarting the level and putting some people on the rewards screen and others back in the battle.
        /// This way, if the host restarts mid-combat, everyone restarts at the combat (or event).
        /// </summary>
        public static IEnumerator<object> WaitForEveryoneToFinish(BattleController battle)
        {
            if (!MpSession.IsActive || !InBattle || battle == null)
            {
                yield break;
            }

            if (!MpSafe.Run("EndGate", () => EnterEndOfBattleGate(battle), false))
            {
                yield break;
            }

            _atEndOfBattleGate = true;
            try
            {
                float waited = 0f;
                float reportInterval = GateFirstReportSeconds;
                float nextReport = reportInterval;

                MpPlugin.Log.LogInfo("Fight over here; waiting for the rest of the party to finish theirs");

                while (!MpSafe.Run("EndGate", () => EndOfBattleGateOpen, true))
                {
                    if (waited > nextReport)
                    {
                        reportInterval = Math.Min(reportInterval * 2f, GateMaxReportSeconds);
                        nextReport = waited + reportInterval;
                        MpPlugin.Log.LogInfo("Still waiting for the party to finish the fight. "
                                             + DescribeTurnState());
                    }

                    waited += Time.unscaledDeltaTime;
                    yield return null;
                }

                MpPlugin.Log.LogInfo("Everyone has finished the fight; on to the rewards");
            }
            finally
            {
                _atEndOfBattleGate = false;
            }
        }

        /// <summary>
        /// Arrive at the end-of-battle gate, announce that our fight is over.
        /// </summary>
        private static bool EnterEndOfBattleGate(BattleController battle)
        {
            ReportBattleFinished(battle.Player != null && battle.Player.IsAlive);
            return !EndOfBattleGateOpen;
        }

        //--
        // Enemy replication
        //--

        /// <summary>
        /// Publish a hit the local player is about to land on a shared enemy.
        /// </summary>
        public static void ReportEnemyDamage(EnemyUnit enemy, DamageInfo info, string gunName)
        {
            if (!InBattle || ApplyingRemoteEffect || !MpSession.IsActive || SpectatingOnly)
            {
                return;
            }

            if (info.Amount <= 0f)
            {
                return;
            }

            MpNet.Send(new EnemyDamageMessage
            {
                EnemyIndex = enemy.Index,
                Amount = info.Amount,
                DamageType = (int)info.DamageType,
                IsAccuracy = info.IsAccuracy,
                GunName = string.IsNullOrEmpty(gunName) ? "Instant" : gunName
            });
        }

        private static void OnEnemyDamage(EnemyDamageMessage message)
        {
            if (message.SenderId == MpNet.LocalPlayerId)
            {
                // Our own hit, we already resolved it locally.
                return;
            }

            var battle = GameMaster.Instance?.CurrentGameRun?.Battle;
            var enemy = FindEnemy(battle, message.EnemyIndex);
            if (enemy == null || MpPrivateEnemies.IsPrivate(enemy))
            {
                // Fucking Eiki Shiki again
                return;
            }

            // Get the source of the attack, to place the gun in the right position and avoid Static Charge retaliation against the wrong guy
            Unit source = UI.MpAllyUnits.GetUnit(message.SenderId);
            if (source == null)
            {
                MpPlugin.Log.LogWarning($"No ally unit for player {message.SenderId}; skipping their hit");
                return;
            }

            var damageType = (DamageType)message.DamageType;
            var info = damageType == DamageType.Attack
                ? DamageInfo.Attack(message.Amount, message.IsAccuracy)
                : DamageInfo.HpLose(message.Amount, true);

            // Deliberately left without a cause, it's already handled elsewhere.
            var damage = Inject(new DamageAction(source, enemy, info, message.GunName));

            // Queued rather than applied directly to avoid incredibly insane desyncs or out-of-order attacks
            battle.RequestDebugAction(damage, "MP remote damage");

            // Fluffy status effect listens on StatisticalTotalDamageReceived, which the game only
            // raises via this companion action. Without it an ally's hits would land but never
            // trigger "when I take attack damage" effects.
            battle.RequestDebugAction(
                Inject(new StatisticalTotalDamageAction(new[] { damage })),
                "MP remote damage stats");

            UI.MpAllyUnits.PlayShoot(message.SenderId, message.GunName, message.EnemyIndex);
        }

        /// <summary>Publish a status effect the local player just put on an enemy.</summary>
        public static void ReportEnemyStatus(EnemyUnit enemy, StatusEffect effect, bool removing)
        {
            NotePlayerAppliedToEnemy(effect?.Id);

            if (!InBattle || ApplyingRemoteEffect || !MpSession.IsActive || SpectatingOnly)
            {
                return;
            }

            MpNet.Send(new EnemyStatusMessage
            {
                EnemyIndex = enemy.Index,
                StatusId = effect.Id,
                HasLevel = effect.HasLevel,
                Level = effect.HasLevel ? effect.Level : 0,
                HasDuration = effect.HasDuration,
                Duration = effect.HasDuration ? effect.Duration : 0,
                Removing = removing
            });
        }

        private static void OnEnemyStatus(EnemyStatusMessage message)
        {
            if (message.SenderId == MpNet.LocalPlayerId)
            {
                return;
            }

            var battle = GameMaster.Instance?.CurrentGameRun?.Battle;
            var enemy = FindEnemy(battle, message.EnemyIndex);
            if (enemy == null || MpPrivateEnemies.IsPrivate(enemy))
            {
                return;
            }

            NotePlayerAppliedToEnemy(message.StatusId);

            ApplyingRemoteEffect = true;
            try
            {
                if (message.Removing)
                {
                    var existing = enemy.StatusEffects.FirstOrDefault(s => s.Id == message.StatusId);
                    if (existing != null)
                    {
                        battle.RequestDebugAction(
                            Inject(new RemoveStatusEffectAction(existing)),
                            "MP remote status remove");
                    }
                    return;
                }

                var template = Library.TryCreateStatusEffect(message.StatusId);
                if (template == null)
                {
                    MpPlugin.Log.LogWarning("Unknown status effect over the wire: " + message.StatusId);
                    return;
                }

                battle.RequestDebugAction(
                    Inject(new ApplyStatusEffectAction(template.GetType(), enemy,
                        message.HasLevel ? message.Level : (int?)null,
                        message.HasDuration ? message.Duration : (int?)null)),
                    "MP remote status");
            }
            catch (Exception e)
            {
                MpPlugin.Log.LogError("Failed to apply replicated enemy status: " + e);
            }
            finally
            {
                ApplyingRemoteEffect = false;
            }
        }

        /// <summary>
        /// Scale Sanae's firepower
        /// </summary>
        public static void ReportCuriosity(EnemyUnit enemy, int firepower)
        {
            if (!InBattle || ApplyingRemoteEffect || !MpSession.IsActive || SpectatingOnly
                || firepower <= 0)
            {
                return;
            }

            MpNet.Send(new CuriosityFirepowerMessage
            {
                EnemyIndex = enemy.Index,
                Firepower = firepower
            });
        }

        private static void OnRemoteCuriosity(CuriosityFirepowerMessage message)
        {
            if (message.SenderId == MpNet.LocalPlayerId)
            {
                return;
            }

            var battle = GameMaster.Instance?.CurrentGameRun?.Battle;
            var enemy = FindEnemy(battle, message.EnemyIndex);
            if (enemy == null || MpPrivateEnemies.IsPrivate(enemy) || message.Firepower <= 0)
            {
                return;
            }

            // Apply plain firepower as a debug action, don't re-publish it or else Sanae would get firepower infinitely forever
            // REALLY strong modern-age miracle girl locks up the game
            battle.RequestDebugAction(
                Inject(new ApplyStatusEffectAction(typeof(Firepower), enemy, message.Firepower)),
                "MP ally ability card");

            // Flash the Curious status effect to show the player what happened
            MpSafe.Run("CuriosityPulse",
                () => enemy.GetStatusEffect<LBoL.EntityLib.StatusEffects.Enemy.Curiosity>()
                    ?.NotifyActivating());
        }

        private static EnemyUnit FindEnemy(BattleController battle, int index)
        {
            if (battle == null)
            {
                return null;
            }
            return battle.EnemyGroup.FirstOrDefault(e => e.Index == index);
        }

        //--
        // Cosmetic card plays and status stuff
        //--

        public static void ReportCardPlayed(string cardId, bool upgraded, int targetEnemyIndex)
        {
            if (!InBattle || !MpSession.IsActive)
            {
                return;
            }

            MpNet.Send(new RemoteCardPlayMessage
            {
                CardId = cardId,
                Upgraded = upgraded,
                TargetEnemyIndex = targetEnemyIndex
            });
        }

        private static void OnRemoteCardPlay(RemoteCardPlayMessage message)
        {
            if (message.SenderId == MpNet.LocalPlayerId)
            {
                return;
            }

            var seat = GetSeat(message.SenderId);
            if (seat == null)
            {
                return;
            }

            seat.LastCardId = message.CardId;
            seat.LastCardUpgraded = message.Upgraded;
            seat.LastCardTargetEnemyIndex = message.TargetEnemyIndex;
            seat.LastCardTime = Time.unscaledTime;

            UI.AllyCardPopup.Show(seat.PlayerId, message.CardId, message.Upgraded);
            UI.MpAllyUnits.AimAt(seat.PlayerId, message.TargetEnemyIndex);
        }

        /// <summary>
        /// Send "I just did an animation" to other clients
        /// </summary>
        public static void ReportAnimation(string animationName)
        {
            if (!InBattle || !MpSession.IsActive || string.IsNullOrEmpty(animationName))
            {
                return;
            }

            MpNet.Send(new RemoteAnimationMessage { AnimationName = animationName });
        }

        private static void OnRemoteAnimation(RemoteAnimationMessage message)
        {
            if (message.SenderId == MpNet.LocalPlayerId)
            {
                return;
            }

            UI.MpAllyUnits.PlayAnimation(message.SenderId, message.AnimationName);
        }

        /// <summary>
        /// Publish a one-shot effect the game just played on our own character.
        /// </summary>
        public static void ReportPerformEffect(string effectName, float delay)
        {
            if (!InBattle || !MpSession.IsActive || string.IsNullOrEmpty(effectName))
            {
                return;
            }

            MpNet.Send(new RemoteEffectMessage { EffectName = effectName, Delay = delay });
        }

        private static void OnRemoteEffect(RemoteEffectMessage message)
        {
            if (message.SenderId == MpNet.LocalPlayerId)
            {
                return;
            }

            UI.MpAllyUnits.PlayEffect(message.SenderId, message.EffectName, message.Delay);
        }

        /// <summary>
        /// Send how a hit landed on us, so the party sees us react to it.
        /// </summary>
        public static void ReportHit(DamageInfo info)
        {
            if (!InBattle || !MpSession.IsActive)
            {
                return;
            }

            MpNet.Send(new RemoteHitMessage
            {
                Damage = info.Damage,
                DamageBlocked = info.DamageBlocked,
                DamageShielded = info.DamageShielded,
                IsGrazed = info.IsGrazed,
                IsAccuracy = info.IsAccuracy,
                DamageType = (int)info.DamageType
            });
        }

        private static void OnRemoteHit(RemoteHitMessage message)
        {
            if (message.SenderId == MpNet.LocalPlayerId)
            {
                return;
            }

            var info = new DamageInfo
            {
                Damage = message.Damage,
                DamageBlocked = message.DamageBlocked,
                DamageShielded = message.DamageShielded,
                IsGrazed = message.IsGrazed,
                IsAccuracy = message.IsAccuracy,
                DamageType = (DamageType)message.DamageType
            };

            UI.MpAllyUnits.PlayHit(message.SenderId, info);
        }

        private static void OnRemoteEmote(RemoteEmoteMessage message)
        {
            if (message.SenderId == MpNet.LocalPlayerId)
            {
                return;
            }

            UI.MpEmotes.Play(message.SenderId, message.Emote);
        }

        private static float _quietSince;

        /// <summary>
        /// Safety net for the input deferral. Avoids player actions being queued for too long and lets chaotic 4-player games actually work properly
        /// </summary>
        private static void TickInputDeferralWatchdog()
        {
            if (!InBattle || _pendingInjections <= 0)
            {
                _quietSince = 0f;
                return;
            }

            var battle = GameMaster.Instance?.CurrentGameRun?.Battle;
            bool quiet = battle != null
                         && battle.IsWaitingPlayerInput
                         && battle._debugActionQueue.Count == 0;

            if (!quiet)
            {
                _quietSince = 0f;
                return;
            }

            if (_quietSince == 0f)
            {
                _quietSince = Time.unscaledTime;
                return;
            }

            if (Time.unscaledTime - _quietSince > 1.5f)
            {
                MpPlugin.Log.LogWarning(
                    $"Clearing {_pendingInjections} stale replicated action(s); the battle has been idle. " +
                    DescribeTurnState());
                _pendingInjections = 0;
                _quietSince = 0f;
            }
        }

        private static float _nextSpentStatusSweep;

        /// <summary>
        /// Level-only enemy effects we have seen above zero (like Graze).
        /// </summary>
        private static readonly HashSet<StatusEffect> _seenAboveZero = new HashSet<StatusEffect>();

        /// <summary>
        /// Ids of status effects a player has put on a shared enemy during this fight.
        /// </summary>
        private static readonly HashSet<string> _playerAppliedToEnemies = new HashSet<string>();

        /// <summary>
        /// Remember that this effect id is one the party puts on enemies, not one an enemy runs
        /// itself. Called from both ends of the replication, see <see cref="_playerAppliedToEnemies"/>.
        /// </summary>
        internal static void NotePlayerAppliedToEnemy(string statusId)
        {
            if (!string.IsNullOrEmpty(statusId))
            {
                _playerAppliedToEnemies.Add(statusId);
            }
        }

        /// <summary>
        /// Take off enemy status effects that have counted down to zero but were never removed.
        /// 
        /// I'm not entirely sure why it works like this, but I opted to just sweep all the status effects that should go away, and make them go away.
        /// </summary>
        private static void SweepSpentStatusEffects()
        {
            if (!InBattle || Time.unscaledTime < _nextSpentStatusSweep)
            {
                return;
            }
            _nextSpentStatusSweep = Time.unscaledTime + 0.5f;

            var battle = GameMaster.Instance?.CurrentGameRun?.Battle;

            if (battle == null || !battle.IsWaitingPlayerInput || battle._debugActionQueue.Count > 0)
            {
                return;
            }

            foreach (var enemy in battle.EnemyGroup)
            {
                if (!enemy.IsAlive)
                {
                    continue;
                }

                foreach (var effect in enemy.StatusEffects.ToList())
                {
                    if (!effect.HasLevel || effect.HasDuration)
                    {
                        continue;
                    }

                    if (!_playerAppliedToEnemies.Contains(effect.Id))
                    {
                        continue;
                    }

                    if (effect.Level > 0)
                    {
                        _seenAboveZero.Add(effect);
                        continue;
                    }

                    if (!_seenAboveZero.Contains(effect))
                    {
                        continue;
                    }

                    MpPlugin.Log.LogWarning(
                        $"'{effect.Id}' left spent at level 0 on {enemy.Id}; removing it");
                    _seenAboveZero.Remove(effect);
                    battle.RequestDebugAction(
                        new RemoveStatusEffectAction(effect), "MP spent status cleanup");
                }
            }
        }

        //--
        // Forced bandaid fixes for enemy HP
        //

        private static float _nextEnemyVitals;

        /// <summary>Counts the host's vitals broadcasts, and the newest one seen from each sender.</summary>
        private static int _vitalsSequence;

        private static readonly Dictionary<int, int> _seenVitals = new Dictionary<int, int>();

        /// <summary>How often the host publishes where the enemies stand.</summary>
        private const float EnemyVitalsInterval = 1f;

        /// <summary>
        /// True when the battle is a little bit quieter for now, and we can safely correct wrong enemy HP values.
        /// </summary>
        private static bool BattleIsSettled(BattleController battle)
        {
            if (battle == null || battle._debugActionQueue.Count > 0 || EnemyTurnRunning)
            {
                return false;
            }

            return battle.IsWaitingPlayerInput || SpectatingOnly || battle.BattleShouldEnd;
        }

        /// <summary>
        /// Host publishes the enemy's actual Life/Block/Barrier every once in a while so that small desyncs can be corrected way more easily.
        /// This is honestly barely even noticeable, which is good. It makes enemies dying synced 99.99% of the time, which is the most noticeable.
        /// </summary>
        private static void BroadcastEnemyVitals()
        {
            if (!MpNet.IsHost || !InBattle || Time.unscaledTime < _nextEnemyVitals)
            {
                return;
            }

            var battle = GameMaster.Instance?.CurrentGameRun?.Battle;
            if (!BattleIsSettled(battle))
            {
                return;
            }

            _nextEnemyVitals = Time.unscaledTime + EnemyVitalsInterval;

            var vitals = new List<int>();
            foreach (var enemy in battle.EnemyGroup)
            {
                if (MpPrivateEnemies.IsPrivate(enemy))
                {
                    continue;
                }

                vitals.Add(enemy.Index);
                vitals.Add(enemy.IsAlive ? enemy.Hp : 0);
                vitals.Add(enemy.Block);
                vitals.Add(enemy.Shield);
                vitals.Add(DamageCapOf(enemy));
            }

            if (vitals.Count > 0)
            {
                MpNet.Send(new EnemyVitalsMessage { Sequence = ++_vitalsSequence, Vitals = vitals });
            }
        }

        /// <summary>
        /// As a client, correct our enemies towards the host's.
        /// </summary>
        private static void OnEnemyVitals(EnemyVitalsMessage message)
        {
            if (message.SenderId == MpNet.LocalPlayerId || !InBattle)
            {
                return;
            }

            // Since we said that enemy vitals are now "unreliable" messages, that means they can arrive out of order.
            // Check that we're not re-applying an older enemy HP message and re-adjusting their HP back upwards.
            if (_seenVitals.TryGetValue(message.SenderId, out int seen) && message.Sequence <= seen)
            {
                return;
            }
            _seenVitals[message.SenderId] = message.Sequence;

            var battle = GameMaster.Instance?.CurrentGameRun?.Battle;

            if (!BattleIsSettled(battle))
            {
                return;
            }

            for (int i = 0; i + 4 < message.Vitals.Count; i += 5)
            {
                var enemy = FindEnemy(battle, message.Vitals[i]);
                if (enemy == null || !enemy.IsAlive)
                {
                    // I can't raise the dead, and believe me, I tried. Not worth it, do not attempt.
                    // Therefore, we are ignoring already-dead enemies here.
                    continue;
                }

                if (MpPrivateEnemies.IsPrivate(enemy))
                {
                    continue;
                }

                CorrectEnemy(battle, enemy, message.Vitals[i + 1], message.Vitals[i + 2], message.Vitals[i + 3]);
                CorrectDamageCap(enemy, message.Vitals[i + 4]);
            }
        }

        /// <summary>
        /// Helper to sync Seija's damage cap correctly
        /// </summary>
        private static int DamageCapOf(EnemyUnit enemy)
        {
            var cap = enemy.StatusEffects.FirstOrDefault(e => e is LimitedDamage);
            return cap != null && cap.HasCount ? cap.Count : -1;
        }

        /// <summary>Put an enemy's damage cap back to the host's. See <see cref="DamageCapOf"/>.</summary>
        private static void CorrectDamageCap(EnemyUnit enemy, int cap)
        {
            if (cap < 0)
            {
                return;
            }

            var effect = enemy.StatusEffects.FirstOrDefault(e => e is LimitedDamage) as LimitedDamage;
            if (effect == null || !effect.HasCount || effect.Count == cap)
            {
                return;
            }

            MpPlugin.Log.LogInfo(
                $"Correcting {enemy.Id}'s damage cap to the host: {effect.Count}->{cap}");

            // The property is what the icon shows; the field is what actually gates the damage.
            effect._internalCount = cap;
            effect.Count = cap;
        }

        /// <summary>
        /// True when nothing but summons are still alive.
        /// </summary>
        private static bool OnlyServantsLeft(BattleController battle) =>
            battle.EnemyGroup.All(e => e.IsServant || e.IsEscaped || !e.IsAlive);

        private static void CorrectEnemy(BattleController battle, EnemyUnit enemy, int hp, int block, int shield)
        {
            int wantedHp = Mathf.Clamp(hp, 0, enemy.MaxHp);

            if (wantedHp <= 0)
            {
                // This fixes Rin throwing a nuclear bomb in the clients' faces when she dies on the host side.
                if (enemy.IsServant && OnlyServantsLeft(battle))
                {
                    return;
                }

                // If an enemy is dead on the host, force-kill them on the client too.
                // This fixes a problem where Seija's damage cap would ignore host HP syncing since she would just reduce the damage correction to 0.
                // The player is marked as getting "credit" for the kill, to avoid Rin spirits detonating with double damage and other shenanigans.
                MpPlugin.Log.LogInfo($"{enemy.Id} is already down on the host; finishing it here");
                battle.RequestDebugAction(
                    Inject(new ForceKillAction(battle.Player, enemy)),
                    "MP enemy correction");
                return;
            }

            int hpDelta = wantedHp - enemy.Hp;
            bool guardChanged = enemy.Block != block || enemy.Shield != shield;

            if (hpDelta == 0 && !guardChanged)
            {
                return;
            }

            MpPlugin.Log.LogInfo(
                $"Correcting {enemy.Id} to the host: hp {enemy.Hp}->{wantedHp}, " +
                $"block {enemy.Block}->{block}, shield {enemy.Shield}->{shield}");

            enemy.Hp = wantedHp;
            enemy.Block = Mathf.Max(0, block);
            enemy.Shield = Mathf.Max(0, shield);

            var view = MpSafe.Run("EnemyCorrectionView",
                () => LBoL.Presentation.Units.GameDirector.GetUnit(enemy), null);
            if (view == null)
            {
                return;
            }

            if (hpDelta < 0)
            {
                view.OnDamageReceived(DamageInfo.HpLose(-hpDelta, true));
            }
            else if (hpDelta > 0)
            {
                view.OnHealingReceived(hpDelta);
            }

            if (guardChanged)
            {
                view.UpdateShieldColliders();
                view._statusWidget?.OnBlockShieldChanged();
            }
        }

        public static void Update()
        {
            if (!MpSession.IsActive)
            {
                return;
            }

            TickInputDeferralWatchdog();
            AnnounceSilentSeats();
            SweepSpentStatusEffects();
            MpSafe.Run("BroadcastEnemyVitals", BroadcastEnemyVitals);

            if (Time.unscaledTime < _nextStatusBroadcast)
            {
                return;
            }
            _nextStatusBroadcast = Time.unscaledTime + StatusSampleInterval;

            var gameRun = GameMaster.Instance?.CurrentGameRun;
            var player = gameRun?.Player;
            if (player == null)
            {
                return;
            }

            var battle = gameRun.Battle;

            var effects = new List<string>();
            foreach (var effect in player.StatusEffects)
            {
                int level = effect.HasLevel ? effect.Level : -1;
                int duration = effect.HasDuration ? effect.Duration : -1;

                // Some status effects (like Midsummer Flowers scaling) require referencing the card that caused them.
                // If that is the case, add it too.
                effects.Add($"{effect.Id}:{level}:{duration}:{effect.SourceCard?.Id ?? string.Empty}");
            }

            SendStatusIfWorthIt(new BattleStatusMessage
            {
                Hp = player.Hp,
                MaxHp = player.MaxHp,
                Block = player.Block,
                Shield = player.Shield,
                HandCount = battle?.HandZone.Count ?? 0,
                DrawCount = battle?.DrawZone.Count ?? 0,
                DiscardCount = battle?.DiscardZone.Count ?? 0,
                StatusEffects = effects
            });

            PublishLocalProgress(player);
        }

        /// <summary>
        /// Separately publish how far along in the fight we are, in a reliable manner.
        /// Status updates don't matter as much, but end of turn/end of combat absolutely does.
        /// </summary>
        private static void PublishLocalProgress(PlayerUnit player)
        {
            ulong seed = InBattle ? BattleSeed : _finishedSeed;
            if (seed == 0)
            {
                // Not in a fight and not fresh out of one, so there is nothing anyone can be waiting for.
                return;
            }

            var local = InBattle ? GetSeat(MpNet.LocalPlayerId) : null;

            SendProgressIfWorthIt(new BattleProgressMessage
            {
                BattleSeed = seed,
                CompletedRound = local?.CompletedRound ?? -1,
                Finished = !InBattle || (local?.Finished ?? false),
                Alive = player.Hp > 0
            });
        }

        /// <summary>How often the local player's status is measured.</summary>
        private const float StatusSampleInterval = 0.2f;

        /// <summary>
        /// Longest this client will go without saying anything, if it can help it.
        /// This is basically a "heartbeat" to check if the player hasn't disconnected.
        /// It will not separately heartbeat if it has sent other data, that's just wasteful.
        /// </summary>
        private const float StatusKeepAliveSeconds = 1f;

        private static byte[] _lastStatus;
        private static float _nextStatusKeepAlive;

        private static byte[] _lastProgress;
        private static float _nextProgressKeepAlive;

        /// <summary>
        /// Send the player's current information if it's been longer than 1 second ago since they last sent it.
        /// </summary>
        private static void SendStatusIfWorthIt(BattleStatusMessage message)
        {
            var payload = MpNet.BodyOf(message);
            bool due = Time.unscaledTime >= _nextStatusKeepAlive;

            if (!due && MpNet.SameBytes(_lastStatus, payload))
            {
                return;
            }

            _lastStatus = payload;
            _nextStatusKeepAlive = Time.unscaledTime + StatusKeepAliveSeconds;
            MpNet.Send(message);
        }

        private static void SendProgressIfWorthIt(BattleProgressMessage message)
        {
            var payload = MpNet.BodyOf(message);
            bool due = Time.unscaledTime >= _nextProgressKeepAlive;

            if (!due && MpNet.SameBytes(_lastProgress, payload))
            {
                return;
            }

            _lastProgress = payload;
            _nextProgressKeepAlive = Time.unscaledTime + StatusKeepAliveSeconds;
            MpNet.Send(message);
        }

        private static void OnBattleStatus(BattleStatusMessage message)
        {
            if (message.SenderId == MpNet.LocalPlayerId)
            {
                return;
            }

            var seat = GetSeat(message.SenderId);
            if (seat == null)
            {
                return;
            }

            seat.Hp = message.Hp;
            seat.MaxHp = message.MaxHp;
            seat.Block = message.Block;
            seat.Shield = message.Shield;
            seat.HandCount = message.HandCount;
            seat.DrawCount = message.DrawCount;
            seat.DiscardCount = message.DiscardCount;
            seat.StatusEffects = message.StatusEffects;

            UI.MpAllyUnits.SyncVitals(seat);
        }

        private static void OnBattleProgress(BattleProgressMessage message)
        {
            if (message.SenderId == MpNet.LocalPlayerId)
            {
                return;
            }

            var seat = GetSeat(message.SenderId);
            if (seat == null)
            {
                return;
            }

            seat.ReportedSeed = message.BattleSeed;

            if (!IsAboutThisFight(message.BattleSeed))
            {
                return;
            }

            // This should only ever increase
            if (message.CompletedRound > seat.CompletedRound)
            {
                seat.CompletedRound = message.CompletedRound;
            }

            if (message.Finished)
            {
                seat.Finished = true;
            }

            seat.Alive = message.Alive;
        }

        /// <summary>
        /// Tell the party this client's fight is done.
        /// </summary>
        public static void ReportBattleFinished(bool survived)
        {
            if (!MpSession.IsActive || _reportedFinished)
            {
                return;
            }

            _reportedFinished = true;
            _finishedSeed = BattleSeed;

            var local = GetSeat(MpNet.LocalPlayerId);
            if (local != null)
            {
                local.Finished = true;
                local.Alive = survived;
                local.CompletedRound = int.MaxValue;
            }

            MpNet.Send(new BattleFinishedMessage { BattleSeed = BattleSeed, Survived = survived });
        }

        /// <summary>Whether this battle's end has already been announced. Per battle.</summary>
        private static bool _reportedFinished;

        private static void OnBattleFinished(BattleFinishedMessage message)
        {
            if (!IsAboutThisFight(message.BattleSeed))
            {
                return;
            }

            var seat = GetSeat(message.SenderId);
            if (seat == null)
            {
                return;
            }
            seat.Finished = true;
            seat.Alive = message.Survived;
            // Their fight is over, so they must never be waited on again.
            seat.CompletedRound = int.MaxValue;
        }

        //--
        // enemy intent RNG
        //--

        public static ulong SeedForEnemyMove(int enemyIndex, int round)
        {
            ulong seed = BattleSeed;
            seed ^= 0x9E3779B97F4A7C15UL * (ulong)(enemyIndex + 1);
            seed ^= 0xBF58476D1CE4E5B9UL * (ulong)(round + 1);
            return seed == 0 ? 1UL : seed;
        }
    }
}
