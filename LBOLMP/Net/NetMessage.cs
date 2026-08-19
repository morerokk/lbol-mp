using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace LBOLMP.Net
{
    /// <summary>
    /// Marks a <see cref="NetMessage"/> subclass with its stable wire id.
    /// Ids must never be reused for a different message once shipped.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public sealed class NetMessageAttribute : Attribute
    {
        public NetMessageAttribute(ushort id)
        {
            Id = id;
        }

        public ushort Id { get; }

        /// <summary>
        /// When true, the host forwards this message to every other peer after handling it.
        /// Most gameplay messages want this; handshake traffic does not.
        /// </summary>
        public bool RelayedByHost { get; set; } = true;
    }

    public abstract class NetMessage
    {
        /// <summary>
        /// Player id of the originator. Stamped by the host on arrival so it cannot be spoofed by
        /// a client, and preserved through the relay so every peer agrees on who did what.
        /// </summary>
        public int SenderId { get; set; } = MpConstants.InvalidPlayerId;

        public abstract void Write(NetWriter w);
        public abstract void Read(NetReader r);

        public override string ToString() => $"{GetType().Name}(from {SenderId})";
    }

    public static class MpConstants
    {
        public const int InvalidPlayerId = -1;
        public const int HostPlayerId = 0;
        public const int BroadcastPlayerId = -2;

        /// <summary>
        /// <c>GameDifficulty.Normal</c> by default.
        /// </summary>
        public const int DefaultDifficulty = 1;

        /// <summary>Number of difficulties the game offers: Easy, Normal, Hard, Lunatic.</summary>
        public const int DifficultyCount = 4;

        /// <summary>
        /// Number of acts a run has.
        /// </summary>
        public const int ActCount = 4;
    }

    public static class MessageRegistry
    {
        private static readonly Dictionary<ushort, Type> IdToType = new Dictionary<ushort, Type>();
        private static readonly Dictionary<Type, NetMessageAttribute> TypeToInfo = new Dictionary<Type, NetMessageAttribute>();

        public static void RegisterAll(Assembly assembly)
        {
            foreach (var type in assembly.GetTypes())
            {
                if (type.IsAbstract || !typeof(NetMessage).IsAssignableFrom(type))
                {
                    continue;
                }

                var attribute = type.GetCustomAttribute<NetMessageAttribute>();
                if (attribute == null)
                {
                    throw new InvalidOperationException($"{type.FullName} derives from NetMessage but has no [NetMessage] attribute");
                }

                if (IdToType.TryGetValue(attribute.Id, out var existing))
                {
                    throw new InvalidOperationException($"Duplicate net message id {attribute.Id}: {existing.FullName} and {type.FullName}");
                }

                IdToType[attribute.Id] = type;
                TypeToInfo[type] = attribute;
            }

            MpPlugin.Log.LogInfo($"Registered {IdToType.Count} network message types");
        }

        public static ushort GetId(NetMessage message) => GetInfo(message.GetType()).Id;

        public static bool IsRelayed(NetMessage message) => GetInfo(message.GetType()).RelayedByHost;

        private static NetMessageAttribute GetInfo(Type type)
        {
            if (!TypeToInfo.TryGetValue(type, out var info))
            {
                throw new InvalidOperationException($"Message type {type.FullName} is not registered");
            }
            return info;
        }

        public static byte[] Serialize(NetMessage message)
        {
            var writer = new NetWriter();
            writer.UShort(GetId(message));
            writer.Int(message.SenderId);
            message.Write(writer);
            return writer.ToArray();
        }

        public static NetMessage Deserialize(byte[] payload)
        {
            var reader = new NetReader(payload);
            ushort id = reader.UShort();
            int senderId = reader.Int();

            if (!IdToType.TryGetValue(id, out var type))
            {
                throw new InvalidOperationException($"Unknown network message id {id}");
            }

            var message = (NetMessage)Activator.CreateInstance(type);
            message.SenderId = senderId;
            message.Read(reader);
            return message;
        }
    }
}
