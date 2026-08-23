using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using LBOLMP.Net;
using LBOLMP.Session;
using LBOLMP.Session.Battle;
using LBoL.Base;
using LBoL.Core;
using LBoL.Core.Battle;
using LBoL.Core.Battle.BattleActions;
using LBoL.Core.Cards;
using LBoL.Core.StatusEffects;
using LBoL.Core.Units;
using LBoL.EntityLib.StatusEffects.Enemy;
using UnityEngine;

namespace LBOLMP.Patches
{
    /// <summary>
    /// Every client has to roll the same enemy max HP, and left alone they do not.
    /// RNGFix would fix this in singleplayer,
    /// but it does not in multiplayer because of all the monkey business we might be doing.
    /// Particularly, <see cref="EnemyIntentSeedPatch"/>.
    /// That patch replaces the whole intent generator on every <c>UpdateTurnMoves</c>, and how many of those have
    /// happened depends on which enemies died when, which differs per client in some cases.
    /// </summary>
    [HarmonyPatch(typeof(EnemyGroupEntry), nameof(EnemyGroupEntry.Generate))]
    public static class EnemyHpRollPatch
    {
        [HarmonyPrefix]
        private static void Prefix(EnemyGroupEntry __instance, GameRunController gameRun, out RandomGen __state)
        {
            __state = null;
            var saved = (RandomGen)null;

            MpSafe.Run("EnemyHpRollPatch", () =>
            {
                if (!MpSession.IsActive || gameRun == null)
                {
                    return;
                }

                // Give back the real number for RNGFix to stay intact
                saved = gameRun.EnemyBattleRng;

                // Deterministic seed for the whole group.
                ulong seed = MpBattleSync.StationSeed(gameRun, __instance.Id) ^ 0x7F_4A_7C_15_9E_37_79_B9UL;
                gameRun.EnemyBattleRng = new RandomGen(seed);
            });

            __state = saved;
        }

        [HarmonyPostfix]
        private static void Postfix(GameRunController gameRun, RandomGen __state)
        {
            if (__state == null || gameRun == null)
            {
                return;
            }

            MpSafe.Run("EnemyHpRollPatch.Restore", () => gameRun.EnemyBattleRng = __state);
        }
    }

    [HarmonyPatch(typeof(EnemyUnit), nameof(EnemyUnit.EnterGameRun))]
    public static class EnemyHpScalingPatch
    {
        [HarmonyPostfix]
        private static void Postfix(EnemyUnit __instance)
        {
            MpSafe.Run("EnemyHpScalingPatch", () =>
            {
                if (MpEnemyScaling.ExtraFighters <= 0)
                {
                    return;
                }

                // Eiki Shiki's mirror copy is individual per player, so it's not scaled.
                if (MpPrivateEnemies.IsPrivate(__instance))
                {
                    return;
                }

                int scaled = Mathf.Max(1, Mathf.RoundToInt(__instance.MaxHp * MpEnemyScaling.MultiplierFor(__instance)));

                __instance.SetMaxHp(scaled, scaled);
            });
        }
    }

    /// <summary>
    /// Enemy intents must match on every screen, otherwise one player gets attacked while another
    /// gets debuffed by the same move. Rather than directly force the chosen intents onto clients and potentially apply Drowning twice,
    /// we pin the RNG to a deterministic value from the host.
    /// (This is a bit wonky with Fox Youkai, Drowning Girl and Rabbits to be honest, but this is acceptable for now)
    /// </summary>
    [HarmonyPatch(typeof(EnemyUnit), nameof(EnemyUnit.UpdateTurnMoves))]
    public static class EnemyIntentSeedPatch
    {
        [HarmonyPrefix]
        private static void Prefix(EnemyUnit __instance)
        {
            MpSafe.Run("EnemyIntentSeedPatch", () =>
            {
                if (!MpSession.IsActive || !MpBattleSync.InBattle)
                {
                    return;
                }

                var gameRun = __instance.Battle?.GameRun;
                if (gameRun == null)
                {
                    return;
                }

                int round = __instance.Battle.RoundCounter;
                ulong seed = MpBattleSync.SeedForEnemyMove(__instance.Index, round);

                gameRun.EnemyMoveRng = new RandomGen(seed);
                gameRun.EnemyBattleRng = new RandomGen(seed ^ 0x51_7C_C1_B7_27_22_0A_95UL);
            });
        }
    }

    /// <summary>
    /// Tell the director/playboard that ally units exist, so that we can see them and right-click them and all that stuff.
    ///
    /// <c>GameDirector.GetUnit</c> only knows the local player and the enemies normally.
    /// </summary>
    [HarmonyPatch(typeof(LBoL.Presentation.Units.GameDirector), nameof(LBoL.Presentation.Units.GameDirector.GetUnit))]
    public static class AllyViewLookupPatch
    {
        [HarmonyPostfix]
        private static void Postfix(Unit unit, ref LBoL.Presentation.Units.UnitView __result)
        {
            if (__result != null || unit == null)
            {
                return;
            }

            __result = MpSafe.Run("AllyViewLookupPatch", () => UI.MpAllyUnits.GetView(unit), null);
        }
    }

    /// <summary>
    /// Tick ally views the same way as the other units, to update their animations and stuff.
    /// Ally views need the same per-frame tick the director gives everyone else.
    /// </summary>
    [HarmonyPatch(typeof(LBoL.Presentation.Units.GameDirector), "MasterTick")]
    public static class AllyViewTickPatch
    {
        [HarmonyPostfix]
        private static void Postfix()
        {
            UI.MpAllyUnits.TickViews();
        }
    }

    /// <summary>
    /// Keeps an ally's cosmetic-only gun attack out of the game's logic.
    /// Normally this is used to apply the HP decrease at the right time (when the gun hits), but this is a single static field and that won't work.
    /// So I've opted to make this cosmetic-only for ally units and sync the HP elsewhere.
    /// </summary>
    [HarmonyPatch(typeof(LBoL.Presentation.Units.GameDirector), nameof(LBoL.Presentation.Units.GameDirector.OnGunHit))]
    public static class AllyGunHitPatch
    {
        [HarmonyPrefix]
        private static bool Prefix()
        {
            return MpSafe.Run("AllyGunHitPatch", () => !UI.MpAllyUnits.TryHandleAllyGunHit(), true);
        }
    }

    /// <summary>Opens and closes the shared-fight bookkeeping.</summary>
    [HarmonyPatch(typeof(GameRunController))]
    public static class BattleLifecyclePatch
    {
        [HarmonyPostfix]
        [HarmonyPatch(nameof(GameRunController.EnterBattle))]
        private static void AfterEnterBattle(GameRunController __instance, EnemyGroup enemyGroup)
        {
            MpSafe.Run("EnterBattle", () =>
            {
                // Before BeginBattle, which is where the first spawns can already happen.
                MpPrivateEnemies.Reset();

                MpBattleSync.BeginBattle(__instance, enemyGroup);
                EnemyDamageHook.HookAll(__instance.Battle);
                PlayerDamageHook.Hook(__instance.Battle);
                MpDownedPlayers.Hook(__instance.Battle);
            });
        }

        [HarmonyPrefix]
        [HarmonyPatch(nameof(GameRunController.LeaveBattle))]
        private static void BeforeLeaveBattle(GameRunController __instance)
        {
            MpSafe.Run("LeaveBattle", () =>
            {
                EnemyDamageHook.UnhookAll();
                PlayerDamageHook.Unhook();
                MpPrivateEnemies.Reset();

                if (!MpBattleSync.InBattle)
                {
                    MpDownedPlayers.Unhook();
                    return;
                }

                // Revive dead players before the game has a chance to kill them or fail the run.
                MpDownedPlayers.ReviveIfWon(__instance);
                MpDownedPlayers.Unhook();

                MpBattleSync.ReportBattleFinished(__instance.Player != null && __instance.Player.IsAlive);
                MpBattleSync.LeaveBattle();
            });
        }
    }

    /// <summary>
    /// Handles the "waiting for other players to finish their turn" gate, effectively pausing the enemy's turn until every player is confirmed done.
    /// This gate is put directly before the enemy's round starts, so that each player can take their normal turns and extra turns in tandem with each other.
    /// It also resolves end-of-turn effects whenever that player ends their turn, even if other players are still playing. This is intended.
    /// Putting the gate here makes extra turns and super extra turns work properly, and also prevents throwing knives from resolving on enemies *while* the enemy is in its turn.
    /// </summary>
    [HarmonyPatch(typeof(BattleController), "EnemyTurnFlow")]
    public static class EnemyTurnBarrierPatch
    {
        [HarmonyPostfix]
        private static void Postfix(BattleController __instance, ref IEnumerator<object> __result)
        {
            var original = __result;
            if (original == null)
            {
                return;
            }

            __result = Gated(__instance, original);
        }

        private static IEnumerator<object> Gated(BattleController battle, IEnumerator<object> enemyTurn)
        {
            yield return MpBattleSync.WaitForEnemyTurn(battle);

            MpBattleSync.EnemyTurnRunning = true;
            try
            {
                yield return enemyTurn;
            }
            finally
            {
                MpBattleSync.EnemyTurnRunning = false;
            }
        }
    }

    /// <summary>
    /// Handles the "waiting for other players to end the combat" gate.
    /// This can legitimately come up in the Eiki Shiki fight, but can also come up in the case of minor desyncs.
    /// Preventing the battle from ending before everyone is done prevents saving & loading (or restarting) from putting players at different points in the game:
    /// one would restart at the start of the fight, others might restart at the post-battle rewards screen.
    /// </summary>
    [HarmonyPatch(typeof(BattleController), nameof(BattleController.Flow))]
    public static class BattleEndBarrierPatch
    {
        [HarmonyPostfix]
        private static void Postfix(BattleController __instance, ref IEnumerator<object> __result)
        {
            var original = __result;
            if (original == null)
            {
                return;
            }

            __result = Gated(__instance, original);
        }

        private static IEnumerator<object> Gated(BattleController battle, IEnumerator<object> flow)
        {
            yield return flow;
            yield return MpBattleSync.WaitForEveryoneToFinish(battle);
        }
    }

    /// <summary>
    /// While downed players still kind-of take turns, spectating players do not take turns at all.
    /// </summary>
    [HarmonyPatch(typeof(BattleController), "PlayerTurnFlow")]
    public static class SpectatorTurnPatch
    {
        [HarmonyPostfix]
        private static void Postfix(BattleController __instance, ref IEnumerator<object> __result)
        {
            var original = __result;
            if (original == null)
            {
                return;
            }

            __result = Gated(__instance, original);
        }

        private static IEnumerator<object> Gated(BattleController battle, IEnumerator<object> playerTurn)
        {
            yield return MpDownedPlayers.WaitWhileSpectating(battle);
            yield return playerTurn;
        }
    }

    /// <summary>
    /// Broadcast the cards the local player plays, so the rest of the party can see them.
    /// </summary>
    [HarmonyPatch(typeof(BattleController))]
    public static class CardUsePatch
    {
        [HarmonyPostfix]
        [HarmonyPatch(nameof(BattleController.RequestUseCard))]
        private static void AfterUseCard(Card card, UnitSelector selector)
        {
            MpSafe.Run("AfterUseCard", () =>
            {
                if (!MpSession.IsActive || !MpBattleSync.InBattle || card == null)
                {
                    return;
                }

                // SelectedEnemy throws for anything but a single-enemy target, which would break AoE or non-target cards if we don't check first.
                int targetIndex = -1;
                if (selector != null && selector.Type == TargetType.SingleEnemy)
                {
                    targetIndex = selector.SelectedEnemy?.Index ?? -1;
                }

                MpBattleSync.ReportCardPlayed(card.Id, card.IsUpgraded, targetIndex);
            });
        }

    }

    /// <summary>
    /// Enemies are shared, so if we land a hit on an enemy, broadcast it to everyone else.
    /// This happens after the attacker's status effects have been taken into account, but *before* the target's status effects are.
    /// Each client will re-apply these individually, so it's fine. This also handles enemies losing Graze properly.
    /// </summary>
    public static class EnemyDamageHook
    {
        private static readonly List<(EnemyUnit Enemy, GameEventHandler<DamageEventArgs> Handler)> Hooked =
            new List<(EnemyUnit, GameEventHandler<DamageEventArgs>)>();

        public static void HookAll(BattleController battle)
        {
            UnhookAll();

            if (!MpSession.IsActive || battle == null)
            {
                return;
            }

            foreach (var enemy in battle.EnemyGroup)
            {
                Hook(enemy, battle);
            }
        }

        public static void Hook(EnemyUnit enemy, BattleController battle)
        {
            if (!MpSession.IsActive || enemy == null)
            {
                return;
            }

            // Don't publish hits on enemies that only we can see (Eiki Shiki's mirror)
            if (MpPrivateEnemies.IsPrivate(enemy))
            {
                return;
            }

            GameEventHandler<DamageEventArgs> handler = args =>
                MpSafe.Run("EnemyDamageHook", () =>
                {
                    // Enemy damage handlers are called when hovering a card over said enemy, to preview the damage it will deal.
                    // These calculate-only things should *not* be broadcast over the network,
                    // otherwise you get a "kills you with my mind" situation on the other end by simply dragging the attack over the enemy repeatedly (lol).
                    if (args.Cause == ActionCause.OnlyCalculate)
                    {
                        return;
                    }

                    // Don't re-publish events that are not from ourselves
                    if (args.Source != battle.Player)
                    {
                        return;
                    }

                    MpBattleSync.ReportEnemyDamage(enemy, args.DamageInfo, args.GunName);
                });

            enemy.DamageReceiving.AddHandler(handler, GameEventPriority.Highest);
            Hooked.Add((enemy, handler));
        }

        public static void UnhookAll()
        {
            foreach (var entry in Hooked)
            {
                MpSafe.Run("EnemyDamageHook.Unhook", () =>
                    entry.Enemy.DamageReceiving.RemoveHandler(entry.Handler, GameEventPriority.Highest));
            }
            Hooked.Clear();
        }
    }

    /// <summary>
    /// Broadcast to other players that we got hit and took damage of some kind.
    /// Runs at the very end of the game event, to also show to other players exactly what damage we took and how much (barrier, block, life)
    /// </summary>
    public static class PlayerDamageHook
    {
        private static PlayerUnit _player;
        private static GameEventHandler<DamageEventArgs> _handler;

        public static void Hook(BattleController battle)
        {
            Unhook();

            if (!MpSession.IsActive || battle?.Player == null)
            {
                return;
            }

            _player = battle.Player;
            _handler = args => MpSafe.Run("PlayerDamageHook", () =>
            {
                // Same speculative pass the enemy hook has to skip: the preview number shown while
                // a card is hovered runs the events with nothing actually happening.
                if (args.Cause == ActionCause.OnlyCalculate || args.IsCanceled)
                {
                    return;
                }

                MpBattleSync.ReportHit(args.DamageInfo);
            });

            _player.DamageReceived.AddHandler(_handler, GameEventPriority.Lowest);
        }

        public static void Unhook()
        {
            if (_player != null && _handler != null)
            {
                MpSafe.Run("PlayerDamageHook.Unhook", () =>
                    _player.DamageReceived.RemoveHandler(_handler, GameEventPriority.Lowest));
            }

            _player = null;
            _handler = null;
        }
    }

    /// <summary>
    /// Apply our patches, scaling and hooks correctly to enemies that are spawned in later (such as summons)
    /// </summary>
    [HarmonyPatch(typeof(BattleController), nameof(BattleController.Spawn),
        typeof(EnemyUnit), typeof(EnemyUnit), typeof(int), typeof(bool))]
    public static class SpawnHookPatch
    {
        /// <summary>
        /// Check if this is not actually our own private enemy (Eiki Shiki mirror copy), in which case never actually mind.
        /// </summary>
        [HarmonyPrefix]
        private static void Prefix(EnemyUnit spawner, EnemyUnit enemyUnit)
        {
            MpSafe.Run("SpawnHookPatch.Mark",
                () => MpPrivateEnemies.OnSpawning(spawner, enemyUnit));
        }

        [HarmonyPostfix]
        private static void Postfix(BattleController __instance, EnemyUnit __result)
        {
            MpSafe.Run("SpawnHookPatch", () => EnemyDamageHook.Hook(__result, __instance));
        }
    }

    /// <summary>
    /// Replicate debuffs you apply to enemies to everyone else.
    /// Same idea as damage, for debuffs a player lands on a shared enemy.
    /// Patched on the action rather than on <c>TryAddStatusEffect</c> because we need to know what
    /// caused the effect. Only things a player owns are replicated: an enemy buffing itself (a
    /// Crow's opening Graze, say) already happens identically on every client anyway. We only care about player-caused statuses.
    /// </summary>
    [HarmonyPatch(typeof(ApplyStatusEffectAction), "MainPhase")]
    public static class StatusReplicationPatch
    {
        [HarmonyPostfix]
        private static void Postfix(ApplyStatusEffectAction __instance)
        {
            MpSafe.Run("StatusReplicationPatch", () =>
            {
                if (!MpSession.IsActive || !MpBattleSync.InBattle)
                {
                    return;
                }

                // Check if this is a replicated status effect applied by someone else, in which case don't re-publish it again.
                // Remember the 99 graze crows incident or the 495 block kedamas
                if (MpBattleSync.ConsumeInjected(__instance))
                {
                    return;
                }

                var args = __instance.Args;
                if (args?.AddResult == null || !(args.Unit is EnemyUnit enemy))
                {
                    return;
                }

                // Just in case somehow a message lands here that's meant for a private enemy (Eiki Shiki mirror copy), ignore it.
                if (MpPrivateEnemies.IsPrivate(enemy))
                {
                    return;
                }

                var battle = __instance.Battle;
                if (battle == null || !IsLocalPlayerSource(__instance.Source, battle))
                {
                    return;
                }

                MpBattleSync.ReportEnemyStatus(enemy, args.Effect, false);
            });
        }

        /// <summary>
        /// True when the effect came from something only this client is running: our own player,
        /// a card in our hand, one of our exhibits. Anything enemy-side is simulated identically
        /// on every client and must not be replicated.
        /// </summary>
        private static bool IsLocalPlayerSource(GameEntity source, BattleController battle)
        {
            switch (source)
            {
                case null:
                    return false;
                case EnemyUnit _:
                    return false;
                case PlayerUnit player:
                    return player == battle.Player;

                // Thank you Junko, it's not like you weren't already too complex.
                // JunkoColor is Overflowing Blemishes, a status effect that exclusively applies to the enemy,
                // and yet the game has decided that it should live on the player instead.
                case JunkoColor _:
                    return false;

                case StatusEffect effect:
                    return effect.Owner == battle.Player;
                case Card _:
                case Exhibit _:
                case UltimateSkill _:
                case Doll _: // In 5 hours this will be relevant
                    return true;
                default:
                    return false;
            }
        }
    }

    /// <summary>
    /// Syncs and ticks stuff that should update in battle, such as player emotes, enemy HP, your current hand state for other players to see, etc.
    /// </summary>
    public static class MpBattleDriver
    {
        public static void Update()
        {
            if (!MpSession.IsActive)
            {
                return;
            }

            MpBattleSync.Update();
            MpJunko.Tick();
            MpDownedPlayers.Tick();
            UI.MpAllyUnits.Tick();
            UI.MpEmotes.Update();
            UI.MpHandView.Tick();
        }
    }
}
