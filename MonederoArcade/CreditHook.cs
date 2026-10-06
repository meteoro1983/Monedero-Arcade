using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace MonederoArcade
{
    /// <summary>
    /// Hook global de teclado (WH_KEYBOARD_LL). Siempre instalado; la tecla de crédito solo cuenta
    /// con Armado = true (overlay visible). También detecta Ctrl+Alt+F5 para abrir la configuración.
    /// El callback es liviano: solo dispara eventos (quien escucha debe postear al hilo de UI).
    /// </summary>
    public class CreditHook : IDisposable
    {
        private const int WH_KEYBOARD_LL = 13;
        private const int WM_KEYDOWN = 0x0100;
        private const int WM_SYSKEYDOWN = 0x0104;

        private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        private readonly LowLevelKeyboardProc _proc;
        private IntPtr _hookId = IntPtr.Zero;

        public bool Armado { get; set; } = false;
        public Keys TeclaCredito { get; set; } = Keys.D5;

        public event Action? CreditoInsertado;
        public event Action? ConfigSolicitada;

        public CreditHook()
        {
            _proc = HookCallback;
        }

        public void Instalar()
        {
            using var curProcess = System.Diagnostics.Process.GetCurrentProcess();
            using var curModule = curProcess.MainModule!;
            _hookId = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(curModule.ModuleName), 0);
        }

        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                int w = (int)wParam;
                if (w == WM_KEYDOWN || w == WM_SYSKEYDOWN)
                {
                    int vkCode = Marshal.ReadInt32(lParam);
                    if (Armado && vkCode == (int)TeclaCredito)
                    {
                        CreditoInsertado?.Invoke();
                    }
                    else if (vkCode == (int)Keys.F5 &&
                             (Control.ModifierKeys & (Keys.Control | Keys.Alt)) == (Keys.Control | Keys.Alt))
                    {
                        ConfigSolicitada?.Invoke();
                    }
                }
            }
            return CallNextHookEx(_hookId, nCode, wParam, lParam);
        }

        public void Dispose()
        {
            if (_hookId != IntPtr.Zero) UnhookWindowsHookEx(_hookId);
        }
    }
}
