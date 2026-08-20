using LBoL.Core;
using LBoLEntitySideloader.PersistentValues;

namespace LBOLMP.Session
{
    /// <summary>
    /// Holds custom save data specific to multiplayer runs.
    /// </summary>
    public sealed class MpRunSaveData : CustomGameRunSaveData
    {
        public override string Name => ReasonableUniqueName();

        /// <summary>
        /// The run's seed
        /// </summary>
        public ulong RunSeed;

        /// <summary>
        /// The character this player picked at the boss select node, which decides the shining exhibits they are offered.
        /// </summary>
        public string BossPick = string.Empty;

        /// <summary>Which act the boss was picked in, to avoid doing anything strange in later acts.</summary>
        public int BossPickStage = -1;

        public override void Save(GameRunController gameRun)
        {
            RunSeed = gameRun?.RootSeed ?? 0UL;
            BossPick = Patches.SetBossSyncPatch.LocalPick ?? string.Empty;
            BossPickStage = Patches.SetBossSyncPatch.LocalPickStage;
        }

        public override void Restore(GameRunController gameRun)
        {
            if (gameRun == null || RunSeed == 0UL || RunSeed != gameRun.RootSeed)
            {
                return;
            }

            Patches.SetBossSyncPatch.RestoreLocalPick(BossPick, BossPickStage);
        }
    }
}
