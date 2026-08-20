using System.Collections.Generic;
using LBOLMP.Net;

namespace LBOLMP.Session.Messages
{
    /// <summary>
    /// Host starts a battle.
    /// </summary>
    [NetMessage(30)]
    public sealed class BattleStartMessage : NetMessage
    {
        public ulong BattleSeed;
        public string EnemyGroupId;
        public int PlayerCount;

        public override void Write(NetWriter w)
        {
            w.ULong(BattleSeed);
            w.String(EnemyGroupId);
            w.Int(PlayerCount);
        }

        public override void Read(NetReader r)
        {
            BattleSeed = r.ULong();
            EnemyGroupId = r.String();
            PlayerCount = r.Int();
        }
    }

    /// <summary>
    /// A player has finished their whole player phase for a round and is waiting before the enemy turn.
    /// Note: this gate is only reached if the player is fully done with their round, right when the enemy would attack instead.
    /// Extra turns (including End of Imperishable Night) do not trigger this message, to let players take their extra turns at the same time as other players' turns.
    /// (Slay the Spire 2 has the non-extra turn players spectate, which is really lame IMO)
    /// </summary>
    [NetMessage(31)]
    public sealed class TurnCompleteMessage : NetMessage
    {
        /// <summary>
        /// The fight this round belongs to, from <c>MpBattleSync.BattleSeed</c>.
        /// </summary>
        public ulong BattleSeed;

        public int Round;

        public override void Write(NetWriter w)
        {
            w.ULong(BattleSeed);
            w.Int(Round);
        }

        public override void Read(NetReader r)
        {
            BattleSeed = r.ULong();
            Round = r.Int();
        }
    }

    /// <summary>
    /// A player is about to hit a shared enemy.
    /// </summary>
    [NetMessage(32)]
    public sealed class EnemyDamageMessage : NetMessage
    {
        public int EnemyIndex;

        /// <summary>Damage after the attacker's buffs, before the target's defences.</summary>
        public float Amount;

        /// <summary>Matches <c>LBoL.Base.DamageType</c>.</summary>
        public int DamageType;

        public bool IsAccuracy;
        public string GunName;

        public override void Write(NetWriter w)
        {
            w.Int(EnemyIndex);
            w.Float(Amount);
            w.Int(DamageType);
            w.Bool(IsAccuracy);
            w.String(GunName);
        }

        public override void Read(NetReader r)
        {
            EnemyIndex = r.Int();
            Amount = r.Float();
            DamageType = r.Int();
            IsAccuracy = r.Bool();
            GunName = r.String();
        }
    }

    /// <summary>A player applied or removed a status effect on a shared enemy.</summary>
    [NetMessage(33)]
    public sealed class EnemyStatusMessage : NetMessage
    {
        public int EnemyIndex;
        public string StatusId;
        public int Level;
        public int Duration;
        public bool HasLevel;
        public bool HasDuration;
        public bool Removing;

        public override void Write(NetWriter w)
        {
            w.Int(EnemyIndex);
            w.String(StatusId);
            w.Int(Level);
            w.Int(Duration);
            w.Bool(HasLevel);
            w.Bool(HasDuration);
            w.Bool(Removing);
        }

        public override void Read(NetReader r)
        {
            EnemyIndex = r.Int();
            StatusId = r.String();
            Level = r.Int();
            Duration = r.Int();
            HasLevel = r.Bool();
            HasDuration = r.Bool();
            Removing = r.Bool();
        }
    }

    /// <summary>
    /// Purely cosmetic. Tells the other clients which card a player just played, to show a card popup.
    /// Note: this currently does not show the card's "pure" status or other cost reductions.
    /// </summary>
    [NetMessage(34, Unreliable = true)]
    public sealed class RemoteCardPlayMessage : NetMessage
    {
        public string CardId;
        public bool Upgraded;
        public int TargetEnemyIndex;

        public override void Write(NetWriter w)
        {
            w.String(CardId);
            w.Bool(Upgraded);
            w.Int(TargetEnemyIndex);
        }

        public override void Read(NetReader r)
        {
            CardId = r.String();
            Upgraded = r.Bool();
            TargetEnemyIndex = r.Int();
        }
    }

    /// <summary>
    /// A player reporting their current HP/block/barrier.
    /// </summary>
    [NetMessage(35, Unreliable = true)]
    public sealed class BattleStatusMessage : NetMessage
    {
        public int Hp;
        public int MaxHp;
        public int Block;
        public int Shield;
        public int HandCount;
        public int DrawCount;
        public int DiscardCount;

        /// <summary>Status effects, encoded as "Id:level:duration" with -1 meaning "not applicable".</summary>
        public List<string> StatusEffects = new List<string>();

        public override void Write(NetWriter w)
        {
            w.Int(Hp);
            w.Int(MaxHp);
            w.Int(Block);
            w.Int(Shield);
            w.Int(HandCount);
            w.Int(DrawCount);
            w.Int(DiscardCount);
            w.StringList(StatusEffects);
        }

        public override void Read(NetReader r)
        {
            Hp = r.Int();
            MaxHp = r.Int();
            Block = r.Int();
            Shield = r.Int();
            HandCount = r.Int();
            DrawCount = r.Int();
            DiscardCount = r.Int();
            StatusEffects = new List<string>(r.StringArray());
        }
    }

    /// <summary>
    /// How far through the fight a player is.
    /// Most wait-gates wait on these messages.
    ///
    /// Split away from <see cref="BattleStatusMessage"/> because that one is marked unreliable.
    /// "I ended my turn" or "I'm done with the combat" are comparatively a lot rarer and should be sent reliably.
    /// </summary>
    [NetMessage(51)]
    public sealed class BattleProgressMessage : NetMessage
    {
        /// <summary>The fight this describes, or zero for none. See <see cref="TurnCompleteMessage.BattleSeed"/>.</summary>
        public ulong BattleSeed;

        /// <summary>Last round this player finished their player phase for, or -1.</summary>
        public int CompletedRound = -1;

        /// <summary>
        /// Whether this player's combat is done.
        /// </summary>
        public bool Finished;

        /// <summary>Whether the player is still alive, for seats that are out of play.</summary>
        public bool Alive = true;

        public override void Write(NetWriter w)
        {
            w.ULong(BattleSeed);
            w.Int(CompletedRound);
            w.Bool(Finished);
            w.Bool(Alive);
        }

        public override void Read(NetReader r)
        {
            BattleSeed = r.ULong();
            CompletedRound = r.Int();
            Finished = r.Bool();
            Alive = r.Bool();
        }
    }

    /// <summary>A player's fight is over, one way or the other.</summary>
    [NetMessage(36)]
    public sealed class BattleFinishedMessage : NetMessage
    {
        /// <summary>The fight that ended. See <see cref="TurnCompleteMessage.BattleSeed"/>.</summary>
        public ulong BattleSeed;

        public bool Survived;

        public override void Write(NetWriter w)
        {
            w.ULong(BattleSeed);
            w.Bool(Survived);
        }

        public override void Read(NetReader r)
        {
            BattleSeed = r.ULong();
            Survived = r.Bool();
        }
    }

    /// <summary>
    /// A player has been knocked out and is now spectating.
    /// They keep their seat and their place in the run, they simply cannot act until the rest of the party wins the fight.
    ///
    /// This is sent rather than inferred from the HP mirror, primarily because players can heal or prevent their death in some cases.
    /// </summary>
    [NetMessage(37)]
    public sealed class PlayerDownMessage : NetMessage
    {
        public override void Write(NetWriter w) { }
        public override void Read(NetReader r) { }
    }

    /// <summary>
    /// The host's opinion of the enemy's current life/block/barrier. This is a quick and dirty fix for kedamas and other out-of-order effects,
    /// I don't foresee this being "fixed the proper way" without significant architectural changes (which would likely make the game feel laggier to play tbh).
    /// </summary>
    [NetMessage(39, Unreliable = true)]
    public sealed class EnemyVitalsMessage : NetMessage
    {
        /// <summary>
        /// Counts up once per broadcast. Unreliable frames can overtake each other, and this one
        /// assigns HP outright rather than adjusting it, so an older HP snapshot arriving after a newer
        /// one would adjust an enemy's health back up. The receiver keeps the highest it has seen.
        /// </summary>
        public int Sequence;

        /// <summary>Enemy index, current HP, block and shield, four ints per enemy.</summary>
        public List<int> Vitals = new List<int>();

        public override void Write(NetWriter w)
        {
            w.Int(Sequence);
            w.IntList(Vitals);
        }

        public override void Read(NetReader r)
        {
            Sequence = r.Int();
            Vitals = new List<int>(r.IntArray());
        }
    }

    /// <summary>
    /// What this player decided to do in the Yachie/Miyoi events.
    /// </summary>
    [NetMessage(40)]
    public sealed class EventBattleChoiceMessage : NetMessage
    {
        public bool Fighting;

        /// <summary>The enemy group the fighter is about to face, or empty for a decline.</summary>
        public string EnemyGroupId = string.Empty;

        public override void Write(NetWriter w)
        {
            w.Bool(Fighting);
            w.String(EnemyGroupId);
        }

        public override void Read(NetReader r)
        {
            Fighting = r.Bool();
            EnemyGroupId = r.String();
        }
    }

    /// <summary>
    /// Purely cosmetic, the animation a player's own character just started.
    /// </summary>
    [NetMessage(41, Unreliable = true)]
    public sealed class RemoteAnimationMessage : NetMessage
    {
        public string AnimationName = string.Empty;

        public override void Write(NetWriter w) => w.String(AnimationName);
        public override void Read(NetReader r) => AnimationName = r.String();
    }

    /// <summary>
    /// Purely cosmetic, a player has just been hit, and this is how it landed on them.
    /// Used to play the right animations for blocking/grazing.
    /// </summary>
    [NetMessage(42, Unreliable = true)]
    public sealed class RemoteHitMessage : NetMessage
    {
        public float Damage;
        public float DamageBlocked;
        public float DamageShielded;
        public bool IsGrazed;
        public bool IsAccuracy;

        /// <summary>Matches <c>LBoL.Base.DamageType</c>.</summary>
        public int DamageType;

        public override void Write(NetWriter w)
        {
            w.Float(Damage);
            w.Float(DamageBlocked);
            w.Float(DamageShielded);
            w.Bool(IsGrazed);
            w.Bool(IsAccuracy);
            w.Int(DamageType);
        }

        public override void Read(NetReader r)
        {
            Damage = r.Float();
            DamageBlocked = r.Float();
            DamageShielded = r.Float();
            IsGrazed = r.Bool();
            IsAccuracy = r.Bool();
            DamageType = r.Int();
        }
    }

    /// <summary>
    /// Emote, playing an animation and popping up a speech bubble
    /// </summary>
    [NetMessage(43, Unreliable = true)]
    public sealed class RemoteEmoteMessage : NetMessage
    {
        public int Emote;

        public override void Write(NetWriter w) => w.Int(Emote);
        public override void Read(NetReader r) => Emote = r.Int();
    }

    /// <summary>
    /// One player's ability card just triggered Sanae's "I'm Really Curious!".
    /// </summary>
    [NetMessage(44)]
    public sealed class CuriosityFirepowerMessage : NetMessage
    {
        public int EnemyIndex;
        public int Firepower;

        public override void Write(NetWriter w)
        {
            w.Int(EnemyIndex);
            w.Int(Firepower);
        }

        public override void Read(NetReader r)
        {
            EnemyIndex = r.Int();
            Firepower = r.Int();
        }
    }

    /// <summary>
    /// How much philosopher's mana the sender has gained so far this fight, for Junko's Overflowing Blemishes. See MpJunko.
    /// </summary>
    [NetMessage(50)]
    public sealed class JunkoImpurityMessage : NetMessage
    {
        public int Philosophy;

        public override void Write(NetWriter w)
        {
            w.Int(Philosophy);
        }

        public override void Read(NetReader r)
        {
            Philosophy = r.Int();
        }
    }

    /// <summary>A downed player has been revived at the end of combat.</summary>
    [NetMessage(38)]
    public sealed class PlayerRevivedMessage : NetMessage
    {
        public int Hp;

        public override void Write(NetWriter w) => w.Int(Hp);
        public override void Read(NetReader r) => Hp = r.Int();
    }
}
