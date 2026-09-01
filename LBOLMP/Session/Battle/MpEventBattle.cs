using System.Collections.Generic;
using System.Linq;
using LBOLMP.Net;
using LBOLMP.Session.Messages;
using UnityEngine;

namespace LBOLMP.Session.Battle
{
    /// <summary>
    /// Helper class for handling events that can lead into combats.
    /// </summary>
    public static class MpEventBattle
    {
        private struct Choice
        {
            public bool Fighting;
            public string EnemyGroupId;
        }

        private static readonly Dictionary<int, Choice> Choices = new Dictionary<int, Choice>();

        /// <summary>True once the client has said they'll take the combat or not.</summary>
        private static bool _answered;

        /// <summary>
        /// True once this event's fight is over.
        /// </summary>
        private static bool _fightResolved;

        /// <summary>
        /// Set when the player loses an event combat. It aborts the event and doesn't let them get post-combat rewards either.
        /// </summary>
        public static bool LocalEventAborted { get; private set; }

        /// <summary>
        /// Keeps track of whether the mod started the battle or the game itself did.
        /// </summary>
        public static bool ModRequestedBattle { get; private set; }

        /// <summary>True when the local player is spectating this fight.</summary>
        public static bool LocalSpectating { get; private set; }

        public static void RegisterHandlers()
        {
            MpNet.On<EventBattleChoiceMessage>(OnChoice);
            MpNet.On<EventBattleChoiceQueryMessage>(OnQuery);
        }

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

        /// <summary>How many players are actually in the combat. Is always at least 1, or else there wouldn't be a combat.</summary>
        public static int FighterCount => Mathf.Max(1, Choices.Values.Count(c => c.Fighting));

        /// <summary>True while a combat from an event is the battle we are in, or about to be in.</summary>
        public static bool Active => _answered && AnyFighting;

        public static bool IsFighting(int playerId) =>
            Choices.TryGetValue(playerId, out var choice) && choice.Fighting;

        /// <summary>
        /// The enemy group the party is encountering.
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

            var choice = new Choice
            {
                Fighting = message.Fighting,
                EnemyGroupId = message.EnemyGroupId
            };

            // Choices are re-sent on request, so most of these are ones we already have.
            bool known = Choices.TryGetValue(message.SenderId, out var had)
                         && had.Fighting == choice.Fighting
                         && had.EnemyGroupId == choice.EnemyGroupId;

            Choices[message.SenderId] = choice;

            if (known)
            {
                return;
            }

            string who = MpSession.Players.FirstOrDefault(p => p.Id == message.SenderId)?.Name
                         ?? message.SenderId.ToString();

            MpPlugin.Log.LogInfo(message.Fighting
                ? $"{who} is taking the event's fight"
                : $"{who} is sitting the event's fight out");
        }

        /// <summary>
        /// Somebody is still waiting on us. Say our choice again if we have one.
        /// </summary>
        private static void OnQuery(EventBattleChoiceQueryMessage message)
        {
            if (message.SenderId == MpNet.LocalPlayerId || !_answered)
            {
                return;
            }

            if (!Choices.TryGetValue(MpNet.LocalPlayerId, out var mine))
            {
                return;
            }

            MpNet.Send(new EventBattleChoiceMessage
            {
                Fighting = mine.Fighting,
                EnemyGroupId = mine.EnemyGroupId
            });
        }

        /// <summary>How often someone still waiting asks the party to repeat itself.</summary>
        private const float QueryInterval = 1.5f;

        /// <summary>
        /// Wait here until everyone has chosen.
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
            float nextQuery = QueryInterval;

            while (!MpSafe.Run("EventBattleGate", () => AllAnswered || !MpSession.IsActive, true))
            {
                if (waited >= nextQuery)
                {
                    nextQuery = waited + QueryInterval;
                    MpSafe.Run("EventBattleQuery",
                        () => MpNet.Send(new EventBattleChoiceQueryMessage()));
                }

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

        /// <summary>Who the party is still waiting for, for the log and the banner.</summary>
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

        public static void BeginModRequest() => ModRequestedBattle = true;

        public static void EndModRequest() => ModRequestedBattle = false;

        /// <summary>Mark that the fight is over and we are an ordinary player again.</summary>
        public static void ClearLocalRole() => LocalSpectating = false;

        /// <summary>
        /// Forget the choices as the combat ends.
        /// </summary>
        public static void EndFight()
        {
            Choices.Clear();
            _answered = false;
            _fightResolved = true;
        }

        public static void AbortLocalEvent(string why)
        {
            if (LocalEventAborted)
            {
                return;
            }

            LocalEventAborted = true;
            MpPlugin.Log.LogInfo(why);
        }

        /// <summary>Called when any dialogue ends, so the abort cannot leak into the next node/event/whatever.</summary>
        public static void ClearEventAbort() => LocalEventAborted = false;
    }
}
