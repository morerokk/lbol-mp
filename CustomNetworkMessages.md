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

// Wherever you need to send a message (as a host or client, doesn't matter).
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
- You can get LBOL MP's GUID for use with `BepInDependency` by accessing `MpApi.PluginGuid`.
- If your mod does not hard-require LBOL MP, you have to double-triple make sure that no code in the LBOL MP API is called if LBOL MP is not installed. Ideally, make a separate class in-between that is never touched otherwise, and make sure the compiler doesn't inline it. How you do this is left as an exercise to the reader (because I don't know). The game will not crash or error over the missing dependency as long as you never try to actually touch said dependency.
