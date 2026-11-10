using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using LBOLMP.Net;
using LBOLMP.Session;
using LBOLMP.Session.Messages;
using LBoL.ConfigData;
using LBoL.Presentation;
using LBoL.Presentation.UI.Widgets;
using LBoL.Presentation.Units;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

namespace LBOLMP.UI
{
    /// <summary>
    /// Text chat. Enter opens the box, Enter sends, Escape closes it without sending.
    /// A sent line pops up over the sender's character like an emote does, just for longer.
    /// </summary>
    /// While the box is open, the game's own shortcuts are muted by <c>ChatInputBlockPatch</c>,
    /// so typing "e" does not end your turn.
    public sealed class MpChat : MonoBehaviour
    {
        public const int MaxLength = 100;

        /// <summary>How long a chat bubble stays up.</summary>
        private const float BubbleSeconds = 5f;

        /// <summary>Half volume, same as the emote tick.</summary>
        private const float SfxVolume = 0.5f;

        /// <summary>Typing this in front of a message turns the bubble into a thought bubble. Shh.</summary>
        private const string ThinkPrefix = "/think ";

        private const string ControlName = "MpChatInput";

        private const float BoxWidth = 640f;
        private const float BoxHeight = 34f;

        /// <summary>Anything that would close our noparse early and let rich text tags through again.</summary>
        private static readonly Regex NoParseClose = new Regex("</?noparse>", RegexOptions.IgnoreCase);

        /// <summary>
        /// How many frames after closing the box the game's shortcuts stay muted.
        /// The box closes in OnGUI, but the game's input system only sees that same Escape or Enter press on a later frame,
        /// and would open the pause menu or confirm something with it.
        /// </summary>
        private const int ClosedGraceFrames = 2;

        /// <summary>True while the box is open and eating keyboard input.</summary>
        public static bool IsOpen { get; private set; }

        /// <summary>True while the game should not see key presses: the box is open, or has only just closed.</summary>
        public static bool IsEatingKeys => IsOpen || Time.frameCount <= _closedFrame + ClosedGraceFrames;

        private static int _closedFrame = -1000;

        /// <summary>How many lines the scoreboard's chat log keeps.</summary>
        private const int LogLength = 10;

        /// <summary>One line of the chat log.</summary>
        public readonly struct LogLine
        {
            public readonly string Name;
            public readonly string Text;
            public readonly bool Thought;

            internal LogLine(string name, string text, bool thought)
            {
                Name = name;
                Text = text;
                Thought = thought;
            }
        }

        private static readonly List<LogLine> LogLines = new List<LogLine>();

        /// <summary>The last few chat lines, oldest first.</summary>
        public static IReadOnlyList<LogLine> Log => LogLines;

        /// <summary>Goes up whenever <see cref="Log"/> changes, so the scoreboard knows to redraw it.</summary>
        public static int LogRevision { get; private set; }

        private string _text = string.Empty;

        /// <summary>The Enter press that opened the box also reaches OnGUI that frame, and must not send it straight away.</summary>
        private int _openedFrame = -1;

        private bool _hadNavigation;

        private GUIStyle _fieldStyle;
        private GUIStyle _hintStyle;

        public static void RegisterHandlers() => MpNet.OnRemote<ChatMessage>(OnRemote);

        /// <summary>
        /// Whether Enter would open the box right now. Also used to keep that Enter press away from the game.
        /// </summary>
        public static bool CanOpen
        {
            get
            {
                if (IsOpen || !MpSession.IsActive || !MpSession.IsInRun)
                {
                    return false;
                }

                // Somebody is already typing into something else, such as the lobby overlay or the game's seed box.
                if (GUIUtility.keyboardControl != 0)
                {
                    return false;
                }

                var selected = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
                var input = selected != null ? selected.GetComponent<TMP_InputField>() : null;
                return input == null || !input.isFocused;
            }
        }

        private void Update()
        {
            // A new session starts with an empty log.
            if (!MpSession.IsActive && LogLines.Count > 0)
            {
                LogLines.Clear();
                LogRevision++;
            }

            if (IsOpen)
            {
                if (!MpSession.IsActive || !MpSession.IsInRun)
                {
                    Close();
                }
                return;
            }

            if ((Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)) && CanOpen)
            {
                MpSafe.Run("MpChat.Open", Open);
            }
        }

        private void OnDisable() => Close();

        private void Open()
        {
            _text = string.Empty;
            _openedFrame = Time.frameCount;
            IsOpen = true;

            // Enter is also uGUI's Submit, which would click whatever button happens to be selected.
            var events = EventSystem.current;
            _hadNavigation = events != null && events.sendNavigationEvents;
            if (events != null)
            {
                events.sendNavigationEvents = false;
            }
        }

        private void Close()
        {
            if (!IsOpen)
            {
                return;
            }

            IsOpen = false;
            _closedFrame = Time.frameCount;
            _text = string.Empty;

            // Nothing else can have had keyboard focus, since OnGUI keeps taking it back.
            GUIUtility.keyboardControl = 0;

            var events = EventSystem.current;
            if (events != null && _hadNavigation)
            {
                events.sendNavigationEvents = true;
            }
        }

        private void OnGUI()
        {
            if (!IsOpen)
            {
                return;
            }

            var e = Event.current;
            if (e.type == EventType.KeyDown && Time.frameCount != _openedFrame)
            {
                if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter)
                {
                    e.Use();
                    string text = _text;
                    Close();
                    MpSafe.Run("MpChat.Send", () => Send(text));
                    return;
                }

                if (e.keyCode == KeyCode.Escape)
                {
                    e.Use();
                    Close();
                    return;
                }
            }

            EnsureStyles();

            float width = Mathf.Min(BoxWidth, Screen.width - 32f);
            var rect = new Rect((Screen.width - width) / 2f, Screen.height * 0.62f, width, BoxHeight);

            GUI.SetNextControlName(ControlName);
            _text = GUI.TextField(rect, _text ?? string.Empty, MaxLength, _fieldStyle);

            if (string.IsNullOrEmpty(_text))
            {
                GUI.Label(rect, L10n.Get(MpText.ChatPrompt), _hintStyle);
            }

            // Keep the caret in the box, even if the mouse clicked somewhere else.
            if (GUI.GetNameOfFocusedControl() != ControlName)
            {
                GUI.FocusControl(ControlName);
            }
        }

        private void EnsureStyles()
        {
            if (_fieldStyle != null)
            {
                return;
            }

            _fieldStyle = new GUIStyle(GUI.skin.textField)
            {
                fontSize = 18,
                alignment = TextAnchor.MiddleLeft,
                padding = new RectOffset(10, 10, 4, 4)
            };

            _hintStyle = new GUIStyle(_fieldStyle);
            _hintStyle.normal.background = null;
            _hintStyle.normal.textColor = new Color(1f, 1f, 1f, 0.45f);
        }

        private static void Send(string text)
        {
            text = text?.Trim() ?? string.Empty;

            bool thought = text.StartsWith(ThinkPrefix, StringComparison.OrdinalIgnoreCase);
            if (thought)
            {
                text = text.Substring(ThinkPrefix.Length).Trim();
            }

            if (text.Length == 0)
            {
                return;
            }

            // Shown here directly, because the chat message is not echoed back to its sender.
            Show(GameDirector.Player, text, thought);
            Record(MpNet.LocalPlayerId, text, thought);
            MpNet.Send(new ChatMessage { Text = text, Thought = thought });
        }

        private static void OnRemote(ChatMessage message)
        {
            string text = message.Text ?? string.Empty;
            if (text.Length > MaxLength)
            {
                text = text.Substring(0, MaxLength);
            }

            if (text.Trim().Length == 0)
            {
                return;
            }

            Record(message.SenderId, text, message.Thought);
            MpSafe.Run("MpChat.OnRemote",
                () => Show(MpAllyUnits.GetView(message.SenderId), text, message.Thought));
        }

        private static void Record(int playerId, string text, bool thought)
        {
            // The name is kept as it was, so the line still reads right after they leave.
            string name = MpSession.Get(playerId)?.Name ?? "?";

            LogLines.Add(new LogLine(name, text, thought));
            if (LogLines.Count > LogLength)
            {
                LogLines.RemoveAt(0);
            }
            LogRevision++;
        }

        /// <summary>Typed text, made safe to put into a TextMeshPro label as-is rather than as rich text tags.</summary>
        internal static string NoParse(string text) =>
            "<noparse>" + NoParseClose.Replace(text ?? string.Empty, string.Empty) + "</noparse>";

        private static void Show(UnitView view, string text, bool thought)
        {
            if (view == null)
            {
                return;
            }

            view.Chat(NoParse(text), BubbleSeconds, thought ? ChatWidget.CloudType.LeftThink : ChatWidget.CloudType.LeftTalk);
            AudioManager.PlaySfx(MessageSfx, SfxVolume);
        }

        /// <summary>
        /// The sound a chat message makes. Currently the same Time Pulse tick as the "Hurry Up" emote.
        /// </summary>
        private static string MessageSfx
        {
            get
            {
                if (_messageSfx != null)
                {
                    return _messageSfx;
                }

                string sfx = MpSafe.Run("MpChat.MessageSfx",
                    () => StatusEffectConfig.FromId("TimeAuraSe")?.SFX, null);

                _messageSfx = string.IsNullOrEmpty(sfx) || sfx == "Default" ? "Buff" : sfx;
                return _messageSfx;
            }
        }

        private static string _messageSfx;
    }
}
