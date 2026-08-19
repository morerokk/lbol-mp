namespace LBOLMP
{
    public static class MpInfo
    {
        public const string Guid = "rokk.lbol.multiplayer.LBOLMP";
        public const string Name = "LBOL MP";
        /// <summary>
        /// Mod version for LBOL MP.
        /// </summary>
        public const string Version = "0.9.5";

        /// <summary>
        /// The current networking protocol version. Only clients with the same versions can join each other.
        /// This is an older remnant, but I'm sticking to it because r2modman does not guarantee updates,
        /// and failures of this type tend to be silent breakage rather than loud error messages.
        /// Simply bump up this version whenever an older version would no longer be able to play nicely with a newer version.
        /// </summary>
        public const int ProtocolVersion = 37;

        public const int MaxPlayers = 4;
    }
}
