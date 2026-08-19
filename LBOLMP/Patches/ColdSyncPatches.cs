using HarmonyLib;
using LBOLMP.Session;
using LBOLMP.Session.Battle;
using LBoL.Core;
using LBoL.Core.Battle.BattleActions;
using LBoL.Core.Units;
using LBoL.EntityLib.StatusEffects.Cirno;

namespace LBOLMP.Patches
{
    /// <summary>
    /// Which client an enemy status effect is being applied on behalf of. This patch exists to keep Cold in sync when players have Absolute Zero.
    ///
    /// Only ever true inside <c>ApplyStatusEffectAction.MainPhase</c>, which is where the game adds the effect.
    /// </summary>
    internal static class MpStatusOrigin
    {
        /// <summary>
        /// True if this is someone else's Cold being applied.
        /// </summary>
        internal static bool Replaying;
    }

    /// <summary>
    /// Records whether the status effect currently landing is our own play or a replay of somebody
    /// else's. See <see cref="ColdStackDamagePatch"/>.
    /// </summary>
    [HarmonyPatch(typeof(ApplyStatusEffectAction), "MainPhase")]
    public static class StatusOriginPatch
    {
        [HarmonyPrefix]
        private static void Prefix(ApplyStatusEffectAction __instance, out bool __state)
        {
            __state = MpStatusOrigin.Replaying;
            MpStatusOrigin.Replaying = MpSafe.Run("StatusOriginPatch",
                () => MpSession.IsActive && MpBattleSync.IsInjected(__instance), false);
        }

        [HarmonyPostfix]
        private static void Postfix(bool __state)
        {
            MpStatusOrigin.Replaying = __state;
        }
    }

    /// <summary>
    /// Cold's stacking damage is dealt by whoever applied the Cold, and only by them.
    ///
    /// This is the one debuff in the game whose damage is not a function of the debuff. Every stack
    /// after the first costs the enemy <c>9 x (Absolute Zero + 2)</c>. To make it worse, Absolute Zero is a status
    /// effect on the *player*, read through <c>GetSeLevel</c>, which asks <c>Battle.Player</c>. In a
    /// single-player game that is the person who played the card. In multiplayer it is the local player, resulting in desyncs.
    /// 
    /// The reason this doesn't work out of the box with the existing syncing is because Cold damage comes directly from <see cref="DamageAction.LoseLife"/>.
    /// To make this yet worse (thanks Cirno), when an enemy loses HP from Cold, they're both the source and the target of the hit.
    ///
    /// So the fix is: whoever melt it, dealt it. The applier deals its damage locally and publishes it as an ordinary hit.
    /// 
    /// For this particular patch, if we're not the causer of the cold damage we just don't do it.
    /// We deliberately apply a 0 damage hit instead of "not doing it", because this visually looks better and avoids Remilia not updating her intent below half HP. 
    /// </summary>
    [HarmonyPatch(typeof(Cold), nameof(Cold.StackDamage), MethodType.Getter)]
    public static class ColdStackDamagePatch
    {
        [HarmonyPostfix]
        private static void Postfix(ref int __result)
        {
            if (MpStatusOrigin.Replaying)
            {
                __result = 0;
            }
        }
    }

    /// <summary>
    /// And here's the other half: publish what our own Cold just dealt to the enemy.
    /// </summary>
    [HarmonyPatch(typeof(Cold), nameof(Cold.Stack))]
    public static class ColdStackRelayPatch
    {
        [HarmonyPostfix]
        private static void Postfix(Cold __instance, bool __result)
        {
            MpSafe.Run("ColdStackRelayPatch", () =>
            {
                // Not our problem to publish, or perhaps it did not stack at all.
                if (!__result || MpStatusOrigin.Replaying)
                {
                    return;
                }

                // Cold on somebody's private opponent stays on it. See MpPrivateEnemies.
                if (!(__instance.Owner is EnemyUnit enemy) || MpPrivateEnemies.IsPrivate(enemy))
                {
                    return;
                }

                int damage = __instance.StackDamage;
                if (damage <= 0)
                {
                    return;
                }

                MpBattleSync.ReportEnemyDamage(enemy, DamageInfo.HpLose(damage, false), "Cold2");
            });
        }
    }
}
