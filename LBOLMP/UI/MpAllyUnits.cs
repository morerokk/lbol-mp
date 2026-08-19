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
        }

        private static readonly Dictionary<int, Ally> Allies = new Dictionary<int, Ally>();

        /// <summary>
        /// Multiplier on any sound effect played for somebody else's character. Lowers sound volume a bit when coming from other players.
        /// </summary>
        private const float AllyVolume = 0.5f;

        /// <summary>
        /// Where each extra player stands, relative to the local player's root.
        /// </summary>
        private static readonly Vector2[] Offsets =
        {
            new Vector2(-1.35f, -1.45f),
            new Vector2(-2.60f, 0.55f),
            new Vector2(-3f, -2.80f)
        };

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
                return;
            }

            var director = GameDirector.Instance;
            if (director == null || director.PlayerUnitView == null || director.playerRoot == null)
            {
                if (Allies.Count > 0)
                {
                    DespawnAll();
                }
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

            int slot = 0;
            foreach (var player in MpSession.ConnectedPlayers)
            {
                if (player.IsLocal)
                {
                    continue;
                }

                if (!Allies.ContainsKey(player.Id) && !string.IsNullOrEmpty(player.CharacterId))
                {
                    Spawn(player, slot);
                }
                slot++;
            }
        }

        private static void Spawn(MpPlayer player, int slot)
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

            LoadAllyAsync(ally, player, slot).Forget();
        }

        private static async UniTask LoadAllyAsync(Ally ally, MpPlayer player, int slot)
        {
            try
            {
                var director = GameDirector.Instance;

                // Parented to the player root so the pair moves together whenever the director
                // repositions the player for a scene.
                var root = new GameObject($"MpAlly_{player.Id}_{ally.CharacterId}");
                root.transform.SetParent(director.playerRoot, false);
                var offset = Offsets[Mathf.Clamp(slot, 0, Offsets.Length - 1)];
                root.transform.localPosition = new Vector3(offset.x, offset.y, 0f);
                root.transform.localScale = Vector3.one * 0.92f;
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
                    view?.DeathAnimation();
                }
                else if (newHp > 0 && unit.Status != UnitStatus.Alive)
                {
                    Revive(ally);
                }
            });
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

            view._invincible = false;
            view.SpineIdle(false);
        }

        /// <summary>
        /// Mirror the owner's buffs and debuffs onto the ally so their status widget reads the
        /// same as it does on their own screen.
        /// </summary>
        private static void SyncStatusEffects(Ally ally, MpBattleSeat seat)
        {
            var unit = ally.Unit;
            var wanted = new Dictionary<string, (int Level, int Duration, string SourceCardId)>();

            foreach (var encoded in seat.StatusEffects)
            {
                var parts = encoded.Split(':');
                if (parts.Length < 3)
                {
                    continue;
                }
                int.TryParse(parts[1], out int level);
                int.TryParse(parts[2], out int duration);
                wanted[parts[0]] = (level, duration, parts.Length > 3 ? parts[3] : string.Empty);
            }

            foreach (var existing in unit.StatusEffects.ToList())
            {
                if (!wanted.ContainsKey(existing.Id))
                {
                    unit._statusEffects.Remove(existing);
                    existing.Owner = null;
                    ally.View?.OnRemoveStatusEffect(existing);
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

                    // Fix midsummer flowers tooltip
                    if (!string.IsNullOrEmpty(entry.Value.SourceCardId))
                    {
                        effect.SourceCard = Library.TryCreateCard(entry.Value.SourceCardId, false);
                    }

                    effect.Owner = unit;
                    unit._statusEffects.Add(effect);
                    ally.View?.OnAddStatusEffect(effect, StatusEffectAddResult.Added);
                }
                else
                {
                    if (current.HasLevel && entry.Value.Level >= 0 && current.Level != entry.Value.Level)
                    {
                        current.Level = entry.Value.Level;
                    }
                    if (current.HasDuration && entry.Value.Duration >= 0 && current.Duration != entry.Value.Duration)
                    {
                        current.Duration = entry.Value.Duration;
                    }
                }
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
