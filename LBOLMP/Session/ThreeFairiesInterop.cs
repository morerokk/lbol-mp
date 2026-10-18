using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using LBOLMP.Net;
using LBOLMP.Session.Battle;
using LBOLMP.Session.Messages;
using LBoL.Core;
using LBoL.Core.Cards;
using LBoL.Core.StatusEffects;
using LBoL.Core.Units;
using LBoL.Presentation;

namespace LBOLMP.Session
{
    /// <summary>
    /// Interop for the Three Fairies of Light character mod, particularly its Silence Signs.
    /// </summary>
    internal static class ThreeFairiesInterop
    {
        [Flags]
        internal enum Rule : ushort
        {
            None = 0,
            GrazeEnemies = 1 << 0,
            GrazePlayers = 1 << 1,
            WeakVulnEnemies = 1 << 2,
            WeakVulnPlayers = 1 << 3,
            FpSpEnemies = 1 << 4,
            FpSpPlayers = 1 << 5,
            FlawlessEnemies = 1 << 6,
            FlawlessPlayers = 1 << 7,
            BlockCapEnemies = 1 << 8
        }

        /// <summary>Phase of Measured Walls' block cap</summary>
        private const int BlockCap = 15;

        /// <summary>Rules from other players' signs. Key is player ID.</summary>
        private static readonly Dictionary<int, Rule> Remote = new Dictionary<int, Rule>();

        /// <summary>All of <see cref="Remote"/> OR'd together.</summary>
        private static Rule _remoteRules;

        private static string _sent;

        private static bool _resolved;
        private static Type _playerType;
        private static MethodInfo _getFairyAt;
        private static object _midPosition;
        private static PropertyInfo _activePerks;
        private static object _lunaPassiveSlot;

        public static void RegisterHandlers() => MpNet.OnRemote<SilenceSignMessage>(OnRemoteRules);

        public static void Reset()
        {
            Remote.Clear();
            _remoteRules = Rule.None;
            _sent = null;
        }

        public static void OnPlayerLeft(int playerId)
        {
            if (Remote.Remove(playerId))
            {
                Recombine();
            }
        }

        private static void Resolve()
        {
            if (_resolved)
            {
                return;
            }
            _resolved = true;

            _playerType = AccessTools.TypeByName("TFOLMod.TFOLModDef+TFOLMod");
            if (_playerType == null)
            {
                return;
            }

            var teamPosition = AccessTools.TypeByName("TFOLMod.TeamPosition");
            var perkSlot = AccessTools.TypeByName("TFOLMod.Perks.PerkSlot");
            _getFairyAt = AccessTools.TypeByName("TFOLMod.PlayerTeamState")
                ?.GetMethod("GetFairyAt", BindingFlags.Public | BindingFlags.Static);
            _activePerks = AccessTools.TypeByName("TFOLMod.Perks.PerkState")
                ?.GetProperty("ActivePerks", BindingFlags.Public | BindingFlags.Static);

            if (_getFairyAt == null || teamPosition == null || _activePerks == null || perkSlot == null)
            {
                MpPlugin.Log.LogWarning("Three Fairies of Light is installed but does not look the way LBOL MP expects! Report this somewhere, yell really loudly. Use all-caps.");
                _playerType = null;
                return;
            }

            _midPosition = Enum.Parse(teamPosition, "Mid");
            _lunaPassiveSlot = Enum.Parse(perkSlot, "LunaPassive");
        }

        /// <summary>
        /// Which rules our own signs would enforce.
        /// </summary>
        private static Rule LocalRules()
        {
            Resolve();

            var player = GameMaster.Instance?.CurrentGameRun?.Battle?.Player;
            if (_playerType == null || !MpBattleSync.InBattle || player == null || !_playerType.IsInstanceOfType(player))
            {
                return Rule.None;
            }

            if (_getFairyAt.Invoke(null, new[] { _midPosition })?.ToString() != "Luna")
            {
                return Rule.None;
            }

            bool Has(string id) => player.StatusEffects.Any(s => s.Id == id);

            // Dark Stillness
            var lunaPerk = (_activePerks.GetValue(null) as IDictionary)?[_lunaPassiveSlot] as Card;
            bool darkStillness = lunaPerk?.Id == "TFOLPassivePerkDarkStillness";
            if (!darkStillness && Has("TFOLSilenceSignMoonlitPurityAntiPreventionSe"))
            {
                return Rule.None;
            }

            var rules = Rule.None;
            if (Has("TFOLSilenceSignClarityOfTheFullMoonGrazeSe"))
            {
                rules |= Rule.GrazeEnemies | (darkStillness ? Rule.None : Rule.GrazePlayers);
            }
            if (Has("TFOLSilenceSignClarityOfTheFullMoonWeakVulnSe"))
            {
                rules |= Rule.WeakVulnPlayers | (darkStillness ? Rule.None : Rule.WeakVulnEnemies);
            }
            if (Has("TFOLSilenceSignStillnessOfTheNightFpSpSe"))
            {
                rules |= Rule.FpSpEnemies | (darkStillness ? Rule.None : Rule.FpSpPlayers);
            }
            if (Has("TFOLSilenceSignWhispersOfFrailtyFlawlessSe"))
            {
                rules |= Rule.FlawlessEnemies | (darkStillness ? Rule.None : Rule.FlawlessPlayers);
            }
            if (Has("TFOLSilenceSignWhispersOfFrailtyBlockBarrierSe"))
            {
                // As written, this should not apply to other players
                // As intended, this seems too disruptive to apply to other players anyway
                rules |= Rule.BlockCapEnemies;
            }
            return rules;
        }

        public static void Publish()
        {
            if (!MpSession.IsActive)
            {
                return;
            }

            var rules = LocalRules();

            string state = string.Join(",", MpSession.ConnectedPlayers.Select(p => p.Id.ToString()).ToArray())
                           + "|" + (ushort)rules;
            if (state == _sent)
            {
                return;
            }

            _sent = state;
            MpNet.Send(new SilenceSignMessage { Rules = (ushort)rules });
        }

        private static void OnRemoteRules(SilenceSignMessage message)
        {
            Remote[message.SenderId] = (Rule)message.Rules;
            Recombine();
        }

        private static void Recombine()
        {
            _remoteRules = Rule.None;
            foreach (var rules in Remote.Values)
            {
                _remoteRules |= rules;
            }
        }

        private static bool AppliesToUnit(Unit unit, Rule enemies, Rule players)
        {
            switch (unit)
            {
                case EnemyUnit _:
                    return (_remoteRules & enemies) != 0;
                case PlayerUnit _:
                    // Other players' stand-ins follow their owners, who get blocked on their own screens.
                    return (_remoteRules & players) != 0 && !UI.MpAllyUnits.IsMirror(unit);
                default:
                    return false;
            }
        }

        /// <summary>
        /// If true, this status effect application is being blocked by a TFoL player.
        /// </summary>
        public static bool BlocksStatus(StatusEffectApplyEventArgs args)
        {
            if (_remoteRules == Rule.None || !MpBattleSync.InBattle || args?.Unit == null)
            {
                return false;
            }

            switch (args.Effect)
            {
                case Graze _:
                    return AppliesToUnit(args.Unit, Rule.GrazeEnemies, Rule.GrazePlayers);
                case Weak _:
                case Vulnerable _:
                    return AppliesToUnit(args.Unit, Rule.WeakVulnEnemies, Rule.WeakVulnPlayers);
                case Firepower _:
                case TempFirepower _:
                case Spirit _:
                case TempSpirit _:
                    return AppliesToUnit(args.Unit, Rule.FpSpEnemies, Rule.FpSpPlayers);
                case Invincible _:
                case InvincibleEternal _:
                    return AppliesToUnit(args.Unit, Rule.FlawlessEnemies, Rule.FlawlessPlayers);
                default:
                    return false;
            }
        }

        /// <summary>
        /// Applies another player's Phase of Measured Walls to an instance of Block/Barrier gain.
        /// </summary>
        public static void CapBlockShield(Unit target, ref float block, ref float shield)
        {
            if (_remoteRules == Rule.None || !MpBattleSync.InBattle
                || !AppliesToUnit(target, Rule.BlockCapEnemies, Rule.None))
            {
                return;
            }

            block = Math.Min(block, BlockCap);
            shield = Math.Min(shield, BlockCap);
        }
    }
}
