using HarmonyLib;
using LBoL.Presentation.UI.Panels;

namespace LBOLMP.Patches
{
    /// <summary>
    /// Add multiplayer button to main menu
    /// </summary>
    [HarmonyPatch(typeof(MainMenuPanel), "OnShowing")]
    public static class MainMenuButtonPatch
    {
        [HarmonyPostfix]
        private static void Postfix(MainMenuPanel __instance)
        {
            UI.MpMainMenuButton.Attach(__instance);
        }
    }
}
