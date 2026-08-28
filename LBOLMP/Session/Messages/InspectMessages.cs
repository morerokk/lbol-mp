using System.Collections.Generic;
using LBOLMP.Net;
using LBoL.Base;

namespace LBOLMP.Session.Messages
{
    /// <summary>
    /// "I am looking at your hand", or with an invalid id, "I have stopped".
    /// Helps save on networking bandwidth by not sending hand states if no one is looking.
    /// </summary>
    [NetMessage(47)]
    public sealed class HandInspectMessage : NetMessage
    {
        /// <summary>Whose hand the sender is watching, or <c>MpConstants.InvalidPlayerId</c>.</summary>
        public int TargetPlayerId;

        public override void Write(NetWriter w) => w.Int(TargetPlayerId);
        public override void Read(NetReader r) => TargetPlayerId = r.Int();
    }

    /// <summary>
    /// Everything a looker needs to see one player's fight the way that player sees it.
    /// That is, their hand, their draw/discard/exile piles, and the mana they have to spend.
    ///
    /// Sent only while somebody is watching, and only when it has actually changed.
    /// Unlike syncing card plays, this one should also write cost reductions and the Pure keyword.
    /// </summary>
    [NetMessage(48)]
    public sealed class PlayerCardsMessage : NetMessage
    {
        /// <summary>What the sender has left to spend this turn.</summary>
        public ManaGroup Mana;

        /// <summary>
        /// Whether the sender's draw pile has to be shown shuffled. This prevents cheating with Kosuzu's "I can't believe it's not Frozen Eye" book exhibit.
        /// You can view their draw pile in order, but only if they have said book.
        /// You can't micromanage or be felt obligated to painstakingly call out everyone's draw piles this way, and I believe that to be for the better.
        /// </summary>
        public bool HideDrawOrder;

        /// <summary>Hand, draw, discard and exile in one list; each card carries its own zone.</summary>
        public List<MpCardState> Cards = new List<MpCardState>();

        public override void Write(NetWriter w)
        {
            MpCardMirror.WriteMana(w, Mana);
            w.Bool(HideDrawOrder);
            MpCardMirror.Write(w, Cards);
        }

        public override void Read(NetReader r)
        {
            Mana = MpCardMirror.ReadMana(r);
            HideDrawOrder = r.Bool();
            Cards = MpCardMirror.Read(r);
        }
    }

    /// <summary>
    /// The sender's deck/library, as the deck/library button shows it.
    /// </summary>
    [NetMessage(49)]
    public sealed class PlayerDeckMessage : NetMessage
    {
        public List<MpCardState> Cards = new List<MpCardState>();

        public override void Write(NetWriter w) => MpCardMirror.Write(w, Cards);
        public override void Read(NetReader r) => Cards = MpCardMirror.Read(r);
    }

    /// <summary>
    /// "Show me what you have exiled." Unlike <see cref="HandInspectMessage"/> this is not a
    /// subscription: it asks once and is answered once.
    /// </summary>
    [NetMessage(54)]
    public sealed class ExilePeekRequestMessage : NetMessage
    {
        /// <summary>Whose exile pile the sender is asking for.</summary>
        public int TargetPlayerId;

        public override void Write(NetWriter w) => w.Int(TargetPlayerId);
        public override void Read(NetReader r) => TargetPlayerId = r.Int();
    }

    /// <summary>
    /// The answer to one <see cref="ExilePeekRequestMessage"/>.
    /// </summary>
    [NetMessage(55)]
    public sealed class ExilePeekMessage : NetMessage
    {
        /// <summary>Who asked. Everyone else ignores it.</summary>
        public int TargetPlayerId;

        public List<MpCardState> Cards = new List<MpCardState>();

        public override void Write(NetWriter w)
        {
            w.Int(TargetPlayerId);
            MpCardMirror.Write(w, Cards);
        }

        public override void Read(NetReader r)
        {
            TargetPlayerId = r.Int();
            Cards = MpCardMirror.Read(r);
        }
    }
}
