using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using LBOLMP.Session;
using LBOLMP.Session.Battle;
using LBoL.Core;
using LBoL.Core.Stations;
using LBoL.Presentation;
using LBoL.Presentation.UI.Panels;

namespace LBOLMP.Patches
{
    /// <summary>
    /// Abort the event entirely for the current player if they chose combat, but was then defeated in combat.
    /// This part handles skipping the post-combat reward from the event (exhibit from Yachie/Miyoi).
    /// </summary>
    [HarmonyPatch(typeof(LBoL.Core.Dialogs.DialogRunner), nameof(LBoL.Core.Dialogs.DialogRunner.Phases))]
    public static class EventAbortPatch
    {
        [HarmonyPostfix]
        private static void Postfix(ref IEnumerable<LBoL.Core.Dialogs.DialogPhase> __result)
        {
            var original = __result;
            if (original == null)
            {
                return;
            }

            __result = Truncated(original);
        }

        private static IEnumerable<LBoL.Core.Dialogs.DialogPhase> Truncated(
            IEnumerable<LBoL.Core.Dialogs.DialogPhase> original)
        {
            foreach (var phase in original)
            {
                // Checked before handing the phase over rather than after, so the line that would
                // have announced the reward is never shown either.
                if (MpSafe.Run("EventAbort", () => MpEventBattle.LocalEventAborted, false))
                {
                    MpPlugin.Log.LogInfo("Cutting the event short; its fight was lost");
                    yield break;
                }

                yield return phase;
            }
        }
    }

    [HarmonyPatch(typeof(AdventureStation), nameof(AdventureStation.OnEnter))]
    public static class EventBattleResetPatch
    {
        [HarmonyPostfix]
        private static void Postfix()
        {
            MpSafe.Run("EventBattleReset", MpEventBattle.Reset);
        }
    }

    /// <summary>
    /// Handles players taking combats from events.
    /// Waits until everyone has either ended the event, OR chosen the combat option.
    /// </summary>
    [HarmonyPatch(typeof(VnPanel), nameof(VnPanel.RunBattle))]
    public static class EventBattleStartPatch
    {
        [HarmonyPostfix]
        private static void Postfix(VnPanel __instance, string enemyGroupName, bool reopenVnPanel,
            ref IEnumerator __result)
        {
            var original = __result;
            if (original == null)
            {
                return;
            }

            __result = Gated(__instance, enemyGroupName, reopenVnPanel, original);
        }

        private static IEnumerator Gated(VnPanel panel, string enemyGroupName, bool reopenVnPanel,
            IEnumerator battle)
        {
            bool synced = MpSafe.Run("EventBattleStart", () =>
                MpSession.IsActive && MpSession.IsInRun && InAdventure(), false);

            if (synced && !MpEventBattle.ModRequestedBattle)
            {
                MpSafe.Run("EventBattleAnnounce", () => MpEventBattle.Announce(true, enemyGroupName));
                yield return MpEventBattle.WaitForEveryone();
                MpSafe.Run("EventBattleRole", MpEventBattle.SettleLocalRole);

                var agreed = MpSafe.Run("EventBattleAgreedGroup",
                    () => MpEventBattle.EnemyGroupId, string.Empty);

                if (!string.IsNullOrEmpty(agreed) && agreed != enemyGroupName)
                {
                    MpPlugin.Log.LogInfo(
                        $"This client rolled '{enemyGroupName}' for the event's fight, but the party "
                        + $"is fighting '{agreed}'; taking the party's");

                    IEnumerator replacement = null;
                    MpSafe.Run("EventBattleRegroup", MpEventBattle.BeginModRequest);
                    MpSafe.Run("EventBattleRegroupStart",
                        () => replacement = panel.RunBattle(agreed, reopenVnPanel));
                    MpSafe.Run("EventBattleRegroupEnd", MpEventBattle.EndModRequest);

                    if (replacement != null)
                    {
                        battle = replacement;
                    }
                }
            }

            yield return battle;
        }

        /// <summary>
        /// The two adventures in the game that can lead to a fight.
        /// </summary>
        private static readonly HashSet<string> CombatAdventures = new HashSet<string>
        {
            "MiyoiBartender",
            "YachieOppression"
        };

        internal static bool InAdventure()
        {
            var station = GameMaster.Instance?.CurrentGameRun?.CurrentStation as AdventureStation;
            var adventure = station?.Adventure;
            return adventure != null && CombatAdventures.Contains(adventure.GetType().Name);
        }
    }

    /// <summary>
    /// If a player decides to spectate rather than take the fight, end the event but still put them in combat as spectator.
    /// </summary>
    [HarmonyPatch(typeof(VnPanel), "CoRunDialog")]
    public static class EventBattleDeclinePatch
    {
        [HarmonyPostfix]
        private static void Postfix(VnPanel __instance, ref IEnumerator __result)
        {
            var original = __result;
            if (original == null)
            {
                return;
            }

            __result = Gated(__instance, original);
        }

        private static IEnumerator Gated(VnPanel panel, IEnumerator dialog)
        {
            yield return dialog;

            MpSafe.Run("EventAbortClear", MpEventBattle.ClearEventAbort);

            bool synced = MpSafe.Run("EventBattleDecline", () =>
                MpSession.IsActive && MpSession.IsInRun && EventBattleStartPatch.InAdventure(), false);

            if (!synced)
            {
                yield break;
            }

            MpSafe.Run("EventBattleDeclineAnnounce", () => MpEventBattle.Announce(false, string.Empty));
            yield return MpEventBattle.WaitForEveryone();
            MpSafe.Run("EventBattleRole", MpEventBattle.SettleLocalRole);

            if (!MpSafe.Run("EventBattleShouldWatch", () => MpEventBattle.LocalSpectating, false))
            {
                yield break;
            }

            string group = MpSafe.Run("EventBattleGroup", () => MpEventBattle.EnemyGroupId, string.Empty);
            if (string.IsNullOrEmpty(group))
            {
                MpPlugin.Log.LogWarning("Somebody took the event's fight but no enemy group came with it");
                yield break;
            }

            MpPlugin.Log.LogInfo($"Watching the party fight '{group}'");

            MpSafe.Run("EventBattleSpectateBegin", MpEventBattle.BeginModRequest);
            IEnumerator battle = null;
            bool started = MpSafe.Run("EventBattleSpectateStart", () =>
            {
                battle = panel.RunBattle(group, true);
                return battle != null;
            }, false);

            if (started)
            {
                yield return battle;
            }

            MpSafe.Run("EventBattleSpectateEnd", MpEventBattle.EndModRequest);
            MpSafe.Run("EventBattleSpectateDone", MpEventBattle.ClearLocalRole);

            MpSafe.Run("EventAbortClearAfterWatch", MpEventBattle.ClearEventAbort);
        }
    }
}
