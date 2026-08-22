using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using LBOLMP.Session;
using LBOLMP.Session.Battle;
using LBoL.Core.Battle;
using LBoL.Core.Battle.BattleActions;
using LBoL.Presentation;
using LBoLEntitySideloader.Entities;

namespace LBOLMP.Net
{
    /// <summary>Who a multiplayer card or status effect is aimed at.</summary>
    public enum MpEffectTarget
    {
        /// <summary>One chosen partner, picked with the targeting arrow.</summary>
        Partner,

        /// <summary>Everyone except the caster.</summary>
        AllPartners,

        /// <summary>Everyone, the caster included.</summary>
        Everyone
    }

    /// <summary>
    /// The body of a multiplayer effect. Declare public fields and they are serialized for you.
    /// Override Write/Read only if you want the bytes yourself.
    /// </summary>
    public abstract class MpEffectPayload
    {
        public virtual void Write(NetWriter w) => MpPayloadSerializer.Write(this, w);

        public virtual void Read(NetReader r) => MpPayloadSerializer.Read(this, r);
    }

    /// <summary>
    /// The one reserved id every modded card and status effect travels on.
    /// Routed by <see cref="Key"/>, so nobody has to allocate a message id of their own, and
    /// adding cards never changes the protocol.
    /// </summary>
    [NetMessage(60)]
    public sealed class MpEffectMessage : NetMessage
    {
        /// <summary>The fight this belongs to. See <c>MpBattleSync.BattleSeed</c>.</summary>
        public ulong BattleSeed;

        /// <summary>Only meaningful for <see cref="MpEffectTarget.Partner"/>.</summary>
        public int TargetPlayerId = MpConstants.BroadcastPlayerId;

        public MpEffectTarget Target;

        /// <summary>Namespaced handler key, e.g. "LBOLMP.MpDonateBlock".</summary>
        public string Key = string.Empty;

        /// <summary>Opaque to us. Only the handler for <see cref="Key"/> knows what is in here.</summary>
        public byte[] Payload = Array.Empty<byte>();

        public override void Write(NetWriter w)
        {
            w.ULong(BattleSeed);
            w.Int(TargetPlayerId);
            w.Byte((byte)Target);
            w.String(Key);
            w.Bytes(Payload);
        }

        public override void Read(NetReader r)
        {
            BattleSeed = r.ULong();
            TargetPlayerId = r.Int();
            Target = (MpEffectTarget)r.Byte();
            Key = r.String();
            Payload = r.Bytes();
        }

        public override string ToString() => $"MpEffect({Key} from {SenderId})";
    }

    /// <summary>What the registry stores. The template base classes implement this for you.</summary>
    public interface IMpEffect
    {
        /// <summary>Namespaced and stable across versions. Two mods must never pick the same one.</summary>
        string Key { get; }

        MpEffectPayload NewPayload();

        /// <summary>
        /// What this does on the receiving player's client. Return actions, do not perform them:
        /// the receiver may be mid-action, mid-animation, or parked at a gate.
        /// </summary>
        IEnumerable<BattleAction> Receive(MpEffectPayload payload, BattleController battle, int senderId);
    }

    /// <summary>
    /// Routes multiplayer card and status effects between clients.
    ///
    /// Everything with exactly one correct answer lives here rather than in the effects themselves:
    /// the fight id check, the "is this for me" check, whether we are still in the fight, and the
    /// action queueing. An effect only decides what to send and what to build on arrival.
    /// </summary>
    public static class MpEffects
    {
        private static readonly Dictionary<string, IMpEffect> ByKey =
            new Dictionary<string, IMpEffect>(StringComparer.Ordinal);

        /// <summary>Entity id to key, so a card or status can find its own handler at runtime.</summary>
        private static readonly Dictionary<string, string> ByEntityId =
            new Dictionary<string, string>(StringComparer.Ordinal);

        /// <summary>
        /// Ceiling on effects one client resolves in a single round. Nothing should ever come close.
        /// This only exists so a card combination nobody anticipated degrades into a log line
        /// instead of a hang.
        /// </summary>
        private const int PerRoundBudget = 64;

        private static int _budgetRound = -1;
        private static int _budgetUsed;

        public static void Register(IMpEffect effect, string entityId)
        {
            if (effect == null || string.IsNullOrEmpty(entityId))
            {
                return;
            }

            if (ByKey.TryGetValue(effect.Key, out var existing))
            {
                if (ReferenceEquals(existing, effect))
                {
                    return;
                }

                throw new InvalidOperationException(
                    $"Duplicate MP effect key '{effect.Key}': {existing.GetType().FullName} and {effect.GetType().FullName}");
            }

            ByKey[effect.Key] = effect;
            ByEntityId[entityId] = effect.Key;
            MpPlugin.Log.LogInfo($"Registered MP effect '{effect.Key}'");
        }

        /// <summary>
        /// Find and register every MP card and status effect in an assembly. Other mods call this
        /// with their own assembly once, at plugin load, the same way <see cref="MessageRegistry"/>
        /// works.
        /// </summary>
        public static void RegisterAll(Assembly assembly)
        {
            foreach (var type in assembly.GetTypes())
            {
                if (type.IsAbstract || type.IsGenericTypeDefinition || !typeof(IMpEffect).IsAssignableFrom(type))
                {
                    continue;
                }

                var effect = (IMpEffect)Activator.CreateInstance(type);

                // UniqueId rather than GetId: that is what Card.Id and StatusEffect.Id read back
                // as at runtime, and Sideloader renames an entity if another mod already claimed
                // the plain id. The wire key stays on GetId so it does not move with local renames.
                var entityId = ((EntityDefinition)effect).UniqueId.ToString();

                MpPayloadSerializer.Validate(effect.NewPayload().GetType());
                Register(effect, entityId);
            }
        }

        public static void RegisterHandlers() => MpNet.On<MpEffectMessage>(OnEffect);

        /// <summary>
        /// Whether a multiplayer effect can be sent at all right now. Cards check this so they stay
        /// unplayable in single player rather than fizzling.
        /// </summary>
        public static bool CanSend =>
            MpSession.IsActive && MpBattleSync.InBattle && !MpBattleSync.SpectatingOnly;

        /// <summary>Every partner who could still do something with an effect.</summary>
        public static IEnumerable<MpBattleSeat> ValidPartners =>
            MpBattleSync.AllSeats.Where(s =>
                s.PlayerId != MpNet.LocalPlayerId
                && !s.IsOutOfPlay
                && !MpBattleSync.IsUnresponsive(s));

        /// <summary>
        /// Publish an effect. The fight id is stamped here, so a straggler from an earlier battle
        /// can never apply to the current one.
        /// </summary>
        public static void Send(string entityId, MpEffectPayload payload, MpEffectTarget target,
                                int targetPlayerId = MpConstants.BroadcastPlayerId)
        {
            if (!CanSend || payload == null)
            {
                return;
            }

            if (!ByEntityId.TryGetValue(entityId ?? string.Empty, out var key))
            {
                MpPlugin.Log.LogWarning($"'{entityId}' tried to send an MP effect but is not registered as one");
                return;
            }

            if (target == MpEffectTarget.Partner && targetPlayerId == MpConstants.InvalidPlayerId)
            {
                return;
            }

            var writer = new NetWriter();
            payload.Write(writer);

            MpNet.Send(new MpEffectMessage
            {
                BattleSeed = MpBattleSync.BattleSeed,
                Target = target,
                TargetPlayerId = target == MpEffectTarget.Partner
                    ? targetPlayerId
                    : MpConstants.BroadcastPlayerId,
                Key = key,
                Payload = writer.ToArray()
            });
        }

        private static void OnEffect(MpEffectMessage message)
        {
            if (!IsForUs(message))
            {
                return;
            }

            // A message about a fight we are no longer in must never land in this one.
            if (message.BattleSeed == 0 || message.BattleSeed != MpBattleSync.BattleSeed)
            {
                return;
            }

            if (!MpBattleSync.InBattle || MpBattleSync.SpectatingOnly)
            {
                return;
            }

            var battle = GameMaster.Instance?.CurrentGameRun?.Battle;
            if (battle == null)
            {
                return;
            }

            var seat = MpBattleSync.GetSeat(MpNet.LocalPlayerId);
            if (seat != null && seat.IsOutOfPlay)
            {
                return;
            }

            if (!ByKey.TryGetValue(message.Key, out var handler))
            {
                MpPlugin.Log.LogWarning(
                    $"No handler for MP effect '{message.Key}'; is somebody running a mod we do not have?");
                return;
            }

            if (!TakeBudget(battle, message.Key))
            {
                return;
            }

            MpSafe.Run("MpEffect:" + message.Key, () =>
            {
                var payload = handler.NewPayload();
                payload.Read(new NetReader(message.Payload));

                var actions = handler.Receive(payload, battle, message.SenderId);
                if (actions != null)
                {
                    MpBattleSync.QueueReplicated(battle, actions, "MP effect " + message.Key);
                }
            });
        }

        private static bool IsForUs(MpEffectMessage message)
        {
            switch (message.Target)
            {
                case MpEffectTarget.Partner:
                    return message.TargetPlayerId == MpNet.LocalPlayerId;

                // The caster already ran their own copy locally, so only the rest apply it.
                case MpEffectTarget.AllPartners:
                    return message.SenderId != MpNet.LocalPlayerId;

                // Sender included, so everybody resolves it in the order the host relayed it.
                case MpEffectTarget.Everyone:
                    return true;

                default:
                    return false;
            }
        }

        private static bool TakeBudget(BattleController battle, string key)
        {
            int round = battle.RoundCounter;
            if (round != _budgetRound)
            {
                _budgetRound = round;
                _budgetUsed = 0;
            }

            if (_budgetUsed >= PerRoundBudget)
            {
                // Once per round rather than once per message, or a runaway would flood the log too.
                if (_budgetUsed == PerRoundBudget)
                {
                    _budgetUsed++;
                    MpPlugin.Log.LogError(
                        $"Refusing more than {PerRoundBudget} MP effects this round (last was '{key}'). " +
                        "Something is looping; please report the card combination.");
                }
                return false;
            }

            _budgetUsed++;
            return true;
        }

        internal static void Reset()
        {
            _budgetRound = -1;
            _budgetUsed = 0;
        }
    }
}
