# Custom network messages

Other mods can send their own networked data through LBOL MP with `LBOLMP.Api.MpApi`.

Every message has a key and a payload (body). The payload is sent as JSON with Unity's `JsonUtility`. That means you can send objects with the following things only:

- `public `fields on `[Serializable]` classes
- Primitives (int, float, double, bool)
- Strings
- Enums
- Arrays
- `List<T>`

Properties and dictionaries are *not* supported.

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
- If your mod does not hard-require LBOL MP, you have to double-triple make sure that no code in the LBOL MP API is called if LBOL MP is not installed. Ideally, make a separate class in-between that is never touched otherwise, and make sure the compiler doesn't inline it. How you do this is left as an exercise to the reader (because I don't know). The game will not crash or error over the missing dependency as long as you never try to actually touch said dependency.

## Reflection alternative

The other approach if you just need to send/receive a network message and don't want to put a dependency on LBOL MP in your mod, is to use reflection.

Example code to put in your mod would be something like this:

```csharp
public static class MpReflectionBridge
{
    private static readonly Type Api = Type.GetType("LBOLMP.Api.MpApi, LBOLMP");

    public static void Send<T>(string key, T payload)
    {
        Api?.GetMethods()
            .First(m => m.Name == "Send")
            .MakeGenericMethod(typeof(T))
            .Invoke(null, new object[] { key, payload, false });
    }

    public static void Subscribe<T>(string key, Action<T> handler)
    {
        Api?.GetMethods()
            .First(m => m.Name == "Subscribe"
                && m.GetParameters()[1].ParameterType.GetGenericTypeDefinition() == typeof(Action<>))
            .MakeGenericMethod(typeof(T))
            .Invoke(null, new object[] { key, handler });
    }
}
```

This gives you immediate access to the `Send` and `Subscribe` methods, letting you send arbitrary data.
