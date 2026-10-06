using System.Diagnostics;
using System.Management;

namespace MonederoArcade
{
    public record LaunchedGame(int ProcessId, string Emulador, string Sistema, string NombreArchivo, string RutaCompleta);

    /// <summary>
    /// Vigila (polling cada 1.5 s) los emuladores conocidos: RetroArch, Dolphin, PCSX2, DuckStation, Redream, PPSSPP.
    /// Al detectar uno nuevo lee su línea de comandos (WMI), extrae la ruta del contenido y deriva el
    /// sistema de la carpeta (roms\<sistema>\...), siguiendo la convención de RetroBat.
    /// </summary>
    public class ProcessWatcher
    {
        private static readonly (string Prefijo, string Emulador)[] Conocidos =
        {
            ("retroarch", "retroarch"),
            ("dolphin", "dolphin"),
            ("pcsx2", "pcsx2"),
            ("duckstation", "duckstation"),
            ("redream", "redream"),
            ("ppsspp", "ppsspp")          // PPSSPPWindows64.exe (PSP standalone)
        };

        private readonly HashSet<int> _vistos = new();
        private readonly HashSet<int> _logueados = new();

        public event Action<LaunchedGame>? JuegoLanzado;
        public event Action<int>? JuegoFinalizado;

        public void Tick()
        {
            var actuales = new HashSet<int>();

            foreach (var p in Process.GetProcesses())
            {
                try
                {
                    var nombre = p.ProcessName.ToLowerInvariant();
                    string? emu = null;
                    foreach (var (prefijo, emulador) in Conocidos)
                    {
                        if (nombre.StartsWith(prefijo)) { emu = emulador; break; }
                    }
                    if (emu == null) continue;

                    actuales.Add(p.Id);
                    if (_vistos.Contains(p.Id)) continue;

                    var cmdLine = ObtenerLineaDeComandos(p.Id);
                    if (_logueados.Add(p.Id))
                        Logger.Log($"{emu} pid={p.Id} proceso={p.ProcessName} cmdline={cmdLine ?? "(no disponible)"}");

                    var contenido = ExtraerRutaContenido(cmdLine);
                    if (contenido == null) continue;

                    var sistema = ObtenerCarpetaSistema(contenido);
                    var nombreArchivo = Path.GetFileName(contenido);
                    if (sistema == null || string.IsNullOrEmpty(nombreArchivo)) continue;

                    _vistos.Add(p.Id);
                    JuegoLanzado?.Invoke(new LaunchedGame(p.Id, emu, sistema, nombreArchivo, contenido));
                }
                catch (Exception ex)
                {
                    Logger.Log("Watcher: " + ex.Message);
                }
                finally
                {
                    p.Dispose();
                }
            }

            var cerrados = _vistos.Where(id => !actuales.Contains(id)).ToList();
            foreach (var id in cerrados)
            {
                _vistos.Remove(id);
                JuegoFinalizado?.Invoke(id);
            }
        }

        private static string? ObtenerLineaDeComandos(int pid)
        {
            try
            {
                using var searcher = new ManagementObjectSearcher(
                    $"SELECT CommandLine FROM Win32_Process WHERE ProcessId = {pid}");
                foreach (ManagementObject mo in searcher.Get())
                {
                    return mo["CommandLine"]?.ToString();
                }
            }
            catch
            {
                // WMI puede fallar por permisos en algunos entornos.
            }
            return null;
        }

        private static bool EsExtensionNoContenido(string t)
        {
            return t.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
                || t.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                || t.EndsWith(".ini", StringComparison.OrdinalIgnoreCase)
                || t.EndsWith(".cfg", StringComparison.OrdinalIgnoreCase)
                || t.EndsWith(".json", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Busca desde el final el último token que parezca la ruta del contenido: sin "-" inicial,
        /// con extensión y sin "=" ; primero se prefieren los que tienen separadores de carpeta.
        /// </summary>
        private static string? ExtraerRutaContenido(string? cmdLine)
        {
            if (string.IsNullOrWhiteSpace(cmdLine)) return null;

            var tokens = new List<string>();
            var actual = "";
            bool entreComillas = false;
            foreach (var c in cmdLine)
            {
                if (c == '"') { entreComillas = !entreComillas; continue; }
                if (c == ' ' && !entreComillas)
                {
                    if (actual.Length > 0) { tokens.Add(actual); actual = ""; }
                    continue;
                }
                actual += c;
            }
            if (actual.Length > 0) tokens.Add(actual);

            for (int pasada = 0; pasada < 2; pasada++)
            {
                for (int i = tokens.Count - 1; i >= 0; i--)
                {
                    var t = tokens[i];
                    if (t.StartsWith("-") || t.Contains('=')) continue;
                    if (!Path.HasExtension(t) || EsExtensionNoContenido(t)) continue;
                    bool conSeparador = t.Contains('\\') || t.Contains('/');
                    if (pasada == 0 && !conSeparador) continue;
                    return t;
                }
            }
            return null;
        }

        /// <summary>
        /// Sistema = carpeta que sigue a "roms" en la ruta (roms\psx\juego.cue -> psx), lo que cubre
        /// subcarpetas por juego. Si no hay "roms" en la ruta, se usa la carpeta contenedora.
        /// </summary>
        private static string? ObtenerCarpetaSistema(string rutaContenido)
        {
            try
            {
                var dir = Path.GetDirectoryName(rutaContenido);
                if (string.IsNullOrEmpty(dir)) return null;
                var partes = dir.Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries);
                for (int i = 0; i < partes.Length - 1; i++)
                {
                    if (partes[i].Equals("roms", StringComparison.OrdinalIgnoreCase))
                        return partes[i + 1].ToLowerInvariant();
                }
                return new DirectoryInfo(dir).Name.ToLowerInvariant();
            }
            catch
            {
                return null;
            }
        }
    }
}
