using System;

namespace LBOLMP
{
    /// <summary>
    /// Dirty hack to ensure that any multiplayer hiccups don't cause problems in the game.
    /// If something goes wrong in the code that's being run, the game has a nasty tendency to lock up actions or cards/mana, and possibly softlock the game.
    /// So here, we just ignore it and assume that things will go right later (they usually do).
    /// Think of things like playing an attack card on an enemy that's already dead from the other person's perspective, and it's too late to cancel the card use.
    /// This would freeze the card on-screen and freeze the floating mana, which is ass.
    /// </summary>
    public static class MpSafe
    {
        public static void Run(string what, Action action)
        {
            try
            {
                action();
            }
            catch (Exception e)
            {
                MpPlugin.Log.LogError($"[{what}] caught exception: {e}");
            }
        }

        public static T Run<T>(string what, Func<T> action, T fallback)
        {
            try
            {
                return action();
            }
            catch (Exception e)
            {
                MpPlugin.Log.LogError($"[{what}] caught exception: {e}");
                return fallback;
            }
        }
    }
}
