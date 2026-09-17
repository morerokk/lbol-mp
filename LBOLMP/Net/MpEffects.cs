using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using LBOLMP.Entities;
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

    /// <summary>
    /// Put this on a card or status effect definition (any Sideloader <c>CardTemplate</c> or <c>StatusEffectTemplate</c>)
    /// to let it send <typeparamref name="TPayload"/> to other players, and receive it.
    /// </summary>
    /// <remarks>
    /// Send from the card or status effect itself with <c>MpEffects.Send(Id, payload, target)</c>.
    /// Receive runs on the other player's client.
    /// Call <c>MpEffects.RegisterAll(Assembly.GetExecutingAssembly())</c> once from your plugin's Awake.
    /// </remarks>
    public interface IMpEffect<TPayload> where TPayload : MpEffectPayload, new()
    {
        /// <summary>
        /// What this does on the receiving player's client.
        /// </summary>
        /// <remarks>
        /// Return the actions and let LBOL MP queue them. Never touch player state directly, because the receiving player may be mid-action when this runs.
        /// (For example, they might be choosing a card to discard. If you change their hand underneath them, the game does very bad things)
        /// </remarks>
        IEnumerable<BattleAction> Receive(TPayload payload, BattleController battle, int senderId);
    }

    /// <summary>
    /// Optional, next to <see cref="IMpEffect{TPayload}"/>: pin the network key instead of using "AssemblyName.Id".
    /// </summary>
    /// <remarks>
    /// Only needed to keep a key stable across a rename. Changing it breaks compatibility with older versions.
    /// </remarks>
    public interface IMpEffectKey
    {
        string Key { get; }
    }

    /// <summary>
    /// Avoid using this, prefer <see cref="IMpEffect{TPayload}"/>.
    /// </summary>
    public interface IMpEffect
    {
        /// <summary>Pick a unique key, to avoid mod conflicts.</summary>
        string Key { get; }

        MpEffectPayload NewPayload();

        /// <summary>
        /// Return the actions and let LBOL MP queue them. Never touch player state directly, because the receiving player may be mid-action when this runs.
        /// (For example, they might be choosing a card to discard. If you change their hand underneath them, the game does very bad things)
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
                if (type.IsAbstract || type.IsGenericTypeDefinition
                    || !typeof(EntityDefinition).IsAssignableFrom(type))
                {
                    continue;
                }

                var payloadType = PayloadTypeOf(type);
                bool untyped = typeof(IMpEffect).IsAssignableFrom(type);
                if (payloadType == null && !untyped)
                {
                    continue;
                }

                var definition = (EntityDefinition)Activator.CreateInstance(type);
                var effect = untyped
                    ? (IMpEffect)definition
                    : (IMpEffect)Activator.CreateInstance(
                        typeof(TypedEffect<>).MakeGenericType(payloadType), definition);

                // UniqueId rather than GetId: that is what Card.Id and StatusEffect.Id read back
                // as at runtime, and Sideloader renames an entity if another mod already claimed
                // the plain id. The wire key stays on GetId so it does not move with local renames.
                var entityId = definition.UniqueId.ToString();

                MpPayloadSerializer.Validate(effect.NewPayload().GetType());
                Register(effect, entityId);
            }
        }

        /// <summary>The payload type of a class's <see cref="IMpEffect{TPayload}"/>, or null.</summary>
        private static Type PayloadTypeOf(Type type)
        {
            var found = type.GetInterfaces()
                .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IMpEffect<>))
                .ToList();

            if (found.Count > 1)
            {
                throw new InvalidOperationException(
                    $"{type.FullName} implements IMpEffect<> more than once; one payload type per definition");
            }

            return found.Count == 1 ? found[0].GetGenericArguments()[0] : null;
        }

        /// <summary>The default key: "AssemblyName.Id".</summary>
        internal static string DefaultKey(EntityDefinition definition) =>
            definition.GetType().Assembly.GetName().Name + "." + definition.GetId();

        /// <summary>What the definition behind a registered effect is, for marker interfaces.</summary>
        private static object DefinitionOf(IMpEffect effect) =>
            effect is ITypedEffect typed ? typed.Definition : effect;

        private interface ITypedEffect
        {
            EntityDefinition Definition { get; }
        }

        /// <summary>Adapts an <see cref="IMpEffect{TPayload}"/> definition to what the registry stores.</summary>
        private sealed class TypedEffect<TPayload> : IMpEffect, ITypedEffect
            where TPayload : MpEffectPayload, new()
        {
            private readonly IMpEffect<TPayload> _effect;

            public TypedEffect(EntityDefinition definition)
            {
                Definition = definition;
                _effect = (IMpEffect<TPayload>)definition;
                Key = definition is IMpEffectKey custom && !string.IsNullOrEmpty(custom.Key)
                    ? custom.Key
                    : DefaultKey(definition);
            }

            public EntityDefinition Definition { get; }

            public string Key { get; }

            public MpEffectPayload NewPayload() => new TPayload();

            public IEnumerable<BattleAction> Receive(MpEffectPayload payload, BattleController battle, int senderId)
                => _effect.Receive((TPayload)payload, battle, senderId);
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

            if (!MpBattleSync.InBattle)
            {
                return;
            }

            var battle = GameMaster.Instance?.CurrentGameRun?.Battle;
            if (battle == null)
            {
                return;
            }

            if (!ByKey.TryGetValue(message.Key, out var handler))
            {
                MpPlugin.Log.LogWarning(
                    $"No handler for MP effect '{message.Key}'; is somebody running a mod we do not have?");
                return;
            }

            if (!Reachable(handler))
            {
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

        /// <summary>
        /// Whether an effect should be processed on the local player at all.
        /// This may be false if the player is downed.
        /// </summary>
        private static bool Reachable(IMpEffect handler)
        {
            var seat = MpBattleSync.GetSeat(MpNet.LocalPlayerId);

            bool notOurFight = MpEventBattle.LocalSpectating
                               || (seat != null && (!seat.Alive || seat.Finished || seat.Spectating));

            if (notOurFight)
            {
                return false;
            }

            bool knockedOut = MpDownedPlayers.LocalDown || (seat != null && seat.Down);

            return !knockedOut || DefinitionOf(handler) is IMpReachesDownedPlayers;
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
                if (_budgetUsed == PerRoundBudget)
                {
                    _budgetUsed++;
                    // Does the game have an action limit?
                    // If it does, then it's not working with the way we're processing networked actions
                    MpPlugin.Log.LogError(
                        $"Refusing more than {PerRoundBudget} MP effects this round (last was '{key}'). " +
                        "Something is looping! Please report the card combination.");
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
