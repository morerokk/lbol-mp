using System.Collections.Generic;
using LBOLMP.Net;

namespace LBOLMP.Session.Messages
{
    //--
    // Handshake traffic ahead, should only be replicated to a single peer and not across everyone.
    //--

    [NetMessage(1, RelayedByHost = false)]
    public sealed class JoinRequestMessage : NetMessage
    {
        public int ProtocolVersion;
        public string PlayerName;

        public override void Write(NetWriter w)
        {
            w.Int(ProtocolVersion);
            w.String(PlayerName);
        }

        public override void Read(NetReader r)
        {
            ProtocolVersion = r.Int();
            PlayerName = r.String();
        }
    }

    [NetMessage(2, RelayedByHost = false)]
    public sealed class JoinAcceptedMessage : NetMessage
    {
        public int AssignedPlayerId;

        public override void Write(NetWriter w) => w.Int(AssignedPlayerId);
        public override void Read(NetReader r) => AssignedPlayerId = r.Int();
    }

    [NetMessage(3, RelayedByHost = false)]
    public sealed class JoinRejectedMessage : NetMessage
    {
        public string Reason;

        public override void Write(NetWriter w) => w.String(Reason);
        public override void Read(NetReader r) => Reason = r.String();
    }

    /// <summary>
    /// Host-authored snapshot of the whole lobby
    /// </summary>
    [NetMessage(4, RelayedByHost = false)]
    public sealed class PlayerListMessage : NetMessage
    {
        public List<MpPlayer> Players = new List<MpPlayer>();

        public override void Write(NetWriter w)
        {
            w.Int(Players.Count);
            foreach (var player in Players)
            {
                w.Int(player.Id);
                w.String(player.Name);
                w.Byte((byte)player.State);
                w.String(player.CharacterId);
                w.Int(player.PlayerTypeIndex);
                w.String(player.InitExhibitId);
                w.StringList(player.StartingDeck);
                w.Int(player.Hp);
                w.Int(player.MaxHp);
                w.Int(player.Money);
                w.Int(player.Power);
            }
        }

        public override void Read(NetReader r)
        {
            Players = new List<MpPlayer>();
            int count = r.Int();
            for (int i = 0; i < count; i++)
            {
                Players.Add(new MpPlayer
                {
                    Id = r.Int(),
                    Name = r.String(),
                    State = (MpPlayerState)r.Byte(),
                    CharacterId = r.String(),
                    PlayerTypeIndex = r.Int(),
                    InitExhibitId = r.String(),
                    StartingDeck = new List<string>(r.StringArray()),
                    Hp = r.Int(),
                    MaxHp = r.Int(),
                    Money = r.Int(),
                    Power = r.Int()
                });
            }
        }
    }

    //--
    // Run setup
    //--

    /// <summary>
    /// What the sender's mods have added to the game, one line per mod. See <c>MpModContent</c>.
    /// </summary>
    [NetMessage(59)]
    public sealed class ModContentMessage : NetMessage
    {
        /// <summary>"guid|characters|cards|exhibits|enemies|adventures".</summary>
        public List<string> Mods = new List<string>();

        public override void Write(NetWriter w) => w.StringList(Mods);

        public override void Read(NetReader r) => Mods = new List<string>(r.StringArray());
    }

    /// <summary>
    /// A player has locked in their character and library on the Start Game screen and is now waiting for the rest of the lobby.
    /// </summary>
    [NetMessage(10)]
    public sealed class PlayerReadyMessage : NetMessage
    {
        public string CharacterId;
        public int PlayerTypeIndex;
        public string InitExhibitId;
        public List<string> Deck = new List<string>();
        public int Difficulty;

        /// <summary>Jade box ids ticked on this player's own panel. Only the host's are used.</summary>
        public List<string> JadeBoxes = new List<string>();

        public override void Write(NetWriter w)
        {
            w.String(CharacterId);
            w.Int(PlayerTypeIndex);
            w.String(InitExhibitId);
            w.StringList(Deck);
            w.Int(Difficulty);
            w.StringList(JadeBoxes);
        }

        public override void Read(NetReader r)
        {
            CharacterId = r.String();
            PlayerTypeIndex = r.Int();
            InitExhibitId = r.String();
            Deck = new List<string>(r.StringArray());
            Difficulty = r.Int();
            JadeBoxes = new List<string>(r.StringArray());
        }
    }

    /// <summary>
    /// Host tells everyone to begin.
    /// </summary>
    [NetMessage(11)]
    public sealed class RunStartMessage : NetMessage
    {
        public ulong Seed;
        public int Difficulty;

        /// The host's balance settings.
        public float EnemyHpScalePerExtraPlayer;
        public float[] EnemyHpEscalationByAct = new float[MpConstants.ActCount];
        public float ReviveHpFraction = 0.2f;
        public bool EnemyResilience = true;
        public bool MultiplayerCards = true;

        /// The jade boxes the whole party starts with, ticked on the host's panel.
        public List<string> JadeBoxes = new List<string>();

        public override void Write(NetWriter w)
        {
            w.ULong(Seed);
            w.Int(Difficulty);
            w.Float(EnemyHpScalePerExtraPlayer);
            for (int i = 0; i < MpConstants.ActCount; i++)
            {
                w.Float(EnemyHpEscalationByAct[i]);
            }
            w.Float(ReviveHpFraction);
            w.Bool(EnemyResilience);
            w.Bool(MultiplayerCards);
            w.StringList(JadeBoxes);
        }

        public override void Read(NetReader r)
        {
            Seed = r.ULong();
            Difficulty = r.Int();
            EnemyHpScalePerExtraPlayer = r.Float();
            EnemyHpEscalationByAct = new float[MpConstants.ActCount];
            for (int i = 0; i < MpConstants.ActCount; i++)
            {
                EnemyHpEscalationByAct[i] = r.Float();
            }
            ReviveHpFraction = r.Float();
            EnemyResilience = r.Bool();
            MultiplayerCards = r.Bool();
            JadeBoxes = new List<string>(r.StringArray());
        }
    }

    /// <summary>
    /// A player has pressed Continue on a saved run and is waiting for the rest of the party.
    /// </summary>
    [NetMessage(15)]
    public sealed class ResumeReadyMessage : NetMessage
    {
        /// <summary>
        /// The save's <c>RootSeed</c>, to avoid players accidentally trying to join the group with their own singleplayer run still in progress.
        /// </summary>
        public ulong Seed;

        /// <summary>
        /// Where this save would put the player, so you can tell that they're still at the last act's boss rewards and not directly on the map yet.
        /// </summary>
        public int StageIndex;
        public int X;
        public int Y;

        /// <summary>The difficulty the saved run is being played on, for the log and the lobby.</summary>
        public int Difficulty;

        /// <summary>Character in the save, so the lobby can show who is playing what.</summary>
        public string CharacterId;

        public override void Write(NetWriter w)
        {
            w.ULong(Seed);
            w.Int(StageIndex);
            w.Int(X);
            w.Int(Y);
            w.Int(Difficulty);
            w.String(CharacterId);
        }

        public override void Read(NetReader r)
        {
            Seed = r.ULong();
            StageIndex = r.Int();
            X = r.Int();
            Y = r.Int();
            Difficulty = r.Int();
            CharacterId = r.String();
        }
    }

    /// <summary>
    /// Host tells everyone to load their save, having checked that they are all the same run.
    /// </summary>
    [NetMessage(16)]
    public sealed class RunResumeMessage : NetMessage
    {
        public ulong Seed;
        public int Difficulty;
        public float EnemyHpScalePerExtraPlayer;
        public float[] EnemyHpEscalationByAct = new float[MpConstants.ActCount];
        public float ReviveHpFraction = 0.2f;
        public bool EnemyResilience = true;
        public bool MultiplayerCards = true;

        // Message that says "hey someone's a bit further behind"
        public string Note;

        public override void Write(NetWriter w)
        {
            w.ULong(Seed);
            w.Int(Difficulty);
            w.Float(EnemyHpScalePerExtraPlayer);
            for (int i = 0; i < MpConstants.ActCount; i++)
            {
                w.Float(EnemyHpEscalationByAct[i]);
            }
            w.Float(ReviveHpFraction);
            w.Bool(EnemyResilience);
            w.Bool(MultiplayerCards);
            w.String(Note);
        }

        public override void Read(NetReader r)
        {
            Seed = r.ULong();
            Difficulty = r.Int();
            EnemyHpScalePerExtraPlayer = r.Float();
            EnemyHpEscalationByAct = new float[MpConstants.ActCount];
            for (int i = 0; i < MpConstants.ActCount; i++)
            {
                EnemyHpEscalationByAct[i] = r.Float();
            }
            ReviveHpFraction = r.Float();
            EnemyResilience = r.Bool();
            MultiplayerCards = r.Bool();
            Note = r.String();
        }
    }

    /// <summary>
    /// A player has saved or finished and gone back to the main menu.
    /// </summary>
    [NetMessage(17)]
    public sealed class BackToLobbyMessage : NetMessage
    {
        public override void Write(NetWriter w) { }
        public override void Read(NetReader r) { }
    }

    /// <summary>
    /// The host will not begin the run the party asked for, and this message explains why.
    /// </summary>
    [NetMessage(18)]
    public sealed class RunStartCancelledMessage : NetMessage
    {
        public string Reason;

        public override void Write(NetWriter w) => w.String(Reason);
        public override void Read(NetReader r) => Reason = r.String();
    }

    /// <summary>
    /// The flags the host's copy of the run is carrying, for everyone else to adopt.
    /// This is because the game secretly has a few scripted encounters for first-time players, and we want that to be synced.
    /// </summary>
    [NetMessage(19)]
    public sealed class RunFlagsMessage : NetMessage
    {
        public ulong Seed;

        public List<string> RunFlags = new List<string>();
        public List<List<string>> StageFlags = new List<List<string>>();

        public override void Write(NetWriter w)
        {
            w.ULong(Seed);
            w.StringList(RunFlags);

            w.Int(StageFlags.Count);
            foreach (var stage in StageFlags)
            {
                w.StringList(stage);
            }
        }

        public override void Read(NetReader r)
        {
            Seed = r.ULong();
            RunFlags = new List<string>(r.StringArray());

            int stages = r.Int();
            StageFlags = new List<List<string>>(stages);
            for (int i = 0; i < stages; i++)
            {
                StageFlags.Add(new List<string>(r.StringArray()));
            }
        }
    }

    /// <summary>
    /// The host's difficulty, as it stands right now on the Start Game screen.
    ///
    /// Sent every time the host moves the selection, and once more to each client as it joins, so a
    /// client that arrives late is not left showing whatever it picked last time.
    /// </summary>
    [NetMessage(14)]
    public sealed class LobbyDifficultyMessage : NetMessage
    {
        public int Difficulty;

        public override void Write(NetWriter w) => w.Int(Difficulty);
        public override void Read(NetReader r) => Difficulty = r.Int();
    }

    /// <summary>
    /// The jade boxes the host has ticked right now on the Start Game screen.
    ///
    /// Sent whenever the host's panel refreshes, and once more to each client as it joins, so that
    /// everybody's panel shows the same list before anyone presses Confirm.
    /// </summary>
    [NetMessage(5)]
    public sealed class LobbyJadeBoxMessage : NetMessage
    {
        public List<string> JadeBoxes = new List<string>();

        public override void Write(NetWriter w) => w.StringList(JadeBoxes);
        public override void Read(NetReader r) => JadeBoxes = new List<string>(r.StringArray());
    }

    /// <summary>Periodic mirror of a player's out-of-combat vitals, for the HUD.</summary>
    [NetMessage(12)]
    public sealed class PlayerStatusMessage : NetMessage
    {
        public int Hp;
        public int MaxHp;
        public int Money;
        public int Power;

        public override void Write(NetWriter w)
        {
            w.Int(Hp);
            w.Int(MaxHp);
            w.Int(Money);
            w.Int(Power);
        }

        public override void Read(NetReader r)
        {
            Hp = r.Int();
            MaxHp = r.Int();
            Money = r.Int();
            Power = r.Int();
        }
    }

    /// <summary>
    /// How much money one player just gained or lost, for the Share the Wealth jade box.
    /// </summary>
    [NetMessage(26)]
    public sealed class SharedMoneyMessage : NetMessage
    {
        public int Delta;

        public override void Write(NetWriter w) => w.Int(Delta);
        public override void Read(NetReader r) => Delta = r.Int();
    }

    /// <summary>Sent when a player leaves the run for any reason, so the rest can carry on (or choose to return to menu).</summary>
    [NetMessage(13)]
    public sealed class PlayerLeftMessage : NetMessage
    {
        public int PlayerId;
        public string Reason;

        public override void Write(NetWriter w)
        {
            w.Int(PlayerId);
            w.String(Reason);
        }

        public override void Read(NetReader r)
        {
            PlayerId = r.Int();
            Reason = r.String();
        }
    }
}
