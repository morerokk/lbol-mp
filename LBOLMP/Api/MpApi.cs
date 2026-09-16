using System;
using System.Collections.Generic;
using System.Linq;
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

            var subscription = new Subscription(key, json => handler(FromJson<T>(json), _currentSender));

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

        private static readonly Dictionary<string, List<Subscription>> Subscribers =
            new Dictionary<string, List<Subscription>>(StringComparer.Ordinal);

        /// <summary>Keys that have already arrived without having a handler defined.</summary>
        private static readonly HashSet<string> UnhandledKeys = new HashSet<string>(StringComparer.Ordinal);

        private static int _currentSender = MpConstants.InvalidPlayerId;

        internal static void RegisterHandlers() => MpNet.On<MpApiMessage>(OnMessage);

        private static void CheckKey(string key)
        {
            if (string.IsNullOrEmpty(key))
            {
                throw new ArgumentException("A message key is required!", nameof(key));
            }
        }

        private static string ToJson<T>(T payload) => JsonUtility.ToJson(new CustomMessage<T> { Value = payload });

        private static T FromJson<T>(string json)
        {
            var envelope = JsonUtility.FromJson<CustomMessage<T>>(json);
            return envelope == null ? default : envelope.Value;
        }

        private static bool SendCore<T>(string key, T payload, int target, bool includeSelf)
        {
            CheckKey(key);

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
            // Our own messages come back from the host; those were already delivered in Send if wanted.
            if (message.SenderId == MpNet.LocalPlayerId)
            {
                return;
            }

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
