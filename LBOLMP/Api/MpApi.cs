using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using LBOLMP.Net;
using LBOLMP.Session;
using UnityEngine;

namespace LBOLMP.Api
{
    /// <summary>
    /// A player in the current session.
    /// </summary>
    public readonly struct MpApiPlayer
    {
        public MpApiPlayer(int id, string name, bool isLocal)
        {
            Id = id;
            Name = name;
            IsLocal = isLocal;
        }

        /// <summary>Player ID during this session. The host is always ID 0.</summary>
        public int Id { get; }

        /// <summary>
        /// The player's set name.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// True if this is the current player on this machine.
        /// </summary>
        public bool IsLocal { get; }

        public bool IsHost => Id == 0;
    }

    /// <summary>
    /// Lets other mods send their own data through LBOL MP's networking.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Messages are identified by a string key.
    /// Prefix it with your mod's name so it can't collide with another mod (such as <c>"MyMod.NotifyFooBarred"</c>).
    /// </para>
    /// <para>
    /// Payloads are sent as JSON. This only works with primitives, strings, enums, arrays, <see cref="List{T}"/>, and <c>[Serializable]</c> classes, as well as structs with public fields.
    /// It does NOT handle properties, dictionaries, or polymorphism. Keep it simple.
    /// </para>
    /// <para>
    /// Everything here must be called from the main thread. Handlers also run on the main thread.
    /// </para>
    /// </remarks>
    public static class MpApi
    {
        /// <summary>LBOL MP's BepInEx GUID, usable for <c>[BepInDependency]</c>.</summary>
        public const string PluginGuid = MpInfo.Guid;

        /// <summary>The host's player id.</summary>
        public const int HostId = MpConstants.HostPlayerId;

        /// <summary>True while connected to a session (even if no one has joined yet).</summary>
        public static bool IsOnline => MpNet.IsOnline;

        /// <summary>True when connected, and at least one other player is present.</summary>
        public static bool IsMultiplayer => MpSession.IsActive;

        /// <summary>True when this machine is the host of the session.</summary>
        public static bool IsHost => MpNet.IsOnline && MpNet.IsHost;

        /// <summary>This machine's player id, or -1 when offline.</summary>
        public static int LocalPlayerId => MpNet.IsOnline ? MpNet.LocalPlayerId : MpConstants.InvalidPlayerId;

        /// <summary>Everyone currently connected, this machine included, ordered by id.</summary>
        public static IReadOnlyList<MpApiPlayer> Players => MpSession.ConnectedPlayers
            .Select(p => new MpApiPlayer(p.Id, p.Name, p.IsLocal))
            .ToList();

        /// <summary>
        /// Send <paramref name="payload"/> to every other player.
        /// </summary>
        /// <param name="includeSelf">If true, also runs the handler locally, right away (even if offline).</param>
        /// <returns>False if it could not be sent (offline, or the payload failed to serialize).</returns>
        public static bool Send<T>(string key, T payload, bool includeSelf = false) =>
            SendCore(key, payload, MpConstants.BroadcastPlayerId, includeSelf);

        /// <summary>
        /// Send <paramref name="payload"/> to one player. Sending to yourself runs your handlers right away.
        /// </summary>
        public static bool SendTo<T>(int playerId, string key, T payload) =>
            SendCore(key, payload, playerId, includeSelf: false);

        /// <summary>
        /// Run <paramref name="handler"/> whenever a message with this key arrives.
        /// </summary>
        /// <remarks>
        /// Dispose it to unsubscribe.
        /// </remarks>
        public static IDisposable Subscribe<T>(string key, Action<T> handler)
        {
            if (handler == null)
            {
                throw new ArgumentNullException(nameof(handler));
            }
            return Subscribe<T>(key, (payload, _) => handler(payload));
        }

        /// <summary>
        /// Run <paramref name="handler"/> whenever a message with this key arrives.
        /// The handler also gets the sender's player id.
        /// </summary>
        /// <remarks>
        /// Dispose it to unsubscribe.
        /// </remarks>
        public static IDisposable Subscribe<T>(string key, Action<T, int> handler)
        {
            CheckKey(key);
            if (handler == null)
            {
                throw new ArgumentNullException(nameof(handler));
            }

            var subscription = new Subscription(key, json =>
            {
                if (TryFromJson<T>(json, key, out var payload))
                {
                    handler(payload, _currentSender);
                }
            });

            if (!Subscribers.TryGetValue(key, out var list))
            {
                list = new List<Subscription>();
                Subscribers[key] = list;
            }
            list.Add(subscription);

            return subscription;
        }

        //--
        // internals
        //--

        private sealed class Subscription : IDisposable
        {
            public readonly string Key;
            public readonly Action<string> Invoke;

            public Subscription(string key, Action<string> invoke)
            {
                Key = key;
                Invoke = invoke;
            }

            public void Dispose()
            {
                if (Subscribers.TryGetValue(Key, out var list))
                {
                    list.Remove(this);
                }
            }
        }

        [Serializable]
        private sealed class CustomMessage<T>
        {
            public T Value;
        }

        /// <summary>
        /// A payload that is a primitive, such as an int or a string.
        /// </summary>
        [Serializable]
        private sealed class BareMessage
        {
            public string Value;
        }

        private static readonly Dictionary<string, List<Subscription>> Subscribers =
            new Dictionary<string, List<Subscription>>(StringComparer.Ordinal);

        /// <summary>Keys that have already arrived without having a handler defined.</summary>
        private static readonly HashSet<string> UnhandledKeys = new HashSet<string>(StringComparer.Ordinal);

        private static int _currentSender = MpConstants.InvalidPlayerId;

        internal static void RegisterHandlers() => MpNet.OnRemote<MpApiMessage>(OnMessage);

        private static void CheckKey(string key)
        {
            if (string.IsNullOrEmpty(key))
            {
                throw new ArgumentException("A message key is required!", nameof(key));
            }
        }

        private static string ToJson<T>(T payload)
        {
            var type = payload.GetType();
            if (!IsBareValue(type))
            {
                return type.IsArray || typeof(IList).IsAssignableFrom(type)
                    ? ListJson(payload, type)
                    : JsonUtility.ToJson(payload);
            }

            string text;
            switch (payload)
            {
                case float f:
                    text = f.ToString("R", CultureInfo.InvariantCulture);
                    break;
                case double d:
                    text = d.ToString("R", CultureInfo.InvariantCulture);
                    break;
                default:
                    text = Convert.ToString(payload, CultureInfo.InvariantCulture);
                    break;
            }

            return JsonUtility.ToJson(new BareMessage { Value = text });
        }

        private static string ListJson(object payload, Type type)
        {
            var envelopeType = typeof(CustomMessage<>).MakeGenericType(type);
            var envelope = Activator.CreateInstance(envelopeType);
            envelopeType.GetField(nameof(CustomMessage<object>.Value)).SetValue(envelope, payload);
            return JsonUtility.ToJson(envelope);
        }

        /// <summary>Something that has to be written out as text, because JSON has no object for it.</summary>
        private static bool IsBareValue(Type type) =>
            type.IsPrimitive || type.IsEnum || type == typeof(string) || type == typeof(decimal);

        private static string WhyNotSerializable(Type type)
        {
            if (type.IsArray || typeof(IList).IsAssignableFrom(type))
            {
                return "Unity will not serialize a list or an array on its own. Put it in a [Serializable] class instead";
            }

            if (!type.IsDefined(typeof(SerializableAttribute), false))
            {
                return $"{type.Name} is not marked [Serializable]";
            }

            if (type.GetFields(BindingFlags.Instance | BindingFlags.Public).Length == 0)
            {
                return $"{type.Name} has no public fields (properties are not fields, and are never sent)";
            }

            return "Unity's serializer would not take it. Dictionaries, interfaces and generics do not survive";
        }

        private static bool TryFromJson<T>(string json, string key, out T payload)
        {
            payload = default;
            if (string.IsNullOrEmpty(json))
            {
                MpPlugin.Log.LogError($"MpApi: '{key}' arrived without a payload.");
                return false;
            }

            var type = typeof(T);
            if (IsBareValue(type))
            {
                var text = JsonUtility.FromJson<BareMessage>(json)?.Value ?? string.Empty;
                payload = (T)(type.IsEnum
                    ? Enum.Parse(type, text)
                    : Convert.ChangeType(text, type, CultureInfo.InvariantCulture));
                return true;
            }

            if (type.IsArray || typeof(IList).IsAssignableFrom(type))
            {
                var envelope = JsonUtility.FromJson<CustomMessage<T>>(json);
                payload = envelope == null ? default : envelope.Value;
            }
            else
            {
                payload = JsonUtility.FromJson<T>(json);
            }

            if (payload == null && !type.IsValueType)
            {
                MpPlugin.Log.LogError(
                    $"MpApi: no {typeof(T).FullName} could be read out of '{key}' ({json}). The sender has to hand "
                    + "Send<T> that same type, and it needs [Serializable] with public fields. Skipping the handler.");
                return false;
            }

            return true;
        }

        private static bool SendCore<T>(string key, T payload, int target, bool includeSelf)
        {
            CheckKey(key);

            // Nothing readable can be made of this, and the other end would only see a null, so error out. You cannot send null, period. You can only send empty classes at the least.
            if (payload == null)
            {
                MpPlugin.Log.LogError(
                    $"MpApi: '{key}' was sent without a payload. To only signal that something happened, "
                    + "send an empty [Serializable] class instead.");
                return false;
            }

            string json;
            try
            {
                json = ToJson(payload);
            }
            catch (Exception e)
            {
                MpPlugin.Log.LogError($"MpApi: could not serialize the payload for '{key}': {e}");
                return false;
            }

            // Unity leaves us an empty object for anything its serializer does not understand.
            if (payload != null && json.Length <= 2)
            {
                MpPlugin.Log.LogError(
                    $"MpApi: a {payload.GetType().FullName} payload for '{key}' serialized to nothing ({json}), because "
                    + WhyNotSerializable(payload.GetType()) + ".");
                return false;
            }

            bool toSelf = includeSelf || (target != MpConstants.BroadcastPlayerId && target == LocalPlayerId);
            if (toSelf)
            {
                Deliver(key, json, LocalPlayerId);
            }

            bool toOthers = target == MpConstants.BroadcastPlayerId || target != LocalPlayerId;
            if (!MpNet.IsOnline || !toOthers)
            {
                return toSelf;
            }

            MpNet.Send(new MpApiMessage { Key = key, Json = json, TargetPlayerId = target });
            return true;
        }

        private static void OnMessage(MpApiMessage message)
        {
            if (message.TargetPlayerId != MpConstants.BroadcastPlayerId
                && message.TargetPlayerId != MpNet.LocalPlayerId)
            {
                return;
            }

            Deliver(message.Key, message.Json, message.SenderId);
        }

        private static void Deliver(string key, string json, int senderId)
        {
            if (!Subscribers.TryGetValue(key, out var list) || list.Count == 0)
            {
                // Probably a mod this machine doesn't have.
                if (UnhandledKeys.Add(key))
                {
                    MpPlugin.Log.LogInfo($"MpApi: received message with key '{key}' but there are no handlers defined, ignoring it from now on.");
                }
                return;
            }

            int previous = _currentSender;
            _currentSender = senderId;
            try
            {
                // Copied, so a handler can unsubscribe while this runs.
                foreach (var subscription in list.ToArray())
                {
                    try
                    {
                        subscription.Invoke(json);
                    }
                    catch (Exception e)
                    {
                        MpPlugin.Log.LogError($"MpApi: a handler for '{key}' from player {senderId} failed: {e}");
                    }
                }
            }
            finally
            {
                _currentSender = previous;
            }
        }
    }

    /// <summary>
    /// The one message every <see cref="MpApi"/> send travels on. Routed by key.
    /// </summary>
    [NetMessage(68)]
    public sealed class MpApiMessage : NetMessage
    {
        public string Key = string.Empty;

        /// <summary>The recipient, or <see cref="MpConstants.BroadcastPlayerId"/> for everyone.</summary>
        public int TargetPlayerId = MpConstants.BroadcastPlayerId;

        public string Json = string.Empty;

        public override void Write(NetWriter w)
        {
            w.String(Key);
            w.Int(TargetPlayerId);
            w.String(Json);
        }

        public override void Read(NetReader r)
        {
            Key = r.String();
            TargetPlayerId = r.Int();
            Json = r.String();
        }

        public override string ToString() => $"MpApi({Key} from {SenderId})";
    }
}
