# Custom network messages

Other mods can send their own networked data through LBOL MP with `LBOLMP.Api.MpApi`.

Every message has a key and a payload (body). The payload is sent as JSON with Unity's `JsonUtility`. That means you can send objects with the following things only:

- `public` fields on `[Serializable]` classes
- Primitives (int, float, double, bool)
- Strings
- Enums
- Arrays
- `List<T>`

Properties and dictionaries are *not* supported.

The payload does not have to be a class. An `int`, `string`, Enum or `List<T>` works too.

A payload is always required, but it is allowed to be empty. If you only want to send to other players that something happened, send an instance of a `[Serializable]` class with no fields at all. Due to technical limitations, you cannot send `null`, as it would be indistinguishable from a message that failed to serialize.

```csharp
// SomethingHappenedMessage.cs
[Serializable]
public class SomethingHappenedMessage
{
    public bool InCombat;
}


// Somewhere else

// You should call this once (such as in your Bepinex plugin, or in a component with `Awake`). Keep the returned object and dispose it to unsubscribe.
MpApi.Subscribe<SomethingHappenedMessage>("MyMod.SomethingHappenedMessage", (message, senderId) =>
{
    bool inCombat = message.InCombat;
    bool isHost = senderId == MpApi.HostId;
});

// Wherever you need to send a message (as a host or client, doesn't matter, this example is for something host-decided).
if (MpApi.IsHost)
{
    MpApi.Send("MyMod.SomethingHappenedMessage", new SomethingHappenedMessage { InCombat = true });
}
```

- `Send(...)` goes to every other player. Pass `includeSelf: true` to also run your own handlers locally right away.
- `SendTo(playerId, ...)` goes to one player.
- `IsOnline`, `IsMultiplayer`, `IsHost`, `LocalPlayerId` and `Players` can be used to look at who's in the session.
- Handlers run on the main thread. A handler that throws is logged, and doesn't affect other handlers.
- Messages with an unrecognized key are logged *once*, and then ignored. You are allowed to send custom network messages even if you're unsure whether other players also have said mod installed.
- You *should* prefix your keys with your mod's name so they don't conflict with other mods. Prefer `MyMod.SomethingHappenedMessage` over `SomethingHappenedMessage`.
- LBOL MP's GUID is `rokk.lbol.multiplayer.LBOLMP` (for BepInDependency purposes). This GUID will not change.
- If your mod *optionally* supports LBOL MP and does not require it, you have to ensure that no LBOL MP code is ever called if it's not enabled. Ideally, make a separate class in-between that is never touched otherwise, and make sure the JIT doesn't inline it (`[MethodImpl(MethodImplOptions.NoInlining)]`).

The safest way to do this is to make a separate Bepinex plugin bundled with your mod, which hard-depends on LBOL MP and your main plugin. It will not be loaded if LBOL MP isn't installed. Put your networking in there.

## Reflection alternative

The other approach if you just need to send/receive network messages the quick and dirty way, is to use reflection.

Example code to put in your mod would be something like this:

```csharp
public static class MpReflectionBridge
{
    private static readonly Type Api = Type.GetType("LBOLMP.Api.MpApi, LBOLMP");

    public static void Send<T>(string key, T payload)
    {
        Api?.GetMethods()
            .First(m => m.Name == "Send")
            .MakeGenericMethod(payload?.GetType() ?? typeof(T))
            .Invoke(null, new object[] { key, payload, false });
    }

    // Keep the returned object and dispose it to unsubscribe. Null if LBOL MP isn't installed.
    public static IDisposable Subscribe<T>(string key, Action<T, int> handler)
    {
        return (IDisposable)Api?.GetMethods()
            .First(m => m.Name == "Subscribe"
                && m.GetParameters()[1].ParameterType.GetGenericTypeDefinition() == typeof(Action<,>))
            .MakeGenericMethod(typeof(T))
            .Invoke(null, new object[] { key, handler });
    }
}
```

This gives you immediate access to the `Send` and `Subscribe` methods, letting you send arbitrary data.
