using System.Runtime.InteropServices;

namespace MonederoArcade
{
    internal static class NativeMethods
    {
        public const uint PROCESS_SUSPEND_RESUME = 0x0800;

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, int dwProcessId);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool CloseHandle(IntPtr hObject);

        [DllImport("ntdll.dll")]
        public static extern int NtSuspendProcess(IntPtr processHandle);

        [DllImport("ntdll.dll")]
        public static extern int NtResumeProcess(IntPtr processHandle);

        [DllImport("user32.dll")]
        public static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

        [DllImport("user32.dll")]
        public static extern uint MapVirtualKey(uint uCode, uint uMapType);

        public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")]
        public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll")]
        public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        [DllImport("user32.dll")]
        public static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        public const uint WM_CLOSE = 0x0010;
        public const int SW_MINIMIZE = 6, SW_RESTORE = 9;

        [DllImport("user32.dll")]
        public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT { public int Left, Top, Right, Bottom; }

        /// <summary>La ventana visible más grande del proceso: la de render a pantalla completa.</summary>
        public static IntPtr VentanaPrincipalGrande(int pid)
        {
            IntPtr mejor = IntPtr.Zero; long area = -1;
            foreach (var h in VentanasVisibles(pid))
            {
                if (!GetWindowRect(h, out var r)) continue;
                long a = (long)(r.Right - r.Left) * (r.Bottom - r.Top);
                if (a > area) { area = a; mejor = h; }
            }
            return mejor;
        }

        /// <summary>
        /// SetForegroundWindow desde un proceso de fondo: Windows lo permite si antes "se presiona" Alt
        /// (el hook de crédito no cuenta como input propio). Necesario para Dolphin con BackgroundInput=False.
        /// </summary>
        public static void TraerAlFrente(IntPtr h)
        {
            keybd_event(0x12, 0, 0, UIntPtr.Zero);
            keybd_event(0x12, 0, 2, UIntPtr.Zero);
            SetForegroundWindow(h);
        }

        /// <summary>Ventanas top-level visibles de un proceso (Qt/Dolphin tienen varias: principal, render, diálogos).</summary>
        public static List<IntPtr> VentanasVisibles(int pid)
        {
            var res = new List<IntPtr>();
            EnumWindows((h, _) =>
            {
                GetWindowThreadProcessId(h, out var p);
                if (p == (uint)pid && IsWindowVisible(h)) res.Add(h);
                return true;
            }, IntPtr.Zero);
            return res;
        }
    }
}
