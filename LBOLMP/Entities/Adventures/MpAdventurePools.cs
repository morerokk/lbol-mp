using System.Linq;
using LBoLEntitySideloader.Entities;

namespace LBOLMP.Entities.Adventures
{
    /// <summary>
    /// Helper class to define the pools that an event is in, and at which weight.
    /// </summary>
    internal static class MpAdventurePools
    {
        /// <summary>Weight of a vanilla event.</summary>
        private const float NormalWeight = 1f;

        private static readonly int[] PerformanceLessonActs = { 2, 3 };

        internal static void Register()
        {
            StageTemplate.ModifyStageList(stages =>
            {
                foreach (var stage in stages)
                {
                    if (PerformanceLessonActs.Contains(stage.Level))
                    {
                        stage.AdventurePool.Add(typeof(MpPerformanceLesson), NormalWeight);
                    }
                }

                return stages;
            });
        }
    }
}
