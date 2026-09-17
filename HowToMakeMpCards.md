# How to make multiplayer cards

Basically, you just have to implement the right interfaces. Roughly speaking, there are 4 types of multiplayer cards:

- Regular cards that just happen to be multiplayer-exclusive
- Cards that immediately send something over the network when played
- Cards that don't send something over the network, but add a status effect that will
- Cards that add a status effect which will react to another player playing a card

In all 4 cases, implement the `LBOLMP.Entities.IMpOnlyCard` interface.

Call `LBOLMP.Session.MpCardAvailability.RegisterAll()` with your own mod's assembly, and LBOL MP will automatically take care of enabling/disabling the card in singleplayer/multiplayer runs, where appropriate.

If your card replaces an existing singleplayer card, call `MpCardAvailability.SetVanillaCardSingleplayerOnly()` with the card ID to just disable that card in multiplayer. This is useful when replacing an entire card with a more multiplayer-appropriate version (for instance, a self-targeted card could become an any-player-targeted card).

You also need to register everything once from your mod's `Awake` for the following to work:

```csharp
public sealed class MyPlugin : BaseUnityPlugin
{
	// ...
	private void Awake()
	{
		// ...
		
		// NOTE: You should probably do this in a separate assembly, unless your mod already hard-depends on LBOL MP.
		// Make the separate assembly hard-depend on LBOL MP, and it will only be loaded if LBOL MP is loaded.
		// Put your multiplayer cards and custom network messages in there.
		
		// Register network effects
		MpEffects.RegisterAll(Assembly.GetExecutingAssembly());
		// Register card availability for multiplayer-only cards
		MpCardAvailability.RegisterAll(Assembly.GetExecutingAssembly());
	}
}
```

## Regular cards that just happen to be multiplayer-exclusive

See the [Hateful Orbs](https://github.com/morerokk/lbol-mp/blob/master/LBOLMP/Entities/Cards/Reimu/MpHatefulOrbsDefinition.cs) example. Basically, just implement `IMpOnlyCard`.

## Cards that immediately send something over the network when played

This is a bit trickier, but LBOL MP provides interfaces for this. An example of a card definition as follows, this gives 5 Block to a single partner. You have to define a payload, and implement the `IMpEffect<T>` interface on the card definition.

The actual card class does not need to implement an interface if it targets all partners, but if you want to target a single player, you must do 2 things:
- Set the `TargetType` on the card config to `SingleEnemy`
- Have your card implement `IMpPartnerTargeted` (this lets the mod hijack the targeting arrow to target other players)

If you need to target any player including yourself, implement `IMpAnyPlayerTargeted` instead. `MpPartyTargeting.Consume()` can also return yourself. If you end up doing something to just yourself, it's better to just immediately perform the action rather than sending it over the network.

```csharp
public sealed class MyPayload : MpEffectPayload
{
    public int Block;
}

public sealed class MyCardDefinition : CardTemplate, IMpEffect<MyPayload>, IMpOnlyCard
{
    // ...your usual GetId, MakeConfig, LoadCardImages, LoadLocalization...
	// If you want it to target a single partner, set the target type to SingleEnemy in MakeConfig() and have the actual card implement the `IMpPartnerTargeted` interface.
	// config.TargetType = TargetType.SingleEnemy;

    // This runs on the receiving player's client.
    public IEnumerable<BattleAction> Receive(MyPayload payload, BattleController battle, int senderId)
    {
        yield return new CastBlockShieldAction(battle.Player, payload.Block, 0);
    }
}

[EntityLogic(typeof(MyCardDefinition))]
public sealed class MyCard : Card, IMpPartnerTargeted
{
    public override bool CanUse => MpPartyTargeting.AnyValidPartner;

    protected override IEnumerable<BattleAction> Actions(UnitSelector selector, ManaGroup consumingMana, Interaction precondition)
    {
		// This sends it to the selected partner.
        MpEffects.Send(Id, new MyPayload { Block = 5 }, MpEffectTarget.Partner, MpPartyTargeting.Consume());
        yield break;
    }
}
```

Examples:
- [Yin-Yang Distribution](https://github.com/morerokk/lbol-mp/blob/master/LBOLMP/Entities/Cards/Reimu/MpYinYangDistributionDefinition.cs): adds a Yin-Yang Orb to each player's hand.
- [Time Dilation](https://github.com/morerokk/lbol-mp/blob/master/LBOLMP/Entities/Cards/Sakuya/MpTimeDilationDefinition.cs): chooses 1 other player, takes an extra turn.
- [Ice Block](https://github.com/morerokk/lbol-mp/blob/master/LBOLMP/Entities/Cards/Cirno/MpIceBlockDefinition.cs): plays the vanilla Ice Block on the chosen player (including yourself).

## Cards that don't send something over the network, but add a status effect that will

I'll skip over the card's own "add a status" part here, because that part is the same as any other card.

Status effects work the same way as cards, including the `IMpEffect<T>` interface. The "Receive" part lives on the definition (since other players don't have an instance of said status effect), sending can be done from inside the status effect.

Remember, you can both send an effect *and* apply something to yourself, if you need to.

The following status would make players draw more cards whenever you start a turn:

```csharp
public sealed class MyDrawPayload : MpEffectPayload
{
    public int Cards;
}

public sealed class MySharedDrawSeDefinition : StatusEffectTemplate, IMpEffect<MyDrawPayload>
{
    // ...your usual GetId, MakeConfig, LoadSprite, LoadLocalization...

    // This runs on the receiving player's client.
    public IEnumerable<BattleAction> Receive(MyDrawPayload payload, BattleController battle, int senderId)
    {
        if (payload.Cards <= 0 || battle.BattleShouldEnd)
        {
            yield break;
        }

        yield return new DrawManyCardAction(payload.Cards);
    }
}

[EntityLogic(typeof(MySharedDrawSeDefinition))]
public sealed class MySharedDrawSe : StatusEffect
{
    protected override void OnAdded(Unit unit)
    {
        ReactOwnerEvent(Owner.TurnStarted, new EventSequencedReactor<UnitEventArgs>(OnTurnStarted));
    }

    private IEnumerable<BattleAction> OnTurnStarted(UnitEventArgs args)
    {
        if (!MpEffects.CanSend || Battle.BattleShouldEnd)
        {
            yield break;
        }

        NotifyActivating();

        // This sends it to every other player.
        MpEffects.Send(Id, new MyDrawPayload { Cards = Level }, MpEffectTarget.AllPartners);
    }
}
```

Examples:
- [Offering to the Ownerless](https://github.com/morerokk/lbol-mp/blob/master/LBOLMP/Entities/StatusEffects/MpOfferingSeDefinition.cs): replicates the next Ability Card you play to all other players.

## Cards that add a status effect which will react to another player playing a card

You can react to other players' card plays in a status effect.

The following example gives you Block whenever another player plays an Attack card:

```csharp
public sealed class MyBackupSeDefinition : StatusEffectTemplate
{
    // ...your usual GetId, MakeConfig, LoadSprite, LoadLocalization...
    // Nothing multiplayer-specific here.
}

[EntityLogic(typeof(MyBackupSeDefinition))]
public sealed class MyBackupSe : StatusEffect
{
    protected override void OnAdded(Unit unit)
    {
        ReactOwnerEvent(MpBattleEvents.PartnerCardPlayed(Battle), new EventSequencedReactor<MpPartnerCardEventArgs>(OnPartnerCardPlayed));
    }

    private IEnumerable<BattleAction> OnPartnerCardPlayed(MpPartnerCardEventArgs args)
    {
        // Skip free copies (double plays, follow-ups, cards played on someone's behalf).
		// This is necessary if your status effect might send a card play itself! Or if you just don't feel like follow-ups or partner-copy-plays should count.
        if (args.IsToken || Battle.BattleShouldEnd)
        {
            yield break;
        }

        // The event only has the card's id, get the config here
        var config = CardConfig.FromId(args.CardId);
        if (config == null || config.Type != CardType.Attack)
        {
            yield break;
        }

        NotifyActivating();
        yield return new CastBlockShieldAction(Owner, Level, 0, BlockShieldType.Normal, false);
    }
}
```

Examples:
- [Mimic](https://github.com/morerokk/lbol-mp/blob/master/LBOLMP/Entities/StatusEffects/MpMimicSeDefinition.cs): listens to card plays from a specific partner and copies it locally. This also remembers a specific player.

## Notes

In all the above cases, the definition's `Receive()` is not run for defeated players. If you want this to happen anyway for defeated players, implement the `IMpReachesDownedPlayers` interface on the card definition. The MpDefibrillator tool card has an example of this. Spectating players will never call Receive().

# How to make your mod soft-depend on LBOL MP

The recommended approach is to make a new, separate BepInEx plugin DLL that's bundled with your mod. Mark LBOL MP and your main mod as a hard-dependency, so that this separate plugin is only ever loaded if LBOL MP is. Put all your multiplayer cards and card registration (`MpCardAvailability.RegisterAll()` and `MpEffects.RegisterAll()`) in this separate plugin's Awake.

The other approach if you just need to send/receive a network message and don't care about all this other stuff, is to use reflection. This way, you don't need a hard-dependency on LBOL MP at all. You *should* still add a soft dependency on LBOL MP just to ensure that your mod loads after LBOL MP does.

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

This gives you immediate access to the `Send` and `Subscribe` methods, letting you send arbitrary data. [Refer to the Custom Network Messages documentation](https://github.com/morerokk/lbol-mp/blob/master/CustomNetworkMessages.md) on how to use this.
