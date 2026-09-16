using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using LBOLMP.Net;
using LBOLMP.Session;
using LBoL.Core;
using LBoL.Presentation.UI;
using LBoL.Presentation.UI.Panels;
using LBoL.Presentation.UI.Widgets;

namespace LBOLMP.Patches
{
    /// <summary>
    /// Force the host to be authoritative of More Map Options' "Sts Map" toggle on the difficulty screen.
    /// </summary>
    [HarmonyPatch]
    public static class LobbyStsMapPatch
    {
        private const string TogglePath = "DifficultyPanel/MainRoot/StSMapToggle";

        /// <summary>The vanilla stage list StsMap swaps out, from the last time the panel was shown.</summary>
        private static Func<Stage[]> _vanillaStages;

        /// <summary>True while we are the ones calling StsMap's ApplyToggle.</summary>
        private static bool _applying;

        private static bool WeAreChoosing => !MpNet.IsOnline || MpNet.IsHost;

        private static bool Prepare() => StsMapInterop.Installed;

        private static MethodBase TargetMethod() => StsMapInterop.ApplyToggleMethod;

        /// <summary>
        /// Runs whenever StsMap shows or flips its toggle.
        /// </summary>
        private static void Postfix(StartGamePanel panel, bool isOn, Func<Stage[]> vanillaFunc)
        {
            _vanillaStages = vanillaFunc;
            if (_applying)
            {
                return;
            }

            MpSafe.Run("LobbyStsMapPostfix", () =>
            {
                if (WeAreChoosing)
                {
                    SetSwitch(panel, isOn, locked: false);
                    if (MpNet.IsOnline)
                    {
                        MpSession.PublishHostStsMap(isOn);
                    }
                    return;
                }

                bool host = MpSession.HostStsMap;
                if (isOn != host)
                {
                    Apply(panel, host);
                }

                SetSwitch(panel, host, locked: true);
            });
        }

        private static void Apply(StartGamePanel panel, bool enabled)
        {
            StsMapInterop.Enabled = enabled;

            _applying = true;
            try
            {
                StsMapInterop.Apply(panel, enabled, _vanillaStages);
            }
            finally
            {
                _applying = false;
            }
        }

        private static void SetSwitch(StartGamePanel panel, bool isOn, bool locked)
        {
            var toggle = panel.transform.Find(TogglePath)?.GetComponent<SwitchWidget>();
            if (toggle == null)
            {
                return;
            }

            if (toggle.IsOn != isOn)
            {
                toggle.SetValueWithoutNotifier(isOn, instant: !panel.isActiveAndEnabled);
            }

            toggle.IsLocked = locked;
        }

        /// <summary>
        /// Move a client's toggle to the host's choice, whether or not the panel is open.
        /// </summary>
        public static void ApplyHostChoice()
        {
            if (!StsMapInterop.Installed || WeAreChoosing)
            {
                return;
            }

            bool host = MpSession.HostStsMap;
            StsMapInterop.Enabled = host;

            // Not shown yet. StsMap reads the flag when the panel first opens.
            var panel = UiManager.GetPanel<StartGamePanel>();
            if (panel == null || _vanillaStages == null)
            {
                return;
            }

            Apply(panel, host);
            SetSwitch(panel, host, locked: true);
        }

        /// <summary>
        /// The stages to start with, rebuilt if the ones confirmed with don't match the host's choice.
        /// (The host can still flip it after a client has already confirmed.)
        /// </summary>
        public static IEnumerable<Stage> StagesFor(bool hostStsMap, IEnumerable<Stage> confirmed)
        {
            var stages = confirmed?.ToList() ?? new List<Stage>();

            if (!StsMapInterop.Installed)
            {
                if (hostStsMap)
                {
                    MpPlugin.Log.LogWarning(
                        "The host is playing with StsMap's map, but StsMap is not installed here! " +
                        "The map will not match theirs");
                }
                return stages;
            }

            if (stages.Any(StsMapInterop.IsStsStage) == hostStsMap)
            {
                return stages;
            }

            var panel = UiManager.GetPanel<StartGamePanel>();
            if (panel == null || _vanillaStages == null)
            {
                MpPlugin.Log.LogWarning("Could not switch to the host's StsMap setting. The map will not match theirs (panel or _vanillaStages was null, report this somewhere, yell loudly or something)");
                return stages;
            }

            MpPlugin.Log.LogInfo($"Starting with the host's StsMap setting ({(hostStsMap ? "on" : "off")})");
            Apply(panel, hostStsMap);
            return panel._stages.ToList();
        }
    }
}
