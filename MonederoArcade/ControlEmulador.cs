using System.Diagnostics;
using System.Windows.Forms;

namespace MonederoArcade
{
    /// <summary>
    /// Abstrae pausa/reanudación/cierre según el emulador:
    /// RetroArch -> comandos UDP; standalone -> suspensión del proceso (por defecto) o hotkey simulada.
    /// Todos los métodos son bloqueantes: llamarlos desde Task.Run.
    /// </summary>
    public class ControlEmulador
    {
        private readonly int _pid;
        private readonly string _emulador;
        private readonly EmuladorConfig _cfg;
        private readonly RetroArchClient _ra = new();

        public bool EsRetroArch => _emulador == "retroarch";

        public ControlEmulador(int pid, string emulador, EmuladorConfig cfg)
        {
            _pid = pid;
            _emulador = emulador.ToLowerInvariant();
            _cfg = cfg;
        }

        private IntPtr _minimizada = IntPtr.Zero;

        public void Pausar()
        {
            if (_cfg.MinimizarAlPausar)
            {
                _minimizada = NativeMethods.VentanaPrincipalGrande(_pid);
                if (_minimizada != IntPtr.Zero)
                {
                    NativeMethods.ShowWindow(_minimizada, NativeMethods.SW_MINIMIZE);
                    Thread.Sleep(400); // que salga de la pantalla exclusiva antes de congelarlo
                    Logger.Log("Ventana del emulador minimizada (salir de pantalla exclusiva)");
                }
            }
            if (EsRetroArch) { _ra.PausarSiHaceFalta(); return; }
            if (_cfg.MetodoPausa == "hotkey") EnviarHotkey(_cfg.TeclaPausa);
            else ProcessControl.Suspender(_pid);
        }

        public void Reanudar()
        {
            if (EsRetroArch) _ra.ReanudarSiHaceFalta();
            else if (_cfg.MetodoPausa == "hotkey") EnviarHotkey(_cfg.TeclaPausa);
            else ProcessControl.Reanudar(_pid);

            if (_minimizada != IntPtr.Zero)
            {
                NativeMethods.ShowWindow(_minimizada, NativeMethods.SW_RESTORE);
                Thread.Sleep(150);
                NativeMethods.TraerAlFrente(_minimizada);
                Logger.Log("Ventana del emulador restaurada y con foco");
                _minimizada = IntPtr.Zero;
            }
        }

        public void ToggleFastForward()
        {
            if (EsRetroArch) _ra.FastForward();
        }

        public void ShowMsg(string msg)
        {
            if (EsRetroArch) _ra.ShowMsg(msg);
        }

        private IntPtr Hwnd()
        {
            try { using var p = Process.GetProcessById(_pid); p.Refresh(); return p.MainWindowHandle; }
            catch { return IntPtr.Zero; }
        }

        public void DevolverFoco()
        {
            var h = Hwnd();
            if (h != IntPtr.Zero) NativeMethods.SetForegroundWindow(h);
        }

        private void EnviarHotkey(string nombre)
        {
            if (!Enum.TryParse<Keys>(nombre, true, out var k))
            {
                Logger.Log($"Hotkey inválida: {nombre}");
                return;
            }
            DevolverFoco();
            Thread.Sleep(150);
            byte vk = (byte)k;
            byte sc = (byte)NativeMethods.MapVirtualKey(vk, 0);
            NativeMethods.keybd_event(vk, sc, 0, UIntPtr.Zero);
            Thread.Sleep(60);
            NativeMethods.keybd_event(vk, sc, 2, UIntPtr.Zero); // KEYEVENTF_KEYUP
            Logger.Log($"Hotkey enviada: {nombre}");
        }

        public bool Vivo()
        {
            try { using var p = Process.GetProcessById(_pid); return !p.HasExited; }
            catch { return false; }
        }

        public bool EsperarSalida(int ms)
        {
            var fin = DateTime.UtcNow.AddMilliseconds(ms);
            while (DateTime.UtcNow < fin)
            {
                if (!Vivo()) return true;
                Thread.Sleep(100);
            }
            return !Vivo();
        }

        /// <summary>
        /// Cierre escalonado: RetroArch -> QUIT x2; standalone -> reanudar si estaba suspendido.
        /// Luego cierre de ventana y, solo como último recurso, Kill.
        /// </summary>
        public void Cerrar()
        {
            if (EsRetroArch)
            {
                _ra.Quit();
                if (EsperarSalida(700)) { Logger.Log("Cierre limpio por QUIT (1)"); return; }
                _ra.Quit();
                if (EsperarSalida(2000)) { Logger.Log("Cierre limpio por QUIT (2)"); return; }
            }
            else
            {
                ProcessControl.Reanudar(_pid); // un proceso suspendido no puede procesar el cierre
            }

            // WM_CLOSE a TODAS las ventanas visibles del proceso: en DuckStation/PCSX2 (Qt) y Dolphin la
            // ventana principal no es la de render, y un diálogo abierto (ej. login de RetroAchievements)
            // bloquea CloseMainWindow. Así salen con código 0 y RetroBat no muestra "cierre inesperado";
            // con Kill, emulatorLauncher registra "Exit code 200".
            var ventanas = NativeMethods.VentanasVisibles(_pid);
            Logger.Log($"Probando cierre de ventanas (WM_CLOSE a {ventanas.Count})");
            foreach (var h in ventanas) NativeMethods.PostMessage(h, NativeMethods.WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
            if (EsperarSalida(EsRetroArch ? 2500 : 6000)) { Logger.Log("Cierre limpio por WM_CLOSE"); return; }

            Logger.Log("Último recurso: Kill (RetroBat lo verá como cierre inesperado)");
            try { using var p = Process.GetProcessById(_pid); p.Kill(); } catch { }
        }
    }
}
