using System.Linq;
using HarmonyLib;
using LBOLMP.Session;
using LBoL.Base;
using LBoL.Core;
using LBoL.EntityLib.Adventures.Shared23;
using LBoL.EntityLib.Adventures.Stage1;
using LBoL.EntityLib.Adventures.Stage3;

namespace LBOLMP.Patches
{
    /// <summary>
    /// Patches some events to look at everyone's current state, not just the host's.
    /// </summary>
    internal static class PartyGate
    {
        internal delegate bool Qualifies(int hp, int maxHp, int power);

        internal static bool WholeParty(GameRunController gameRun, Qualifies qualifies)
        {
            if (!MpSession.IsActive || !MpSession.IsInRun)
            {
                return true;
            }

            var self = gameRun?.Player;

            foreach (var player in MpSession.ConnectedPlayers)
            {
                bool mine = player.IsLocal && self != null;
                int hp = mine ? self.Hp : player.Hp;
                int maxHp = mine ? self.MaxHp : player.MaxHp;
                int power = mine ? self.Power : player.Power;

                if (maxHp <= 0)
                {
                    MpPlugin.Log.LogInfo($"Unknown Max HP for {player.Name}, event will not be given");
                    return false;
                }

                if (!qualifies(hp, maxHp, power))
                {
                    return false;
                }
            }

            return true;
        }
    }

    [HarmonyPatch(typeof(RumiaDriving.RumiaDrivingWeighter), nameof(RumiaDriving.RumiaDrivingWeighter.WeightFor))]
    public static class RumiaDrivingPartyGatePatch
    {
        private const int RequiredPower = 40;

        [HarmonyPostfix]
        private static void Postfix(GameRunController gameRun, ref float __result)
        {
            if (__result <= 0f)
            {
                return;
            }

            bool everyone = MpSafe.Run("RumiaDrivingPartyGate",
                () => PartyGate.WholeParty(gameRun, (hp, maxHp, power) => power >= RequiredPower), true);

            if (!everyone)
            {
                __result = 0f;
            }
        }
    }

    [HarmonyPatch(typeof(BackgroundDancers.BackgroundDancersWeighter), nameof(BackgroundDancers.BackgroundDancersWeighter.WeightFor))]
    public static class BackgroundDancersPartyGatePatch
    {
        private static bool AboveTenPercent(int hp, int maxHp) => hp * 10 > maxHp;

        [HarmonyPostfix]
        private static void Postfix(GameRunController gameRun, ref float __result)
        {
            if (__result <= 0f)
            {
                return;
            }

            bool everyone = MpSafe.Run("BackgroundDancersPartyGate",
                () => PartyGate.WholeParty(gameRun, (hp, maxHp, power) => AboveTenPercent(hp, maxHp)), true);

            if (!everyone)
            {
                __result = 0f;
            }
        }
    }

    /// <summary>
    /// Hina's event is now weighted at "what percentage of people in the party have a misfortune in their deck?".
    /// </summary>
    [HarmonyPatch(typeof(HinaCollect.HinaCollectWeighter), nameof(HinaCollect.HinaCollectWeighter.WeightFor))]
    public static class HinaCollectPartyWeightPatch
    {
        [HarmonyPostfix]
        private static void Postfix(GameRunController gameRun, ref float __result)
        {
            float weight = MpSafe.Run("HinaCollectPartyWeight", () => PartyShare(gameRun), -1f);

            if (weight >= 0f)
            {
                __result = weight;
            }
        }

        private static float PartyShare(GameRunController gameRun)
        {
            if (!MpSession.IsActive || !MpSession.IsInRun)
            {
                return -1f;
            }

            var self = gameRun?.Player;
            int total = 0;
            int carrying = 0;

            foreach (var player in MpSession.ConnectedPlayers)
            {
                total++;

                bool mine = player.IsLocal && self != null;
                if (mine ? HasRemovableMisfortune(gameRun) : player.HasRemovableMisfortune)
                {
                    carrying++;
                }
            }

            if (total == 0)
            {
                return -1f;
            }

            float weight = carrying / (float)total;
            MpPlugin.Log.LogInfo($"Hina event: {carrying} of {total} players have a Misfortune, weighting at {weight}");
            return weight;
        }

        private static bool HasRemovableMisfortune(GameRunController gameRun) =>
            gameRun.BaseDeckWithoutUnremovable.Any(c => c.CardType == CardType.Misfortune);
    }
}
