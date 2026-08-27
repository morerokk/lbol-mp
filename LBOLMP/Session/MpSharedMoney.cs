using System.Linq;
using LBOLMP.Entities.JadeBoxes;
using LBOLMP.Net;
using LBOLMP.Session.Messages;
using LBoL.Core;
using LBoL.Presentation;

namespace LBOLMP.Session
{
    /// <summary>
    /// The Share the Wealth jade box shares money across the party in a pool.
    /// </summary>
    internal static class MpSharedMoney
    {
        /// <summary>
        /// Set while gaining/losing someone else's money, so we don't send the message straight back and end up looping.
        /// </summary>
        private static bool _applying;

        public static void RegisterHandlers() => MpNet.On<SharedMoneyMessage>(OnRemote);

        /// <summary>Whether the run in progress is sharing their money.</summary>
        private static bool Sharing(GameRunController gameRun) =>
            gameRun != null && MpNet.IsOnline && gameRun.HasJadeBox<MpShareTheWealth>();

        public static void OnLocalChange(GameRunController gameRun, int delta)
        {
            if (_applying || delta == 0 || !Sharing(gameRun))
            {
                return;
            }

            MpNet.Send(new SharedMoneyMessage { Delta = delta });
        }

        private static void OnRemote(SharedMoneyMessage message)
        {
            if (message.SenderId == MpNet.LocalPlayerId || message.Delta == 0)
            {
                return;
            }

            var gameRun = GameMaster.Instance?.CurrentGameRun;
            if (!Sharing(gameRun))
            {
                return;
            }

            MpSafe.Run("MpSharedMoney.OnRemote", () => Apply(gameRun, message.Delta));
        }

        private static void Apply(GameRunController gameRun, int delta)
        {
            _applying = true;
            try
            {
                if (delta > 0)
                {
                    gameRun.GainMoney(delta);
                }
                else
                {
                    gameRun.LoseMoney(-delta);
                }
            }
            finally
            {
                _applying = false;
            }

            Announce(gameRun);
        }

        /// <summary>
        /// Flash the jade box when this happens
        /// </summary>
        private static void Announce(GameRunController gameRun)
        {
            var jadeBox = gameRun.JadeBoxes.FirstOrDefault(box => box is MpShareTheWealth);
            jadeBox?.NotifyActivating();
        }
    }
}
