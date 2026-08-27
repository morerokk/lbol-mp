using HarmonyLib;
using LBOLMP.Session;
using LBoL.Core;

namespace LBOLMP.Patches
{
    /// <summary>
    /// Make <c>{PlayerName}</c> name the player whose card it actually is.
    /// </summary>
    [HarmonyPatch(typeof(GameEntity), nameof(GameEntity.PlayerName), MethodType.Getter)]
    public static class PlayerNamePatch
    {
        [HarmonyPostfix]
        private static void Postfix(GameEntity __instance, ref UnitName __result)
        {
            var replacement = MpSafe.Run("PlayerNamePatch", () => MpCardOwner.NameFor(__instance), null);
            if (replacement != null)
            {
                __result = replacement;
            }
        }
    }
}
