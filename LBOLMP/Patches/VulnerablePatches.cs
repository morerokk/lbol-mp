using HarmonyLib;
using LBOLMP.Net;
using LBOLMP.Session;
using LBoL.Base;
using LBoL.Core;
using LBoL.Core.StatusEffects;
using LBoL.Core.Units;

namespace LBOLMP.Patches
{
    /// <summary>
    /// Make Vulnerable amplify by the attacker's bonus percentage rather than the local player's.
    /// (Laevateinn)
    /// </summary>
    [HarmonyPatch(typeof(Vulnerable), "OnDamageReceiving")]
    public static class VulnerableAttackerPatch
    {
        [HarmonyPrefix]
        private static bool Prefix(Vulnerable __instance, DamageEventArgs args)
        {
            int extra = MpSafe.Run("VulnerableAttackerPatch", () => AttackerExtra(__instance, args), int.MinValue);
            if (extra == int.MinValue)
            {
                return true;
            }

            var info = args.DamageInfo;
            if (info.DamageType != DamageType.Attack)
            {
                return false;
            }

            info.Damage = info.Amount * (1f + (50 + extra) / 100f);
            args.DamageInfo = info;
            args.AddModifier(__instance);
            return false;
        }

        /// <summary>
        /// The percentage to use, or <c>int.MinValue</c> to leave the hit to the game.
        /// </summary>
        private static int AttackerExtra(Vulnerable effect, DamageEventArgs args)
        {
            if (!MpSession.IsActive || args == null || effect.Owner is PlayerUnit)
            {
                return int.MinValue;
            }

            // Only a partner's hit is modified.
            int attacker = UI.MpAllyUnits.PlayerFor(args.Source);
            return attacker == MpConstants.InvalidPlayerId
                ? int.MinValue
                : MpPlayerExhibits.EnemyVulnerableExtra(attacker);
        }
    }
}
