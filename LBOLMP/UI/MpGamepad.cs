using LBoL.Core;
using LBoL.Presentation.InputSystemExtend;
using LBoL.Presentation.UI;
using UnityEngine.InputSystem;

namespace LBOLMP.UI
{
    /// <summary>
    /// Reads the back button with gamepads to open hand previews.
    /// </summary>
    internal static class MpGamepad
    {
        private static InputAction _cancel;
        private static bool _missing;

        internal static bool BackPressed()
        {
            if (!IsGamepad)
            {
                return false;
            }

            var cancel = Cancel();
            return cancel != null && cancel.triggered;
        }

        internal static bool IsGamepad =>
            Singleton<InputDeviceManager>.Instance != null
            && Singleton<InputDeviceManager>.Instance.CurrentInputDevice == InputDeviceType.Gamepad;

        private static InputAction Cancel()
        {
            if (_cancel != null || _missing)
            {
                return _cancel;
            }

            var manager = UiManager.Instance;
            if (manager == null)
            {
                return null;
            }

            _cancel = MpSafe.Run("MpGamepad.Cancel",
                () => manager.inputActions?.FindActionMap("UI", false)?.FindAction("Cancel", false),
                null);

            if (_cancel == null)
            {
                _missing = true;
                MpPlugin.Log.LogWarning(
                    "No UI/Cancel input action. The back button will not open a partner's hand");
            }

            return _cancel;
        }
    }
}
