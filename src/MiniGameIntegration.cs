using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using NezhaDesktop.MiniGames;

namespace NezhaDesktop
{
    public partial class PetWindow
    {
        private MiniGameWindow miniGameWindow;
        private bool miniGameHotkey;
        private const int MiniGameHotkeyId = 0x4E5A;
        [DllImport("user32.dll", SetLastError = true)] private static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint key);
        [DllImport("user32.dll", SetLastError = true)] private static extern bool UnregisterHotKey(IntPtr window, int id);
        private void InitializeMiniGames(bool useTray)
        {
            SourceInitialized += delegate {
                if (!useTray) return;
                IntPtr handle = new WindowInteropHelper(this).Handle;
                miniGameHotkey = RegisterHotKey(handle, MiniGameHotkeyId, 0x4003, 0x48); // Ctrl+Alt+H, no repeat
                if (miniGameHotkey) HwndSource.FromHwnd(handle).AddHook(MiniGameHotkeyHook);
            };
            Closed += delegate {
                if (miniGameWindow != null) miniGameWindow.Close();
                if (miniGameHotkey) UnregisterHotKey(new WindowInteropHelper(this).Handle, MiniGameHotkeyId);
            };
        }
        private IntPtr MiniGameHotkeyHook(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (message == 0x0312 && wParam.ToInt32() == MiniGameHotkeyId && miniGameWindow != null)
            { if (miniGameWindow.IsVisible) miniGameWindow.QuickHide(); else miniGameWindow.Restore(); handled = true; }
            return IntPtr.Zero;
        }
        public void OpenMiniGames()
        {
            if (miniGameWindow == null) {
                string root = AppDomain.CurrentDomain.BaseDirectory;
                miniGameWindow = new MiniGameWindow(root, Path.Combine(root, "data", "mini-games"),
                    delegate { return new PetSnapshot(Engine.Config.Name, Engine.Level, Engine.State.Energy, Engine.State.Mood); }, miniGameHotkey);
                miniGameWindow.Closed += delegate { miniGameWindow = null; };
            }
            miniGameWindow.Restore();
        }
    }
}
