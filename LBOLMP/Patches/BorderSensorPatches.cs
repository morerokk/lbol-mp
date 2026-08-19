using System.Collections;
using System.Linq;
using HarmonyLib;
using LBOLMP.Session;
using LBoL.Core;
using LBoL.Core.Units;
using LBoL.EntityLib.Exhibits.Adventure;

namespace LBOLMP.Patches
{
    /// <summary>
    /// Tells the party when somebody obtains Border Sensor. See <see cref="MpBorderSensor"/> for
    /// what they do about it.
    ///
    /// Patched on the exhibit rather than on Yukari's trade, which is where it comes from in
    /// practice. The trade is a private nested iterator inside another exhibit, and it is not the
    /// only source. A jade box starts the run with one, and Doremy's tunnel hands one over as
    /// well (though that is currently disabled).
    ///
    /// It fires again when a save is loaded, since restoring a run adds the exhibits back one by
    /// one. We intentionally run with this behavior to recover from lost connections and lobby rejoins.
    /// </summary>
    [HarmonyPatch(typeof(JingjieGanzhiyi), "OnAdded")]
    internal static class BorderSensorAnnouncePatch
    {
        [HarmonyPostfix]
        private static void Postfix()
        {
            MpSafe.Run("BorderSensorAnnouncePatch", MpBorderSensor.Announce);
        }
    }

    /// <summary>
    /// Gaining an exhibit you already have throws an exception, and this could now happen if 2 players accept the trade at the same time.
    /// So if we happen to already have the exhibit, we instead skip it.
    /// The second player does lose the Eye Cream they traded, since the trade gives it right away. Sorry!
    /// </summary>
    [HarmonyPatch(typeof(GameRunController))]
    internal static class BorderSensorDuplicatePatch
    {
        [HarmonyPrefix]
        [HarmonyPatch(nameof(GameRunController.GainExhibitRunner))]
        private static bool BeforeRunner(GameRunController __instance, Exhibit exhibit,
            ref IEnumerator __result)
        {
            if (!AlreadyHeld(__instance, exhibit))
            {
                return true;
            }

            __result = Enumerable.Empty<object>().GetEnumerator();
            return false;
        }

        [HarmonyPrefix]
        [HarmonyPatch(nameof(GameRunController.GainExhibitInstantly))]
        private static bool BeforeInstantly(GameRunController __instance, Exhibit exhibit)
        {
            return !AlreadyHeld(__instance, exhibit);
        }

        private static bool AlreadyHeld(GameRunController gameRun, Exhibit exhibit)
        {
            return MpSafe.Run("BorderSensorDuplicatePatch", () =>
            {
                if (!MpSession.IsActive || !(exhibit is JingjieGanzhiyi)
                    || gameRun?.Player?.HasExhibit<JingjieGanzhiyi>() != true)
                {
                    return false;
                }

                MpPlugin.Log.LogInfo(
                    "Skipping a second Border Sensor; a partner's has already arrived");
                return true;
            }, false);
        }
    }
}
