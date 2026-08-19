using LBOLMP.Net;
using LBoL.Core;
using LBoL.Presentation;

namespace LBOLMP.Session
{
    /// <summary>
    /// Re-enables achievements and profile records on multiplayer runs, when the game would normally think that this is a seeded run.
    /// It's technically an unseeded run, it just so happens that the randomly-generated seed comes from the host.
    /// </summary>
    internal static class MpRunCredit
    {
        private static GameRunController _credited;

        internal static void Reset()
        {
            _credited = null;
        }

        internal static void Tick()
        {
            if (!MpNet.IsOnline)
            {
                return;
            }

            var run = GameMaster.Instance?.CurrentGameRun;
            if (run == null)
            {
                _credited = null;
                return;
            }

            if (ReferenceEquals(run, _credited))
            {
                return;
            }

            _credited = run;

            if (run.IsAutoSeed)
            {
                return;
            }

            run.IsAutoSeed = true;
        }
    }
}
