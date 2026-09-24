using System;
using System.Linq;
using LBOLMP.Net;
using LBOLMP.Session.Battle;
using LBoL.Core;
using LBoL.Core.Battle;
using LBoL.Core.StatusEffects;
using LBoL.Core.Units;

namespace LBOLMP.Session
{
    /// <summary>
    /// Interop for the Youmu character mod's green starting exhibit (Honed Roukanken).
    /// Lock On on enemies doesn't drop below the exhibit's Value2, and removing it halves it instead.
    /// </summary>
    /// The exhibit only runs on its owner's machine, and it decides what freezes at the end of its owner's turn, which is a
    /// different moment on everyone's screen. So in multiplayer everyone applies the same rule at the start of the enemy's
    /// turn instead, from the minimum amount that each player publishes (see <see cref="MpPlayerExhibits"/>).
    internal static class YoumuInterop
    {
        public const string LockOnExhibitId = "YoumuExhibitG";

        /// <summary>
        /// The Lock On floor our own exhibits give, 0 without one.
        /// </summary>
        public static int LocalLockOnFloor(PlayerUnit player) =>
            player?.Exhibits
                .Where(e => e.Id == LockOnExhibitId)
                .Select(e => e.Config.Value2 ?? 0)
                .DefaultIfEmpty(0)
                .Max() ?? 0;

        /// <summary>
        /// Lock On on this enemy doesn't drop below this minimum.
        /// 0 if there's no floor, or if we're not in multiplayer.
        /// </summary>
        public static int LockOnFloor(EnemyUnit enemy)
        {
            if (enemy == null || !MpSession.IsActive)
            {
                return 0;
            }

            int floor = LocalLockOnFloor(enemy.Battle?.Player);

            if (MpPrivateEnemies.IsPrivate(enemy))
            {
                return floor;
            }

            return MpSession.ConnectedPlayers
                .Where(p => p.Id != MpNet.LocalPlayerId)
                .Select(p => MpPlayerExhibits.EnemyLockOnFloor(p.Id))
                .Aggregate(floor, Math.Max);
        }

        /// <summary>
        /// Do what the exhibit does to a Lock On removal, when somebody else owns the exhibit.
        /// </summary>
        public static void StandInForRemoval(StatusEffectEventArgs args, BattleController battle)
        {
            if (!(args?.Effect is LockedOn) || !(args.Unit is EnemyUnit enemy) || battle == null
                || battle.BattleShouldEnd || !enemy.IsAlive)
            {
                return;
            }

            // Our own exhibit already did it.
            if (LocalLockOnFloor(battle.Player) > 0)
            {
                return;
            }

            int floor = LockOnFloor(enemy);
            var lockedOn = enemy.GetStatusEffect<LockedOn>();
            if (floor <= 0 || lockedOn == null)
            {
                return;
            }

            args.CanCancel = true;
            args.CancelBy(lockedOn);
            int level = lockedOn.Level;
            lockedOn.Level = level >= floor * 2 ? level / 2 : Math.Min(level, floor);
            lockedOn.NotifyActivating();
        }

        /// <summary>
        /// True when a Lock On removal was replaced by halving it instead, by the exhibit or by <see cref="StandInForRemoval"/>.
        /// Everyone else has to do the same, so the removal is still synced.
        /// </summary>
        public static bool IsLockOnFloorCancel(StatusEffectEventArgs args) =>
            args.Effect is LockedOn
            && (args.CancelSource is LockedOn || (args.CancelSource as Exhibit)?.Id == LockOnExhibitId);
    }
}
