# Technical Writeup

tl;dr: I got very lucky. There are a lot of accidental reasons why the multiplayer mod even exists.

## Iterators

Lost Branch of Legend is, programming-wise, an extremely well-put together game. Everything in the game runs on a giant iterator, a state machine of sorts, with some extra sprinkles of events on top (which are also part of the iterators). It's perfect for a turn-based card game. It uses C# iterators and Unity coroutines very nicely.

The entire combat part of the game is put together roughly like this:

- Player Round
	- Player Turn
		- Play cards
			- (Optionally, the card play is modified by your status effects, such as Firepower)
			- Card actions (damage the enemy)
		- End the turn
	- (repeat for extra turns)
- Enemy Round
	- Enemy actions

As a result, the multiplayer mod can:

- Arbitrarily pause the state machine at any point (so that the game waits for other players to end the turn)
- Inject new card plays or actions at any point (to replicate the actions of other players into your own game)

And it all just works out. Replaying the same actions in the same order on every machine will, ideally, result in the same end state. But I'll get back to that later, because that's not quite true right now.

Very early on in the mod, when I discovered that "End of Imperishable Night" would cause turn desyncs, I simply moved the "waiting for other players" gate to be right before the enemy round, rather than right after the end of the turn. And that was that. I could not imagine being able to do this in Slay The Spire 1, for example.

## Debug Actions

These are used as developer tools/cheats to test the game. They let you resolve any action at any time, while still queueing it as part of the battle, so that the game doesn't lock up and/or crash from trying to randomly do something in the middle of another action.

Well, the multiplayer mod can use these to resolve multiplayer actions, too. When your teammate plays an attack card that deals 5 damage, the networking can simply inject that as a debug action in the middle of the game, and now everything lines up nicely.

## RNGFix

But even then, in early versions of the mod, things diverged very quickly. Between 2 players, we would see enemies take slightly different actions, or even get entirely different combats. That's because the game RNG is very weird and not very replayable. It's technically deterministic, but can be thrown off by something as simple as picking a different card in the first combat, and that's that, it's over. Of course, that's a problem for a multiplayer mod that relies on the game being deterministic. It would have taken me a lot of effort and research to fix this, more effort and research than I would be able to muster. I might not have been able to figure this out at all. I was never one for math.

[RNGFix](https://github.com/Neoshrimp/TheGoodLBoLMods) is a fantastic mod by Neoshrimp that's already done all the work on this! And it's why LBOL MP needs RNGFix to work. By simply dropping in RNGFix, we saw that enemy encounters and enemy intents were suddenly no longer out of sync. And that was that. Seriously, huge props to Neoshrimp, they're half the reason this mod is able to exist.

## Steam Networking

Also an accident.

The game just happens to use Steam's "Rich Presence" feature. It's a novelty feature that makes Steam show other people exactly who/what you're playing, and where you are in the run. Such as "Sakuya is exploring the Bamboo Forest, Hard". Well, it turns out that this means the game is already importing a lot of Steamworks library stuff. Mods have access to this for free, without needing to be stuck in layers of dependency hell trying to get a non-Steam integrated game to integrate with Steam. I'm sure it would have been possible to some extent, but it would have been very hard.

But the game already has it integrated, so I can also use its networking features for free. And better yet, the developers don't even need to enable anything for this to work, it's just always on. It took no time at all to add Steam to the list of supported networking features, meaning that you no longer need to port-forward or hassle with IP addresses to join friends.

The problem with Steam networking is that it gets a bit unhappy under packet loss, and it also gets unhappy if you leave too much in the buffers. But yet again, Steam has solutions for this. I decided to mark the least-important network messages as "unreliable", that being the enemy HP and status sync. If your connection is bad, Steam is allowed to drop a packet that says "enemy HP is now at 50", because it won't be long until another packet that says "enemy HP is now at 50" or "enemy HP is now at 0" comes through. They're idempotent, so it doesn't matter if a packet is dropped. Messages have sequence numbers, so it doesn't matter if they arrive out of order.

## Lag Compensation

Earlier, I said that replaying actions in the same order will give the same result. That's true, but LBoL has a few cases where things are ordering-dependent. Primarily Vulnerable and Lock On status effects.

### The current scenario

Suppose the following scenario:

- A is host
- B is client
- A plays a card that applies Vulnerable to an enemy
- B plays a 10-damage attack card *at the exact same time*

B saw themselves dealing 10 damage to the enemy, then the enemy getting Vulnerable. A saw themselves applying Vulnerable to the enemy, and B dealing 15 damage instead. Oops, now A sees a dead enemy, but for B they are still alive with 5 HP! More on that in a bit.

This is made worse by the fact that LBoL likes its shooting animations. An attack doesn't *connect* until the bullet particle hits the enemy, basically. It's not like Slay the Spire 2, where an attack connects *immediately* (as you may notice, they designed the game around this being a fact to make multiplayer easier).

### The scrapped alternative scenario

Early on in the LBOL MP mod's development, we tried to make every card play entirely host-authoritative, like Slay the Spire 2. It would have worked like this:

- A is host
- B is client
- A plays a card that applies Vulnerable to an enemy
- B wants to play a 10-damage attack card *at the exact same time*
- B's card play is submitted to the host
- A sees B's card added to the queue, after their vulnerable
- A sends the results of everyone's card plays back to B
- B finally sees that their attack card actually did 15 damage, because A's card resolved first

This would absolutely and completely fix desyncs. But it also felt very bad to play, even on good connections, because of the bullet delay. Everyone except A is waiting ages for their card to resolve. You feel really disconnected from the game, and that sucks a lot of the fun out of it, for such a flashy game. I would not have settled for this scrapped scenario, it felt *that* bad. I would at least have had to remove the delay between playing an attack card and the bullet hitting the enemy. That's better, but still doesn't feel good, and as you will read in the next paragraph, it's unnecessary.

### The current scenario, but consistent

So we opted to stick with the current implementation, which is that everyone sees their own cards resolve immediately. And now we have a slight desync! Or do we?

Well, we don't actually desync, because the host periodically re-broadcasts the exact HP, Block, Barrier, and Seija's damage cap. In average gameplay, applying Vulnerable is not *that* common (you're not going to be appyling it 10 separate times in a second), and people are not watching enemy health bars like a hawk. The host said the enemy has 350 HP, who cares about a small jump from 345 to 350? I bet most people wouldn't notice. Not even *I* have seen it happen in actual gameplay, unless I went out of my way to intentionally try to break it and kept an eye on the HP bar.

And so, we achieve eventual consistency anyway. The game continues to feel good, and the game state is still consistent, because what the host sees is ultimately the truth. Even if that truth only actually arrives to other clients 0.2 seconds later. We end up with the same consistency and reliability as the hypothetical "host-authoritative card queue" system, but with 1/10th of the necessary code and none of the "laggy" feeling. Some bandaids are required to get there, but this game has a finite amount of bandaids to apply, and eventually you've got them all.

If you just keep applying bandaids, eventually you will run out of things to band-aid. "The mod can't break" vs. "The mod can only break in scenarios that don't happen" are equivalent in practice.

## Multiplayer Cards

These are a bit weird, but once again I'm saved by the game and some dirty hacks.

### Networking

Arguably the easier part of multiplayer cards.

To send an action, the only thing the card needs is this:
```csharp
[EntityLogic(typeof(MpYinYangDistributionDefinition))]
public sealed class MpYinYangDistribution : Card
{
    protected override IEnumerable<BattleAction> Actions(
        UnitSelector selector, ManaGroup consumingMana, Interaction precondition)
    {
        MpEffects.Send(Id, new MpYinYangDistributionPayload { Upgraded = IsUpgraded }, MpEffectTarget.AllPartners);
        yield break;
    }
}
```

And the payload:
```csharp
public sealed class MpYinYangDistributionPayload : MpEffectPayload
{
    public bool Upgraded;
}
```

And that's it. You define a payload, you optionally set parameters, and it just sends this over the network. The card can of course do anything else you want it to in addition.

The Receive part is a bit trickier. Since other players don't have an instance of the card, I had to put this in the card's *definition*. And in order to have the card do anything useful, I have to make it respect the game's Iterator system, as described above. Fortunately, that's easy too, and doing it this way even gives you full access to every other action that the game can normally do (as the result of a status or a card). For instance, Yin-Yang Distribution adds a Yin-Yang Orb to your hand:
```csharp
public override IEnumerable<BattleAction> Receive(
    MpYinYangDistributionPayload payload, BattleController battle, int senderId)
{
    yield return new AddCardsToHandAction(
        Library.CreateCards<YinyangCard>(1, payload.Upgraded), AddCardsType.Normal, false);
}
```

Again, the only reason this works is because the game uses Iterators. Whenever there's a free moment, we can just have the game resolve other players' actions instead of waiting on our own.

## Partner targeting

Targeting other players works, even on a controller. That's because partner-targeted cards implement an empty interface, and the card masquerades as a card type that targets a single enemy.

But when you actually start selecting a target, a Harmony patch sees that you're holding a partner-targeted card, and will replace the list of valid targets: substituting ally players instead of enemies. Since allies are also represented as units, you can simply point at a player and use them as a card target. Problem solved.
