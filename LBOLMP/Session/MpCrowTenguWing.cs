using HarmonyLib;
using LBOLMP.Entities.Exhibits;
using LBOLMP.Net;
using LBOLMP.Session.Messages;
using LBoL.Core;
using LBoL.EntityLib.Exhibits.Common;
using LBoL.Presentation;
using LBoL.Presentation.UI;
using LBoL.Presentation.UI.Panels;

namespace LBOLMP.Session
{
    /// <summary>
    /// Crow Tengu's Wing lets you pick other nodes off-path, but map votes are unanimous, so one player having it is useless.
    /// Whoever picks it up gives everyone else a <see cref="MpCrowTenguFeather"/>, or 3 more charges on the one they have.
    /// </summary>
    internal static class MpCrowTenguWing
    {
        /// <summary>Wings picked up by partners that we haven't turned into feather charges yet.</summary>
        private static int _pendingWings;

        public static void RegisterHandlers() => MpNet.OnRemote<CrowTenguWingMessage>(OnRemote);

        public static void Reset() => _pendingWings = 0;

        /// <summary>The local player picked up Crow Tengu's Wing.</summary>
        public static void Announce()
        {
            if (!MpSession.IsActive)
            {
                return;
            }

            MpPlugin.Log.LogInfo("Crow Tengu's Wing obtained, giving the rest of the party a feather");
            MpNet.Send(new CrowTenguWingMessage());
        }

        private static void OnRemote(CrowTenguWingMessage message)
        {
            _pendingWings++;
            Tick();
        }

        public static void Tick()
        {
            if (_pendingWings <= 0)
            {
                return;
            }

            var gameRun = GameMaster.Instance?.CurrentGameRun;
            if (gameRun?.Player == null)
            {
                return;
            }

            int wings = _pendingWings;
            _pendingWings = 0;

            MpSafe.Run("MpCrowTenguWing.Grant", () =>
            {
                int charges = wings * MpCrowTenguFeatherDefinition.ChargesPerWing;

                var feather = gameRun.Player.GetExhibit<MpCrowTenguFeather>();
                if (feather != null)
                {
                    feather.AddCharges(charges);
                    MpPlugin.Log.LogInfo($"A partner found another Crow Tengu's Wing, feather is now on {feather.Counter} charges");
                    return;
                }

                // A new feather starts on one wing's worth already.
                feather = Library.CreateExhibit<MpCrowTenguFeather>();
                gameRun.GainExhibitInstantly(feather);
                if (charges > feather.Counter)
                {
                    feather.AddCharges(charges - feather.Counter);
                }

                UiManager.GetPanel<SystemBoard>()?.OnExhibitAdded(feather, 0f);

                MpPlugin.Log.LogInfo($"A partner found Crow Tengu's Wing, got a feather with {feather.Counter} charges");
            });
        }
    }

    /// <summary>
    /// Tells the party when somebody picks up Crow Tengu's Wing. See <see cref="MpCrowTenguWing"/>.
    /// </summary>
    /// <remarks>
    /// Unlike Border Sensor, this is on OnGain rather than OnAdded: loading a save adds exhibits again, which would add another 3 charges on every load.
    /// </remarks>
    [HarmonyPatch(typeof(Exhibit), "OnGain")]
    internal static class CrowTenguWingAnnouncePatch
    {
        [HarmonyPostfix]
        private static void Postfix(Exhibit __instance)
        {
            // Crow Tengu's Wing does not have its own implementation, so we patch the base and check if it's crow tengu's wing.
            if (__instance is TiangouYuyi)
            {
                MpSafe.Run("CrowTenguWingAnnouncePatch", MpCrowTenguWing.Announce);
            }
        }
    }
}
