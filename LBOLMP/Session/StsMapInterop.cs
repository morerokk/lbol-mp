using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using LBoL.Core;
using LBoL.Presentation.UI.Panels;

namespace LBOLMP.Session
{
    // Dirty interop class for Valon's More Map Options (StS Map) mod
    internal static class StsMapInterop
    {
        public const string Guid = "valon.misc.StsMap";

        private const string ToggleTypeName = "StsMap.Source.Patching.MapButtonPatch";
        private const string StageNamespace = "StsMap.Source.Stages";

        private static bool _resolved;
        private static FieldInfo _useStsMap;
        private static MethodInfo _applyToggle;

        public static bool Installed
        {
            get
            {
                Resolve();
                return _applyToggle != null;
            }
        }

        /// <summary><c>MapButtonPatch.ApplyToggle(StartGamePanel, bool, Func&lt;Stage[]&gt;)</c>, which sets the panel's stages.</summary>
        public static MethodInfo ApplyToggleMethod
        {
            get
            {
                Resolve();
                return _applyToggle;
            }
        }

        private static void Resolve()
        {
            if (_resolved)
            {
                return;
            }
            _resolved = true;

            var type = AppDomain.CurrentDomain.GetAssemblies()
                .Select(assembly => assembly.GetType(ToggleTypeName, false))
                .FirstOrDefault(found => found != null);
            if (type == null)
            {
                return;
            }

            _useStsMap = AccessTools.Field(type, "UseStSMap");
            var applyToggle = AccessTools.Method(type, "ApplyToggle",
                new[] { typeof(StartGamePanel), typeof(bool), typeof(Func<Stage[]>) });

            if (_useStsMap == null || _useStsMap.FieldType != typeof(bool) || applyToggle == null)
            {
                MpPlugin.Log.LogWarning(
                    "StsMap is installed but does not look the way this expects! The map toggle is not synced.");
                return;
            }

            _applyToggle = applyToggle;
        }

        /// <summary>Whether this machine's toggle is on. False if StsMap is not installed.</summary>
        public static bool Enabled
        {
            get => Installed && (bool)_useStsMap.GetValue(null);
            set
            {
                if (Installed)
                {
                    _useStsMap.SetValue(null, value);
                }
            }
        }

        /// <summary>Point the panel's stages at StsMap's or the vanilla ones.</summary>
        public static void Apply(StartGamePanel panel, bool enabled, Func<Stage[]> vanillaStages)
        {
            if (Installed)
            {
                _applyToggle.Invoke(null, new object[] { panel, enabled, vanillaStages });
            }
        }

        public static bool IsStsStage(Stage stage) => stage != null && stage.GetType().Namespace == StageNamespace;
    }
}
