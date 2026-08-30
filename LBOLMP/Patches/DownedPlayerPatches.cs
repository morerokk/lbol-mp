using HarmonyLib;
using LBOLMP.Session;
using LBOLMP.Session.Battle;
using LBoL.Core.Battle.BattleActions;

namespace LBOLMP.Patches
{
    /// <summary>
    /// A player who is out of the fight stops drawing cards at the start of their turn.
    /// </summary>
    [HarmonyPatch(typeof(DrawManyCardAction), "ResolvePhase")]
    public static class DownedDrawPatch
    {
        [HarmonyPrefix]
        private static bool Prefix(DrawManyCardAction __instance)
        {
            bool skip = MpSafe.Run("DownedDrawPatch", () =>
                MpSession.IsActive
                && MpBattleSync.InBattle
                && MpDownedPlayers.OutOfFight
                && __instance.Battle != null
                && __instance.Battle.StartTurnDrawing, false);

            return !skip;
        }
    }
}
