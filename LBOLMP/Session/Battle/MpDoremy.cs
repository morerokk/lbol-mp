using System.Collections.Generic;
using LBoL.Core;
using LBoL.Core.Battle.BattleActions;
using LBoL.EntityLib.Exhibits.Common;
using LBoL.EntityLib.JadeBoxes;

namespace LBOLMP.Session.Battle
{
    /// <summary>
    /// Doremy is the only enemy in the game that sleeps.
    /// She wakes from any damage except the Black Notebook and one jade box, and the game checks for that. So we should check it here too.
    /// </summary>
    internal static class MpDoremy
    {
        private static readonly HashSet<DamageEventArgs> Undisturbed = new HashSet<DamageEventArgs>();

        internal static void Reset() => Undisturbed.Clear();

        internal static bool ShouldNotRemoveSleep(GameEntity actionSource) =>
            actionSource is HeiseBijiben || actionSource is QuickAct1;

        internal static void MarkUndisturbed(DamageAction damage)
        {
            if (damage == null)
            {
                return;
            }

            foreach (var args in damage.DamageArgs)
            {
                Undisturbed.Add(args);
            }
        }

        internal static bool IsUndisturbed(DamageEventArgs args) =>
            args != null && Undisturbed.Contains(args);
    }
}
