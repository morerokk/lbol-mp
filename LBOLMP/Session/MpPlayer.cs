using System;
using System.Collections.Generic;

namespace LBOLMP.Session
{
    public enum MpPlayerState
    {
        /// <summary>Connected, sitting in the lobby, hasn't configured a run yet.</summary>
        Lobby,

        /// <summary>Has locked in a character/deck and is waiting for everyone else.</summary>
        Ready,

        /// <summary>The run has started for this player.</summary>
        InRun,

        /// <summary>Connection lost. Kept in the list so the UI can say who disconnected.</summary>
        Disconnected,

        /// <summary>
        /// Has pressed Continue on a saved run and is waiting for everyone else to do the same.
        /// </summary>
        Resuming
    }

    /// <summary>
    /// What one participant looks like to everybody else in the F2 menu.
    /// </summary>
    public sealed class MpPlayer
    {
        public int Id;
        public string Name = "Player";
        public MpPlayerState State = MpPlayerState.Lobby;

        /// <summary>Entity id of the chosen character, e.g. "Reimu" or "YoumuMod". Empty until they pick one.</summary>
        public string CharacterId = string.Empty;

        /// <summary>0 for type A, 1 for type B.</summary>
        public int PlayerTypeIndex;

        public string InitExhibitId = string.Empty;

        /// <summary>Starting library, as "CardId" or "CardId+" for an upgraded copy.</summary>
        public List<string> StartingDeck = new List<string>();

        public int Difficulty = Net.MpConstants.DefaultDifficulty;

        /// <summary>Jade boxes this player had ticked when they confirmed. Only the host's count.</summary>
        public List<string> JadeBoxes = new List<string>();

        // This is basically the run's identity, to figure out if we can load the run or not
        public ulong ResumeSeed;

        public int ResumeStage = -1;
        public int ResumeX = -1;
        public int ResumeY = -1;

        // Cheap status mirror, refreshed periodically so the HUD and lobby can show everyone
        public int Hp;
        public int MaxHp;
        public int Money;
        public int Power;

        /// <summary>
        /// True when their deck holds a Misfortune that Hina can take away.
        /// </summary>
        public bool HasRemovableMisfortune;

        /// <summary>What their spell card costs.</summary>
        public int MaxPower;

        public bool IsLocal => Id == Net.MpNet.LocalPlayerId;
        public bool IsHost => Id == Net.MpConstants.HostPlayerId;

        public override string ToString() => $"#{Id} {Name}";
    }
}
