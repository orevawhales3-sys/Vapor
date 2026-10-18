using System;
using System.Runtime.InteropServices;

namespace DynamicOverlay.Poc
{
    internal static class NativeMethods
    {
        // Hotkey Constants (winuser.h)
        public const int HOTKEY_ID_TOGGLE = 0x9001;
        public const uint MOD_ALT = 0x0001;
        public const uint MOD_CONTROL = 0x0002;
        public const uint MOD_NOREPEAT = 0x4000;
        public const uint VK_SPACE = 0x20;
        public const int WM_HOTKEY = 0x0312;
        public const int WM_DISPLAYCHANGE = 0x007E;
        public const int ERROR_HOTKEY_ALREADY_REGISTERED = 1409;

        // Window Activation & Lifecycle Constants (winuser.h)
        public const int WM_SIZE = 0x0005;
        public const int SIZE_RESTORED = 0;
        public const int SIZE_MINIMIZED = 1;
        public const int WM_ACTIVATE = 0x0006;
        public const int WA_INACTIVE = 0;
        public const int WA_ACTIVE = 1;
        public const int WA_CLICKACTIVE = 2;
        public const int WM_CLOSE = 0x0010;
        public const int WM_NCACTIVATE = 0x0086;
        public const int WM_MOUSEACTIVATE = 0x0021;
        public const int WM_SYSCOMMAND = 0x0112;
        public const int SC_MINIMIZE = 0xF020;
        public const int SC_RESTORE = 0xF120;
        public const int SC_CLOSE = 0xF060;
        public const int WM_PARENTNOTIFY = 0x0210;
        public const int WM_LBUTTONDOWN = 0x0201;
        public const int WM_LBUTTONUP = 0x0202;
        public const int WM_RBUTTONDOWN = 0x0204;
        public const int WM_MBUTTONDOWN = 0x0207;
        public const int WM_ENTERSIZEMOVE = 0x0231;
        public const int WM_EXITSIZEMOVE = 0x0232;

        // ShowWindow Constants (winuser.h)
        public const int SW_HIDE = 0;
        public const int SW_SHOWNORMAL = 1;
        public const int SW_SHOWMINIMIZED = 2;
        public const int SW_SHOWMAXIMIZED = 3;
        public const int SW_RESTORE = 9;

        // Application-defined diagnostic / verification messages (WM_APP + n)
        public const int WM_APP_SET_MODE = 0x8001;
        public const int WM_APP_SYNC_DOM = 0x8002;
        public const int WM_APP_INCREMENT_COUNTER = 0x8003;
        public const int WM_APP_SWITCH_TAB = 0x8004;
        public const int WM_APP_MINIMIZE_WINDOW = 0x8005;
        public const int WM_APP_RESTORE_WINDOW = 0x8006;
        public const int WM_APP_HIDE_WINDOW = 0x8007;
        public const int WM_APP_REQUEST_CLOSE = 0x8008;
        public const int WM_APP_OPEN_VIEW = 0x8009;
        public const int WM_APP_TOGGLE_MAXIMIZE = 0x800A;
        public const int WM_APP_TOGGLE_CAPTURE_PROTECTION = 0x800B;
        public const int WM_APP_ADD_SHORTCUT = 0x800C;
        public const int WM_APP_REMOVE_SHORTCUT = 0x800D;
        public const int WM_APP_OPEN_SHORTCUT = 0x800E;
        public const int WM_APP_UPDATE_NOTE = 0x800F;
        public const int WM_APP_VISUAL_TAB_ACTION = 0x8010;

        // Window Hierarchy Constants (winuser.h)
        public const uint GA_PARENT = 1;
        public const uint GA_ROOT = 2;
        public const uint GA_ROOTOWNER = 3;
        public const uint GW_OWNER = 4;

        // Window Display Affinity Constants (winuser.h)
        public const uint WDA_NONE = 0x00000000;
        public const uint WDA_MONITOR = 0x00000001;
        public const uint WDA_EXCLUDEFROMCAPTURE = 0x00000011;

        // Desktop Window Manager Attributes (dwmapi.h)
        public const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
        public const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
        public const int DWMWCP_ROUND = 2;

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetWindowDisplayAffinity(IntPtr hWnd, uint dwAffinity);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetWindowDisplayAffinity(IntPtr hWnd, out uint pdwAffinity);

        [DllImport("dwmapi.dll", PreserveSig = true)]
        public static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool IsChild(IntPtr hWndParent, IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern IntPtr GetAncestor(IntPtr hwnd, uint gaFlags);

        [DllImport("user32.dll")]
        public static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);

        [DllImport("user32.dll")]
        public static extern IntPtr SetFocus(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern IntPtr GetFocus();

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool BringWindowToTop(IntPtr hWnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool IsIconic(IntPtr hWnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, [MarshalAs(UnmanagedType.Bool)] bool fAttach);

        [DllImport("kernel32.dll")]
        public static extern uint GetCurrentThreadId();
    }
}
