using System;
using HarmonyLib;
using LBOLMP.Session;
using LBoL.Core.Dialogs;
using LBoL.Core.Stations;
using LBoL.Presentation;
using LBoL.Presentation.UI.Panels;

namespace LBOLMP.Patches
{
    /// <summary>
    /// Disable Doremy's "skip directly to the Act 3 boss" option in multiplayer.
    /// This is an absolute meme option anyway, and it would require implementing extra syncing for just this one event,
    /// to ensure 1 player can't skip ahead of the rest of the players.
    /// So we just say no to all that nonsense, and disable the option in multiplayer.
    /// The option is disabled rather than removed, to avoid breaking the game UI.
    /// We also ONLY disable the tunnel option, not the "Continue" option after the 2nd option to avoid softlocks.
    /// </summary>
    [HarmonyPatch(typeof(VnPanel), "ShowOptions")]
    public static class DoremyPortalOptionPatch
    {
        /// <summary>
        /// The Yarn line id of "[Use Tunnel]", from
        /// <c>StreamingAssets/Localization/*/Dialogs/Adventure/DoremyPortal.yaml</c>.
        /// (Thanks Nolav for the yarn decompilations!)
        /// </summary>
        private const string TunnelOptionLineId = "line:0891a52";

        private static readonly AccessTools.FieldRef<DialogOption, bool> AvailableRef =
            SafeFieldRef<bool>("<Available>k__BackingField");

        private static readonly AccessTools.FieldRef<DialogOption, string> LineIdRef =
            SafeFieldRef<string>("_lineId");

        private static AccessTools.FieldRef<DialogOption, T> SafeFieldRef<T>(string name)
        {
            try
            {
                return AccessTools.FieldRefAccess<DialogOption, T>(name);
            }
            catch (Exception e)
            {
                // Swallow all exceptions this static initializer might throw, or else the whole class can't load.
                MpPlugin.Log.LogError($"Could not reach DialogOption.{name}: {e.Message}");
                return null;
            }
        }

        [HarmonyPrefix]
        private static void Prefix(DialogOption[] options)
        {
            MpSafe.Run("DoremyPortalOption", () =>
            {
                if (options == null || options.Length == 0 || AvailableRef == null || LineIdRef == null)
                {
                    return;
                }

                if (!MpSession.IsActive || !MpSession.IsInRun || !InDoremyPortal())
                {
                    return;
                }

                var lineIds = new string[options.Length];
                for (int i = 0; i < options.Length; i++)
                {
                    lineIds[i] = LineIdRef(options[i]);
                }

                int tunnel = IndexOfTunnel(lineIds);
                if (tunnel < 0 || !AvailableRef(options[tunnel]))
                {
                    return;
                }

                AvailableRef(options[tunnel]) = false;
                MpPlugin.Log.LogInfo("Hiding Doremy's tunnel: skipping to the boss would desync the rest of the party");
            });
        }

        /// <summary>
        /// Which option in this prompt is the tunnel, or -1 for none of them.
        /// </summary>
        internal static int IndexOfTunnel(string[] lineIds)
        {
            for (int i = 0; i < lineIds.Length; i++)
            {
                if (lineIds[i] == TunnelOptionLineId)
                {
                    return i;
                }
            }

            if (lineIds.Length > 1)
            {
                MpPlugin.Log.LogWarning(
                    $"Could not find Doremy's tunnel by line id ({TunnelOptionLineId}) in a {lineIds.Length}-option " +
                    "dialogue prompt. This patch won't do anything now. Check that the game's dialogue hasn't changed, or update the line ID if it has.");
            }

            return -1;
        }

        internal static bool InDoremyPortal()
        {
            var station = GameMaster.Instance?.CurrentGameRun?.CurrentStation as AdventureStation;
            return station?.Adventure?.GetType().Name == "DoremyPortal";
        }
    }

    /// <summary>
    /// NOTE: SUPER UNTESTED fallback to avoid teleporting the party anyway, just in case the game updates and now the Doremy portal option is suddenly available anyway.
    /// Disables the teleport and keeps the teleporter in line with the rest of the party. This basically gives them Border Sensor for free, but whatever.
    /// </summary>
    [HarmonyPatch(typeof(LBoL.EntityLib.Adventures.FirstPlace.DoremyPortal),
        nameof(LBoL.EntityLib.Adventures.FirstPlace.DoremyPortal.TeleportBoss))]
    public static class DoremyPortalTeleportPatch
    {
        [HarmonyPrefix]
        private static bool Prefix()
        {
            return MpSafe.Run("DoremyPortalTeleport", () =>
            {
                if (!MpSession.IsActive || !MpSession.IsInRun)
                {
                    return true;
                }

                MpPlugin.Log.LogWarning("Refusing to skip to the boss in multiplayer.");
                return false;
            }, true);
        }
    }
}
