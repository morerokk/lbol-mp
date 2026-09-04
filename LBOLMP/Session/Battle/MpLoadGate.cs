namespace LBOLMP.Session.Battle
{
    /// <summary>
    /// Yet another "wait for other players" gate, this is for handling loading screens.
    /// This waits at the very start of a battle for all players to load in.
    /// This fixes things like start of combat enemy debuffs (Weak/Vulnerable).
    /// </summary>
    internal static class MpLoadGate
    {
        private static bool _armed;

        /// <summary>Enables the waiting gate for the next battle.</summary>
        internal static void Arm(string why)
        {
            if (!_armed)
            {
                MpPlugin.Log.LogInfo($"The party will wait for each other at the next combat: {why}");
            }

            _armed = true;
        }

        /// <summary>
        /// Disables the waiting gate for the next battle.
        /// </summary>
        internal static void Disarm()
        {
            _armed = false;
        }

        internal static void Reset() => _armed = false;

        /// <summary>
        /// Returns true if the next/current combat should wait for all players.
        /// </summary>
        /// <remarks>
        /// This also automatically clears the flag.
        /// </remarks>
        internal static bool Consume()
        {
            bool armed = _armed;
            Disarm();
            return armed;
        }
    }
}
