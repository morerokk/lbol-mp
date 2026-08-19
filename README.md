# LBOL MP

A multiplayer mod for the game Touhou: Lost Branch of Legend.

Allows multiplayer play over Steam or via direct TCP connections. The game is fully playable in multiplayer, much like Slay the Spire 2.

This is the source code for the repo. For downloading or installing the mod, see these links:

- [Steam Workshop](https://steamcommunity.com/sharedfiles/filedetails/?id=3784495171)
- [Thunderstore](https://thunderstore.io/c/touhou-lost-branch-of-legend/p/Rokk/LBOLMP/)

## Technical writeup

Coming soon.

tl;dr: the host syncs the game's difficulty and seed to everyone else. The mod spawns in units for each other player in the party, which will reflect the other players' actual HP/status/etc.

Whenever you play a card (or when *literally anything else* triggers an effect), it is replicated to everyone else, including the player animations and the card's gun animation. When your turn(s) have ended, you wait for everyone else to finish their turn, too. Once this happens, the enemies take a turn. Each player is hit locally by enemies (since everyone is essentially running their own simulation of the game), and it all just kinda happens to line up. This absolutely needs RngFix to work, since the game's own RNG would otherwise cause desyncs.

Events are forcibly synced to everyone too, so everyone sees the same events.

Some RNG streams (like shop/exhibit/card reward RNG) are shifted so that players don't end up building the exact same decks.
