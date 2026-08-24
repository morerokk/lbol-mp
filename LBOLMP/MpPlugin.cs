using System;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using LBOLMP.Net;
using LBOLMP.Session;
using LBOLMP.UI;
using UnityEngine;

namespace LBOLMP
{
    [BepInPlugin(MpInfo.Guid, MpInfo.Name, MpInfo.Version)]
    [BepInDependency(LBoLEntitySideloader.PluginInfo.GUID, BepInDependency.DependencyFlags.HardDependency)]
    [BepInProcess("LBoL.exe")]
    public sealed class MpPlugin : BaseUnityPlugin
    {
        public static MpPlugin Instance { get; private set; }

        internal static ManualLogSource Log { get; private set; }

        private static readonly Harmony HarmonyInstance = new Harmony(MpInfo.Guid);

        public static ConfigEntry<int> DefaultPort;
        public static ConfigEntry<string> LastJoinAddress;
        public static ConfigEntry<string> PlayerName;
        public static ConfigEntry<KeyCode> LobbyHotkey;
        public static ConfigEntry<KeyCode> DiagnosticsHotkey;
        public static ConfigEntry<bool> SharedPartyPositions;

        /// <summary>Toggled at runtime. Shows the combat sync state on screen.</summary>
        public static bool ShowDiagnostics { get; private set; }
        public static ConfigEntry<float> EnemyHpScalePerExtraPlayer;

        /// <summary>
        /// Per-act enemy HP escalation, indexed by act minus one. Host authoritative.
        /// </summary>
        public static ConfigEntry<float>[] EnemyHpEscalationByAct;

        /// <summary>
        /// Whether enemies are Resilient.
        /// </summary>
        public static ConfigEntry<bool> EnableEnemyResilience;

        public static ConfigEntry<float> ReviveHpFraction;

        /// <summary>
        /// Whether multiplayer cards can be found in a run.
        /// </summary>
        public static ConfigEntry<bool> MultiplayerCardsEnabled;

        public static ConfigEntry<bool> VerboseLogging;
        public static ConfigEntry<bool> ForceCombatEvents;
        public static ConfigEntry<bool> ForceDoremyEvent;

        private void Awake()
        {
            Instance = this;
            Log = Logger;

            // Without these the entry point MonoBehaviour gets destroyed on scene change.
            DontDestroyOnLoad(gameObject);
            gameObject.hideFlags = HideFlags.HideAndDontSave;

            SetupConfig();

            Log.LogInfo($"LBOL MP v{MpInfo.Version} starting up");
            MpStrings.Load();
            L10n.Verify();

            try
            {
                LBoLEntitySideloader.EntityManager.RegisterSelf();
                new Session.MpRunSaveData().RegisterSelf(MpInfo.Guid);

                MessageRegistry.RegisterAll(Assembly.GetExecutingAssembly());
                MpEffects.RegisterAll(Assembly.GetExecutingAssembly());
                Session.MpCardAvailability.RegisterAll(Assembly.GetExecutingAssembly());
                HarmonyInstance.PatchAll(Assembly.GetExecutingAssembly());
                Log.LogInfo("Harmony patches and sideloader status effects applied");
            }
            catch (Exception e)
            {
                Log.LogError($"Failed to initialise LBOL MP: {e}");
            }

            MpSession.EnsureHandlers();

            gameObject.AddComponent<LobbyOverlay>();
            gameObject.AddComponent<RemotePlayerBoard>();
        }

        private void SetupConfig()
        {
            DefaultPort = Config.Bind("Network", nameof(DefaultPort), 7777,
                "Port used when hosting, and the default port offered when joining.");
            LastJoinAddress = Config.Bind("Network", nameof(LastJoinAddress), "127.0.0.1",
                "Remembered address of the last session you joined.");
            PlayerName = Config.Bind("Network", nameof(PlayerName), "Player",
                "The name other players see you as.");
            LobbyHotkey = Config.Bind("Interface", nameof(LobbyHotkey), KeyCode.F2,
                "Key that toggles the multiplayer lobby overlay.");
            DiagnosticsHotkey = Config.Bind("Interface", nameof(DiagnosticsHotkey), KeyCode.F3,
                "Key that toggles the combat sync diagnostics overlay.");
            SharedPartyPositions = Config.Bind("Interface", nameof(SharedPartyPositions), false,
                "Show every player (including you) in their 'real' positions. Only affects your own screen.");
            EnemyHpScalePerExtraPlayer = Config.Bind("Balance", nameof(EnemyHpScalePerExtraPlayer), 1f,
                "Extra enemy max HP per additional player, as a fraction. 1 means a 100 HP enemy has 200 HP with two players. The host's setting is used.");

			// Escalation stuff, to make acts progressively harder with more players, as players can grow faster than enemies.
            var escalationDefaults = new[] { 0f, 0.1f, 0.15f, 0.2f };
            EnemyHpEscalationByAct = new ConfigEntry<float>[MpConstants.ActCount];
            for (int act = 1; act <= MpConstants.ActCount; act++)
            {
                EnemyHpEscalationByAct[act - 1] = Config.Bind(
                    "Balance",
                    $"EnemyHpScalePerExtraPlayerEscalationAct{act}",
                    escalationDefaults[act - 1],
                    $"Additional enemy HP in Act {act}. This stacks one more time for each player. If the value is 0.1, the 2nd player will add +10% HP, the 3rd player will add +20% HP, for +30% total HP. Stacks additively with EnemyHpScalePerExtraPlayer.");
            }

            EnableEnemyResilience = Config.Bind("Balance", nameof(EnableEnemyResilience), true,
                "Give every enemy the Resilient status effect in multiplayer. For each player past the first, an enemy loses 1 more Weak, Vulnerable and Lock On at the end of its turn, and gains 1 less Firepower Down (never less than 1). Turn this off to leave debuffs exactly as strong as they are in single player. The host's setting is used.");
            ReviveHpFraction = Config.Bind("Balance", nameof(ReviveHpFraction), 0.2f,
                "How much of their max health should a defeated player be revived with. Number between 0-1. You always revive with at least 1 HP. The host's setting is used.");
            MultiplayerCardsEnabled = Config.Bind("Balance", nameof(MultiplayerCardsEnabled), true,
                "If enabled, multiplayer cards can be found during runs. The host's setting is used.");
            ForceCombatEvents = Config.Bind("Debug", nameof(ForceCombatEvents), false,
                "DEBUG: Force Yachie or Miyoi events at event nodes.");
            ForceDoremyEvent = Config.Bind("Debug", nameof(ForceDoremyEvent), false,
                "DEBUG: Force Doremy event at event nodes.");
            VerboseLogging = Config.Bind("Debug", nameof(VerboseLogging), false,
                "Log every network message. Very noisy, but useful when a desync happens.");
        }

        private void Update()
        {
            if (Input.GetKeyDown(DiagnosticsHotkey.Value))
            {
                ShowDiagnostics = !ShowDiagnostics;
                if (ShowDiagnostics)
                {
                    Log.LogInfo("Combat sync state: " + Session.Battle.MpBattleSync.DescribeTurnState());
                }
            }

            Net.SteamNet.EnsureCallbacks();
            UI.MpPortraits.Warm();

            KeepRunningWhileOnline();

            MpNet.Pump();
            MpSession.Update();
            Patches.MpBattleDriver.Update();
        }

        private bool _runInBackgroundForced;
        private bool _runInBackgroundBefore;

        /// <summary>
        /// The game already does this, but the LBOL MP mod double-dip triple *NEEDS* this to be the case.
        /// Keeps the game running in the background/focus loss, so that you don't disconnect from the game when alt tabbing.
        /// </summary>
        private void KeepRunningWhileOnline()
        {
            bool wanted = MpNet.IsOnline;
            if (wanted == _runInBackgroundForced)
            {
                return;
            }

            if (wanted)
            {
                _runInBackgroundBefore = Application.runInBackground;
                Application.runInBackground = true;
                if (!_runInBackgroundBefore)
                {
                    Log.LogInfo("Keeping the game running while unfocused for the duration of the session. Report this to the mod author! This was expected to never happen.");
                }
            }
            else
            {
                Application.runInBackground = _runInBackgroundBefore;
            }

            _runInBackgroundForced = wanted;
        }

        private void OnApplicationQuit()
        {
            MpNet.Shutdown("Application quit");
            Net.SteamNet.LeaveLobby();
        }

        private void OnDestroy()
        {
            MpNet.Shutdown("Plugin unloaded");
            Net.SteamNet.LeaveLobby();
            HarmonyInstance?.UnpatchSelf();
        }
    }
}
