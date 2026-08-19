using LBOLMP.Net;
using LBOLMP.Session.Messages;
using LBoL.Core;
using LBoL.EntityLib.Exhibits.Adventure;
using LBoL.Presentation;
using LBoL.Presentation.UI;
using LBoL.Presentation.UI.Panels;

namespace LBOLMP.Session
{
    /// <summary>
    /// The Border Sensor is given to the whole party.
    /// </summary>
    internal static class MpBorderSensor
    {
        /// <summary>Somebody has one and we do not, yet.</summary>
        private static bool _owed;

        /// <summary>
        /// Set while handing ourselves a copy, so we don't replicate the same message infinitely.
        /// </summary>
        /// Dirty but works.
        private static bool _granting;

        public static void RegisterHandlers() => MpNet.On<BorderSensorMessage>(OnRemote);

        public static void Reset()
        {
            _owed = false;
            _granting = false;
        }

        /// <summary>The local player got a border sensor.</summary>
        public static void Announce()
        {
            if (_granting || !MpSession.IsActive)
            {
                return;
            }

            MpPlugin.Log.LogInfo("Border Sensor obtained; taking the rest of the party to Act 4 too");
            MpNet.Send(new BorderSensorMessage());
        }

        private static void OnRemote(BorderSensorMessage message)
        {
            if (message.SenderId == MpNet.LocalPlayerId)
            {
                return;
            }

            _owed = true;
            Tick();
        }

        /// <summary>Give border sensor ASAP, even if we currently can't.</summary>
        public static void Tick()
        {
            if (!_owed)
            {
                return;
            }

            var gameRun = GameMaster.Instance?.CurrentGameRun;
            if (gameRun?.Player == null)
            {
                return;
            }

            _owed = false;

            MpSafe.Run("MpBorderSensor.Grant", () =>
            {
                if (gameRun.Player.HasExhibit<JingjieGanzhiyi>())
                {
                    return;
                }

                var exhibit = Library.CreateExhibit<JingjieGanzhiyi>();

                _granting = true;
                try
                {
                    gameRun.GainExhibitInstantly(exhibit);
                }
                finally
                {
                    _granting = false;
                }

                UiManager.GetPanel<SystemBoard>()?.OnExhibitAdded(exhibit, 0f);

                MpPlugin.Log.LogInfo("A partner's Border Sensor carries this run into Act 4 as well");
            });
        }
    }
}
