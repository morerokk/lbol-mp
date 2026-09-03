using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using LBOLMP.Session;
using LBoL.Core;

namespace LBOLMP.Patches
{
    /// <summary>
    /// Give everyone the host's booster packs.
    /// </summary>
    [HarmonyPatch(typeof(GameRunController), MethodType.Constructor, typeof(GameRunStartupParameters))]
    public static class HostPacksPatch
    {
        [HarmonyPostfix]
        private static void Postfix(GameRunController __instance)
        {
            MpSafe.Run("HostPacksPatch", () =>
            {
                var packs = MpSession.RunPacks;
                if (!MpSession.IsActive || packs == null)
                {
                    return;
                }

                var theirs = new List<string>(packs);
                var ours = __instance.Packs ?? new List<string>();

                if (!theirs.OrderBy(p => p).SequenceEqual(ours.OrderBy(p => p)))
                {
                    MpPlugin.Log.LogInfo(
                        $"Using the host's booster packs ({Describe(theirs)}) "
                        + $"rather than the ones enabled here ({Describe(ours)})");
                }

                __instance.Packs = theirs;
            });
        }

        private static string Describe(List<string> packs) =>
            packs.Count == 0 ? "none" : string.Join(", ", packs.ToArray());
    }
}
