using System.Collections.Generic;
using System.Linq;
using LBOLMP.Net;
using LBOLMP.Session.Messages;
using UnityEngine;

namespace LBOLMP.Session.Battle
{
    /// <summary>
    /// The fight an event offers you, which each player accepts or declines for themselves.
    ///
    /// Two adventures in the game can lead to a battle. Everyone reads the same event, but they each
    /// choose, so the party can split — and the fight cannot begin until the split is known, because
    /// three things depend on it: whether a fight happens at all, who is in it, and how much health
    /// the enemies get (they scale to the number of players actually swinging, not the party).
    ///
    /// So every player announces their answer and then waits: the ones who took the fight wait at
    /// the <c>battle</c> command, the ones who declined wait at the end of their dialogue, and
    /// nobody's event finishes early. Once every answer is in, everyone enters the same battle —
    /// participants to fight it, the rest to watch. Spectators reuse the machinery a knocked-out
    /// player already runs on: no turn, no input, nothing published, no enemy turn, and no hold on
    /// anybody else's.
    ///
    /// Everyone brought in matters even for those only watching, because it is what keeps the host
    /// in the battle. The host is the authority on enemy health, and a host who had declined and
    /// stayed out would stop publishing corrections entirely. A watching host is in fact the better
    /// authority: with no hits of its own to apply first, its copy of the enemies is a pure result of
    /// the replicated stream.
    /// </summary>
    public static class MpEventBattle
    {
        private struct Choice
        {
            public bool Fighting;
            public string EnemyGroupId;
        }

        private static readonly Dictionary<int, Choice> Choices = new Dictionary<int, Choice>();

        /// <summary>True once this client has said what it is doing about this event's fight.</summary>
        private static bool _answered;

        /// <summary>
        /// True once this event's fight has been and gone.
        ///
        /// Without it the gate at the end of the dialogue fires a second time. A fighter answers at
        /// the battle command, fights, and <see cref="EndFight"/> clears the answers on the way out
        /// — so by the time their dialogue ends they look like somebody who never answered at all,
        /// announce a decline nobody asked for, and wait for a second round of answers. It happens
        /// to resolve when the other players' dialogues end too, which is precisely the kind of
        /// accident that works until one player lingers and the other is left waiting on them.
        /// </summary>
        private static bool _fightResolved;

        /// <summary>
        /// Set when the fight an event offered was lost. That player's event ends there: no battle
        /// reward, and none of the rest of the event either.
        ///
        /// Losing the fight, not being knocked down in it. Go down while somebody else finishes the
        /// job and you are revived, you carry a Regret for it, and you collect with everybody else.
        /// </summary>
        public static bool LocalEventAborted { get; private set; }

        /// <summary>
        /// Set while the mod, rather than the event's script, is the one asking for the battle.
        ///
        /// Two places do that: starting the fight for somebody who declined it so they can watch,
        /// and re-starting it for a fighter whose own roll disagreed with the party's. Both call
        /// the same patched method the script calls, and neither is a fresh answer to the event —
        /// the gate has already been through, and running it again would have this client announce
        /// a second time and wait for a party that has finished answering.
        /// </summary>
        public static bool ModRequestedBattle { get; private set; }

        /// <summary>True when the local player is in this fight only to watch it.</summary>
        public static bool LocalSpectating { get; private set; }

        public static void RegisterHandlers() => MpNet.On<EventBattleChoiceMessage>(OnChoice);

        /// <summary>Wipe the slate when a new event begins, and when a run ends.</summary>
        public static void Reset()
        {
            Choices.Clear();
            _answered = false;
            _fightResolved = false;
            LocalSpectating = false;
            ModRequestedBattle = false;
            LocalEventAborted = false;
        }

        /// <summary>Everyone has said what they are doing.</summary>
        public static bool AllAnswered =>
            MpSession.ConnectedPlayers.All(p => Choices.ContainsKey(p.Id));

        public static bool AnyFighting => Choices.Values.Any(c => c.Fighting);

        /// <summary>How many players are actually in the fight; never below one.</summary>
        public static int FighterCount => Mathf.Max(1, Choices.Values.Count(c => c.Fighting));

        /// <summary>True while a fight from an event is the battle we are in or about to be in.</summary>
        public static bool Active => _answered && AnyFighting;

        public static bool IsFighting(int playerId) =>
            Choices.TryGetValue(playerId, out var choice) && choice.Fighting;

        /// <summary>
        /// The enemy group the party is fighting: the announced group of the lowest player id that
        /// took the fight, which for a host that took it is the host's.
        ///
        /// It has to be picked by a rule rather than taken from whichever answer happens to come
        /// first, because the answers are not all the same. Miyoi's opponent is
        /// <c>{"37","38","39"}</c> sampled in <c>InitVariables</c> — from <c>StationRng</c> in the
        /// vanilla game, but RngFix transpiles that to <c>AdventureRng</c>, and <c>AdventureRng</c>
        /// has been personal since the first event of the run. Nearly everything drawn from it is
        /// drawn a player-dependent number of times: <c>RollCard</c> re-rolls around the cards you
        /// already own, <c>SampleManyOrAll</c> draws once per exhibit you are carrying, and a Yarn
        /// <c>random()</c> only happens on the branch you chose. That is right for the things it
        /// decides — your card offers are supposed to be yours — and wrong for exactly this one
        /// draw, which names an enemy group the whole party walks into together.
        ///
        /// So the party's answer is one player's answer, chosen the same way on every client.
        /// <c>Choices</c> is a dictionary and enumerates in insertion order, which is arrival order,
        /// which is nobody's idea of a rule — ordering by id is what makes every client name the
        /// same fight. See EventBattleStartPatch for the half that makes the fighters use it.
        /// </summary>
        public static string EnemyGroupId =>
            Choices
                .Where(pair => pair.Value.Fighting && !string.IsNullOrEmpty(pair.Value.EnemyGroupId))
                .OrderBy(pair => pair.Key)
                .Select(pair => pair.Value.EnemyGroupId)
                .FirstOrDefault() ?? string.Empty;

        public static void Announce(bool fighting, string enemyGroupId)
        {
            if (_answered || _fightResolved)
            {
                return;
            }

            _answered = true;
            var choice = new Choice { Fighting = fighting, EnemyGroupId = enemyGroupId ?? string.Empty };
            Choices[MpNet.LocalPlayerId] = choice;

            MpPlugin.Log.LogInfo(fighting
                ? $"Taking the event's fight against '{enemyGroupId}'; waiting for the rest of the party to choose"
                : "Declining the event's fight; waiting for the rest of the party to choose");

            MpNet.Send(new EventBattleChoiceMessage
            {
                Fighting = fighting,
                EnemyGroupId = choice.EnemyGroupId
            });
        }

        private static void OnChoice(EventBattleChoiceMessage message)
        {
            if (message.SenderId == MpNet.LocalPlayerId)
            {
                return;
            }

            Choices[message.SenderId] = new Choice
            {
                Fighting = message.Fighting,
                EnemyGroupId = message.EnemyGroupId
            };

            string who = MpSession.Players.FirstOrDefault(p => p.Id == message.SenderId)?.Name
                         ?? message.SenderId.ToString();

            MpPlugin.Log.LogInfo(message.Fighting
                ? $"{who} is taking the event's fight"
                : $"{who} is sitting the event's fight out");
        }

        /// <summary>
        /// Hold here until everyone has chosen.
        ///
        /// No time limit, for the same reason the turn gate has none: a player reading an event can
        /// take as long as they like, and giving up on them is the desync this exists to prevent.
        /// The only ways out are everybody answering, or the session ending under us.
        /// </summary>
        public static IEnumerator<object> WaitForEveryone()
        {
            if (!MpSession.IsActive || _fightResolved)
            {
                yield break;
            }

            float waited = 0f;
            float reportInterval = 5f;
            float nextReport = reportInterval;

            while (!MpSafe.Run("EventBattleGate", () => AllAnswered || !MpSession.IsActive, true))
            {
                if (waited > nextReport)
                {
                    reportInterval = Mathf.Min(reportInterval * 2f, 30f);
                    nextReport = waited + reportInterval;
                    MpPlugin.Log.LogInfo("Still waiting on the party's event choices. " + Describe());
                }

                waited += Time.unscaledDeltaTime;
                yield return null;
            }

            MpPlugin.Log.LogInfo(AnyFighting
                ? $"Everyone has chosen; {FighterCount} player(s) fighting '{EnemyGroupId}'"
                : "Everyone has chosen; nobody took the fight");
        }

        /// <summary>Who the party is still waiting on, for the log and the banner.</summary>
        public static IEnumerable<string> StillChoosing =>
            MpSession.ConnectedPlayers
                .Where(p => !Choices.ContainsKey(p.Id))
                .Select(p => p.Name);

        public static string Describe() =>
            "waiting on: " + string.Join(", ", StillChoosing.DefaultIfEmpty("nobody"));

        /// <summary>
        /// Decide, once everyone has answered, whether this client is only here to watch. Called
        /// just before the battle starts on both paths.
        /// </summary>
        public static void SettleLocalRole()
        {
            LocalSpectating = AnyFighting && !IsFighting(MpNet.LocalPlayerId);
            if (LocalSpectating)
            {
                MpPlugin.Log.LogInfo("Sitting this one out; watching the party fight");
            }
        }

        /// <summary>Scope guard for a battle the mod asks for rather than the event's script.</summary>
        public static void BeginModRequest() => ModRequestedBattle = true;

        public static void EndModRequest() => ModRequestedBattle = false;

        /// <summary>The fight is over; we are an ordinary player again.</summary>
        public static void ClearLocalRole() => LocalSpectating = false;

        /// <summary>
        /// Forget the choices as the fight ends.
        ///
        /// These must not outlive the battle they were made for. <see cref="Active"/> decides how
        /// far enemy health scales and who is marked as watching, and a stale yes would carry both
        /// into the next ordinary battle — enemies scaled for one player, and anybody who sat the
        /// event out unable to take a turn in a fight that has nothing to do with it.
        ///
        /// <see cref="LocalSpectating"/> deliberately survives: the spectator's own battle call has
        /// not returned yet, and it clears that itself on the way out.
        /// </summary>
        public static void EndFight()
        {
            Choices.Clear();
            _answered = false;
            _fightResolved = true;
        }

        /// <summary>
        /// The fight this event offered was lost, so the event stops here.
        ///
        /// Both halves of the reward are on the far side of the battle in the script — the exhibit
        /// the fight was for, and whatever the rest of the event hands out — so being revived and
        /// then walked through them would mean losing the fight and collecting anyway.
        /// </summary>
        public static void AbortLocalEvent()
        {
            if (LocalEventAborted)
            {
                return;
            }

            LocalEventAborted = true;
            MpPlugin.Log.LogInfo("The event's fight was lost; the rest of this event is forfeit");
        }

        /// <summary>Called when any dialogue ends, so the abort cannot leak into the next one.</summary>
        public static void ClearEventAbort() => LocalEventAborted = false;
    }
}
