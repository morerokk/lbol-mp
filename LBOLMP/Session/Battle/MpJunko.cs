using System.Collections.Generic;
using LBOLMP.Net;
using LBOLMP.Session.Messages;
using LBoL.Core;
using LBoL.Core.Battle;
using LBoL.EntityLib.EnemyUnits.Character;
using LBoL.EntityLib.StatusEffects.Enemy;
using LBoL.Presentation;
using UnityEngine;

namespace LBOLMP.Session.Battle
{
    /// <summary>
    /// Make Junko's overflowing blemishes a shared pool of P mana rather than gaining +14 firepower on Turn 1 (lmao)
    /// </summary>
    internal static class MpJunko
    {
        /// <summary>
        /// Philosopher's mana each player has gained this fight, by player id, including our own.
        /// </summary>
        private static readonly Dictionary<int, int> Gained = new Dictionary<int, int>();

        /// <summary>Somebody else's total needs to be re-checked.</summary>
        /// Why did I do it this way? Probably something related to "I can't directly apply it off the main thread or very bad things happen" tbh
        private static bool _pending;

        internal static void RegisterHandlers()
        {
            MpNet.On<JunkoImpurityMessage>(OnRemoteImpurity);
        }

        /// <summary>Called at the start of every fight as well as at the end of a session.</summary>
        internal static void Reset()
        {
            Gained.Clear();
            _pending = false;
        }

        /// <summary>True if in a multiplayer session where this is necessary.</summary>
        internal static bool Active => MpSession.IsActive && MpBattleSync.InBattle;

        /// <summary>
        /// Everyone's philosopher's mana added together.
        /// </summary>
        private static int Pooled
        {
            get
            {
                int total = 0;
                foreach (var amount in Gained.Values)
                {
                    total += amount;
                }
                return total;
            }
        }

        /// <summary>
        /// The level of Overflowing Blemishes that the party's pooled P mana has triggered.
        /// </summary>
        private static int PartyLevel()
        {
            int fighters = Mathf.Max(1, MpBattleSync.PlayerCountAtBattleStart);
            return JunkoColor.GetLevel(Pooled / fighters);
        }

        internal static IEnumerable<BattleAction> OnManaGained(JunkoColor effect, ManaEventArgs args)
        {
            var battle = effect?.Battle;
            if (battle == null || battle.BattleShouldEnd)
            {
                yield break;
            }

            var junko = effect._junko;
            if (junko == null || !junko.IsAlive)
            {
                yield break;
            }

            int philosophy = args?.Value.Philosophy ?? 0;
            if (philosophy <= 0)
            {
                yield break;
            }

            effect.Count += philosophy;

            Gained[MpNet.LocalPlayerId] = effect.Count;
            MpNet.Send(new JunkoImpurityMessage { Philosophy = effect.Count });

            foreach (var action in Climb(effect, junko))
            {
                yield return action;
            }
        }

        /// <summary>
        /// Raises the effect to whatever the pool now says, and gives Junko the Firepower she *should* be having
        /// (even if accidentally called twice due to networking)
        /// </summary>
        private static IEnumerable<BattleAction> Climb(JunkoColor effect, Junko junko)
        {
            int level = PartyLevel();
            if (level <= effect.Level)
            {
                yield break;
            }

            MpPlugin.Log.LogInfo(
                $"Junko's impurity is now level {level}: the party has gained {Pooled} "
                + "philosopher's mana between them");

            effect.Level = level;
            effect.NotifyActivating();

            foreach (var action in junko.JunkoColorActions(level))
            {
                yield return action;
            }
        }

        private static void OnRemoteImpurity(JunkoImpurityMessage message)
        {
            if (message.SenderId == MpNet.LocalPlayerId)
            {
                return;
            }

            Gained[message.SenderId] = message.Philosophy;
            _pending = true;
        }

        /// <summary>
        /// Applies milestones that someone else's P mana gain has triggered.
        /// </summary>
        internal static void Tick()
        {
            if (!_pending || !Active)
            {
                return;
            }

            MpSafe.Run("MpJunko.Tick", () =>
            {
                var battle = GameMaster.Instance?.CurrentGameRun?.Battle;
                if (battle == null || battle.BattleShouldEnd)
                {
                    return;
                }

                var effect = battle.Player?.GetStatusEffect<JunkoColor>();
                if (effect == null)
                {
                    return;
                }

                _pending = false;

                var junko = effect._junko;
                if (junko == null || !junko.IsAlive)
                {
                    return;
                }

                foreach (var action in Climb(effect, junko))
                {
                    MpBattleSync.QueueReplicated(battle, action, "MP Junko impurity");
                }
            });
        }
    }
}
