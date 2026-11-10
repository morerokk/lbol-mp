using LBOLMP.Net;

namespace LBOLMP.Session.Messages
{
    /// <summary>
    /// A typed chat line
    /// </summary>
    [NetMessage(75)]
    public sealed class ChatMessage : NetMessage
    {
        public string Text = string.Empty;

        /// <summary>Drawn as a thought bubble rather than a speech bubble.</summary>
        public bool Thought;

        public override void Write(NetWriter w)
        {
            w.String(Text);
            w.Bool(Thought);
        }

        public override void Read(NetReader r)
        {
            Text = r.String();
            Thought = r.Bool();
        }
    }
}
