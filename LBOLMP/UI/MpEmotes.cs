using LBOLMP.Net;
using LBOLMP.Session;
using LBOLMP.Session.Battle;
using LBOLMP.Session.Messages;
using LBoL.ConfigData;
using LBoL.Presentation;
using LBoL.Presentation.UI.Widgets;
using LBoL.Presentation.Units;
using UnityEngine;

namespace LBOLMP.UI
{
    /// <summary>
    /// Something to do with your hands while somebody else finishes their turn.
    /// </summary>
    /// Have you tried right-clicking the player that's taking forever? Because this will take a while
    public static class MpEmotes
    {
        private readonly struct Emote
        {
            internal readonly MpText Line;

            /// <summary>Animation to play alongside it, or null to just speak.</summary>
            internal readonly string Animation;

            /// <summary>If true, plays a clock tick SFX.</summary>
            internal readonly bool ClockTick;

            internal Emote(MpText line, string animation, bool clockTick)
            {
                Line = line;
                Animation = animation;
                ClockTick = clockTick;
            }
        }

        private static readonly Emote[] Emotes =
        {
            new Emote(MpText.EmoteNice, null, false),
            new Emote(MpText.EmoteNegative, null, false),
            new Emote(MpText.EmoteHurryUp, "skill", true)
        };

        private static readonly KeyCode[] Keys =
        {
            KeyCode.Alpha1, KeyCode.Alpha2, KeyCode.Alpha3
        };

        private static readonly KeyCode[] NumpadKeys =
        {
            KeyCode.Keypad1, KeyCode.Keypad2, KeyCode.Keypad3
        };

        /// <summary>How long a bubble stays up.</summary>
        private const float BubbleSeconds = 2.5f;

        private const float Cooldown = 1.2f;

        /// <summary>Half volume tick sound.</summary>
        private const float TickVolume = 0.5f;

        private static float _nextEmote;

        /// <summary>
        /// True while you're fidget spinning it rather than actually being able to play the game.
        /// This prevents keyboard users from pressing "3" and doing an emote when they just want to play a card.
        /// </summary>
        public static bool Available
        {
            get
            {
                if (!MpSession.IsActive || !MpBattleSync.InBattle)
                {
                    return false;
                }

                if (MpDownedPlayers.OutOfFight || MpBattleSync.AtEndOfBattleGate)
                {
                    return true;
                }

                return MpBattleSync.LocalTurnComplete
                       && !MpBattleSync.AllSeatsCompleted(MpBattleSync.CurrentRound);
            }
        }

        public static void Update()
        {
            if (!Available || Time.unscaledTime < _nextEmote)
            {
                return;
            }

            for (int i = 0; i < Emotes.Length; i++)
            {
                if (Input.GetKeyDown(Keys[i]) || Input.GetKeyDown(NumpadKeys[i]))
                {
                    MpSafe.Run("MpEmotes.Send", () => Send(i));
                    return;
                }
            }
        }

        private static void Send(int index)
        {
            _nextEmote = Time.unscaledTime + Cooldown;

            var emote = Emotes[index];

            // Shown here directly, because the emote message is not echoed back to its sender.
            Speak(GameDirector.Player, emote);
            MpNet.Send(new RemoteEmoteMessage { Emote = index });

            if (emote.Animation != null)
            {
                var view = GameDirector.Player;
                if (view != null)
                {
                    view.PlayAnimation(emote.Animation);
                }
            }
        }

        /// <summary>Somebody else emoted. Their pose arrives separately, on the animation channel.</summary>
        public static void Play(int playerId, int index)
        {
            if (index < 0 || index >= Emotes.Length)
            {
                return;
            }

            MpSafe.Run("MpEmotes.Play", () => Speak(MpAllyUnits.GetView(playerId), Emotes[index]));
        }

        private static void Speak(UnitView view, Emote emote)
        {
            if (view == null)
            {
                return;
            }

            view.Chat(L10n.Get(emote.Line), BubbleSeconds, ChatWidget.CloudType.LeftTalk);

            if (emote.ClockTick)
            {
                AudioManager.PlaySfx(ClockTick, TickVolume);
            }
        }

        /// <summary>
        /// The tick sound for the "Hurry Up" emote. Stolen from Time Pulse's gain SFX.
        /// </summary>
        private static string ClockTick
        {
            get
            {
                if (_clockTick != null)
                {
                    return _clockTick;
                }

                string sfx = MpSafe.Run("MpEmotes.ClockTick",
                    () => StatusEffectConfig.FromId("TimeAuraSe")?.SFX, null);

                _clockTick = string.IsNullOrEmpty(sfx) || sfx == "Default" ? "Buff" : sfx;

                MpPlugin.Log.LogInfo($"Hurry-up emote will play the Time Pulse cue '{_clockTick}'");
                return _clockTick;
            }
        }

        private static string _clockTick;
    }
}
