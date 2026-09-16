using System;
using LBOLMP.Session;
using LBoL.Core;
using LBoL.Core.Units;
using LBoL.Presentation;
using LBoL.Presentation.UI;
using LBoL.Presentation.UI.Panels;
using UnityEngine;

namespace LBOLMP.UI
{
    /// <summary>
    /// Shows the previewed player's spell card and Power on the button at the left, instead of your own.
    /// </summary>
    internal static class MpUsView
    {
        /// <summary>True while the button is showing somebody else's spellcard.</summary>
        private static bool _swapped;

        private static Sprite _ownSkill;

        private static UltimateSkill _ownTooltip;

        private static string _shownUs;

        /// <summary>Put another player's spell card on the button.</summary>
        internal static void Show()
        {
            var panel = Panel();
            string usId = MpHandInspect.UsId;

            // Nothing heard from them yet; leave the button alone until there is something to show.
            if (panel == null || string.IsNullOrEmpty(usId))
            {
                return;
            }

            if (!_swapped)
            {
                _swapped = true;
                _ownSkill = panel.skillImage.sprite;
                _ownTooltip = panel._descriptionTs == null ? null : panel._descriptionTs.Skill;
            }

            if (_shownUs != usId)
            {
                _shownUs = usId;
                Wear(panel, usId);
            }

            Gauges(panel, MpHandInspect.Power, MpHandInspect.PowerPerLevel);
        }

        /// <summary>Give the button back its own spell card.</summary>
        internal static void Restore()
        {
            if (!_swapped)
            {
                return;
            }

            _swapped = false;
            _shownUs = null;

            var panel = Panel();
            if (panel == null)
            {
                return;
            }

            if (_ownSkill != null)
            {
                panel.skillImage.sprite = _ownSkill;
            }

            if (panel._descriptionTs != null && _ownTooltip != null)
            {
                panel._descriptionTs.Skill = _ownTooltip;
            }

            _ownSkill = null;
            _ownTooltip = null;

            // Redraws the gauges and the number from our own player.
            MpSafe.Run("MpUsView.Restore", () => panel.OnPowerChanged(true));
        }

        private static void Wear(UltimateSkillPanel panel, string usId)
        {
            MpSafe.Run("MpUsView.Wear", () =>
            {
                var sprite = ResourcesHelper.TryGetSprite<UltimateSkill>(usId);
                if (sprite != null)
                {
                    panel.skillImage.sprite = sprite;
                }

                if (panel._descriptionTs == null)
                {
                    return;
                }

                var skill = Library.TryCreateUs(usId);
                if (skill == null)
                {
                    return;
                }

                // Detached from any player, the same way the mirrored cards and exhibits are
                skill.GameRun = GameMaster.Instance?.CurrentGameRun;
                panel._descriptionTs.Skill = skill;
            });
        }

        private static void Gauges(UltimateSkillPanel panel, int power, int perLevel)
        {
            if (perLevel <= 0)
            {
                return;
            }

            int level = power / perLevel;
            float part = (power % perLevel) / (float)perLevel;

            panel.gauge1.fillAmount = Fill(0);
            panel.gauge2.fillAmount = Fill(1);
            panel.gauge3.fillAmount = Fill(2);

            Color color;
            switch (level)
            {
                case 0: color = Color.white; break;
                case 1: color = panel.gauge1FontColor; break;
                case 2: color = panel.gauge2FontColor; break;
                default: color = panel.gauge3FontColor; break;
            }

            string text = "<color=#" + ColorUtility.ToHtmlStringRGB(color) + ">" + power
                          + " </color>/ " + perLevel;
            if (panel.powerText.text != text)
            {
                panel.powerText.text = text;
            }

            float Fill(int gauge) => level > gauge ? 1f : level == gauge ? part : 0f;
        }

        private static UltimateSkillPanel Panel()
        {
            try
            {
                return UiManager.GetPanel<UltimateSkillPanel>();
            }
            catch (InvalidOperationException)
            {
                return null;
            }
        }
    }
}
