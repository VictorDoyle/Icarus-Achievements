using System;
using System.Windows.Forms;
using Gma.System.MouseKeyHook;

namespace IcarusAchievements
{
    /// <summary>
    /// Manage global hotkeys that work even when other applications have focus (e.g playing steam then hitting shift tab will also show Icarus)
    /// This detects Shift+Tab from anywhere in Windows
    /// </summary>
    public class HotkeyManager : IDisposable
    {
        private IKeyboardMouseEvents _globalHook;
        private bool _shiftPressed = false;
        private bool _disposed = false;
        public event Action ShiftTabPressed;

        public HotkeyManager()
        {
            // global keyboard hook that monitors ALL keyboard input
            _globalHook = Hook.GlobalEvents();

            // Subscribe to key down and key up events
            _globalHook.KeyDown += OnKeyDown;
            _globalHook.KeyUp += OnKeyUp;
        }

        /// <summary>
        /// Handle key press events
        /// </summary>
        private void OnKeyDown(object sender, KeyEventArgs e)
        {
            // check if Shift is currently pressed
            if (e.KeyCode == Keys.LShiftKey || e.KeyCode == Keys.RShiftKey || e.KeyCode == Keys.ShiftKey)
            {
                _shiftPressed = true;
            }

            // Need Shift + Tab to invoke
            if (e.KeyCode == Keys.Tab && _shiftPressed)
            {
                ShiftTabPressed?.Invoke();
            }
        }

        /// <summary>
        /// handle key release events
        /// </summary>
        private void OnKeyUp(object sender, KeyEventArgs e)
        {
            // watch until shift key is released
            if (e.KeyCode == Keys.LShiftKey || e.KeyCode == Keys.RShiftKey || e.KeyCode == Keys.ShiftKey)
            {
                _shiftPressed = false;
            }
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _globalHook?.Dispose();
                _disposed = true;
            }
        }
    }
}