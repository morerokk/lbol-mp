using LBOLMP.Session;
using UnityEngine;

namespace LBOLMP.UI
{
    /// <summary>
    /// The same "waiting for the party" line the fights use, for the wait before the run: you have
    /// locked in a character or asked to continue, and somebody else has not gotten there yet.
    /// </summary>
    internal static class MpLobbyBanner
    {
        private static readonly MpBanner Waiting = new MpBanner(
            "MpLobbyWaitBanner", new Vector2(0.5f, 0.1f), 1f, 1f, MpBanner.Source.Menu);

        internal static void Tick()
        {
            MpSafe.Run("MpLobbyBanner", () => Waiting.Show(
                MpSession.IsActive && MpSession.State == MpSessionState.WaitingForPlayers
                    ? MpSession.DescribeRunWait()
                    : null));
        }
    }
}
