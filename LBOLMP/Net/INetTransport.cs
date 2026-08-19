namespace LBOLMP.Net
{
    /// <summary>
    /// Determines how data is sent over the network.
    /// 
    /// Currently supported: direct TCP connections, Steam connections.
    /// </summary>
    public interface INetTransport
    {
        /// <summary>Short description of how this session is connected, for the lobby and the log.</summary>
        string Describe { get; }

        /// <summary>Reason the last start attempt failed, if it did.</summary>
        string LastError { get; }

        /// <summary>
        /// Called once per frame from the main thread. Accepts pending connection requests and handles putting incoming/outgoing data where it needs to be.
        /// TCP has separate threads for this, but because of how Steam networking works, for Steam this is responsible for putting data into its own buffer right away.
        /// This is to clear up Steam's own internal buffer ASAP, even (and especially) if the game lags. Very bad things happen if network messages are left hanging inside Steam's buffers.
        /// </summary>
        void Poll();

        void Shutdown(string reason);
    }
}
