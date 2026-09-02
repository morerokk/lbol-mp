using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using LBOLMP.Net;
using LBOLMP.Session;
using LBoL.ConfigData;
using LBoL.Core;
using LBoL.Core.Cards;
using LBoL.Base;
using LBoL.Core.Randoms;
using LBoL.Core.Units;
using LBoL.Presentation;

namespace LBOLMP.Patches
{
    /// <summary>
    /// When the player clicks "Confirm" to start the run, instead wait for everyone else to also confirm.
    /// Then when everyone has confirmed, apply the host's seed to everyone.
    /// </summary>
    [HarmonyPatch(typeof(GameMaster))]
    public static class StartGameInterceptPatch
    {
        private sealed class PendingRun
        {
            public GameDifficulty Difficulty;
            public PuzzleFlag Puzzles;
            public PlayerUnit Player;
            public PlayerType PlayerType;
            public Exhibit InitExhibit;
            public int? InitMoneyOverride;
            public IEnumerable<Card> Deck;
            public IEnumerable<Stage> Stages;
            public Type DebutAdventureType;
            public IEnumerable<JadeBox> JadeBoxes;
            public GameMode GameMode;
            public bool ShowRandomResult;
        }

        private static PendingRun _pending;
        private static bool _allowThrough;

        [HarmonyPrefix]
        [HarmonyPatch(nameof(GameMaster.StartGame), typeof(ulong?), typeof(GameDifficulty), typeof(PuzzleFlag),
            typeof(PlayerUnit), typeof(PlayerType), typeof(Exhibit), typeof(int?), typeof(IEnumerable<Card>),
            typeof(IEnumerable<Stage>), typeof(Type), typeof(IEnumerable<JadeBox>), typeof(GameMode), typeof(bool))]
        private static bool StartGamePrefix(ulong? seed, GameDifficulty difficulty, PuzzleFlag puzzles,
            PlayerUnit player, PlayerType playerType, Exhibit initExhibit, int? initMoneyOverride,
            IEnumerable<Card> deck, IEnumerable<Stage> stages, Type debutAdventureType,
            IEnumerable<JadeBox> jadeBoxes, GameMode gameMode, bool showRandomResult)
        {
            if (_allowThrough || !MpNet.IsOnline)
            {
                return true;
            }

            if (MpSession.State != MpSessionState.Lobby)
            {
                // Already waiting, ignore the click.
                // Due to the hacky way we're waiting, this can silently break spellcards.
                MpPlugin.Log.LogWarning(
                    "Start Game pressed again while the lobby was still being waited on; " +
                    "ignoring the run, repairing the spell card's owner when it starts");
                return false;
            }

            // Build the deck now: it is a lazily-built sequence and we need it twice.
            var deckList = deck?.ToList() ?? new List<Card>();

            _pending = new PendingRun
            {
                Difficulty = difficulty,
                Puzzles = puzzles,
                Player = player,
                PlayerType = playerType,
                InitExhibit = initExhibit,
                InitMoneyOverride = initMoneyOverride,
                Deck = deckList,
                Stages = stages?.ToList() ?? new List<Stage>(),
                DebutAdventureType = debutAdventureType,
                JadeBoxes = jadeBoxes?.ToList() ?? new List<JadeBox>(),
                GameMode = gameMode,
                ShowRandomResult = showRandomResult
            };

            MpSession.SubmitLocalReady(
                player?.Id ?? string.Empty,
                playerType == PlayerType.TypeB ? 1 : 0,
                initExhibit?.Id ?? string.Empty,
                deckList.Select(DescribeCard).ToList(),
                (int)difficulty,
                _pending.JadeBoxes.Select(jadeBox => jadeBox.Id).ToList());

            MpPlugin.Log.LogInfo("Start Game held: waiting for the rest of the lobby");
            return false;
        }

        private static string DescribeCard(Card card) => card.IsUpgraded ? card.Id + "+" : card.Id;

        /// <summary>
        /// Turn the host's list of ids into jade boxes that this player should start a run with.
        /// </summary>
        private static List<JadeBox> BuildJadeBoxes(IReadOnlyList<string> ids)
        {
            var jadeBoxes = new List<JadeBox>();
            var groups = new HashSet<string>();

            foreach (var id in ids)
            {
                if (jadeBoxes.Any(existing => existing.Id == id))
                {
                    continue;
                }

                var jadeBox = Library.TryCreateJadeBox(id);
                if (jadeBox == null)
                {
                    MpPlugin.Log.LogWarning(
                        $"The host is playing with the jade box {id}, which was not found locally! " +
                        "the run will not match theirs");
                    continue;
                }

                if (jadeBox.Config.Group.Any(groups.Contains))
                {
                    MpPlugin.Log.LogWarning($"Skipping {id}: another jade box already covers its group");
                    continue;
                }

                foreach (var group in jadeBox.Config.Group)
                {
                    groups.Add(group);
                }

                jadeBoxes.Add(jadeBox);
            }

            return jadeBoxes;
        }

        /// <summary>
        /// Called once the host has broadcast the shared seed. Begins the run for everyone.
        /// </summary>
        public static void BeginPendingRun(ulong seed)
        {
            if (_pending == null)
            {
                MpPlugin.Log.LogWarning("Run start arrived but this client had nothing pending");
                return;
            }

            var pending = _pending;
            _pending = null;

            // Apply the host's difficulty (but not their Requests, since those are all allowed to be individual)
            var difficulty = (GameDifficulty)MpSession.RunDifficulty;
            if (difficulty != pending.Difficulty)
            {
                MpPlugin.Log.LogInfo(
                    $"Starting on the host's difficulty ({difficulty}) rather than the one selected here ({pending.Difficulty})");
            }

            // Same for the jade boxes, which have to match or the party is playing two different games.
            var jadeBoxes = BuildJadeBoxes(MpSession.RunJadeBoxes);
            var ours = pending.JadeBoxes.Select(jadeBox => jadeBox.Id).ToList();
            if (!jadeBoxes.Select(jadeBox => jadeBox.Id).SequenceEqual(ours))
            {
                MpPlugin.Log.LogInfo(
                    $"Starting with the host's jade boxes ({MpSession.DescribeJadeBoxes(jadeBoxes.Select(j => j.Id))}) "
                    + $"rather than the ones selected here ({MpSession.DescribeJadeBoxes(ours)})");
            }

            MpPlugin.Log.LogInfo($"Starting multiplayer run with seed {seed} on {difficulty}");

            RepairUsOwner(pending.Player);

            var money = MpSafe.Run("PooledStartingMoney",
                () => PooledStartingMoney(pending, jadeBoxes), pending.InitMoneyOverride);

            _allowThrough = true;
            try
            {
                GameMaster.StartGame(seed, difficulty, pending.Puzzles, pending.Player, pending.PlayerType,
                    pending.InitExhibit, money, pending.Deck, pending.Stages,
                    pending.DebutAdventureType, jadeBoxes, pending.GameMode,
                    pending.ShowRandomResult);
            }
            finally
            {
                _allowThrough = false;
            }
        }

        // Makes Share the Wealth work with different amounts of character starting money
        private static int? PooledStartingMoney(PendingRun pending, List<JadeBox> jadeBoxes)
        {
            if (!jadeBoxes.Any(jadeBox => jadeBox.Id == nameof(Entities.JadeBoxes.MpShareTheWealth)))
            {
                return pending.InitMoneyOverride;
            }

            int pot = pending.InitMoneyOverride ?? pending.Player?.Config.InitialMoney ?? 0;

            foreach (var player in MpSession.ConnectedPlayers.Where(p => !p.IsLocal))
            {
                var config = PlayerUnitConfig.FromId(player.CharacterId);
                if (config == null)
                {
                    MpPlugin.Log.LogWarning(
                        $"{player.Name} is playing '{player.CharacterId}', which this game does not have; "
                        + "their starting money is not in the pot");
                    continue;
                }

                pot += config.InitialMoney;
            }

            MpPlugin.Log.LogInfo($"Share the Wealth: the party opens on a pooled {pot} gold");
            return pot;
        }

        /// <summary>
        /// Because clicking "Confirm" makes the game already secretly do some bookkeeping in the background,
        /// pressing it again results in your spellcard being ownerless.
        /// This is normally not an issue, but AoE or non-attack spellcards will now fail and break. This patch fixes that automatically if detected.
        /// </summary>
        public static void RepairUsOwner(PlayerUnit player)
        {
            MpSafe.Run("RepairUsOwner", () =>
            {
                var us = player?.Us;
                if (us == null || ReferenceEquals(us.Owner, player))
                {
                    return;
                }

                MpPlugin.Log.LogWarning(
                    $"{us.Id} was owned by a discarded player unit; pointing it back at {player.Id}");
                us.Owner = player;
            });
        }

        public static void Cancel()
        {
            _pending = null;
        }

        public static bool HasPending => _pending != null;
    }

    /// <summary>
    /// On run start, bump each player's "personal RNG" a little bit.
    /// This way, different players don't see the exact same shop contents, card rewards, Eirin choices, event rewards, or exhibits.
    /// "Personal RNG" is basically any random reward that they get.
    /// "Non-personal RNG" implies things like enemy HP values, enemy intents, enemy encounters, and so forth.
    /// </summary>
    [HarmonyPatch(typeof(GameRunController), MethodType.Constructor, typeof(GameRunStartupParameters))]
    public static class PersonalRngPatch
    {
        [HarmonyPostfix]
        private static void Postfix(GameRunController __instance)
        {
            ulong salt = MpPersonalRng.Salt;
            if (!MpNet.IsOnline || salt == 0)
            {
                return;
            }

            // Personal rewards
            __instance.ShopRng = new RandomGen(__instance.RootSeed ^ salt ^ 0x11);
            __instance.ExhibitRng = new RandomGen(__instance.RootSeed ^ salt ^ 0x33);
            __instance.ShiningExhibitRng = new RandomGen(__instance.RootSeed ^ salt ^ 0x44);
            __instance.CardRng = new RandomGen(__instance.RootSeed ^ salt ^ 0x55);

            // Also customize event RNG, because this is also used to determine what rewards everyone is offered at events.
            // The host separately forces the same events on everyone, so this works fine.
            __instance.AdventureRng = new RandomGen(__instance.RootSeed ^ salt ^ 0x66);

            MpPlugin.Log.LogInfo($"Personalised reward RNG for player {MpNet.LocalPlayerId}");

            // And again for the streams RngFix takes over, if it is installed.
            // (The mod effectively cannot function without RNGFix anyway though)
            MpSafe.Run("RngFixInterop", () => RngFixInterop.Personalise(__instance, __instance.RootSeed ^ salt));
        }
    }
}
