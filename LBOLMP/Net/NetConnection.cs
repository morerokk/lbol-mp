using System;
using System.Collections.Concurrent;
using System.Threading;

namespace LBOLMP.Net
{
    /// <summary>
    /// One connection to one peer.
    /// 
    /// This handles sending and receiving messages off the main thread, and in the right order,
    /// so that the main thread can simply send and receive messages whenever and however we want.
    /// </summary>
    public abstract class NetConnection
    {
        private int _closed;

        /// <summary>Frames received from the remote end, awaiting main-thread dispatch.</summary>
        public readonly ConcurrentQueue<byte[]> Inbox = new ConcurrentQueue<byte[]>();

        /// <summary>Set once when the link drops, so the main thread can report a reason.</summary>
        public string DisconnectReason { get; private set; }

        public bool IsClosed => Volatile.Read(ref _closed) != 0;

        /// <summary>Player id assigned to the peer on the far side. Only meaningful on the host.</summary>
        public int PlayerId { get; set; } = MpConstants.InvalidPlayerId;

        /// <summary>True once the peer has completed the join handshake.</summary>
        public bool HandshakeComplete { get; set; }

        /// <summary>Who is on the other end, for the log. An address, or a Steam name.</summary>
        public string RemoteEndPoint { get; protected set; } = "unknown";

        public void Send(byte[] payload)
        {
            if (IsClosed || payload == null)
            {
                return;
            }

            try
            {
                SendCore(payload);
            }
            catch (Exception e)
            {
                Close(L10n.Encode(MpText.ReasonSendFailed, e.Message));
            }
        }

        public void Close(string reason)
        {
            if (Interlocked.Exchange(ref _closed, 1) != 0)
            {
                return;
            }

            DisconnectReason = reason;

            try
            {
                CloseCore(reason);
            }
            catch (Exception)
            {
                // Already torn down, or torn down by the far end. Nothing left worth reporting.
            }
        }

        protected abstract void SendCore(byte[] payload);

        protected abstract void CloseCore(string reason);

        /// <summary>
        /// Called from the main thread each frame, for transports that have no reader thread of
        /// their own. TCP fills its inbox from a background thread and does nothing here.
        /// </summary>
        public virtual void Poll()
        {
        }
    }
}
