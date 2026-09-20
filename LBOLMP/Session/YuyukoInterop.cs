using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;

namespace LBOLMP.Session
{
    // Dirty interop class for rmrfmaxxc's Yuyuko character mod
    internal static class YuyukoInterop
    {
        public const string Guid = "rmrfmaxxc.lbol.YuyukoCharacterMod";

        private const string ActionTypeName = "YuyukoCharacterMod.BattleActions.TriggerLawOfMortalityAction";
        private const string ArgsTypeName = "YuyukoCharacterMod.BattleActions.TriggerLawOfMortalityEventArgs";

        private static bool _resolved;
        private static Type _actionType;
        private static MethodInfo _getPhases;
        private static PropertyInfo _args;
        private static PropertyInfo _amount;

        public static bool Installed
        {
            get
            {
                Resolve();
                return _getPhases != null;
            }
        }

        /// <summary>
        /// <c>TriggerLawOfMortalityAction.GetPhases()</c>, which runs once per trigger.
        /// </summary>
        public static MethodBase GetPhasesMethod
        {
            get
            {
                Resolve();
                return _getPhases;
            }
        }

        private static void Resolve()
        {
            if (_resolved)
            {
                return;
            }
            _resolved = true;

            var actionType = FindType(ActionTypeName);
            var argsType = FindType(ArgsTypeName);
            if (actionType == null || argsType == null)
            {
                return;
            }

            var getPhases = AccessTools.Method(actionType, "GetPhases");
            _args = AccessTools.Property(actionType, "Args");
            _amount = AccessTools.Property(argsType, "Amount");

            if (getPhases == null || _args == null || _amount == null || _amount.PropertyType != typeof(int))
            {
                MpPlugin.Log.LogWarning(
                    "The Yuyuko mod is installed but does not look the way this expects! Law of Mortality is not synced.");
                return;
            }

            _actionType = actionType;
            _getPhases = getPhases;

            MpPlugin.Log.LogInfo("Adding support for the Yuyuko mod manually...");
        }

        private static Type FindType(string name) => AppDomain.CurrentDomain.GetAssemblies()
            .Select(assembly => assembly.GetType(name, false))
            .FirstOrDefault(found => found != null);

        /// <summary>
        /// How many times a Law of Mortality is triggered locally.
        /// </summary>
        public static int AmountOf(object action)
        {
            if (!Installed || action == null || !_actionType.IsInstanceOfType(action))
            {
                return 0;
            }

            var args = _args.GetValue(action);
            return args == null ? 0 : (int)_amount.GetValue(args);
        }
    }
}
