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

        /// <summary>
        /// Juego de PC lanzado por emulatorLauncher: _pid es el del launcher (vive mientras viva el juego)
        /// y el juego es su árbol de procesos hijos.
        /// </summary>
        public bool EsPc => _emulador == "pc";

        // Árbol congelado al pausar: se reanuda exactamente lo que se suspendió.
        private List<int> _arbolPausado = new();

        private List<int> PidsJuego() => ProcessTree.Descendientes(_pid).Select(d => d.Pid).ToList();

        private IntPtr VentanaJuego()
        {
            IntPtr mejor = IntPtr.Zero; long area = -1;
            foreach (var pid in PidsJuego())
            {
                var h = NativeMethods.VentanaPrincipalGrande(pid);
                if (h == IntPtr.Zero || !NativeMethods.GetWindowRect(h, out var r)) continue;
                long a = (long)(r.Right - r.Left) * (r.Bottom - r.Top);
                if (a > area) { area = a; mejor = h; }
            }
            return mejor;
        }

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
                _minimizada = EsPc ? VentanaJuego() : NativeMethods.VentanaPrincipalGrande(_pid);
                if (_minimizada != IntPtr.Zero)
                {
                    NativeMethods.ShowWindow(_minimizada, NativeMethods.SW_MINIMIZE);
                    Thread.Sleep(400); // que salga de la pantalla exclusiva antes de congelarlo
                    Logger.Log("Ventana del emulador minimizada (salir de pantalla exclusiva)");
                }
            }
            if (EsRetroArch) { _ra.PausarSiHaceFalta(); return; }
            if (EsPc)
            {
                _arbolPausado = PidsJuego();
                Logger.Log($"Juego de PC: árbol = {string.Join(", ", ProcessTree.Descendientes(_pid).Select(d => $"{d.Nombre}({d.Pid})"))}");
                if (_cfg.SilenciarAlPausar) AudioMute.Silenciar(_arbolPausado, true);
                if (_cfg.MetodoPausa == "hotkey") { EnviarHotkey(_cfg.TeclaPausa); return; }
                foreach (var pid in _arbolPausado) ProcessControl.Suspender(pid);
                return;
            }
            if (_cfg.MetodoPausa == "hotkey") EnviarHotkey(_cfg.TeclaPausa);
            else ProcessControl.Suspender(_pid);
        }

        public void Reanudar()
        {
            if (EsRetroArch) _ra.ReanudarSiHaceFalta();
            else if (EsPc)
            {
                if (_cfg.MetodoPausa == "hotkey") EnviarHotkey(_cfg.TeclaPausa);
                else foreach (var pid in _arbolPausado) ProcessControl.Reanudar(pid);
                if (_cfg.SilenciarAlPausar) AudioMute.Silenciar(_arbolPausado, false);
                _arbolPausado = new();
            }
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
            if (EsPc) return VentanaJuego();
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
            else if (EsPc)
            {
                CerrarJuegoPc();
                return;
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

        /// <summary>
        /// Juego de PC: reanudar el árbol, WM_CLOSE a sus ventanas y esperar a que emulatorLauncher
        /// termine (lo hace solo cuando el juego sale). Si no responde, Kill del árbol del juego (nunca
        /// del launcher: con el sistema windows emulatorLauncher siempre devuelve 0 → sin "cierre inesperado").
        /// </summary>
        private void CerrarJuegoPc()
        {
            foreach (var pid in _arbolPausado) ProcessControl.Reanudar(pid);
            var arbol = PidsJuego();
            foreach (var pid in arbol) ProcessControl.Reanudar(pid);
            if (_cfg.SilenciarAlPausar && _arbolPausado.Count > 0) AudioMute.Silenciar(_arbolPausado, false);
            _arbolPausado = new();

            var ventanas = arbol.SelectMany(NativeMethods.VentanasVisibles).ToList();
            Logger.Log($"Juego de PC: WM_CLOSE a {ventanas.Count} ventana(s) de {arbol.Count} proceso(s)");
            foreach (var h in ventanas) NativeMethods.PostMessage(h, NativeMethods.WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
            if (EsperarSalida(_cfg.EsperaCierreMs)) { Logger.Log("Cierre limpio por WM_CLOSE (juego de PC)"); return; }

            // Muchos juegos piden "¿salir?" o ignoran WM_CLOSE en pantalla completa: se termina el árbol.
            foreach (var pid in PidsJuego().AsEnumerable().Reverse())
            {
                try { using var p = Process.GetProcessById(pid); p.Kill(); Logger.Log($"Kill {p.ProcessName}({pid})"); } catch { }
            }
            if (EsperarSalida(5000)) Logger.Log("Juego de PC cerrado (Kill); emulatorLauncher terminó");
            else Logger.Log("emulatorLauncher sigue vivo tras cerrar el juego");
        }
    }
}
