using System.Diagnostics;
using System.Runtime.InteropServices;

namespace MonederoArcade
{
    /// <summary>
    /// Salida universal con el control (por defecto Select+Start mantenidos) para los juegos que su emulador no
    /// sabe cerrar: los programas de PC (sistemas de salida_sistemas, por defecto windows). RetroArch, MAME, Dolphin,
    /// etc. ya salen con el hotkey de RetroBat (es_padtokey.cfg) y no se tocan.
    /// Cierre: WM_CLOSE a todas las ventanas del juego (su propio cierre: guarda config y saves) → espera → Kill de
    /// lo que quede, incluidos los procesos que el juego dejó huérfanos (launchers de Unreal/FNA...) → resolución
    /// del escritorio → foco a EmulationStation.
    /// </summary>
    public class SalidaUniversal : IDisposable
    {
        public class Juego
        {
            public int LauncherPid { get; init; }
            public string Sistema { get; init; } = "";
            public string Rom { get; init; } = "";
            // Todo proceso visto en el árbol del juego, con su hora de inicio (evita matar un PID reciclado).
            public Dictionary<int, DateTime> Pids { get; } = new();
            public bool LauncherVivo { get; set; } = true;
            public DateTime? SinVentanaDesde { get; set; }
            public bool Cerrando { get; set; }
        }

        private readonly List<Juego> _juegos = new();
        private readonly object _lock = new();
        private readonly System.Windows.Forms.Timer _timer = new() { Interval = 50 };
        private ushort _mascara = 0x0030; // BACK + START
        private int _msMantener = 500;
        private DateTime? _apretadoDesde;
        private bool _disparado;

        /// <summary>Se dispara en el hilo de UI cuando el combo se mantuvo apretado con un juego abierto.</summary>
        public event Action<Juego>? SalidaPedida;

        public SalidaUniversal() => _timer.Tick += (_, _) => Poll();

        public void Configurar(string combo, int ms)
        {
            _mascara = GamepadCredit.Mascara(combo);
            _msMantener = Math.Max(0, ms);
        }

        public void Agregar(int launcherPid, string sistema, string rom)
        {
            lock (_lock)
            {
                if (_juegos.Any(j => j.LauncherPid == launcherPid)) return;
                _juegos.Add(new Juego { LauncherPid = launcherPid, Sistema = sistema, Rom = rom });
            }
            Logger.Log($"Salida universal armada para [{sistema}] {rom} (launcher {launcherPid})");
            Refrescar();
        }

        public void LauncherTerminado(int launcherPid)
        {
            lock (_lock)
            {
                var j = _juegos.FirstOrDefault(x => x.LauncherPid == launcherPid);
                if (j != null) j.LauncherVivo = false;
            }
            Refrescar();
        }

        /// <summary>
        /// Cada 1,5 s (timer del watcher): suma al juego los procesos nuevos de su árbol y resuelve los huérfanos.
        /// Los hijos de un proceso muerto siguen teniendo su PID como padre, así que el árbol se encuentra igual
        /// después de que emulatorLauncher termina.
        /// </summary>
        public void Refrescar()
        {
            List<Juego> copia;
            lock (_lock) copia = _juegos.ToList();

            foreach (var j in copia)
            {
                if (j.Cerrando) continue;
                foreach (var (pid, _) in ProcessTree.Descendientes(j.LauncherPid))
                    if (!j.Pids.ContainsKey(pid) && Inicio(pid) is DateTime t) j.Pids[pid] = t;

                if (j.LauncherVivo) continue;

                var vivos = Vivos(j);
                if (vivos.Count == 0)
                {
                    Quitar(j);
                    continue;
                }
                // Launcher terminado pero quedan procesos del juego:
                // - con ventana: el exe lanzado era un launcher que abrió el juego real y salió → se mantiene la
                //   salida con el combo para ese juego;
                // - sin ventana (5 s): restos que el juego dejó al salir → se terminan.
                bool conVentana = vivos.Any(pid => NativeMethods.VentanasVisibles(pid).Count > 0);
                if (conVentana) { j.SinVentanaDesde = null; continue; }
                j.SinVentanaDesde ??= DateTime.UtcNow;
                if ((DateTime.UtcNow - j.SinVentanaDesde.Value).TotalSeconds < 5) continue;
                foreach (var pid in vivos) Matar(pid, "huérfano sin ventana");
                Quitar(j);
            }

            bool hay;
            lock (_lock) hay = _juegos.Any(j => !j.Cerrando);
            if (hay && !_timer.Enabled) { _apretadoDesde = null; _disparado = true; _timer.Start(); }
            else if (!hay && _timer.Enabled) _timer.Stop();
        }

        private void Quitar(Juego j)
        {
            lock (_lock) _juegos.Remove(j);
        }

        private void Poll()
        {
            if (_mascara == 0) return;
            bool apretado = false;
            for (uint i = 0; i < 4; i++)
                if ((GamepadCredit.LeerBotones(i) & _mascara) == _mascara) { apretado = true; break; }

            if (!apretado)
            {
                // Hay que soltar el combo antes de que vuelva a valer (y al armarse, si ya venía apretado).
                _apretadoDesde = null;
                _disparado = false;
                return;
            }
            if (_disparado) return;
            _apretadoDesde ??= DateTime.UtcNow;
            if ((DateTime.UtcNow - _apretadoDesde.Value).TotalMilliseconds < _msMantener) return;

            _disparado = true;
            Juego? j;
            lock (_lock) j = _juegos.LastOrDefault(x => !x.Cerrando);
            if (j == null) return;
            j.Cerrando = true;
            Logger.Log($"Combo de salida: cerrando [{j.Sistema}] {j.Rom}");
            SalidaPedida?.Invoke(j);
        }

        /// <summary>Cierre escalonado del juego. Bloqueante: llamar desde Task.Run.</summary>
        public void Cerrar(Juego j, int esperaCierreMs, bool restaurarResolucion)
        {
            try
            {
                foreach (var (pid, _) in ProcessTree.Descendientes(j.LauncherPid))
                    if (!j.Pids.ContainsKey(pid) && Inicio(pid) is DateTime t) j.Pids[pid] = t;

                var vivos = Vivos(j);
                foreach (var pid in vivos) ProcessControl.Reanudar(pid); // un proceso suspendido no procesa WM_CLOSE

                var ventanas = vivos.SelectMany(NativeMethods.VentanasVisibles).ToList();
                Logger.Log($"Salida: WM_CLOSE a {ventanas.Count} ventana(s) de {vivos.Count} proceso(s)");
                foreach (var h in ventanas) NativeMethods.PostMessage(h, NativeMethods.WM_CLOSE, IntPtr.Zero, IntPtr.Zero);

                var fin = DateTime.UtcNow.AddMilliseconds(esperaCierreMs);
                while (DateTime.UtcNow < fin && Vivos(j).Count > 0) Thread.Sleep(100);

                var quedan = Vivos(j);
                if (quedan.Count == 0) Logger.Log("Salida: el juego cerró solo (WM_CLOSE)");
                // Pidió "¿salir?", ignoró WM_CLOSE en pantalla completa o está colgado: se termina el árbol.
                foreach (var pid in quedan.AsEnumerable().Reverse()) Matar(pid, "no cerró");

                // emulatorLauncher termina solo cuando el juego sale; ahí restaura lo suyo y RetroBat retoma.
                fin = DateTime.UtcNow.AddMilliseconds(5000);
                while (DateTime.UtcNow < fin && Existe(j.LauncherPid)) Thread.Sleep(100);
                if (Existe(j.LauncherPid)) Logger.Log("Salida: emulatorLauncher sigue vivo");

                if (restaurarResolucion) Pantalla.RestaurarSiCambio();
                FocoAEmulationStation();
            }
            catch (Exception ex) { Logger.Log("Salida: " + ex.Message); }
            finally { Quitar(j); }
        }

        private static void FocoAEmulationStation()
        {
            foreach (var p in Process.GetProcessesByName("emulationstation"))
            {
                using (p)
                {
                    var h = NativeMethods.VentanaPrincipalGrande(p.Id);
                    if (h == IntPtr.Zero) continue;
                    NativeMethods.TraerAlFrente(h);
                    Logger.Log("Salida: foco devuelto a EmulationStation");
                    return;
                }
            }
        }

        private static DateTime? Inicio(int pid)
        {
            try { using var p = Process.GetProcessById(pid); return p.StartTime; }
            catch { return null; }
        }

        private static bool Existe(int pid)
        {
            try { using var p = Process.GetProcessById(pid); return !p.HasExited; }
            catch { return false; }
        }

        private static List<int> Vivos(Juego j) =>
            j.Pids.Where(kv => Inicio(kv.Key) == kv.Value).Select(kv => kv.Key).ToList();

        private static void Matar(int pid, string motivo)
        {
            try
            {
                using var p = Process.GetProcessById(pid);
                ProcessControl.Reanudar(pid);
                var nombre = p.ProcessName;
                p.Kill();
                Logger.Log($"Salida: Kill {nombre}({pid}) — {motivo}");
            }
            catch { }
        }

        public void Dispose() => _timer.Dispose();
    }

    /// <summary>Vuelve el escritorio a su modo guardado si un juego cerrado a la fuerza lo dejó cambiado.</summary>
    internal static class Pantalla
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct DEVMODE
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
            public short dmSpecVersion, dmDriverVersion, dmSize, dmDriverExtra;
            public int dmFields, dmPositionX, dmPositionY, dmDisplayOrientation, dmDisplayFixedOutput;
            public short dmColor, dmDuplex, dmYResolution, dmTTOption, dmCollate;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
            public short dmLogPixels;
            public int dmBitsPerPel, dmPelsWidth, dmPelsHeight, dmDisplayFlags, dmDisplayFrequency;
            public int dmICMMethod, dmICMIntent, dmMediaType, dmDitherType, dmReserved1, dmReserved2;
            public int dmPanningWidth, dmPanningHeight;
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern bool EnumDisplaySettings(string? device, int modeNum, ref DEVMODE dm);

        [DllImport("user32.dll")]
        private static extern int ChangeDisplaySettings(IntPtr devMode, int flags);

        private const int ENUM_CURRENT_SETTINGS = -1, ENUM_REGISTRY_SETTINGS = -2;

        private static DEVMODE? Leer(int modo)
        {
            var dm = new DEVMODE { dmSize = (short)Marshal.SizeOf<DEVMODE>() };
            return EnumDisplaySettings(null, modo, ref dm) ? dm : null;
        }

        public static void RestaurarSiCambio()
        {
            if (Leer(ENUM_CURRENT_SETTINGS) is not DEVMODE a || Leer(ENUM_REGISTRY_SETTINGS) is not DEVMODE r) return;
            if (a.dmPelsWidth == r.dmPelsWidth && a.dmPelsHeight == r.dmPelsHeight &&
                a.dmDisplayFrequency == r.dmDisplayFrequency) return;
            var res = ChangeDisplaySettings(IntPtr.Zero, 0);
            Logger.Log($"Salida: resolución {a.dmPelsWidth}x{a.dmPelsHeight}@{a.dmDisplayFrequency} → " +
                       $"{r.dmPelsWidth}x{r.dmPelsHeight}@{r.dmDisplayFrequency} (resultado {res})");
        }
    }
}
