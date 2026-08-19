using System;
using System.IO;
using System.Linq;
using HarmonyLib;
using LBoL.Core;
using LBoL.Core.PlatformHandlers;

namespace LBOLMP.Patches
{
    /// <summary>
    /// For local testing purposes only.
    /// This class handles separating 2 instances from each other, savedata and log-wise, so that you can locally debug the mod with just 1 PC and 1 player.
    /// Steam is likely unavailable if you run the game this way!
    /// </summary>
    [HarmonyPatch(typeof(SteamPlatformHandler), nameof(SteamPlatformHandler.GetSaveDataFolder))]
    public static class SaveIsolationPatch
    {
        private const string EnvironmentVariable = "LBOLMP_INSTANCE";
        private const string CommandLineFlag = "--mp-instance=";

        private static string _suffix;
        private static bool _resolved;
        private static bool _logged;

        /// <summary>Instance name for this process, or empty when running normally.</summary>
        public static string InstanceName
        {
            get
            {
                if (_resolved)
                {
                    return _suffix;
                }

                _resolved = true;
                _suffix = MpSafe.Run("SaveIsolationPatch.Resolve", Resolve, string.Empty);
                return _suffix;
            }
        }

        private static string Resolve()
        {
            var fromEnvironment = Environment.GetEnvironmentVariable(EnvironmentVariable);
            if (!string.IsNullOrWhiteSpace(fromEnvironment))
            {
                return Sanitise(fromEnvironment);
            }

            var argument = Environment.GetCommandLineArgs()
                .FirstOrDefault(a => a.StartsWith(CommandLineFlag, StringComparison.OrdinalIgnoreCase));

            return argument == null
                ? string.Empty
                : Sanitise(argument.Substring(CommandLineFlag.Length));
        }

        private static string Sanitise(string value)
        {
            var cleaned = new string(value.Trim()
                .Where(c => char.IsLetterOrDigit(c) || c == '-' || c == '_')
                .ToArray());

            return cleaned.Length > 24 ? cleaned.Substring(0, 24) : cleaned;
        }

        [HarmonyPostfix]
        private static void Postfix(ref string __result)
        {
            var folder = __result;
            __result = MpSafe.Run("SaveIsolationPatch", () => Redirect(folder), folder);
        }

        private static string Redirect(string folder)
        {
            var instance = InstanceName;
            if (string.IsNullOrEmpty(instance) || string.IsNullOrEmpty(folder))
            {
                return folder;
            }

            var redirected = folder + "_mp" + instance;

            if (!_logged)
            {
                _logged = true;
                MpPlugin.Log.LogInfo($"Save data redirected to {redirected}");
            }

            return redirected;
        }
    }
}
