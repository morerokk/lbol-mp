using System.Collections.Generic;
using System.Linq;
using LBoL.Core.Battle;
using LBoL.Core.Battle.BattleActions;
using LBoL.Core.Units;
using LBoL.EntityLib.EnemyUnits.Character;

namespace LBOLMP.Session.Battle
{
    /// <summary>
    /// Handles Eiki Shiki's special mirror copy handling.
    /// tl;dr: Eiki Shiki summons a mirror of your own character's Act 1 boss equivalent, and this mirror is exclusive to you.
    /// The mirror image is not scaled by multiplayer HP counts, and damage done by your partners is not replicated to this mirror copy.
    /// Essentially, you are all fighting Eiki, but only you are fighting the mirror copy, and this class handles that.
    /// </summary>
    /// This really is too much code for one gimmick fight, but it's fine.
    public static class MpPrivateEnemies
    {
        private static readonly HashSet<EnemyUnit> Private =
            new HashSet<EnemyUnit>(ReferenceEqualityComparer.Instance);

        private sealed class ReferenceEqualityComparer : IEqualityComparer<EnemyUnit>
        {
            public static readonly ReferenceEqualityComparer Instance = new ReferenceEqualityComparer();

            public bool Equals(EnemyUnit x, EnemyUnit y) => ReferenceEquals(x, y);

            public int GetHashCode(EnemyUnit obj) =>
                System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
        }

        // Whether the mirror copy has "run away" because you were defeated (prevents softlocks if a player dies at this battle).
        private static bool _dismissed;

        public static void Reset()
        {
            Private.Clear();
            _dismissed = false;
        }

        /// <summary>
        /// Decide whether an enemy about to be spawned is a private enemy.
        /// </summary>
        public static void OnSpawning(EnemyUnit spawner, EnemyUnit spawned)
        {
            if (spawned == null)
            {
                return;
            }

            if (spawner is Siji || IsPrivate(spawner))
            {
                Private.Add(spawned);
            }
        }

        /// <summary>True if this enemy exists on this client only.</summary>
        public static bool IsPrivate(Unit unit) => unit is EnemyUnit enemy && Private.Contains(enemy);

        /// <summary>
        /// Send this client's private enemies away by having them take a Run Away action,
        /// because the current client was defeated.
        /// </summary>
        public static void Dismiss(BattleController battle)
        {
            if (_dismissed || battle == null || Private.Count == 0)
            {
                return;
            }

            _dismissed = true;

            foreach (var enemy in battle.EnemyGroup.ToList())
            {
                if (!IsPrivate(enemy) || !enemy.IsAlive || enemy.IsEscaped)
                {
                    continue;
                }

                MpPlugin.Log.LogInfo($"Out of the fight! Dismissing private enemy {enemy.Id}");
                battle.RequestDebugAction(new EscapeAction(enemy), "MP dismissing a private enemy");
            }
        }
    }
}
