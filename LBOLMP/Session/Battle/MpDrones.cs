using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using LBOLMP.Entities;
using LBOLMP.Net;
using LBOLMP.Session.Messages;
using LBoL.Core.Battle;
using LBoL.Core.Battle.BattleActions;
using LBoL.Core.Units;
using LBoL.EntityLib.EnemyUnits.Normal.Drones;
using LBoL.EntityLib.StatusEffects.Enemy;
using LBoL.Presentation;

namespace LBOLMP.Session.Battle
{
    /// <summary>
    /// Make EMP Device work
    /// </summary>
    internal static class MpDrones
    {
        /// <summary>
        /// How long vanilla's EMP keeps a drone stunned.
        /// </summary>
        private const int EmpTurns = 2;

        /// <summary>
        /// <c>Drone.Stun()</c>, which is protected and differs per drone.
        /// (Why?)
        /// </summary>
        private static readonly MethodInfo StunMethod = AccessTools.Method(typeof(Drone), "Stun");

        internal static void RegisterHandlers()
        {
            MpNet.On<DroneStunMessage>(OnRemoteStun);

            if (StunMethod == null)
            {
                MpPlugin.Log.LogWarning("Could not find Stun() method on drone! EMP Device will not work properly.");
            }
        }

        internal static void Report(EnemyUnit drone)
        {
            if (drone == null || !MpBattleSync.InBattle || !MpSession.IsActive
                || MpBattleSync.SpectatingOnly || MpPrivateEnemies.IsPrivate(drone))
            {
                return;
            }

            MpNet.Send(new DroneStunMessage { EnemyIndex = drone.Index });
        }

        private static void OnRemoteStun(DroneStunMessage message)
        {
            if (message.SenderId == MpNet.LocalPlayerId || !MpBattleSync.InBattle)
            {
                return;
            }

            var battle = GameMaster.Instance?.CurrentGameRun?.Battle;
            var enemy = battle?.EnemyGroup.FirstOrDefault(e => e.Index == message.EnemyIndex);
            if (!(enemy is Drone drone) || !drone.IsAlive || MpPrivateEnemies.IsPrivate(drone))
            {
                return;
            }

            MpBattleSync.QueueReplicated(battle, new MpDeferredAction(_ => Stun(drone)), "MP remote EMP");
        }

        private static IEnumerable<BattleAction> Stun(Drone drone)
        {
            yield return new ApplyStatusEffectAction<Emi>(drone, null, EmpTurns, null, null, 0f, true);
            StunMethod?.Invoke(drone, null);
        }
    }
}
