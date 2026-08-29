using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Cysharp.Threading.Tasks;
using LBOLMP.Net;
using LBOLMP.Session;
using LBOLMP.Session.Battle;
using LBoL.Base;
using LBoL.ConfigData;
using LBoL.Core;
using LBoL.Core.Cards;
using LBoL.Core.StatusEffects;
using LBoL.Core.Units;
using LBoL.Presentation;
using LBoL.Presentation.Effect;
using LBoL.Presentation.UI;
using LBoL.Presentation.UI.Panels;
using LBoL.Presentation.Units;
using UnityEngine;

namespace LBOLMP.UI
{
    /// <summary>
    /// Puts the rest of the party on the play field as real character units.
    /// </summary>
    public static class MpAllyUnits
    {
        private sealed class Ally
        {
            public int PlayerId;
            public string CharacterId;
            public PlayerUnit Unit;
            public UnitView View;
            public GameObject Root;
            public bool Loading;
            public bool Shooting;

            /// <summary>Whether this ally is off stage, mirroring the local player.</summary>
            public bool Hidden;

            public int LastHp = int.MinValue;
            public int LastBlock = int.MinValue;
            public int LastShield = int.MinValue;

            /// <summary>Whether we have seen a status effect list from this player yet.</summary>
            public bool StatusPrimed;
        }

        private static readonly Dictionary<int, Ally> Allies = new Dictionary<int, Ally>();

        /// <summary>
        /// Multiplier on any sound effect played for somebody else's character. Lowers sound volume a bit when coming from other players.
        /// </summary>
        private const float AllyVolume = 0.5f;

        /// <summary>
        /// How big everybody else is drawn. You are always full size, in either layout.
        /// </summary>
        private const float AllyScale = 0.92f;

        /// <summary>
        /// Where each extra player stands, relative to the local player's root.
        /// </summary>
        private static readonly Vector2[] Offsets =
        {
            new Vector2(-1.35f, -1.75f),
            new Vector2(-1.35f, 0.8f),
            new Vector2(-2.7f, -0.70f),
            new Vector2(-2.7f, 1.85f),
            //new Vector2(-4.05f, 0.35f)
            // Player 6 would cover up the UI, so she's placed above and slightly behind the local player instead
            new Vector2(-0.6f, 2f)
        };

        /// <summary>The local player's view, or null.</summary>
        private static UnitView _displaced;

        /// <summary>Where the game had the local player's view before we moved it.</summary>
        private static Vector3 _displacedHome;

        private static bool SeatedLayout =>
            MpPlugin.SharedPartyPositions != null && MpPlugin.SharedPartyPositions.Value;

        /// <summary>
        /// Where a player is (literally). <c>Vector2.zero</c> is the front, where a single player game would put you.
        ///
        /// By default everybody is the main character of their own screen, so you are on the front
        /// position and the rest of the party queues up behind you. If SharedPartyPositions is enabled, you are in the position you are "actually" in in the party.
        /// </summary>
        private static Vector2 StandingSpot(int playerId)
        {
            int slot;
            if (SeatedLayout)
            {
                // Seats are places in the roster rather than player ids, so this survives somebody
                // leaving and rejoining under a different number.
                int seat = MpSession.SeatIndexOf(playerId);
                slot = seat < 0 ? Offsets.Length - 1 : seat - 1;
            }
            else
            {
                slot = QueueIndexOf(playerId);
            }

            return slot < 0 ? Vector2.zero : Offsets[Mathf.Clamp(slot, 0, Offsets.Length - 1)];
        }

        /// <summary>
        /// This player's place in the queue behind the local player, or -1 for the local player.
        /// </summary>
        private static int QueueIndexOf(int playerId)
        {
            int slot = 0;
            foreach (var player in MpSession.ConnectedPlayers)
            {
                if (player.IsLocal)
                {
                    continue;
                }

                if (player.Id == playerId)
                {
                    return slot;
                }

                slot++;
            }

            return -1;
        }

        /// <summary>
        /// Puts everybody in the right position. Unfortunately, this has to run every frame rather than once at spawn,
        /// because seats shift when somebody leaves.
        /// </summary>
        private static void PlaceEveryone(GameDirector director)
        {
            foreach (var ally in Allies.Values)
            {
                if (ally.Root == null)
                {
                    continue;
                }

                var spot = StandingSpot(ally.PlayerId);
                ally.Root.transform.localPosition = new Vector3(spot.x, spot.y, 0f);
            }

            PlaceLocalPlayer(director.PlayerUnitView);
        }

        /// <summary>
        /// Moves the local player off the front position into their seat, or puts them back.
        /// </summary>
        private static void PlaceLocalPlayer(UnitView view)
        {
            if (!SeatedLayout || view == null)
            {
                ReturnLocalPlayer();
                return;
            }

            // Remember the original origin
            if (!ReferenceEquals(view, _displaced))
            {
                _displaced = view;
                _displacedHome = view.transform.localPosition;
            }

            var spot = StandingSpot(MpNet.LocalPlayerId);
            view.transform.localPosition = _displacedHome + new Vector3(spot.x, spot.y, 0f);
        }

        /// <summary>Puts the local player back on their usual spot, if we moved them off it.</summary>
        private static void ReturnLocalPlayer()
        {
            if (_displaced != null)
            {
                _displaced.transform.localPosition = _displacedHome;
            }

            _displaced = null;
        }

        /// <summary>The mirror unit for a player, usable as the source of a replicated action.</summary>
        public static PlayerUnit GetUnit(int playerId) =>
            Allies.TryGetValue(playerId, out var ally) ? ally.Unit : null;

        /// <summary>
        /// True if this unit is somebody else's mirror rather than a real participant here.
        /// </summary>
        public static bool IsMirror(Unit unit)
        {
            if (unit == null)
            {
                return false;
            }

            foreach (var ally in Allies.Values)
            {
                if (ReferenceEquals(ally.Unit, unit))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>The on-screen view for a player, once their model has finished loading.</summary>
        public static UnitView GetView(int playerId) =>
            Allies.TryGetValue(playerId, out var ally) && !ally.Loading ? ally.View : null;

        /// <summary>
        /// Which player a view on stage belongs to, or <c>InvalidPlayerId</c> for the local
        /// player's own character and for anything that is not an ally at all.
        /// </summary>
        public static int PlayerFor(UnitView view)
        {
            if (view == null)
            {
                return Net.MpConstants.InvalidPlayerId;
            }

            foreach (var ally in Allies.Values)
            {
                if (ReferenceEquals(ally.View, view))
                {
                    return ally.PlayerId;
                }
            }
            return Net.MpConstants.InvalidPlayerId;
        }

        /// <summary>
        /// Which player a mirror unit belongs to, or <c>InvalidPlayerId</c> for anything that is
        /// not an ally.
        /// </summary>
        public static int PlayerFor(Unit unit)
        {
            if (unit == null)
            {
                return Net.MpConstants.InvalidPlayerId;
            }

            foreach (var ally in Allies.Values)
            {
                if (ReferenceEquals(ally.Unit, unit))
                {
                    return ally.PlayerId;
                }
            }
            return Net.MpConstants.InvalidPlayerId;
        }

        /// <summary>
        /// Which player a unit belongs to, the one this client is playing included.
        /// </summary>
        /// <remarks>
        /// <see cref="PlayerFor(Unit)"/> knows only the mirrors: our own unit is the game's own and
        /// was never registered here, so it comes back unrecognised from that one.
        /// </remarks>
        public static int PlayerForIncludingLocal(Unit unit)
        {
            if (unit == null)
            {
                return MpConstants.InvalidPlayerId;
            }

            int playerId = PlayerFor(unit);
            if (playerId != MpConstants.InvalidPlayerId)
            {
                return playerId;
            }

            var director = GameDirector.Instance;
            return director != null && ReferenceEquals(unit, director.PlayerUnitView?.Unit)
                ? MpNet.LocalPlayerId
                : MpConstants.InvalidPlayerId;
        }

        /// <summary>Every loaded ally view, for the targeting arrow.</summary>
        public static IEnumerable<UnitView> LoadedViews =>
            Allies.Values.Where(a => !a.Loading && a.View != null && !a.Hidden).Select(a => a.View);

        /// <summary>The on-screen view for an ally unit, so the director can find it.</summary>
        public static UnitView GetView(Unit unit)
        {
            if (unit == null)
            {
                return null;
            }

            foreach (var ally in Allies.Values)
            {
                if (ReferenceEquals(ally.Unit, unit))
                {
                    return ally.View;
                }
            }
            return null;
        }

        /// <summary>
        /// Called every frame. Spawns allies once the local character is on screen and clears them when it is not,
        /// which is also how we survive the director tearing scene down between loads.
        /// </summary>
        public static void Tick()
        {
            if (!MpSession.IsActive || !MpSession.IsInRun)
            {
                if (Allies.Count > 0)
                {
                    DespawnAll();
                }
                ReturnLocalPlayer();
                return;
            }

            var director = GameDirector.Instance;
            if (director == null || director.PlayerUnitView == null || director.playerRoot == null)
            {
                if (Allies.Count > 0)
                {
                    DespawnAll();
                }
                ReturnLocalPlayer();
                return;
            }

            foreach (var id in Allies.Keys.ToList())
            {
                var player = MpSession.Get(id);
                if (player == null || player.State == MpPlayerState.Disconnected)
                {
                    Despawn(id);
                }
            }

            foreach (var player in MpSession.ConnectedPlayers)
            {
                if (player.IsLocal)
                {
                    continue;
                }

                if (!Allies.ContainsKey(player.Id) && !string.IsNullOrEmpty(player.CharacterId))
                {
                    Spawn(player);
                }
            }

            PlaceEveryone(director);
        }

        private static void Spawn(MpPlayer player)
        {
            PlayerUnit unit = MpSafe.Run("MpAllyUnits.Create",
                () => Library.TryCreatePlayerUnit(player.CharacterId), null);

            if (unit == null)
            {
                MpPlugin.Log.LogWarning($"Unknown character id over the wire: {player.CharacterId}");
                // Park a placeholder so we don't retry the lookup every single frame.
                Allies[player.Id] = new Ally { PlayerId = player.Id, CharacterId = player.CharacterId };
                return;
            }

            int maxHp = player.MaxHp > 0 ? player.MaxHp : unit.MaxHp;
            int hp = player.Hp > 0 ? Mathf.Min(player.Hp, maxHp) : maxHp;
            unit.SetMaxHp(hp, maxHp);

            var ally = new Ally
            {
                PlayerId = player.Id,
                CharacterId = player.CharacterId,
                Unit = unit,
                Loading = true,
                LastHp = hp
            };
            Allies[player.Id] = ally;

            LoadAllyAsync(ally, player).Forget();
        }

        private static async UniTask LoadAllyAsync(Ally ally, MpPlayer player)
        {
            try
            {
                var director = GameDirector.Instance;

                // Parented to the player root so the pair moves together whenever the director
                // repositions the player for a scene.
                var root = new GameObject($"MpAlly_{player.Id}_{ally.CharacterId}");
                root.transform.SetParent(director.playerRoot, false);
                var spot = StandingSpot(player.Id);
                root.transform.localPosition = new Vector3(spot.x, spot.y, 0f);
                root.transform.localScale = Vector3.one * AllyScale;
                ally.Root = root;

                var instance = UnityEngine.Object.Instantiate(director.unitPrefab, root.transform);
                var view = instance.GetComponent<UnitView>();
                view.Unit = ally.Unit;

                var hud = UiManager.GetPanel<UnitStatusHud>();
                if (hud != null)
                {
                    view.SetStatusWidget(hud.CreateStatusWidget(ally.Unit), 1f);
                    view.SetInfoWidget(hud.CreateInfoWidget(ally.Unit), 1f);
                }

                view.IsHidden = false;
                await view.LoadUnitModelAsync(ally.Unit.ModelName, true, null);

                ally.Unit.SetView(view);
                view.SetPlayerHpBarLength(ally.Unit.MaxHp);

                ally.View = view;
                ally.Loading = false;

                ally.Hidden = director.PlayerUnitView != null && director.PlayerUnitView.IsHidden;
                if (ally.Hidden)
                {
                    view.IsHidden = true;
                }
                else
                {
                    view.Show(true);
                    view.SetStatusVisible(true, true);
                }

                RestoreEffectLoops(ally);

                MpPlugin.Log.LogInfo($"Spawned ally unit for {player.Name} ({ally.CharacterId})");
            }
            catch (Exception e)
            {
                MpPlugin.Log.LogError($"Failed to spawn ally unit for player {player.Id}: {e}");
                ally.Loading = false;
            }
        }

        private static void Despawn(int playerId)
        {
            if (!Allies.TryGetValue(playerId, out var ally))
            {
                return;
            }

            MpSafe.Run("MpAllyUnits.Despawn", () =>
            {
                if (ally.View != null)
                {
                    UnityEngine.Object.Destroy(ally.View.gameObject);
                }
                if (ally.Root != null)
                {
                    UnityEngine.Object.Destroy(ally.Root);
                }
            });

            Allies.Remove(playerId);
        }

        public static void DespawnAll()
        {
            foreach (var id in Allies.Keys.ToList())
            {
                Despawn(id);
            }
        }

        /// <summary>
        /// Advance the ally views' own clocks, alongside the ones the director already ticks.
        /// </summary>
        public static void TickViews()
        {
            if (Allies.Count == 0)
            {
                return;
            }

            foreach (var ally in Allies.Values)
            {
                var view = ally.View;
                if (view == null || ally.Loading)
                {
                    continue;
                }

                MpSafe.Run("MpAllyUnits.TickView", () => view.Tick());
            }
        }

        /// <summary>
        /// Take the fight off every mirror. Called once the battle is over.
        /// </summary>
        public static void ClearCombatState()
        {
            foreach (var ally in Allies.Values.ToList())
            {
                SyncOutOfBattle(MpSession.Get(ally.PlayerId));
            }
        }

        /// <summary>
        /// Put a player's out-of-combat status on their mirror unit.
        /// </summary>
        /// Between battles there are no seats, so <see cref="SyncVitals"/> has nothing to read and
        /// the mirrors would otherwise keep whatever they were showing when the fight ended.     
        public static void SyncOutOfBattle(MpPlayer player)
        {
            if (player == null || !Allies.TryGetValue(player.Id, out var ally) || ally.Unit == null)
            {
                return;
            }

            MpSafe.Run("MpAllyUnits.SyncOutOfBattle", () =>
            {
                var unit = ally.Unit;
                var view = ally.View;

                StripStatusEffects(ally);

                if (unit.Block != 0 || unit.Shield != 0)
                {
                    unit.Block = 0;
                    unit.Shield = 0;
                    view?.UpdateShieldColliders();
                    view?._statusWidget?.OnBlockShieldChanged();
                }

                ally.LastBlock = 0;
                ally.LastShield = 0;

                // Nobody has reported for them yet. Leave the health alone rather than zeroing it.
                if (player.MaxHp <= 0)
                {
                    return;
                }

                if (unit.MaxHp != player.MaxHp)
                {
                    unit.SetMaxHp(Mathf.Clamp(player.Hp, 0, player.MaxHp), player.MaxHp);
                    view?.SetPlayerHpBarLength(player.MaxHp);
                    view?.OnMaxHpChanged();
                }

                int hp = Mathf.Clamp(player.Hp, 0, player.MaxHp);
                int delta = ally.LastHp == int.MinValue ? 0 : hp - ally.LastHp;

                unit.Hp = hp;
                ally.LastHp = hp;

                // Both of these only tween the bar towards what we just wrote. The damage numbers
                // and the flinch come from elsewhere and stay out of it.
                if (view != null && delta < 0)
                {
                    view.OnDamageReceived(DamageInfo.HpLose(-delta, true));
                }
                else if (view != null && delta > 0)
                {
                    view.OnHealingReceived(delta);
                }

                // Went down at the boss and was patched up on the way out.
                if (hp > 0 && unit.Status != UnitStatus.Alive)
                {
                    Revive(ally);
                }
            });
        }

        /// <summary>
        /// Take every mirrored buff and debuff off an ally, the same way SyncStatusEffects drops
        /// the ones a seat has stopped reporting.
        /// </summary>
        private static void StripStatusEffects(Ally ally)
        {
            var unit = ally.Unit;
            if (unit == null)
            {
                return;
            }

            foreach (var existing in unit.StatusEffects.ToList())
            {
                unit._statusEffects.Remove(existing);
                existing.Owner = null;
                ally.View?.OnRemoveStatusEffect(existing);
                StopUnitEffects(ally, existing);
            }
        }

        /// <summary>Push a seat's networked vitals onto its on-screen unit (puts a player's HP onto their actual mirror unit).</summary>
        public static void SyncVitals(MpBattleSeat seat)
        {
            if (!Allies.TryGetValue(seat.PlayerId, out var ally) || ally.Unit == null)
            {
                return;
            }

            MpSafe.Run("MpAllyUnits.SyncVitals", () =>
            {
                var unit = ally.Unit;
                var view = ally.View;

                if (seat.MaxHp > 0 && unit.MaxHp != seat.MaxHp)
                {
                    unit.SetMaxHp(Mathf.Clamp(seat.Hp, 0, seat.MaxHp), seat.MaxHp);
                    view?.SetPlayerHpBarLength(seat.MaxHp);
                    view?.OnMaxHpChanged();
                }

                int newHp = seat.MaxHp > 0 ? Mathf.Clamp(seat.Hp, 0, seat.MaxHp) : seat.Hp;
                int delta = ally.LastHp == int.MinValue ? 0 : newHp - ally.LastHp;

                int newBlock = Mathf.Max(0, seat.Block);
                int newShield = Mathf.Max(0, seat.Shield);
                bool guardChanged = newBlock != ally.LastBlock || newShield != ally.LastShield;

                unit.Block = newBlock;
                unit.Shield = newShield;
                unit.Hp = newHp;

                // Animates the HP bar towards the values just written. The flinch that goes with it
                // is deliberately not here: see PlayHit.
                if (delta < 0 && view != null)
                {
                    view.OnDamageReceived(DamageInfo.HpLose(-delta, true));
                }
                else if (delta > 0 && view != null)
                {
                    view.OnHealingReceived(delta);
                }

                if (guardChanged && view != null)
                {
                    view.UpdateShieldColliders();
                    view._statusWidget?.OnBlockShieldChanged();

                    if (ally.LastShield != int.MinValue)
                    {
                        bool gained = false;

                        if (newShield > ally.LastShield)
                        {
                            view.CreateLocalShieldEffect("GainShield", true);
                            gained = true;
                        }
                        if (newBlock > ally.LastBlock)
                        {
                            view.CreateLocalShieldEffect("GainBlock", false);
                            gained = true;
                        }

                        if (gained)
                        {
                            AudioManager.PlaySfx("ShieldCast", AllyVolume);
                        }
                    }
                }

                ally.LastHp = newHp;
                ally.LastBlock = newBlock;
                ally.LastShield = newShield;

                SyncStatusEffects(ally, seat);

                if (newHp <= 0 && unit.Status == UnitStatus.Alive)
                {
                    unit.Status = UnitStatus.Dead;
                    PlayDeath(ally);
                }
                else if (newHp > 0 && unit.Status != UnitStatus.Alive)
                {
                    Revive(ally);
                }
            });
        }

        /// <summary>
        /// Plays the death animation on other players.
        /// </summary>
        private static void PlayDeath(Ally ally)
        {
            if (ally.View == null || ally.Loading)
            {
                return;
            }

            MpSafe.Run("MpAllyUnits.PlayDeath",
                () => MpPlugin.Instance.StartCoroutine(DeathRoutine(ally.View)));
        }

        /// <summary>
        /// Mirrors the game's own death sequence but at 50% volume.
        /// </summary>
        private static IEnumerator DeathRoutine(UnitView view)
        {
            view.SetStatusVisible(false);
            view.DeathAnimation();

            string effect = "UnitDeath";
            string sfx = "UnitDeathExplode";
            float delay = 1f;

            if (view._dieLevel == 0)
            {
                effect = "UnitDeathSmall";
                sfx = "UnitDeathExplodeSmall";
                delay = 0.5f;
            }
            else if (view._dieLevel == 2)
            {
                effect = "UnitDeathLarge";
                sfx = "UnitDeathExplodeLarge";
                delay = 1.8f;
            }

            EffectManager.CreateEffect(effect, view.transform, 0f, null, false, true);
            yield return new WaitForSeconds(delay);

            AudioManager.PlaySfx(sfx, AllyVolume);
            view.Die();
        }

        /// <summary>
        /// Restore a revived player's unit view.
        /// </summary>
        internal static void Undie(UnitView view)
        {
            view._invincible = false;
            view.Show(true);
            view.effectRootIgnoreHiding.gameObject.SetActive(true);
            view.selectorCollider.gameObject.SetActive(true);
            view.SpineIdle(false);
        }

        public static void Revive(int playerId)
        {
            if (Allies.TryGetValue(playerId, out var ally))
            {
                MpSafe.Run("MpAllyUnits.Revive", () => Revive(ally));
            }
        }

        private static void Revive(Ally ally)
        {
            if (ally.Unit != null)
            {
                ally.Unit.Status = UnitStatus.Alive;
            }

            var view = ally.View;
            if (view == null)
            {
                return;
            }

            Undie(view);
        }

        /// <summary>
        /// Mirror the owner's buffs and debuffs onto the ally so their status widget reads the
        /// same as it does on their own screen.
        /// </summary>
        private static void SyncStatusEffects(Ally ally, MpBattleSeat seat)
        {
            var unit = ally.Unit;
            var wanted = new Dictionary<string, (int Level, int Duration, string SourceCardId, int Count)>();

            foreach (var encoded in seat.StatusEffects)
            {
                var parts = encoded.Split(':');
                if (parts.Length < 3)
                {
                    continue;
                }
                int.TryParse(parts[1], out int level);
                int.TryParse(parts[2], out int duration);
                int count = -1;
                if (parts.Length > 4)
                {
                    int.TryParse(parts[4], out count);
                }
                wanted[parts[0]] =
                    (level, duration, parts.Length > 3 ? parts[3] : string.Empty, count);
            }

            foreach (var existing in unit.StatusEffects.ToList())
            {
                if (!wanted.ContainsKey(existing.Id))
                {
                    unit._statusEffects.Remove(existing);
                    existing.Owner = null;
                    ally.View?.OnRemoveStatusEffect(existing);
                    StopUnitEffects(ally, existing);
                }
            }

            foreach (var entry in wanted)
            {
                var current = unit.StatusEffects.FirstOrDefault(s => s.Id == entry.Key);
                if (current == null)
                {
                    var effect = Library.TryCreateStatusEffect(entry.Key);
                    if (effect == null)
                    {
                        continue;
                    }
                    if (effect.HasLevel && entry.Value.Level >= 0)
                    {
                        effect.Level = entry.Value.Level;
                    }
                    if (effect.HasDuration && entry.Value.Duration >= 0)
                    {
                        effect.Duration = entry.Value.Duration;
                    }
                    if (effect.HasCount && entry.Value.Count >= 0)
                    {
                        effect.Count = entry.Value.Count;
                    }

                    // Fix midsummer flowers tooltip
                    if (!string.IsNullOrEmpty(entry.Value.SourceCardId))
                    {
                        effect.SourceCard = Library.TryCreateCard(entry.Value.SourceCardId, false);
                        Session.MpCardOwner.Set(effect.SourceCard, ally.PlayerId);
                    }

                    effect.Owner = unit;
                    unit._statusEffects.Add(effect);
                    ally.View?.OnAddStatusEffect(effect, StatusEffectAddResult.Added);
                    StartUnitEffects(ally, effect, ally.StatusPrimed);
                }
                else
                {
                    // Ensure that multiple applications of a status replay the effect each time
                    bool gained = ally.StatusPrimed &&
                        ((current.HasLevel && entry.Value.Level >= 0 && entry.Value.Level > current.Level) ||
                         (current.HasDuration && entry.Value.Duration >= 0 && entry.Value.Duration > current.Duration));

                    if (current.HasLevel && entry.Value.Level >= 0 && current.Level != entry.Value.Level)
                    {
                        current.Level = entry.Value.Level;
                    }
                    if (current.HasDuration && entry.Value.Duration >= 0 && current.Duration != entry.Value.Duration)
                    {
                        current.Duration = entry.Value.Duration;
                    }
                    if (current.HasCount && entry.Value.Count >= 0 && current.Count != entry.Value.Count)
                    {
                        current.Count = entry.Value.Count;
                    }

                    if (gained && ally.View != null)
                    {
                        Announce(ally.View, current);
                    }
                }
            }

            ally.StatusPrimed = true;
        }

        // Burst FX that should be replicated
        private const string BurstId = "Burst";
        private const string BurstLoop = "MarisaBurstLoop";
        private const string BurstStart = "MarisaBurstStart";
        private const string BurstEnd = "MarisaBurstEnd";
        private const string BurstGainSfx = "MarisaBurst";
        private const string BurstLoseSfx = "MarisaBurstLose";

        /// <summary>
        /// Whitelist of status effects whose special effects should be replayed on other clients (like Graze or Mental States)
        /// </summary>
        private static readonly HashSet<string> Announced = new HashSet<string>
        {
            BurstId,
            "MoodPassion", "MoodPeace", "MoodEpiphany",
            "Graze",
            "Invincible", "InvincibleEternal", "Grace", "Immune",
            "Firepower", "TempFirepower"
        };

        /// <summary>
        /// Play a status effect's special effects and audio cues on allies, at half volume.
        /// </summary>
        private static void StartUnitEffects(Ally ally, StatusEffect effect, bool audible)
        {
            var view = ally.View;
            if (view == null)
            {
                return;
            }

            if (audible)
            {
                Announce(view, effect);
            }

            string loop = effect.UnitEffectName;
            if (!string.IsNullOrEmpty(loop) && view.TryPlayEffectLoop(loop))
            {
                view.SendEffectMessage(loop, "OnPropertyChanged", effect);
            }

            if (effect.Id == BurstId)
            {
                view.TryPlayEffectLoop(BurstLoop);
            }
        }

        /// <summary>Stop special FX like mental states or burst and graze.</summary>
        private static void StopUnitEffects(Ally ally, StatusEffect effect)
        {
            var view = ally.View;
            if (view == null)
            {
                return;
            }

            EndLoop(view, effect.UnitEffectName);

            if (effect.Id == BurstId)
            {
                view.PlayEffectOneShot(BurstEnd, 0f);
                AudioManager.PlaySfx(BurstLoseSfx, AllyVolume);
                EndLoop(view, BurstLoop);
            }
        }

        /// <summary>
        /// Plays one-shot effects like firepower gain.
        /// </summary>
        private static void Announce(UnitView view, StatusEffect effect)
        {
            var config = effect.Config;

            if (config != null && !string.IsNullOrEmpty(config.VFX) && config.VFX != "Default")
            {
                EffectManager.CreateEffect(config.VFX, view.EffectRoot, 0f, null, false, true);
            }

            if (effect.Id == BurstId)
            {
                // Burst's own flash and cue are not in its config, so they go on by hand.
                view.PlayEffectOneShot(BurstStart, 0f);
                AudioManager.PlaySfx(BurstGainSfx, AllyVolume);
            }
            else if (Announced.Contains(effect.Id) && config != null
                && !string.IsNullOrEmpty(config.SFX) && config.SFX != "Default")
            {
                AudioManager.PlaySfx(config.SFX, AllyVolume);
            }
        }

        private static void EndLoop(UnitView view, string effectName)
        {
            if (!string.IsNullOrEmpty(effectName) && view._effectDictionary.ContainsKey(effectName))
            {
                view.EndEffectLoop(effectName, true);
            }
        }

        /// <summary>
        /// Start status FX loops for an ally's statuses at any arbitrary point in time, even if their model loads in later.
        /// </summary>
        private static void RestoreEffectLoops(Ally ally)
        {
            if (ally.Unit == null)
            {
                return;
            }

            foreach (var effect in ally.Unit.StatusEffects.ToList())
            {
                StartUnitEffects(ally, effect, false);
            }
        }

        //--
        // playboard presence
        //--

        /// <summary>
        /// Play the entrance animation alongside the local player.
        /// </summary>
        public static void PlayDebut()
        {
            foreach (var ally in Allies.Values)
            {
                var view = ally.View;
                if (view == null || ally.Loading || ally.Hidden)
                {
                    continue;
                }

                MpSafe.Run("MpAllyUnits.PlayDebut", () => view.DebutAnimation());
            }
        }

        /// <summary>
        /// Follow the local player off and back onto the stage (for Gaps).
        /// </summary>
        public static void SetHidden(bool hidden, bool withStatus)
        {
            foreach (var ally in Allies.Values)
            {
                ally.Hidden = hidden;

                var view = ally.View;
                if (view == null)
                {
                    continue;
                }

                MpSafe.Run("MpAllyUnits.SetHidden", () =>
                {
                    if (hidden)
                    {
                        view.IsHidden = true;
                    }
                    else
                    {
                        view.Show(withStatus);
                    }
                });
            }
        }

        /// <summary>
        /// Replay a one-shot effect the game played on an ally's own character.
        /// </summary>
        public static void PlayEffect(int playerId, string effectName, float delay)
        {
            if (string.IsNullOrEmpty(effectName))
            {
                return;
            }

            if (!Allies.TryGetValue(playerId, out var ally) || ally.View == null || ally.Loading
                || ally.Hidden)
            {
                return;
            }

            MpSafe.Run("MpAllyUnits.PlayEffect",
                () => ally.View.PlayEffectOneShot(effectName, delay));
        }

        /// <summary>
        /// Replay the animation an ally's character plays as they use a card.
        /// Attacks already animate separately through their guns.
        /// </summary>
        public static void PlayAnimation(int playerId, string animationName)
        {
            if (string.IsNullOrEmpty(animationName))
            {
                return;
            }

            if (!Allies.TryGetValue(playerId, out var ally) || ally.View == null || ally.Loading)
            {
                return;
            }

            if (ally.Shooting || ally.View._status != UnitView.ShootStatus.Idle)
            {
                return;
            }

            MpSafe.Run("MpAllyUnits.PlayAnimation", () => ally.View.PlayAnimation(animationName));
        }

        /// <summary>
        /// React to a hit the way its owner's own client did.
        /// </summary>
        public static void PlayHit(int playerId, DamageInfo info)
        {
            if (!Allies.TryGetValue(playerId, out var ally) || ally.View == null || ally.Loading)
            {
                return;
            }

            MpSafe.Run("MpAllyUnits.PlayHit", () =>
            {
                var view = ally.View;

                view.UpdateShieldColliders();
                view.ComingDamage = info;
                view.Hit();
            });
        }

        /// <summary>Point the ally at whatever they just aimed at.</summary>
        public static void AimAt(int playerId, int targetEnemyIndex)
        {
            if (!Allies.TryGetValue(playerId, out var ally) || ally.View == null)
            {
                return;
            }

            MpSafe.Run("MpAllyUnits.AimAt", () => AimInternal(ally, targetEnemyIndex));
        }

        private static UnitView AimInternal(Ally ally, int targetEnemyIndex)
        {
            var battle = GameMaster.Instance?.CurrentGameRun?.Battle;
            if (battle == null || ally.View == null)
            {
                return null;
            }

            var target = battle.EnemyGroup.FirstOrDefault(e => e.Index == targetEnemyIndex && e.IsAlive)
                         ?? battle.FirstAliveEnemy;
            var targetView = target != null ? GameDirector.GetEnemy(target) : null;
            if (targetView == null)
            {
                return null;
            }

            ally.View.Target = targetView;
            ally.View.Targets = new List<UnitView> { targetView };
            return targetView;
        }

        /// <summary>
        /// Play an ally's gun visually-only.
        /// </summary>
        public static void PlayShoot(int playerId, string gunName, int targetEnemyIndex)
        {
            if (string.IsNullOrEmpty(gunName) || gunName == "Instant" || gunName == "Empty")
            {
                return;
            }

            if (!Allies.TryGetValue(playerId, out var ally) || ally.View == null || ally.Shooting)
            {
                return;
            }

            MpSafe.Run("MpAllyUnits.PlayShoot", () =>
            {
                var targetView = AimInternal(ally, targetEnemyIndex);
                if (targetView == null)
                {
                    return;
                }

                StageGunHit(targetView, gunName);

                ally.Shooting = true;
                MpPlugin.Instance.StartCoroutine(ShootRoutine(ally, gunName));
            });
        }

        /// <summary>
        /// Hard cap on cosmetic shots (thanks Cirno)
        /// </summary>
        private const float ShootTimeLimit = 8f;

        private static IEnumerator ShootRoutine(Ally ally, string gunName)
        {
            bool started = MpSafe.Run("MpAllyUnits.StartShoot", () =>
            {
                MpPlugin.Instance.StartCoroutine(ally.View.Shoot(gunName, GunType.Single));
                return true;
            }, false);

            if (started)
            {
                float waited = 0f;
                while (waited < ShootTimeLimit
                       && ally.View != null
                       && ally.View._status != UnitView.ShootStatus.Idle)
                {
                    waited += Time.unscaledDeltaTime;
                    yield return null;
                }
            }

            MpSafe.Run("MpAllyUnits.EndShoot", () => ForceIdle(ally));
            ally.Shooting = false;
        }

        /// <summary>
        /// Last resort for a gun animation that never came back to idle. This is supposedly a vanilla bug too? Idk
        /// </summary>
        private static void ForceIdle(Ally ally)
        {
            var view = ally.View;
            if (view == null || view._status == UnitView.ShootStatus.Idle)
            {
                return;
            }

            MpPlugin.Log.LogWarning($"Ally shot never finished; forcing {ally.CharacterId} back to idle");

            view._shootCounting = false;
            view.ShowEndActs();
        }

        //--
        // Gun hit presentation
        //--

        private static GunHitArgs _allyGunHit;
        private static GunHitArgs _displacedGunHit;

        private static void StageGunHit(UnitView targetView, string gunName)
        {
            _displacedGunHit = GameDirector._gunHitArgs;
            _allyGunHit = new GunHitArgs(
                false,
                new List<(UnitView, DamageInfo)> { (targetView, DamageInfo.Attack(0f)) },
                gunName);
            GameDirector._gunHitArgs = _allyGunHit;
        }

        /// <summary>
        /// True if the hit being presented is an ally's cosmetic shot, in which case it has been
        /// dealt with here and the game's own presentation must not run.
        /// </summary>
        internal static bool TryHandleAllyGunHit()
        {
            var staged = GameDirector._gunHitArgs;

            if (staged == null)
            {
                return true;
            }

            if (!ReferenceEquals(staged, _allyGunHit))
            {
                return false;
            }

            GameDirector._gunHitArgs = _displacedGunHit;
            _allyGunHit = null;
            _displacedGunHit = null;

            foreach (var pair in staged.Pairs)
            {
                // Still end the hit pose, or the enemy stays flinching at the bullets.
                if (pair.Item1 != null)
                {
                    pair.Item1.HitEnd();
                }
            }

            return true;
        }

        /// <summary>
        /// Screen point just above an ally's head, in Unity's screen space (origin bottom-left),
        /// which is what <c>RectTransformUtility</c> expects.
        /// </summary>
        public static bool TryGetHeadScreenPoint(int playerId, out Vector2 position)
        {
            position = default;

            if (!Allies.TryGetValue(playerId, out var ally) || ally.View == null)
            {
                return false;
            }

            var camera = Camera.main;
            if (camera == null)
            {
                return false;
            }

            var anchor = ally.View.ChatPoint != null ? ally.View.ChatPoint : ally.View.transform;
            var screen = camera.WorldToScreenPoint(anchor.position);
            if (screen.z < 0f)
            {
                return false;
            }

            position = new Vector2(screen.x, screen.y);
            return true;
        }
    }
}
