using HarmonyLib;
using LBOLMP.Session;
using LBoL.Presentation;

namespace LBOLMP.Patches
{
    /// <summary>
    /// Decide whether multiplayer-only cards exist for this run.
    ///
    /// CoSetupGameRun is the one place both paths meet: CoNewGameRun and CoRestoreGameRun each
    /// call it once the run exists but before the first stage is entered, so a fresh run and a
    /// loaded save are handled by the same hook.
    /// </summary>
    [HarmonyPatch(typeof(GameMaster), "CoSetupGameRun")]
    public static class CardAvailabilityRunPatch
    {
        [HarmonyPrefix]
        private static void Prefix() => MpSafe.Run("CardAvailabilityRunPatch", MpCardAvailability.OnRunSetup);
    }

    /// <summary>
    /// Back to the menu, so put them back on display in the Museum.
    /// </summary>
    [HarmonyPatch(typeof(GameMaster), nameof(GameMaster.LeaveGameRun))]
    public static class CardAvailabilityMenuPatch
    {
        [HarmonyPostfix]
        private static void Postfix() => MpSafe.Run("CardAvailabilityMenuPatch", MpCardAvailability.OnLeftRun);
    }
}
