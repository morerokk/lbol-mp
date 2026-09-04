using System;
using System.Collections.Generic;
using System.Linq;
using LBoL.Presentation;
using LBoL.Presentation.UI;

namespace LBOLMP.UI
{
    /// <summary>
    /// Close and clean up the game's own panels as the lobby restarts a level.
    /// </summary>
    internal static class MpUiTeardown
    {
        internal static void CloseOpenWindows()
        {
            MpSafe.Run("MpUiTeardown.Dialog", CloseDialog);
            MpSafe.Run("MpUiTeardown.Panels", ClosePanels);
        }

        private static void CloseDialog()
        {
            var manager = UiManager.Instance;
            var dialog = manager == null ? null : manager._currentDialog;
            if (dialog == null)
            {
                return;
            }

            MpPlugin.Log.LogInfo($"Closing the open '{dialog.GetType().Name}' before the level restarts");

            // Hide is declared on UiDialog<TPayload>, which we can't call directly.
            var hide = dialog.GetType().GetMethod("Hide", new[] { typeof(bool) });
            if (hide != null)
            {
                hide.Invoke(dialog, new object[] { false });
            }

            // Hide() clears _currentDialog itself, so if it's still here at this point, we would run into major issues.
            // Just forget about the damn thing and force set it to null.
            if (manager._currentDialog == dialog)
            {
                MpPlugin.Log.LogWarning($"'{dialog.GetType().Name}' did not cleanly close! Force-closing it anyway.");
                manager._currentDialog = null;
            }
        }

        private static void ClosePanels()
        {
            var openPanels = new HashSet<Type>(GameMaster.GameRunUiList);
            if (openPanels.Count == 0)
            {
                return;
            }

            foreach (var ui in UiManager.EnumerateAll().ToList())
            {
                var panel = ui as UiPanelBase;
                if (panel == null || !panel.IsVisible || !openPanels.Contains(panel.GetType()))
                {
                    continue;
                }

                panel.Hide(false);
            }
        }
    }
}
