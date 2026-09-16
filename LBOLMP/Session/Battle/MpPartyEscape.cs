using LBOLMP.Net;
using LBOLMP.Session.Messages;
using LBoL.Core.Battle.BattleActions;
using LBoL.Presentation;

namespace LBOLMP.Session.Battle
{
    /// <summary>
    /// When one player runs away from a fight (<see cref="PlayerEscapeAction"/>, such as the new Modern Duper card), everyone else also does.
    /// </summary>
    internal static class MpPartyEscape
    {
        /// <summary>
        /// What fight we already announced our escape for, to only send it once.
        /// </summary>
        private static ulong _announcedSeed;

        internal static void RegisterHandlers() => MpNet.On<PartyEscapedMessage>(OnRemoteEscape);

        internal static void Reset() => _announcedSeed = 0;

        internal static void Report(int money)
        {
            if (!MpSession.IsActive || !MpBattleSync.InBattle || MpEventBattle.LocalSpectating
                || _announcedSeed == MpBattleSync.BattleSeed)
            {
                return;
            }

            _announcedSeed = MpBattleSync.BattleSeed;
            MpPlugin.Log.LogInfo("Ran away from the fight, taking the other players with us");
            MpNet.Send(new PartyEscapedMessage { BattleSeed = MpBattleSync.BattleSeed, Money = money });
        }

        private static void OnRemoteEscape(PartyEscapedMessage message)
        {
            if (message.SenderId == MpNet.LocalPlayerId || !MpBattleSync.InBattle
                || message.BattleSeed == 0 || message.BattleSeed != MpBattleSync.BattleSeed
                || MpEventBattle.LocalSpectating)
            {
                return;
            }

            var battle = GameMaster.Instance?.CurrentGameRun?.Battle;
            if (battle == null || battle._escape)
            {
                return;
            }

            // Noted before queueing, so our own escape doesn't get announced back.
            _announcedSeed = message.BattleSeed;

            MpPlugin.Log.LogInfo($"Player {message.SenderId} ran away from the fight, replaying the escape action with them");
            MpBattleSync.QueueReplicated(battle, new PlayerEscapeAction(message.Money), "MP party escape");
        }
    }
}
