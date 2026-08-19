using System.Collections.Generic;
using LBOLMP.Net;

namespace LBOLMP.Session.Messages
{
    /// <summary>A player clicked a reachable node on the map. One vote per player, so re-clicking replaces it.</summary>
    [NetMessage(20)]
    public sealed class MapVoteMessage : NetMessage
    {
        public int StageIndex;
        public int X;
        public int Y;

        /// <summary>
        /// The node the voter was standing on when they cast this.
        /// </summary>
        public int FromX = -1;
        public int FromY = -1;

        /// <summary>
        /// Which map decision this vote belongs to. Counts every commit the party has made.
        /// </summary>
        public int Decision;

        public override void Write(NetWriter w)
        {
            w.Int(StageIndex);
            w.Int(X);
            w.Int(Y);
            w.Int(FromX);
            w.Int(FromY);
            w.Int(Decision);
        }

        public override void Read(NetReader r)
        {
            StageIndex = r.Int();
            X = r.Int();
            Y = r.Int();
            FromX = r.Int();
            FromY = r.Int();
            Decision = r.Int();
        }
    }

    /// <summary>
    /// Host has made a decision once a node has been confirmed by everyone. Everyone enters this node, regardless of how they voted.
    /// </summary>
    [NetMessage(21)]
    public sealed class MapCommitMessage : NetMessage
    {
        public int StageIndex;
        public int X;
        public int Y;

        /// <summary>Adventure type name for a Friend node, or empty.</summary>
        public string AdventureType = string.Empty;

        public override void Write(NetWriter w)
        {
            w.Int(StageIndex);
            w.Int(X);
            w.Int(Y);
            w.String(AdventureType);
        }

        public override void Read(NetReader r)
        {
            StageIndex = r.Int();
            X = r.Int();
            Y = r.Int();
            AdventureType = r.String();
        }
    }

    /// <summary>Host's pick at the boss-select node. Everyone fights this boss.</summary>
    [NetMessage(25)]
    public sealed class BossChosenMessage : NetMessage
    {
        public int StageIndex;
        public string BossId;

        public override void Write(NetWriter w)
        {
            w.Int(StageIndex);
            w.String(BossId);
        }

        public override void Read(NetReader r)
        {
            StageIndex = r.Int();
            BossId = r.String();
        }
    }

    /// <summary>
    /// Generic rendezvous to make sure everyone is synced up at shops (oops, my bad). A player announces they have finished whatever the current phase is
    /// (event dialogue, card reward, shop, boss reward...) and the host releases everyone at once
    /// with <see cref="BarrierReleaseMessage"/>.
    /// </summary>
    [NetMessage(22)]
    public sealed class BarrierArriveMessage : NetMessage
    {
        public string BarrierId;

        public override void Write(NetWriter w) => w.String(BarrierId);
        public override void Read(NetReader r) => BarrierId = r.String();
    }

    [NetMessage(23)]
    public sealed class BarrierReleaseMessage : NetMessage
    {
        public string BarrierId;

        public override void Write(NetWriter w) => w.String(BarrierId);
        public override void Read(NetReader r) => BarrierId = r.String();
    }

    /// <summary>Host tells everyone to advance to the next act.</summary>
    [NetMessage(24)]
    public sealed class NextStageMessage : NetMessage
    {
        public int StageIndex;

        public override void Write(NetWriter w) => w.Int(StageIndex);
        public override void Read(NetReader r) => StageIndex = r.Int();
    }

    /// <summary>
    /// Somebody in the party is holding the Border Sensor, so the party is going to Act 4.
    /// </summary>
    [NetMessage(45)]
    public sealed class BorderSensorMessage : NetMessage
    {
        public override void Write(NetWriter w)
        {
        }

        public override void Read(NetReader r)
        {
        }
    }

    /// <summary>
    /// The host has restarted the level, so everybody restarts it.
    /// Has some rudimentary checks to prevent restarts if the party is out of sync.
    /// </summary>
    [NetMessage(46)]
    public sealed class StationRestartMessage : NetMessage
    {
        /// <summary>The host's save timing, as a <c>SaveTiming</c> ordinal.</summary>
        public int Timing;

        /// <summary>The act the host is restarting in, and the node it is standing on.</summary>
        public int StageIndex;
        public int X;
        public int Y;

        public override void Write(NetWriter w)
        {
            w.Int(Timing);
            w.Int(StageIndex);
            w.Int(X);
            w.Int(Y);
        }

        public override void Read(NetReader r)
        {
            Timing = r.Int();
            StageIndex = r.Int();
            X = r.Int();
            Y = r.Int();
        }
    }
}
