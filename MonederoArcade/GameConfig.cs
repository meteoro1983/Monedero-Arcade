namespace MonederoArcade
{
    public class GameConfig
    {
        public int TiempoOverlay { get; set; } = 20;
        public int IntervaloLoop { get; set; } = 1200;
        public int TiempoCountdown { get; set; } = 20;
        public string CreditKeyName { get; set; } = "D5";   // tecla "5" por defecto
        public string CreditPadName { get; set; } = "BACK"; // Select en XInput
        public int PulsosPorCredito { get; set; } = 1;
        public bool OsdRetroArch { get; set; } = false;     // mensajes OSD nativos de RetroArch (SHOW_MSG)
        public bool FastForward { get; set; } = false;      // solo RetroArch
        public int TiempoFastForward { get; set; } = 0;     // segundos de fast-forward desde el lanzamiento
        public string Aviso { get; set; } = "overlay";      // overlay | osd | ambos (osd = mensaje dentro de RetroArch)

        public bool UsaOverlay => Aviso != "osd";
        public bool UsaOsd => OsdRetroArch || Aviso == "osd" || Aviso == "ambos";
    }

    public class EmuladorConfig
    {
        public string MetodoPausa { get; set; } = "suspend"; // suspend | hotkey
        public string TeclaPausa { get; set; } = "Space";
        /// <summary>
        /// Minimiza la ventana del emulador antes de pausar y la restaura al reanudar: saca de la pantalla
        /// completa EXCLUSIVA (Dolphin en D3D11) para que el cartel se vea. RetroBat pisa la opción de
        /// pantalla sin bordes de Dolphin en cada lanzamiento, por eso se resuelve desde acá.
        /// </summary>
        public bool MinimizarAlPausar { get; set; } = false;
        /// <summary>Juegos de PC: silencia su audio mientras están suspendidos (evita el zumbido de DirectSound).</summary>
        public bool SilenciarAlPausar { get; set; } = false;
        /// <summary>Juegos de PC: cuánto esperar tras WM_CLOSE antes de terminar el proceso.</summary>
        public int EsperaCierreMs { get; set; } = 4000;
    }

    /// <summary>
    /// Resuelve config.ini (defaults por sistema) + <sistema>\<rom>.ini (override individual).
    /// El .ini de cada ROM nace con activo=0 (manda el default del sistema); con activo=1
    /// sus valores pisan al default.
    /// </summary>
    public class ConfigManager
    {
        private readonly string _baseDir;
        private readonly string _generalIniPath;

        public string BaseDir => _baseDir;
        public string GeneralIniPath => _generalIniPath;

        public ConfigManager(string baseDir)
        {
            _baseDir = baseDir;
            _generalIniPath = Path.Combine(_baseDir, "config.ini");
            EnsureGeneralConfig();
        }

        private void EnsureGeneralConfig()
        {
            if (File.Exists(_generalIniPath)) return;
            Directory.CreateDirectory(_baseDir);
            File.WriteAllText(_generalIniPath,
@"; Config general del monedero. Una seccion [sistema] por carpeta de ROMs de RetroBat
; (usar el mismo nombre que usa RetroBat, ej: dreamcast, psx, gamecube, wii, snes, nes, megadrive, pcengine)
; Tambien se edita desde la interfaz: Ctrl+Alt+F5, o clic derecho en el icono de la bandeja.

[general]
; pin_config=1234
; (si se define pin_config, Ctrl+Alt+F5 pide ese PIN antes de abrir la configuracion)
fuente=PressStart2P.ttf
sonido_credito=coin-sound.mp3
volumen_sonido=80
; aspecto_pantalla: auto (CRT 4:3 salvo monitores modernos de 720+ lineas) | 4:3 | real
aspecto_pantalla=auto
texto_linea1=INSERT COIN
texto_linea2=TO CONTINUE
texto_gameover1=GAME OVER
texto_gameover2=RETURNING TO MENU
sistemas_exentos=mame,neogeo,fbneo,fb,naomi,atomiswave,model2,model3,teknoparrot
; sistemas_pc: sistemas cuyos juegos son programas de PC (los lanza emulatorLauncher)
sistemas_pc=windows
; Salida universal: manteniendo salida_combo (botones XInput separados por +) salida_ms milisegundos se cierra
; el juego de los sistemas de salida_sistemas (por juego: salida=1/0 en su .ini). Los emuladores ya salen
; con el hotkey de RetroBat. salida_combo=NONE la apaga.
salida_combo=BACK+START
salida_ms=500
salida_sistemas=windows
salida_restaurar_resolucion=1

[default]
tiempo_overlay=20
intervalo_loop=1200
tiempo_countdown=20
credit_key=D5
credit_pad=BACK
osd_retroarch=0
pulsos_por_credito=1
fast_forward=0
tiempo_fast_forward=0

[dreamcast]
tiempo_overlay=20
tiempo_countdown=20

[psx]
tiempo_overlay=20
tiempo_countdown=20

[ps2]
tiempo_overlay=25
tiempo_countdown=20

[n64]
tiempo_overlay=20
tiempo_countdown=20
; parallel_n64 usa OpenGL a pantalla completa y puede tapar el overlay: ambos = overlay + mensaje de RetroArch
aviso=ambos

[gamecube]
tiempo_overlay=25
tiempo_countdown=20

[wii]
tiempo_overlay=25
tiempo_countdown=20

[snes]
tiempo_overlay=8
tiempo_countdown=20

[nes]
tiempo_overlay=6
tiempo_countdown=20

[megadrive]
tiempo_overlay=6
tiempo_countdown=20

[pcengine]
tiempo_overlay=8
tiempo_countdown=20

; Emuladores standalone: metodo_pausa = suspend (suspende el proceso, no requiere hotkeys)
;                                     o hotkey (simula la tecla tecla_pausa en la ventana del emulador)
[emulador:dolphin]
metodo_pausa=suspend
tecla_pausa=F10
; Dolphin usa pantalla completa exclusiva y tapa el cartel: se minimiza mientras está pausado
minimizar_al_pausar=1

[emulador:pcsx2]
metodo_pausa=suspend
tecla_pausa=Space

[emulador:duckstation]
metodo_pausa=suspend
tecla_pausa=Space

[emulador:redream]
metodo_pausa=suspend
tecla_pausa=Space

[emulador:ppsspp]
metodo_pausa=suspend
tecla_pausa=Space

; Juegos de PC (sistemas de [general] sistemas_pc, por defecto windows): se suspende todo el árbol de
; procesos del juego. Se minimiza (la pantalla completa exclusiva tapa el cartel) y se silencia (el audio
; de un proceso suspendido puede quedar zumbando en bucle).
[emulador:pc]
metodo_pausa=suspend
minimizar_al_pausar=1
silenciar_al_pausar=1
espera_cierre_ms=4000

[windows]
; los juegos de PC tienen intros y menús largos
tiempo_overlay=60
tiempo_countdown=20
");
        }

        /// <summary>Lista blanca de sistemas arcade exentos del sistema de crédito (además de MAME).</summary>
        public const string ExentosPorDefecto = "mame,neogeo,fbneo,fb,naomi,atomiswave,model2,model3,teknoparrot";

        /// <summary>Se relee en cada lanzamiento: lo que se cambie en el configurador aplica al próximo juego.</summary>
        public bool EsSistemaExento(string sistema)
        {
            var lista = new IniFile(_generalIniPath).GetString("general", "sistemas_exentos", ExentosPorDefecto);
            return lista.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                        .Contains(sistema, StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Juego exento del crédito aunque su sistema no lo esté: exento=1 en su .ini (vale aunque tenga activo=0).
        /// Para juegos que manejan sus propias fichas (ej. Doom con modo arcade). La salida universal sigue activa.
        /// </summary>
        public bool EsJuegoExento(string sistema, string nombreArchivoRom)
        {
            var romIni = RomIniPath(sistema, nombreArchivoRom);
            return File.Exists(romIni) && new IniFile(romIni).GetBool("monedero", "exento", false);
        }

        private static GameConfig LeerBase(IniFile g, string sistema)
        {
            return new GameConfig
            {
                TiempoOverlay = g.GetInt(sistema, "tiempo_overlay", g.GetInt("default", "tiempo_overlay", 20)),
                IntervaloLoop = g.GetInt(sistema, "intervalo_loop", g.GetInt("default", "intervalo_loop", 1200)),
                TiempoCountdown = g.GetInt(sistema, "tiempo_countdown", g.GetInt("default", "tiempo_countdown", 20)),
                CreditKeyName = g.GetString(sistema, "credit_key", g.GetString("default", "credit_key", "D5")),
                CreditPadName = g.GetString(sistema, "credit_pad", g.GetString("default", "credit_pad", "BACK")),
                PulsosPorCredito = g.GetInt(sistema, "pulsos_por_credito", g.GetInt("default", "pulsos_por_credito", 1)),
                OsdRetroArch = g.GetBool(sistema, "osd_retroarch", g.GetBool("default", "osd_retroarch", false)),
                FastForward = g.GetBool(sistema, "fast_forward", g.GetBool("default", "fast_forward", false)),
                TiempoFastForward = g.GetInt(sistema, "tiempo_fast_forward", g.GetInt("default", "tiempo_fast_forward", 0)),
                Aviso = g.GetString(sistema, "aviso", g.GetString("default", "aviso", "overlay")).Trim().ToLowerInvariant(),
            };
        }

        public GameConfig LeerSistema(string sistema) => LeerBase(new IniFile(_generalIniPath), sistema);

        public EmuladorConfig ResolveEmulador(string emulador)
        {
            var g = new IniFile(_generalIniPath);
            var sec = "emulador:" + emulador.ToLowerInvariant();
            var defTecla = emulador.ToLowerInvariant() == "dolphin" ? "F10" : "Space";
            bool pc = emulador.ToLowerInvariant() == "pc";
            return new EmuladorConfig
            {
                MetodoPausa = g.GetString(sec, "metodo_pausa", "suspend").Trim().ToLowerInvariant(),
                TeclaPausa = g.GetString(sec, "tecla_pausa", defTecla).Trim(),
                MinimizarAlPausar = g.GetBool(sec, "minimizar_al_pausar", emulador.ToLowerInvariant() == "dolphin" || pc),
                SilenciarAlPausar = g.GetBool(sec, "silenciar_al_pausar", pc),
                EsperaCierreMs = g.GetInt(sec, "espera_cierre_ms", 4000)
            };
        }

        /// <summary>Sistemas cuyos juegos son programas de PC lanzados por emulatorLauncher (por defecto "windows").</summary>
        public bool EsSistemaPc(string sistema)
        {
            var lista = new IniFile(_generalIniPath).GetString("general", "sistemas_pc", "windows");
            return lista.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                        .Contains(sistema, StringComparer.OrdinalIgnoreCase);
        }

        // ---- Salida universal (Select+Start) para juegos de PC ----

        public string SalidaCombo => new IniFile(_generalIniPath).GetString("general", "salida_combo", "BACK+START");
        public int SalidaMs => new IniFile(_generalIniPath).GetInt("general", "salida_ms", 500);
        public bool SalidaRestaurarResolucion => new IniFile(_generalIniPath).GetBool("general", "salida_restaurar_resolucion", true);

        /// <summary>
        /// ¿Monedero se encarga de salir de este juego con el combo? Por sistema (salida_sistemas, por defecto
        /// windows) o por juego: salida=1/0 en su .ini (vale aunque el .ini tenga activo=0).
        /// </summary>
        public bool UsaSalidaUniversal(string sistema, string nombreArchivoRom)
        {
            var romIni = RomIniPath(sistema, nombreArchivoRom);
            if (File.Exists(romIni))
            {
                var v = new IniFile(romIni).GetString("monedero", "salida", "").Trim();
                if (v == "1") return true;
                if (v == "0") return false;
            }
            var lista = new IniFile(_generalIniPath).GetString("general", "salida_sistemas", "windows");
            return lista.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                        .Contains(sistema, StringComparer.OrdinalIgnoreCase);
        }

        public string RomIniPath(string sistema, string nombreArchivoRom) =>
            Path.Combine(_baseDir, sistema, nombreArchivoRom + ".ini");

        private static void CrearPlantilla(string path, GameConfig c)
        {
            IniFile.WriteFreshSection(path, "monedero", new (string, string)[]
            {
                ("activo", "0"),
                ("tiempo_overlay", c.TiempoOverlay.ToString()),
                ("intervalo_loop", c.IntervaloLoop.ToString()),
                ("tiempo_countdown", c.TiempoCountdown.ToString()),
                ("fast_forward", c.FastForward ? "1" : "0"),
                ("tiempo_fast_forward", c.TiempoFastForward.ToString()),
            });
        }

        public GameConfig Resolve(string sistema, string nombreArchivoRom)
        {
            var general = new IniFile(_generalIniPath);
            var cfg = LeerBase(general, sistema);

            var romIniPath = RomIniPath(sistema, nombreArchivoRom);
            if (!File.Exists(romIniPath))
            {
                CrearPlantilla(romIniPath, cfg); // nace desactivada (activo=0)
                return cfg;
            }

            var romIni = new IniFile(romIniPath);
            if (!romIni.GetBool("monedero", "activo", false)) return cfg;

            cfg.TiempoOverlay = romIni.GetInt("monedero", "tiempo_overlay", cfg.TiempoOverlay);
            cfg.IntervaloLoop = romIni.GetInt("monedero", "intervalo_loop", cfg.IntervaloLoop);
            cfg.TiempoCountdown = romIni.GetInt("monedero", "tiempo_countdown", cfg.TiempoCountdown);
            cfg.CreditKeyName = romIni.GetString("monedero", "credit_key", cfg.CreditKeyName);
            cfg.CreditPadName = romIni.GetString("monedero", "credit_pad", cfg.CreditPadName);
            cfg.PulsosPorCredito = romIni.GetInt("monedero", "pulsos_por_credito", cfg.PulsosPorCredito);
            cfg.OsdRetroArch = romIni.GetBool("monedero", "osd_retroarch", cfg.OsdRetroArch);
            cfg.FastForward = romIni.GetBool("monedero", "fast_forward", cfg.FastForward);
            cfg.TiempoFastForward = romIni.GetInt("monedero", "tiempo_fast_forward", cfg.TiempoFastForward);
            cfg.Aviso = romIni.GetString("monedero", "aviso", cfg.Aviso).Trim().ToLowerInvariant();
            return cfg;
        }

        public void ActivarYGuardar(string sistema, string nombreArchivoRom, GameConfig cfg)
        {
            var romIni = new IniFile(RomIniPath(sistema, nombreArchivoRom));
            romIni.Set("monedero", "activo", "1");
            romIni.Set("monedero", "tiempo_overlay", cfg.TiempoOverlay.ToString());
            romIni.Set("monedero", "intervalo_loop", cfg.IntervaloLoop.ToString());
            romIni.Set("monedero", "tiempo_countdown", cfg.TiempoCountdown.ToString());
            romIni.Set("monedero", "fast_forward", cfg.FastForward ? "1" : "0");
            romIni.Set("monedero", "tiempo_fast_forward", cfg.TiempoFastForward.ToString());
            romIni.Save();
        }

        // ---- Soporte de la interfaz de configuración ----

        public List<string> ListarSistemas()
        {
            var g = new IniFile(_generalIniPath);
            return g.Sections()
                    .Where(s => !s.Equals("general", StringComparison.OrdinalIgnoreCase)
                             && !s.StartsWith("emulador:", StringComparison.OrdinalIgnoreCase))
                    .ToList();
        }

        public void GuardarSistema(string sistema, GameConfig c)
        {
            var g = new IniFile(_generalIniPath);
            g.Set(sistema, "tiempo_overlay", c.TiempoOverlay.ToString());
            g.Set(sistema, "intervalo_loop", c.IntervaloLoop.ToString());
            g.Set(sistema, "tiempo_countdown", c.TiempoCountdown.ToString());
            g.Set(sistema, "fast_forward", c.FastForward ? "1" : "0");
            g.Set(sistema, "tiempo_fast_forward", c.TiempoFastForward.ToString());
            g.Save();
        }

        public void AgregarSistema(string sistema)
        {
            var g = new IniFile(_generalIniPath);
            if (g.Sections().Any(s => s.Equals(sistema, StringComparison.OrdinalIgnoreCase))) return;
            GuardarSistema(sistema, LeerBase(g, "default"));
        }

        private static readonly HashSet<string> ExtIgnoradas = new(StringComparer.OrdinalIgnoreCase)
        {
            ".ini", ".xml", ".txt", ".jpg", ".jpeg", ".png", ".gif", ".mp4", ".avi", ".pdf",
            ".srm", ".sav", ".state", ".cfg", ".log", ".bak", ".db", ".nfo", ".md", ".dat"
        };

        /// <summary>Pre-genera los .ini (activo=0) de todos los archivos de una carpeta de ROMs. Devuelve -1 si la carpeta no existe.</summary>
        public int EscanearCarpeta(string sistema, string carpeta)
        {
            if (!Directory.Exists(carpeta)) return -1;
            var baseCfg = LeerBase(new IniFile(_generalIniPath), sistema);
            int creados = 0;
            foreach (var f in Directory.GetFiles(carpeta))
            {
                if (ExtIgnoradas.Contains(Path.GetExtension(f))) continue;
                var iniPath = RomIniPath(sistema, Path.GetFileName(f));
                if (File.Exists(iniPath)) continue;
                CrearPlantilla(iniPath, baseCfg);
                creados++;
            }
            return creados;
        }
    }
}
