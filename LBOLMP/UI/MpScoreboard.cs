using System;
using System.Collections.Generic;
using System.Linq;
using LBoL.Presentation.I10N;
using LBoL.Presentation.UI;
using LBoL.Presentation.UI.Panels;
using LBOLMP.Session;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace LBOLMP.UI
{
    /// <summary>
    /// The party scoreboard, on screen for as long as its key is held.
    /// </summary>
    /// <remarks>
    /// Built into the game's own canvas rather than a canvas of the mod's, so the cloned text is
    /// scaled by the same CanvasScaler it was authored against. Sizes here are in that canvas's
    /// units, which are roughly a 3840-wide reference, not screen pixels.
    ///
    /// The row text is copied from a settings row label, which is the game's plain UI body face
    /// rather than the display font used by the turn banner.
    /// </remarks>
    internal static class MpScoreboard
    {
        private static RectTransform _root;
        private static RectTransform _table;
        private static TextMeshProUGUI _textTemplate;
        private static bool _givenUp;

        /// <summary>Which players the rows were built for, so a join or leave rebuilds them.</summary>
        private static string _builtFor = string.Empty;

        /// <summary>One updater per cell that shows a live number.</summary>
        private static readonly List<Action> Updaters = new List<Action>();

        internal static void Tick()
        {
            bool wanted = MpSafe.Run("MpScoreboard.Wanted",
                () => MpSession.IsActive && MpSession.IsInRun
                      && Input.GetKey(MpPlugin.ScoreboardHotkey.Value), false);

            if (!wanted)
            {
                Hide();
                return;
            }

            MpSafe.Run("MpScoreboard.Show", Show);
        }

        private static void Hide()
        {
            if (_root != null && _root.gameObject.activeSelf)
            {
                _root.gameObject.SetActive(false);
            }
        }

        private static void Show()
        {
            if (_givenUp || !Ensure())
            {
                return;
            }

            var players = MpSession.ConnectedPlayers.ToList();

            string signature = string.Join(",", players.Select(p => p.Id + ":" + p.CharacterId).ToArray());
            if (signature != _builtFor)
            {
                Rebuild(players);
                _builtFor = signature;
            }

            if (!_root.gameObject.activeSelf)
            {
                _root.gameObject.SetActive(true);
            }

            // Cheap enough to run every frame: each one sets a single string.
            foreach (var update in Updaters)
            {
                update();
            }
        }

        //--
        // construction
        //--

        private static bool Ensure()
        {
            if (_root != null && _textTemplate != null)
            {
                return true;
            }

            var notifier = TryPanel<BattleNotifier>();
            var settings = TryPanel<SettingPanel>();

            // Neither panel exists while the game is loading a scene. Nothing is wrong, so this
            // must not give up: it simply tries again on the next frame the key is held.
            if (notifier == null || settings == null)
            {
                return false;
            }

            var parent = notifier.transform.parent as RectTransform;
            var label = settings.transform.Find(TemplatePath);
            var template = label == null ? null : label.GetComponent<TextMeshProUGUI>();

            // Both panels are up but are not laid out the way this expects, which retrying cannot
            // fix. This is the only case worth giving up on.
            if (parent == null || template == null)
            {
                MpPlugin.Log.LogWarning("Could not find the pieces to build the scoreboard from; it stays off");
                _givenUp = true;
                return false;
            }

            _textTemplate = template;

            // A fresh table has no rows, whatever the last set of players was.
            _builtFor = string.Empty;

            _root = Panel("MpScoreboard", parent);
            _root.anchorMin = Vector2.zero;
            _root.anchorMax = Vector2.one;
            _root.offsetMin = Vector2.zero;
            _root.offsetMax = Vector2.zero;

            // The dim. Not a raycast target: holding a key should not swallow clicks underneath.
            var dim = _root.gameObject.AddComponent<Image>();
            dim.color = new Color(0f, 0f, 0f, 0.72f);
            dim.raycastTarget = false;

            _table = Panel("Table", _root);
            _table.anchorMin = new Vector2(0.5f, 0.5f);
            _table.anchorMax = new Vector2(0.5f, 0.5f);
            _table.pivot = new Vector2(0.5f, 0.5f);
            _table.anchoredPosition = Vector2.zero;
            _table.sizeDelta = new Vector2(Width, 0f);

            var layout = _table.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = RowSpacing;
            layout.childAlignment = TextAnchor.UpperLeft;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            layout.childControlWidth = true;
            layout.childControlHeight = true;

            var fitter = _table.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            _root.gameObject.SetActive(false);
            return true;
        }

        /// <summary>Column widths, in the game canvas's units.</summary>
        private const float IconWidth = 90f;
        private const float NameWidth = 620f;
        private const float HpWidth = 340f;
        private const float PowerWidth = 300f;
        private const float GoldWidth = 220f;

        private const float Width = IconWidth + NameWidth + HpWidth + PowerWidth + GoldWidth + 250f;
        private const float RowHeight = 96f;
        private const float RowSpacing = 18f;
        private const float ColumnSpacing = 50f;

        private static void Rebuild(List<MpPlayer> players)
        {
            Updaters.Clear();

            for (int i = _table.childCount - 1; i >= 0; i--)
            {
                UnityEngine.Object.Destroy(_table.GetChild(i).gameObject);
            }

            Header();

            foreach (var player in players)
            {
                Row(player);
            }
        }

        private static void Header()
        {
            var row = NewRow("Header");

            Spacer(row, IconWidth);
            Cell(row, NameWidth, L10n.Get(MpText.ScoreboardName), Muted);
            Cell(row, HpWidth, L10n.Get(MpText.ScoreboardHp), Muted);
            Cell(row, PowerWidth, L10n.Get(MpText.ScoreboardPower), Muted);
            Cell(row, GoldWidth, L10n.Get(MpText.ScoreboardGold), Muted);
        }

        private static readonly Color Muted = new Color(0.72f, 0.74f, 0.80f);

        private static void Row(MpPlayer player)
        {
            var row = NewRow("Player" + player.Id);

            Icon(row, player.CharacterId);

            // The name is the one cell that does not change while the board is open.
            Cell(row, NameWidth, player.Name, Color.white);

            var hp = Cell(row, HpWidth, string.Empty, Color.white);
            var power = Cell(row, PowerWidth, string.Empty, Color.white);
            var gold = Cell(row, GoldWidth, string.Empty, Color.white);

            int id = player.Id;

            Updaters.Add(() =>
            {
                // Re-read rather than closing over the MpPlayer, which is replaced wholesale when
                // the host broadcasts a fresh list.
                var live = MpSession.Get(id);
                if (live == null)
                {
                    return;
                }

                Set(hp, live.Hp + "/" + live.MaxHp);
                Set(power, live.MaxPower > 0 ? live.Power + "/" + live.MaxPower : live.Power.ToString());
                Set(gold, live.Money.ToString());
            });
        }

        private static void Set(TextMeshProUGUI cell, string text)
        {
            if (cell != null && cell.text != text)
            {
                cell.text = text;
            }
        }

        private static RectTransform NewRow(string name)
        {
            var row = Panel(name, _table);

            var layout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = ColumnSpacing;
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = true;
            layout.childControlWidth = true;
            layout.childControlHeight = true;

            var size = row.gameObject.AddComponent<LayoutElement>();
            size.minHeight = RowHeight;
            size.preferredHeight = RowHeight;

            return row;
        }

        private static TextMeshProUGUI Cell(RectTransform row, float width, string text, Color color)
        {
            var clone = UnityEngine.Object.Instantiate(_textTemplate, row, false);
            clone.gameObject.name = "Cell";
            clone.gameObject.SetActive(true);

            // Same trap as everywhere else: the key would put the game's own label back.
            var localized = clone.GetComponent<LocalizedText>();
            if (localized != null)
            {
                localized.key = string.Empty;
            }

            clone.text = text;
            clone.color = color;
            clone.alignment = TextAlignmentOptions.MidlineLeft;
            clone.enableAutoSizing = false;
            clone.textWrappingMode = TextWrappingModes.NoWrap;
            clone.overflowMode = TextOverflowModes.Ellipsis;

            Sized(clone.gameObject, width);
            return clone;
        }

        private static void Icon(RectTransform row, string characterId)
        {
            var holder = Panel("Icon", row);

            var image = holder.gameObject.AddComponent<Image>();
            image.raycastTarget = false;
            image.preserveAspect = true;
            image.sprite = MpSafe.Run("MpScoreboard.Icon", () => MpPortraits.For(characterId), null);

            // No portrait for a character this install does not have; the row still reads fine.
            image.enabled = image.sprite != null;

            Sized(holder.gameObject, IconWidth);
        }

        private static void Spacer(RectTransform row, float width) =>
            Sized(Panel("Spacer", row).gameObject, width);

        private static void Sized(GameObject target, float width)
        {
            var size = target.AddComponent<LayoutElement>();
            size.minWidth = width;
            size.preferredWidth = width;
            size.flexibleWidth = 0f;
        }

        private static RectTransform Panel(string name, Transform parent)
        {
            var holder = new GameObject(name, typeof(RectTransform));
            var rect = (RectTransform)holder.transform;
            rect.SetParent(parent, false);
            return rect;
        }

        //--
        // borrowed pieces
        //--

        /// <summary>
        /// A settings row label, which is the game's ordinary UI text rather than the display font
        /// the turn banner uses. The board itself hangs off the turn banner's layer, so it draws
        /// above the board and inherits the game canvas's scaling.
        /// </summary>
        private const string TemplatePath = "Root/Preference/LeftPanel/TurboMode/KeyTmp";

        /// <summary>
        /// <c>UiManager.GetPanel</c> throws rather than returning null for a panel that has not been
        /// loaded, and this runs every frame including on screens where nothing is up yet.
        /// </summary>
        private static TPanel TryPanel<TPanel>() where TPanel : UiPanelBase
        {
            try
            {
                return UiManager.GetPanel<TPanel>();
            }
            catch (InvalidOperationException)
            {
                return null;
            }
        }
    }
}
