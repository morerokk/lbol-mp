using HarmonyLib;
using LBOLMP.UI;
using LBoL.Presentation.UI;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace LBOLMP.Patches
{
    /// <summary>
    /// Mutes the game's keyboard shortcuts while the chat box is open, so typing "e" does not end your turn.
    /// </summary>
    [HarmonyPatch(typeof(UiManager), "TryGetInputHandler", typeof(InputAction.CallbackContext), typeof(bool))]
    public static class ChatInputBlockPatch
    {
        [HarmonyPrefix]
        private static bool Prefix(InputAction.CallbackContext context, ref IInputActionHandler __result)
        {
            if (!MpSafe.Run("ChatInputBlockPatch", () => ShouldBlock(context), false))
            {
                return true;
            }

            __result = null;
            return false;
        }

        private static bool ShouldBlock(InputAction.CallbackContext context)
        {
            if (MpChat.IsOpen)
            {
                return true;
            }

            // The Escape or Enter press that just closed the chat box, arriving late.
            if (context.control is KeyControl && MpChat.IsEatingKeys)
            {
                return true;
            }

            // The Enter press that is about to open the chat box should not confirm anything in the game as well.
            return context.control is KeyControl key
                   && (key.keyCode == Key.Enter || key.keyCode == Key.NumpadEnter)
                   && MpChat.CanOpen;
        }
    }
}
